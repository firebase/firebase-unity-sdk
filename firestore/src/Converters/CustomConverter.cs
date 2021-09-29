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
using System.Linq;
using System.Reflection;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  internal static class CustomConverter {
    private static readonly MethodInfo _method = typeof(CustomConverter).GetMethod(nameof(CreateInstance), BindingFlags.Static | BindingFlags.NonPublic);

    // TODO: More validation, e.g. converter must be a concrete type, and the target type should not declare any
    // properties with FirestorePropertyAttribute (although its base type might).
    // TODO: Check that targetType isn't a nullable value type? We currently advise that, but we could enforce it.

    // TODO: Caching? We only do this once per property or type that's decorated with an attribute
    // with a Converter property. Caching adds significant complexity.
    internal static IFirestoreInternalConverter ForConverterType(BclType converterType, BclType targetType) {
      var nonNullableType = Nullable.GetUnderlyingType(targetType) ?? targetType;
      var expectedSuperClass = typeof(FirestoreConverter<>).MakeGenericType(nonNullableType);
      if (!converterType.IsSubclassOf(expectedSuperClass)) {
        throw new InvalidOperationException(String.Format("Type {0} does not inherit a custom converter for {1}",
            converterType, nonNullableType));
      }
      // TODO: Allow private constructors? (We probably want a helper method to do this, given that we do it in a bunch of places.)
      var instance = Activator.CreateInstance(converterType);
      return (IFirestoreInternalConverter)_method.MakeGenericMethod(nonNullableType).Invoke(null, new[] { instance });
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

    public // TODO(b/141568173): Significant newline.
    object DeserializeMap(DeserializationContext context, FieldValueProxy mapValue) {
      var poco = ValueDeserializer.DeserializeMap(context, mapValue, typeof(Dictionary<string, object>));
      var converted = _wrappedConverter.FromFirestore(poco);
      Preconditions.CheckState(converted != null, "Converter deserialized to null value");
      return converted;
    }

    public // TODO(b/141568173): Significant newline.
    object DeserializeValue(DeserializationContext context, FieldValueProxy value) {
      var poco = ValueDeserializer.Deserialize(context, value, typeof(object));
      var converted = _wrappedConverter.FromFirestore(poco);
      Preconditions.CheckState(converted != null, "Converter deserialized to null value");
      return converted;
    }

    public // TODO(b/141568173): Significant newline.
    FieldValueProxy Serialize(SerializationContext context, object value) {
      var poco = _wrappedConverter.ToFirestore((T)value);
      Preconditions.CheckState(poco != null, "Converter serialized to null value");
      return ValueSerializer.Serialize(context, poco);
    }

    public void SerializeMap(SerializationContext context, object value, IDictionary<string, FieldValueProxy> map) {
      var poco = _wrappedConverter.ToFirestore((T)value);
      Preconditions.CheckState(poco != null, "Converter serialized to null value");
      // TODO: Change ValueSerializer.SerializeMap to accept a map rather than return it? Then we can just pass it through.
      var resultMap = ValueSerializer.SerializeMap(context, poco);
      foreach (var entry in resultMap) {
        map.Add(entry);
      }
    }
  }
}
