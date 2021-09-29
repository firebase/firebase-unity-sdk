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

using Firebase.Firestore.Converters;
using Firebase.Firestore.Internal;
using System.Collections.Generic;

namespace Firebase.Firestore {
  // TODO: Serialize some other types, e.g. Guid?
  // TODO: Protect against stack overflows?
  // TODO: Deliberate pass through of Value to Value (and maps/collections) to make it easier to plug in other mappers?

  /// <summary>
  /// Provides conversions from .NET types to FieldValueProxy.
  /// </summary>
  internal static class ValueSerializer {
    /// <summary>
    /// Serializes a single input to a Value.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="value">The value to serialize.</param>
    /// <returns>The FieldValueProxy representation.</returns>
    internal static FieldValueProxy Serialize(SerializationContext context, object value) {
      if (value == null) {
        return FieldValueProxy.Null();
      }
      return context.GetConverter(value.GetType()).Serialize(context, value);
    }

    /// <summary>
    /// Serializes a map-based input to a dictionary of fields to values.
    /// This is effectively the map-only part of <see cref="Serialize"/>, but without wrapping the
    /// result in a Value.
    /// </summary>
    internal static Dictionary<string, FieldValueProxy> SerializeMap(SerializationContext context, object value) {
      Preconditions.CheckNotNull(value, nameof(value));
      var map = new Dictionary<string, FieldValueProxy>();
      context.GetConverter(value.GetType()).SerializeMap(context, value, map);
      return map;
    }
  }
}
