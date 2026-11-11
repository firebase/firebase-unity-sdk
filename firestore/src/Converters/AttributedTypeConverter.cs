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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  internal sealed class AttributedTypeConverter : MapConverterBase {
    // Note: the use of a dictionary for writable properties and a list for readable ones is purely
    // driven by how we use this. If we ever want a dictionary for readable properties, that's easy to do.
    private readonly Dictionary<string, AttributedProperty> _writableProperties;
    private readonly AttributedProperty[] _readableProperties;
    private readonly Func<object> _createInstance;
    private readonly FirestoreDataAttribute _attribute;

    private AttributedTypeConverter(BclType targetType, FirestoreDataAttribute attribute)
        : base(targetType) {
      _attribute = attribute;

      // Check for user bugs in terms of attribute specifications.
      Preconditions.CheckState(
          Enum.IsDefined(typeof(UnknownPropertyHandling), _attribute.UnknownPropertyHandling),
          "Type {0} has invalid {1} value",
          targetType.FullName,
          nameof(FirestoreDataAttribute.UnknownPropertyHandling));

      _createInstance = CreateObjectCreator(targetType);

      var readableProperties = new List<AttributedProperty>();
      var writableProperties = new Dictionary<string, AttributedProperty>(StringComparer.Ordinal);
      // We look for static properties specifically to find problems. We'll never use static properties.
      foreach (PropertyInfo property in targetType.GetProperties(
          BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        var propertyAttribute = Attribute.GetCustomAttribute(
            property, typeof(FirestorePropertyAttribute), inherit: true) as FirestorePropertyAttribute;
        if (propertyAttribute == null) continue;

        var attributedProperty = new AttributedProperty(targetType, property, propertyAttribute);
        string firestoreName = attributedProperty.FirestoreName;

        if (attributedProperty.IsNullableValue) {
          throw new NotSupportedException(String.Format(
              "{0}.{1} is Nullable value type, which we do not support now due to a Unity limitation. " +
              "Try using a reference type instead.",
              targetType.FullName, property.Name));
        }

        // Note that we check readable and writable properties separately. We could theoretically have
        // two separate properties, one read-only and one write-only, with the same name in Firestore.
        if (attributedProperty.CanRead) {
          // This is O(N), but done once per type so should be okay.
          Preconditions.CheckState(
              !readableProperties.Any(p => p.FirestoreName == firestoreName),
              "Type {0} contains multiple readable properties with name {1}",
              targetType.FullName, firestoreName);
          readableProperties.Add(attributedProperty);
        }
        if (attributedProperty.CanWrite) {
          Preconditions.CheckState(
              !writableProperties.ContainsKey(firestoreName),
              "Type {0} contains multiple writable properties with name {1}",
              targetType.FullName, firestoreName);
          writableProperties[firestoreName] = attributedProperty;
        }
      }
      _readableProperties = readableProperties.ToArray();
      _writableProperties = writableProperties;
    }

    // Only used in the constructor, but extracted for readability.
    private static Func<object> CreateObjectCreator(BclType type) {
      if (type.IsValueType) {
        return () => Activator.CreateInstance(type);
      }
      ConstructorInfo ctor = type
          .GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
          .SingleOrDefault(c => c.GetParameters().Length == 0);
      Preconditions.CheckState(
          ctor != null, "Type {0} has no parameterless constructor", type.FullName);

      if (ctor.IsPublic) {
        try {
          return Expression.Lambda<Func<object>>(Expression.New(ctor)).Compile();
        } catch (Exception) {
          // Fall back to ConstructorInfo.Invoke on strict AOT targets.
        }
      }

      return () => {
        try {
          return ctor.Invoke(parameters: null);
        } catch (TargetInvocationException e) {
          if (e.InnerException != null) throw e.InnerException;
          throw;
        }
      };
    }

    /// <summary>
    /// Factory method to construct a converter for an attributed type.
    /// </summary>
    internal static IFirestoreInternalConverter ForType(BclType targetType) {
      var attribute = Attribute.GetCustomAttribute(
          targetType, typeof(FirestoreDataAttribute), inherit: false) as FirestoreDataAttribute;
      // This would be an internal library bug. We shouldn't be calling it in this case.
      Preconditions.CheckState(
          attribute != null, "Type {0} is not decorated with {1}.",
          targetType.FullName, nameof(FirestoreDataAttribute));

      return attribute.ConverterType == null
          ? new AttributedTypeConverter(targetType, attribute)
          : CustomConverter.ForConverterType(attribute.ConverterType, targetType);
    }

    public override object DeserializeMap(DeserializationContext context, FirestoreValue mapValue) {
      Preconditions.CheckState(
          mapValue.Kind == FirestoreValueKind.Map, "Expected to receive a map FirestoreValue");

      object ret = _createInstance();
      foreach (KeyValuePair<string, FirestoreValue> kv in mapValue.MapValue) {
        if (_writableProperties.TryGetValue(kv.Key, out AttributedProperty property)) {
          property.SetValue(context, kv.Value, ret);
        } else {
          switch (_attribute.UnknownPropertyHandling) {
            case UnknownPropertyHandling.Ignore:
              break;
            case UnknownPropertyHandling.Warn:
              Firebase.LogUtil.LogMessage(
                  LogLevel.Warning,
                  String.Format("No writable property for Firestore field {0} in type {1}",
                      kv.Key, TargetType.FullName));
              break;
            case UnknownPropertyHandling.Throw:
              throw new ArgumentException(String.Format(
                  "No writable property for Firestore field {0} in type {1}",
                  kv.Key, TargetType.FullName));
          }
        }
      }

      AttributedIdAssigner.MaybeAssignId(ret, context.DocumentReference);
      // NOTE: We don't support ReadTime, UpdateTime, etc. so we don't have the
      // AttributedTimestampAssigner that google-cloud-dotnet does.
      return ret;
    }

    public override void SerializeMap(
        SerializationContext context, object value, IDictionary<string, FirestoreValue> map) {
      for (int i = 0; i < _readableProperties.Length; i++) {
        AttributedProperty property = _readableProperties[i];
        map[property.FirestoreName] = property.GetSerializedValue(context, value);
      }
    }

    private sealed class AttributedProperty {
      private readonly PropertyInfo _propertyInfo;
      private readonly bool _isServerTimestamp;
      private readonly IFirestoreInternalConverter _converter;
      private readonly Func<SerializationContext, object, FirestoreValue> _fastGetter;
      private readonly Action<DeserializationContext, FirestoreValue, object> _fastSetter;

      /// <summary>
      /// The name to use in Firestore serialization/deserialization. Defaults to the property
      /// name, but may be specified in <see cref="FirestorePropertyAttribute"/>.
      /// </summary>
      internal readonly string FirestoreName;
      internal bool CanRead => _propertyInfo.CanRead;
      internal bool CanWrite => _propertyInfo.CanWrite;

      internal bool IsNullableValue =>
          _propertyInfo.PropertyType.IsGenericType &&
          _propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(Nullable<>);

      internal AttributedProperty(
          BclType ownerType, PropertyInfo property, FirestorePropertyAttribute attribute) {
        _propertyInfo = property;
        FirestoreName = attribute.Name ?? property.Name;
        _isServerTimestamp = property.IsDefined(typeof(ServerTimestampAttribute), inherit: true);
        if (attribute.ConverterType != null) {
          _converter = CustomConverter.ForConverterType(
              attribute.ConverterType, property.PropertyType);
        }

        string typeName = property.DeclaringType.FullName;
        Preconditions.CheckState(
            property.GetIndexParameters().Length == 0,
            "{0}.{1} is an indexer, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));

        // Annoyingly, we can't easily check whether the property is static - we have to check the individual methods.
        MethodInfo getMethod = property.GetGetMethod(nonPublic: true);
        MethodInfo setMethod = property.GetSetMethod(nonPublic: true);
        Preconditions.CheckState(
            getMethod == null || !getMethod.IsStatic,
            "{0}.{1} is static, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));
        Preconditions.CheckState(
            setMethod == null || !setMethod.IsStatic,
            "{0}.{1} is static, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestorePropertyAttribute));

        if (!ownerType.IsValueType && _converter == null && !_isServerTimestamp) {
          if (getMethod != null) {
            _fastGetter = TryCompileFastGetter(ownerType, property.PropertyType, getMethod);
          }
          if (setMethod != null) {
            _fastSetter = TryCompileFastSetter(ownerType, property.PropertyType, setMethod);
          }
        }

        // NOTE: We don't support ValueTupleConverter even though google-cloud-dotnet
        // does since it depends on .Net 4.7.
      }

      private static Func<SerializationContext, object, FirestoreValue> TryCompileFastGetter(
          BclType ownerType, BclType propType, MethodInfo getMethod) {
        try {
          ParameterExpression ctxParam = Expression.Parameter(typeof(SerializationContext), "ctx");
          ParameterExpression objParam = Expression.Parameter(typeof(object), "obj");
          MethodCallExpression rawGet =
              Expression.Call(Expression.Convert(objParam, ownerType), getMethod);

          if (propType == typeof(string)) {
            var fn = Expression.Lambda<Func<object, string>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromString(fn(obj));
          }
          if (propType == typeof(int)) {
            var fn = Expression.Lambda<Func<object, int>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromInteger(fn(obj));
          }
          if (propType == typeof(long)) {
            var fn = Expression.Lambda<Func<object, long>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromInteger(fn(obj));
          }
          if (propType == typeof(double)) {
            var fn = Expression.Lambda<Func<object, double>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromDouble(fn(obj));
          }
          if (propType == typeof(float)) {
            var fn = Expression.Lambda<Func<object, float>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromDouble(fn(obj));
          }
          if (propType == typeof(bool)) {
            var fn = Expression.Lambda<Func<object, bool>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromBoolean(fn(obj));
          }
          if (propType == typeof(Timestamp)) {
            var fn = Expression.Lambda<Func<object, Timestamp>>(rawGet, objParam).Compile();
            return (_, obj) => FirestoreValue.FromTimestamp(fn(obj));
          }

          var boxedFn = Expression.Lambda<Func<object, object>>(
              Expression.Convert(rawGet, typeof(object)), objParam).Compile();
          return (ctx, obj) => ValueSerializer.Serialize(ctx, boxedFn(obj));
        } catch (Exception) {
          return null;
        }
      }

      private static Action<DeserializationContext, FirestoreValue, object> TryCompileFastSetter(
          BclType ownerType, BclType propType, MethodInfo setMethod) {
        try {
          ParameterExpression objParam = Expression.Parameter(typeof(object), "obj");
          if (propType == typeof(string)) {
            ParameterExpression valParam = Expression.Parameter(typeof(string), "v");
            var act = Expression.Lambda<Action<object, string>>(
                Expression.Call(Expression.Convert(objParam, ownerType), setMethod, valParam),
                objParam, valParam).Compile();
            return (ctx, fv, target) => {
              if (fv.IsNull) act(target, null);
              else if (fv.Kind == FirestoreValueKind.String) act(target, fv.StringValue);
              else act(target, (string)ValueDeserializer.Deserialize(ctx, fv, typeof(string)));
            };
          }
          if (propType == typeof(int)) {
            ParameterExpression valParam = Expression.Parameter(typeof(int), "v");
            var act = Expression.Lambda<Action<object, int>>(
                Expression.Call(Expression.Convert(objParam, ownerType), setMethod, valParam),
                objParam, valParam).Compile();
            return (ctx, fv, target) => {
              if (fv.Kind == FirestoreValueKind.Integer) act(target, checked((int)fv.IntegerValue));
              else act(target, (int)ValueDeserializer.Deserialize(ctx, fv, typeof(int)));
            };
          }
          if (propType == typeof(long)) {
            ParameterExpression valParam = Expression.Parameter(typeof(long), "v");
            var act = Expression.Lambda<Action<object, long>>(
                Expression.Call(Expression.Convert(objParam, ownerType), setMethod, valParam),
                objParam, valParam).Compile();
            return (ctx, fv, target) => {
              if (fv.Kind == FirestoreValueKind.Integer) act(target, fv.IntegerValue);
              else act(target, (long)ValueDeserializer.Deserialize(ctx, fv, typeof(long)));
            };
          }
          if (propType == typeof(double)) {
            ParameterExpression valParam = Expression.Parameter(typeof(double), "v");
            var act = Expression.Lambda<Action<object, double>>(
                Expression.Call(Expression.Convert(objParam, ownerType), setMethod, valParam),
                objParam, valParam).Compile();
            return (ctx, fv, target) => {
              if (fv.Kind == FirestoreValueKind.Double) act(target, fv.DoubleValue);
              else if (fv.Kind == FirestoreValueKind.Integer) act(target, fv.IntegerValue);
              else act(target, (double)ValueDeserializer.Deserialize(ctx, fv, typeof(double)));
            };
          }
          if (propType == typeof(bool)) {
            ParameterExpression valParam = Expression.Parameter(typeof(bool), "v");
            var act = Expression.Lambda<Action<object, bool>>(
                Expression.Call(Expression.Convert(objParam, ownerType), setMethod, valParam),
                objParam, valParam).Compile();
            return (ctx, fv, target) => {
              if (fv.Kind == FirestoreValueKind.Boolean) act(target, fv.BooleanValue);
              else act(target, (bool)ValueDeserializer.Deserialize(ctx, fv, typeof(bool)));
            };
          }

          ParameterExpression boxedVal = Expression.Parameter(typeof(object), "v");
          var boxedAct = Expression.Lambda<Action<object, object>>(
              Expression.Call(
                  Expression.Convert(objParam, ownerType),
                  setMethod,
                  Expression.Convert(boxedVal, propType)),
              objParam, boxedVal).Compile();
          return (ctx, fv, target) =>
              boxedAct(target, ValueDeserializer.Deserialize(ctx, fv, propType));
        } catch (Exception) {
          return null;
        }
      }

      // Note: these methods have to handle null values when there's a custom converter involved, just like ValueSerializer/ValueDeserializer do.
      internal FirestoreValue GetSerializedValue(SerializationContext context, object obj) {
        if (_isServerTimestamp) {
          return FirestoreValue.ServerTimestampSentinel;
        }
        if (_fastGetter != null) {
          return _fastGetter(context, obj);
        }
        object propertyValue;
        try {
          propertyValue = _propertyInfo.GetValue(obj, index: null);
        } catch (TargetInvocationException e) {
          if (e.InnerException != null) throw e.InnerException;
          throw;
        }
        return _converter == null ? ValueSerializer.Serialize(context, propertyValue)
            : propertyValue == null ? FirestoreValue.Null
            : _converter.Serialize(context, propertyValue);
      }

      internal void SetValue(DeserializationContext context, FirestoreValue value, object target) {
        if (_fastSetter != null) {
          _fastSetter(context, value, target);
          return;
        }
        object converted = _converter == null
            ? ValueDeserializer.Deserialize(context, value, _propertyInfo.PropertyType)
            : value.IsNull ? null
            : _converter.DeserializeValue(context, value);
        try {
          _propertyInfo.SetValue(target, converted, index: null);
        } catch (TargetInvocationException e) {
          if (e.InnerException != null) throw e.InnerException;
          throw;
        }
      }
    }
  }
}
