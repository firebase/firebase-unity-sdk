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
using System.Reflection;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class CustomConverter {
    private static readonly MethodInfo _method =
        typeof(CustomConverter).GetMethod(nameof(CreateInstance), BindingFlags.Static | BindingFlags.NonPublic);

    // TODO: More validation, e.g. converter must be a concrete type, and the target type should not declare any
    // properties with FirestorePropertyAttribute (although its base type might).
    // TODO: Check that targetType isn't a nullable value type? We currently advise that, but we could enforce it.

    // TODO: Caching? We only do this once per property or type that's decorated with an attribute
    // with a Converter property. Caching adds significant complexity.
    internal static IFirestoreInternalConverter ForConverterType(BclType converterType, BclType targetType) {
      BclType nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
      BclType expectedSuperClass = typeof(FirestoreConverter<>).MakeGenericType(nonNullableType);
      if (!converterType.IsSubclassOf(expectedSuperClass)) {
        throw new InvalidOperationException(String.Format(
            "Type {0} does not inherit a custom converter for {1}", converterType, nonNullableType));
      }
      // TODO: Allow private constructors? (We probably want a helper method to do this, given that we do it in a bunch of places.)
      object instance;
      try {
        instance = Activator.CreateInstance(converterType);
      } catch (TargetInvocationException e) {
        if (e.InnerException != null) throw e.InnerException;
        throw;
      }
      return (IFirestoreInternalConverter)_method
          .MakeGenericMethod(nonNullableType)
          .Invoke(null, new[] { instance });
    }

    // Method to make the reflection simpler.
    internal static IFirestoreInternalConverter CreateInstance<T>(FirestoreConverter<T> wrappedConverter) =>
        new CustomConverter<T>(wrappedConverter);
  }

  /// <summary>
  /// A converter that wraps a user-specified <see cref="FirestoreConverter{T}"/>.
  /// </summary>
  internal sealed class CustomConverter<T> : IFirestoreInternalConverter {
    private readonly FirestoreConverter<T> _wrappedConverter;

    internal CustomConverter(FirestoreConverter<T> wrappedConverter) {
      _wrappedConverter = wrappedConverter;
    }

    public object DeserializeMap(DeserializationContext context, FirestoreValue mapValue) {
      object poco = ValueDeserializer.DeserializeMap(context, mapValue, typeof(Dictionary<string, object>));
      T converted = _wrappedConverter.FromFirestore(poco);
      Preconditions.CheckState(converted != null, "Converter deserialized to null value");
      return converted;
    }

    public object DeserializeValue(DeserializationContext context, FirestoreValue value) {
      object poco = ValueDeserializer.Deserialize(context, value, typeof(object));
      T converted = _wrappedConverter.FromFirestore(poco);
      Preconditions.CheckState(converted != null, "Converter deserialized to null value");
      return converted;
    }

    public FirestoreValue Serialize(SerializationContext context, object value) {
      object poco = _wrappedConverter.ToFirestore((T)value);
      Preconditions.CheckState(poco != null, "Converter serialized to null value");
      return ValueSerializer.Serialize(context, poco);
    }

    public void SerializeMap(
        SerializationContext context, object value, IDictionary<string, FirestoreValue> map) {
      object poco = _wrappedConverter.ToFirestore((T)value);
      Preconditions.CheckState(poco != null, "Converter serialized to null value");
      // TODO: Change ValueSerializer.SerializeMap to accept a map rather than return it? Then we can just pass it through.
      Dictionary<string, FirestoreValue> resultMap = ValueSerializer.SerializeMap(context, poco);
      foreach (KeyValuePair<string, FirestoreValue> entry in resultMap) {
        map.Add(entry);
      }
    }
  }
}
