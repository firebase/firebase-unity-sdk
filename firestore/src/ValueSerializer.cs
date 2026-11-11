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

namespace Firebase.Firestore {
  // TODO: Serialize some other types, e.g. Guid?
  // TODO: Protect against stack overflows?
  // TODO: Deliberate pass through of Value to Value (and maps/collections) to make it easier to plug in other mappers?

  /// <summary>
  /// Provides conversions from .NET types to <see cref="FirestoreValue"/>.
  /// </summary>
  internal static class ValueSerializer {
    /// <summary>
    /// Serializes a single input to a Value.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The <see cref="FirestoreValue"/> representation.</returns>
    internal static FirestoreValue Serialize(SerializationContext context, object value) {
      if (value == null) {
        return FirestoreValue.Null;
      }
      if (value is FirestoreValue fv) {
        return fv;
      }
      // Fast-path common primitives without dictionary lookup:
      switch (value) {
        case string s:
          return FirestoreValue.FromString(s);
        case int i:
          return FirestoreValue.FromInteger(i);
        case long l:
          return FirestoreValue.FromInteger(l);
        case double d:
          return FirestoreValue.FromDouble(d);
        case bool b:
          return FirestoreValue.FromBoolean(b);
        case float f:
          return FirestoreValue.FromDouble(f);
        case Timestamp ts:
          return FirestoreValue.FromTimestamp(ts);
      }
      return context.GetConverter(value.GetType()).Serialize(context, value);
    }

    /// <summary>
    /// Serializes a map-based input to a dictionary of fields to values.
    /// This is effectively the map-only part of <see cref="Serialize"/>, but without wrapping the
    /// result in a Value.
    /// </summary>
    internal static Dictionary<string, FirestoreValue> SerializeMap(
        SerializationContext context, object value) {
      Preconditions.CheckNotNull(value, nameof(value));
      var map = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      context.GetConverter(value.GetType()).SerializeMap(context, value, map);
      return map;
    }
  }
}
