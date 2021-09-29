// Copyright 2018 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Firebase.Firestore.Internal;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  internal sealed class AttributedTypeConverter : MapConverterBase {
    // Note: the use of a dictionary for writable properties and a list for readable ones is purely
    // driven by how we use this. If we ever want a dictionary for readable properties, that's easy to do.
    private readonly Dictionary<string, AttributedProperty> _writableProperties;
    private readonly List<AttributedProperty> _readableProperties;
    private readonly Func<object> _createInstance;
    private readonly FirestoreDataAttribute _attribute;

    private AttributedTypeConverter(BclType targetType, FirestoreDataAttribute attribute) : base(targetType) {
      _attribute = attribute;

      // Check for user bugs in terms of attribute specifications.
      Preconditions.CheckState(Enum.IsDefined(typeof(UnknownPropertyHandling), _attribute.UnknownPropertyHandling),
          "Type {0} has invalid {1} value", targetType.FullName, nameof(FirestoreDataAttribute.UnknownPropertyHandling));

      _createInstance = CreateObjectCreator(targetType);

      List<AttributedProperty> readableProperties = new List<AttributedProperty>();
      Dictionary<string, AttributedProperty> writableProperties = new Dictionary<string, AttributedProperty>();
      // We look for static properties specifically to find problems. We'll never use static properties.
      foreach (var property in targetType.GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        var propertyAttribute = Attribute.GetCustomAttribute(property, typeof(FirestorePropertyAttribute), inherit: true) as FirestorePropertyAttribute;
        if (propertyAttribute == null) {
          continue;
        }
        var attributedProperty = new AttributedProperty(property, propertyAttribute);
        string firestoreName = attributedProperty.FirestoreName;

        if (attributedProperty.IsNullableValue) {
          throw new NotSupportedException(
            String.Format("{0}.{1} is Nullable value type, which we do not support now due to a Unity limitation. " +
                              "Try using a reference type instead.",
                          targetType.FullName, property.Name));
        }

        // Note that we check readable and writable properties separately. We could theoretically have
        // two separate properties, one read-only and one write-only, with the same name in Firestore.
        if (attributedProperty.CanRead) {
          // This is O(N), but done once per type so should be okay.
          Preconditions.CheckState(!readableProperties.Any(p => p.FirestoreName == firestoreName),
              "Type {0} contains multiple readable properties with name {1}", targetType.FullName, firestoreName);
          readableProperties.Add(attributedProperty);
        }
        if (attributedProperty.CanWrite) {
          Preconditions.CheckState(!writableProperties.ContainsKey(firestoreName),
              "Type {0} contains multiple writable properties with name {1}", targetType.FullName, firestoreName);
          writableProperties[firestoreName] = attributedProperty;
        }
      }
      _readableProperties = readableProperties;
      _writableProperties = writableProperties;
    }

    // Only used in the constructor, but extracted for readability.
    private static Func<object> CreateObjectCreator(BclType type) {
      if (type.IsValueType) {
        return () => Activator.CreateInstance(type);
      } else {
        // TODO: Consider using a compiled expression tree for this.
        var ctor = type
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .SingleOrDefault(c => c.GetParameters().Length == 0);
        Preconditions.CheckState(ctor != null, "Type {0} has no parameterless constructor", type.FullName);
        return () => {
          try {
            return ctor.Invoke(parameters: null);
          } catch (TargetInvocationException e) {
            if (e.InnerException != null) {
              throw e.InnerException;
            } else {
              throw;
            }
          }
        };
      }
    }

    /// <summary>
    /// Factory method to construct a converter for an attributed type.
    /// </summary>
    internal static IFirestoreInternalConverter ForType(BclType targetType) {
      var attribute = Attribute.GetCustomAttribute(targetType, typeof(FirestoreDataAttribute), inherit: false) as FirestoreDataAttribute;
      // This would be an internal library bug. We shouldn't be calling it in this case.
      Preconditions.CheckState(attribute != null, "Type {0} is not decorated with {1}.", targetType.FullName, nameof(FirestoreDataAttribute));

      return attribute.ConverterType == null
          ? new AttributedTypeConverter(targetType, attribute)
          : CustomConverter.ForConverterType(attribute.ConverterType, targetType);
    }

    public // TODO(b/141568173): Significant newline.
    override object DeserializeMap(DeserializationContext context, FieldValueProxy mapValue) {
      Preconditions.CheckState(mapValue.type() == FieldValueProxy.Type.Map, "Expected to receive a map FieldValue");

      object ret = _createInstance();
      using (var map = FirestoreCpp.ConvertFieldValueToMap(mapValue)) {
        var iter = map.Iterator();

        while (iter.HasMore()) {
          var key = iter.UnsafeKeyView();
          FieldValueProxy value = iter.UnsafeValueView();
          iter.Advance();

          if (_writableProperties.TryGetValue(key, out var property)) {
            property.SetValue(context, value, ret);

          } else {
            switch (_attribute.UnknownPropertyHandling) {
              case UnknownPropertyHandling.Ignore:
                break;
              case UnknownPropertyHandling.Warn:
                Firebase.LogUtil.LogMessage(LogLevel.Warning, String.Format(
                    "No writable property for Firestore field {0} in type {1}", key, TargetType.FullName));
                break;
              case UnknownPropertyHandling.Throw:
                throw new ArgumentException(String.Format("No writable property for Firestore field {key} in type {0}", TargetType.FullName));
            }
          }
        }
      }

      AttributedIdAssigner.MaybeAssignId(ret, context.DocumentReference);
      // NOTE: We don't support ReadTime, UpdateTime, etc. so we don't have the
      // AttributedTimestampAssigner that google-cloud-dotnet does.
      return ret;
    }

    public override void SerializeMap(SerializationContext context, object value, IDictionary<string, FieldValueProxy> map) {
      foreach (var property in _readableProperties) {
        map[property.FirestoreName] = property.GetSerializedValue(context, value);
      }
    }

    private sealed class AttributedProperty {
      private readonly PropertyInfo _propertyInfo;
      private FieldValueProxy _sentinelValue;
      private readonly IFirestoreInternalConverter _converter;

      /// <summary>
      /// The name to use in Firestore serialization/deserialization. Defaults to the property
      /// name, but may be specified in <see cref="FirestorePropertyAttribute"/>.
      /// </summary>
      internal readonly string FirestoreName;
      internal bool CanRead => _propertyInfo.CanRead;
      internal bool CanWrite => _propertyInfo.CanWrite;

      internal bool IsNullableValue => _propertyInfo.PropertyType.IsGenericType &&
          _propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);

      internal AttributedProperty(PropertyInfo property, FirestorePropertyAttribute attribute) {
        _propertyInfo = property;
        FirestoreName = attribute.Name ?? property.Name;
        if (property.IsDefined(typeof(ServerTimestampAttribute), inherit: true)) {
          _sentinelValue = FieldValueProxy.ServerTimestamp();
        }
        if (attribute.ConverterType != null) {
          _converter = CustomConverter.ForConverterType(attribute.ConverterType, property.PropertyType);
        }

        string typeName = property.DeclaringType.FullName;
        Preconditions.CheckState(property.GetIndexParameters().Length == 0,
            "{0}.{1} is an indexer, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));

        // Annoyingly, we can't easily check whether the property is static - we have to check the individual methods.
        var getMethod = property.GetGetMethod(nonPublic: true);
        var setMethod = property.GetSetMethod(nonPublic: true);
        Preconditions.CheckState(getMethod == null || !getMethod.IsStatic,
            "{0}.{1} is static, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));
        Preconditions.CheckState(setMethod == null || !setMethod.IsStatic,
            "{0}.{1} is static, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));

        // NOTE: We don't support ValueTupleConverter even though google-cloud-dotnet
        // does since it depends on .Net 4.7.
      }

      // TODO: Consider creating delegates for the property get/set methods.
      // Note: these methods have to handle null values when there's a custom converter involved, just like ValueSerializer/ValueDeserializer do.
      internal FieldValueProxy GetSerializedValue(SerializationContext context, object obj) {
        if (_sentinelValue != null) {
          return _sentinelValue;
        }
        object propertyValue = _propertyInfo.GetValue(obj, index: null);
        return _converter == null ? ValueSerializer.Serialize(context, propertyValue)
            : propertyValue == null ? FieldValueProxy.Null()
            : _converter.Serialize(context, propertyValue);
      }

      internal void SetValue(DeserializationContext context, FieldValueProxy value, object target) {
        object converted =
            _converter == null ? ValueDeserializer.Deserialize(context, value, _propertyInfo.PropertyType)
            : value.is_null() ? null
            : _converter.DeserializeValue(context, value);
        _propertyInfo.SetValue(target, converted, index: null);
      }
    }
  }
}
