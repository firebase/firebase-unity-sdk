/*
 * Copyright 2018 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using  System.Collections.Generic;

namespace Firebase.Database.Internal {
internal static class Utilities {
  // Wrapper around Variant.FromObject.
  // It wraps the ArgumentException that Variant.FromObject can throw into a DatabaseException.
  internal static Variant MakeVariant(object value) {
    try {
      return Variant.FromObject(value);
    } catch (System.ArgumentException e) {
      throw new DatabaseException("Failed to parse object " + value, e);
    }
  }

  // Wrapper around Variant.FromObject.
  // This will throw a DatabaseException if the value is not a valid type for priorities.
  internal static Variant MakePriorityVariant(object value) {
    Variant v = MakeVariant(value);
    switch (v.type()) {
      case Variant.Type.Int64:
        return Variant.FromDouble(System.Convert.ToDouble(v.int64_value()));
      case Variant.Type.Null:
      case Variant.Type.Double:
      case Variant.Type.StaticString:
      case Variant.Type.MutableString:
        return v;
      case Variant.Type.Map:
        // Check whether this is a "ServerValue.Timestamp", which are permitted.
        var map = v.map();
        if (map.Count == 1) {
          // We use a foreach loop to check the single element in the map because Microsoft
          // recommends using "foreach" instead of using the enumerator directly.
          foreach (KeyValuePair<Variant,Variant> item in map) {
            if (item.Key.is_string() && item.Key.string_value() == ".sv" &&
                item.Value.is_string() && item.Value.string_value() == "timestamp") {
              // This is a "ServerValue.Timestamp".
              return v;
            }
          }
        }
        // For other maps, break and throw the exception below.
        break;
      default:
        // Nothing here, throw an exception below.
        break;
    }
    throw new DatabaseException(
        "Invalid Firebase Database priority (must be a string, double, ServerValue, or null)");
  }
}
}
