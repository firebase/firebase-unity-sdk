/*
 * Copyright 2017 Google LLC
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

using System.Collections.Generic;

namespace Firebase {

// Contains extension methods for converting the SWIG generated Variant class
// into types needed by the public facing APIs.
internal static class VariantExtension {

  // Options to be used when converting Variant maps.
  internal enum KeyOptions {
    UseObjectKeys,  // When converting maps, keys will be of type object.
    UseStringKeys,  // When converting maps, force keys to be strings.
  }

  private const KeyOptions DefaultKeyOptions = KeyOptions.UseObjectKeys;

  // Converts a Variant to a generic C# object.
  public static object ToObject(this Variant variant) {
    return ToObject(variant, DefaultKeyOptions);
  }

  // Converts a Variant to a generic C# object.
  // This function holds a reference to variant and prevents the GC from collecting it during
  // ToObjectInternal() because
  // 1. Optimization in Xcode can cause the parent variant being dereference while the program is
  //    runnnig in deeper recursion level.
  // 2. Child variants do not hold a reference to its parent variant.
  // 3. Only the root variant owns the C++ instance, while all its descendant merely points to
  //    part of it ancestor's C++ instance.
  // As a result, without KeepAlive(), the root variant can be garbage collected during the
  // recursion, which causes crash or missing children.
  // TODO(b/122084709): Universally fix this issue
  public static object ToObject(this Variant variant, KeyOptions options) {
    object obj = ToObjectInternal(variant, options);
    global::System.GC.KeepAlive(variant);
    return obj;
  }

  // Internal recursive function to converts a Variant to a generic C# object.
  private static object ToObjectInternal(this Variant variant, KeyOptions options) {
    switch (variant.type()) {
      case Variant.Type.Int64:
        return variant.int64_value();

      case Variant.Type.Double:
        return variant.double_value();

      case Variant.Type.Bool:
        return variant.bool_value();

      case Variant.Type.StaticString:
      case Variant.Type.MutableString:
        return variant.string_value();

      case Variant.Type.Vector:
        var list = new List<object>();
        foreach (var v in variant.vector()) {
          list.Add(v.ToObjectInternal(options));
        }
        return list;

      case Variant.Type.Map:
        if (options == KeyOptions.UseStringKeys) {
          return ToStringVariantMap(variant.map(), options);
        } else {
          var dict = new Dictionary<object, object>();
          foreach (var kvp in variant.map()) {
            object copiedKey = kvp.Key.ToObjectInternal(options);
            object copiedValue = kvp.Value.ToObjectInternal(options);
            dict[copiedKey] = copiedValue;
          }
          return dict;
        }

      case Variant.Type.StaticBlob:
      case Variant.Type.MutableBlob:
        return variant.blob_as_bytes();

      case Variant.Type.Null:
      default:
        return null;
    }
  }

  // Converts a VariantVariantMap (generated from the C# map<Variant, Variant>)
  // into a Dictionary of string to object.
  public static IDictionary<string, object> ToStringVariantMap(
      this VariantVariantMap originalMap) {
    // Using object keys for child dictionaries is the old behavior, so we keep it.
    // But it probably makes more sense to use string keys for child maps as well.
    return ToStringVariantMap(originalMap, DefaultKeyOptions);
  }

  // Converts a VariantVariantMap (generated from the C# map<Variant, Variant>)
  // into a Dictionary of string to object.
  public static IDictionary<string, object> ToStringVariantMap(
      this VariantVariantMap originalMap, KeyOptions options) {
    Dictionary<string, object> result = new Dictionary<string, object>();
    foreach (KeyValuePair<Variant, Variant> kvp in originalMap) {
      string copiedKey;
      if (kvp.Key.is_string()) {
        copiedKey = kvp.Key.string_value();
      } else if (kvp.Key.is_fundamental_type()) {
        copiedKey = kvp.Key.AsString().string_value();
      } else {
        throw new System.InvalidCastException(
            "Unable to convert dictionary keys to string");
      }
      object copiedValue = kvp.Value.ToObject(options);
      result[copiedKey] = copiedValue;
    }
    return result;
  }
}

}  // namespace Firebase

