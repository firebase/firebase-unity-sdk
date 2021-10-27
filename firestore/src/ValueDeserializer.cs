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

using Firebase.Firestore.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using BclType = System.Type;

namespace Firebase.Firestore {
  // TODO: A lot of optimization of this and ValueSerializer, *after* writing comprehensive benchmarks.
  // We may be able to avoid a lot of boxing by a combination of making methods generic and building
  // delegates for serialization/deserialization of specific types using expression trees. The initial
  // aim is just to work out the functionality required, and optimize later.

  /// <summary>
  /// Provides conversions from FieldValueProxy values to .NET types.
  /// </summary>
  internal static class ValueDeserializer {
    /// <summary>
    /// Deserializes from a FieldValueProxy to a .NET type.
    /// </summary>
    /// <param name="context">The context for the deserialization operation. Never null.</param>
    /// <param name="value">The value to deserialize. Must not be null.</param>
    /// <param name="targetType">The target type. The method tries to convert to this type. If the type is
    /// object, it uses the default representation of the value.</param>
    /// <returns>The deserialized value</returns>
    internal static object Deserialize(DeserializationContext context, FieldValueProxy value, BclType targetType) {
      Preconditions.CheckNotNull(context, nameof(context));
      Preconditions.CheckNotNull(value, nameof(value));

      // If we're asked for a FieldValueProxy, just return it. Since it's immutable, no
      // cloning is necessary.
      if (targetType == typeof(FieldValueProxy)) {
        return value;
      }
      if (targetType == typeof(object)) {
        targetType = GetTargetType(value);
      }

      BclType underlyingType = Nullable.GetUnderlyingType(targetType);
      if (value.is_null()) {
        if (!targetType.IsValueType || underlyingType != null) {
          return null;
        } else {
          throw new ArgumentException(String.Format("Unable to convert null value to {0}", targetType.FullName));
        }
      }

      // We deserialize to T and Nullable<T> the same way for all non-null values. Use the converter
      // associated with the non-nullable version of the target type.
      BclType nonNullableTargetType = underlyingType ?? targetType;
      return context.GetConverter(nonNullableTargetType).DeserializeValue(context, value);
    }

    internal static object DeserializeMap(DeserializationContext context, FieldValueProxy mapValue, BclType targetType) {
      if (targetType == typeof(object)) {
        targetType = typeof(Dictionary<string, object>);
      }
      return context.GetConverter(targetType).DeserializeMap(context, mapValue);
    }

    private static BclType GetTargetType(FieldValueProxy value) {
      // TODO: Use an array instead?
      switch (value.type()) {
        case FieldValueProxy.Type.Null:
          // Any nullable type is fine here; we'll return null anyway.
          return typeof(string);
        case FieldValueProxy.Type.Array:
          return typeof(List<object>);
        case FieldValueProxy.Type.Boolean:
          return typeof(bool);
        case FieldValueProxy.Type.Blob:
          return typeof(Blob);
        case FieldValueProxy.Type.Double:
          return typeof(double);
        case FieldValueProxy.Type.GeoPoint:
          return typeof(GeoPoint);
        case FieldValueProxy.Type.Integer:
          return typeof(long);
        case FieldValueProxy.Type.Map:
          return typeof(Dictionary<string, object>);
        case FieldValueProxy.Type.Reference:
          return typeof(DocumentReference);
        case FieldValueProxy.Type.String:
          return typeof(string);
        case FieldValueProxy.Type.Timestamp:
          return typeof(Timestamp);
        default:
          throw new ArgumentException(String.Format("Unable to convert value type {0} to System.Object", value.type()));
      }
    }
  }
}
