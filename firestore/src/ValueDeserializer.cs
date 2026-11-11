// Copyright 2017 Google LLC
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
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore {
  // TODO: A lot of optimization of this and ValueSerializer, *after* writing comprehensive benchmarks.
  // We may be able to avoid a lot of boxing by a combination of making methods generic and building
  // delegates for serialization/deserialization of specific types using expression trees. The initial
  // aim is just to work out the functionality required, and optimize later.

  /// <summary>
  /// Provides conversions from <see cref="FirestoreValue"/> values to .NET types.
  /// </summary>
  internal static class ValueDeserializer {
    /// <summary>
    /// Deserializes from a <see cref="FirestoreValue"/> to a .NET type.
    /// </summary>
    /// <param name="context">The context for the deserialization operation. Never null.</param>
    /// <param name="value">The value to deserialize.</param>
    /// <param name="targetType">The target type. The method tries to convert to this type. If the type is
    /// object, it uses the default representation of the value.</param>
    /// <returns>The deserialized value</returns>
    internal static object Deserialize(
        DeserializationContext context, FirestoreValue value, BclType targetType) {
      Preconditions.CheckNotNull(context, nameof(context));
      Preconditions.CheckNotNull(targetType, nameof(targetType));

      // If we're asked for a FirestoreValue, just return it. Since it's immutable, no
      // cloning is necessary.
      if (targetType == typeof(FirestoreValue)) {
        return value;
      }
      if (targetType == typeof(object)) {
        return DeserializeDefaultObject(context, value);
      }

      BclType underlyingType = Nullable.GetUnderlyingType(targetType);
      if (value.IsNull) {
        if (!targetType.IsValueType || underlyingType != null) {
          return null;
        }
        throw new ArgumentException(
            String.Format("Unable to convert null value to {0}", targetType.FullName));
      }

      // We deserialize to T and Nullable<T> the same way for all non-null values. Use the converter
      // associated with the non-nullable version of the target type.
      BclType nonNullableTargetType = underlyingType ?? targetType;
      return context.GetConverter(nonNullableTargetType).DeserializeValue(context, value);
    }

    internal static object DeserializeMap(
        DeserializationContext context, FirestoreValue mapValue, BclType targetType) {
      if (targetType == typeof(object) || targetType == typeof(Dictionary<string, object>) ||
          targetType == typeof(IDictionary<string, object>)) {
        return DeserializeDefaultMap(context, mapValue.MapValue);
      }
      return context.GetConverter(targetType).DeserializeMap(context, mapValue);
    }

    private static object DeserializeDefaultObject(
        DeserializationContext context, FirestoreValue value) {
      switch (value.Kind) {
        case FirestoreValueKind.Null:
          return null;
        case FirestoreValueKind.Boolean:
          return value.BooleanValue;
        case FirestoreValueKind.Integer:
          return value.IntegerValue;
        case FirestoreValueKind.Double:
          return value.DoubleValue;
        case FirestoreValueKind.String:
          return value.StringValue;
        case FirestoreValueKind.Timestamp:
          return value.TimestampValue;
        case FirestoreValueKind.Blob:
          return value.BlobValue;
        case FirestoreValueKind.GeoPoint:
          return value.GeoPointValue;
        case FirestoreValueKind.Reference: {
          DocumentReferenceValue rv = value.ReferenceValue;
          FirebaseFirestore db = context.Firestore.GetDatabaseInstanceForReference(
              rv.ProjectId, rv.DatabaseId);
          return new DocumentReference(rv.Path, db);
        }
        case FirestoreValueKind.Array: {
          IReadOnlyList<FirestoreValue> arr = value.ArrayValue;
          var list = new List<object>(arr.Count);
          for (int i = 0; i < arr.Count; i++) {
            list.Add(DeserializeDefaultObject(context, arr[i]));
          }
          return list;
        }
        case FirestoreValueKind.Map:
          return DeserializeDefaultMap(context, value.MapValue);
        default:
          throw new ArgumentException(
              String.Format("Unable to convert value type {0} to System.Object", value.Kind));
      }
    }

    private static Dictionary<string, object> DeserializeDefaultMap(
        DeserializationContext context, IDictionary<string, FirestoreValue> map) {
      var dict = new Dictionary<string, object>(map?.Count ?? 0, StringComparer.Ordinal);
      if (map != null) {
        foreach (KeyValuePair<string, FirestoreValue> kv in map) {
          dict[kv.Key] = DeserializeDefaultObject(context, kv.Value);
        }
      }
      return dict;
    }
  }
}
