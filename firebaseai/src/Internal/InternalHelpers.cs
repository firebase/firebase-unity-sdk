/*
 * Copyright 2025 Google LLC
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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Firebase.AI.Internal {

// Include this to just shorthand it in this file
using JsonDict = Dictionary<string, object>;

// Options used by the extensions below to modify logic, mostly around throwing exceptions.
[Flags]
internal enum JsonParseOptions {
  None = 0,
  // Throw if the key is missing from the dictionary, useful for required fields.
  ThrowMissingKey = 1 << 0,
  // Throw if the key was found, but it failed to cast to the expected type.
  ThrowInvalidCast = 1 << 1,
  // Combination of the above.
  ThrowEverything = ThrowMissingKey | ThrowInvalidCast,
}

// Contains extension methods for commonly done logic.
internal static class FirebaseAIExtensions {

  // Tries to find the object with the key, and cast it to the type T
  public static bool TryParseValue<T>(this JsonDict jsonDict, string key,
      out T value, JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    if (jsonDict.TryGetValue(key, out object obj)) {
      if (obj is T tObj) {
        value = tObj;
        return true;
      } else if (obj is long asLong &&
          (typeof(T) == typeof(int) ||
           typeof(T) == typeof(float) ||
           typeof(T) == typeof(double))) {
        // MiniJson puts all ints as longs, so special case it
        // Also, if the number didn't have a decimal point, we still want to
        // allow it to be a float or double.
        value = (T)Convert.ChangeType(asLong, typeof(T));
        return true;
      } else if (typeof(T) == typeof(float) && obj is double asDouble) {
        // Similarly, floats are stored as doubles.
        value = (T)(object)(float)asDouble;
        return true;
      } else if (options.HasFlag(JsonParseOptions.ThrowInvalidCast)) {
        throw new InvalidCastException(
            $"Invalid JSON format: '{key}' is not of type '{typeof(T).Name}', " +
            $"Actual type: {obj.GetType().FullName}.");
      }
    } else if (options.HasFlag(JsonParseOptions.ThrowMissingKey)) {
      throw new KeyNotFoundException(
          $"Invalid JSON format: Unable to locate expected key {key}");
    }
    value = default;
    return false;
  }

  // Casts the found object to type T, otherwise returns default (or throws)
  public static T ParseValue<T>(this JsonDict jsonDict, string key,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
      T defaultValue = default) {
    if (TryParseValue(jsonDict, key, out T value, options)) {
      return value;
    } else {
      return defaultValue;
    }
  }

  // Casts the found object to type T, otherwise returns null (or throws)
  public static T? ParseNullableValue<T>(this JsonDict jsonDict, string key,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct {
    if (TryParseValue(jsonDict, key, out T value, options)) {
      return value;
    } else {
      return null;
    }
  }

  // Tries to convert the string found with the key to an enum, using the given function.
  public static bool TryParseEnum<T>(this JsonDict jsonDict, string key,
      Func<string, T> parseFunc, out T value,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    if (jsonDict.TryParseValue(key, out string enumStr, options)) {
      value = parseFunc(enumStr);
      return true;
    } else {
      value = default;
      return false;
    }
  }

  // Casts the found string to an enum, otherwise returns default (or throws)
  public static T ParseEnum<T>(this JsonDict jsonDict, string key,
      Func<string, T> parseFunc,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
      T defaultValue = default) {
    if (TryParseEnum(jsonDict, key, parseFunc, out T value, options)) {
      return value;
    } else {
      return defaultValue;
    }
  }

  // Casts the found string to an enum, otherwise returns null (or throws)
  public static T? ParseNullableEnum<T>(this JsonDict jsonDict, string key,
      Func<string, T> parseFunc,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct, Enum {
    if (TryParseEnum(jsonDict, key, parseFunc, out T value, options)) {
      return value;
    } else {
      return null;
    }
  }

  // Tries to convert the given Dictionary found at the key with the given function.
  public static bool TryParseObject<T>(this JsonDict jsonDict, string key,
      Func<JsonDict, T> parseFunc, out T value,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    if (TryParseValue(jsonDict, key, out JsonDict innerDict, options)) {
      value = parseFunc(innerDict);
      return true;
    } else {
      value = default;
      return false;
    }
  }

  // Casts the found Dictionary to an object, otherwise returns default (or throws)
  public static T ParseObject<T>(this JsonDict jsonDict, string key,
      Func<JsonDict, T> parseFunc,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast,
      T defaultValue = default) {
    if (TryParseObject(jsonDict, key, parseFunc, out T value, options)) {
      return value;
    } else {
      return defaultValue;
    }
  }

  // Casts the found Dictionary to an object, otherwise returns null (or throws)
  public static T? ParseNullableObject<T>(this JsonDict jsonDict, string key,
      Func<JsonDict, T> parseFunc,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) where T : struct {
    if (TryParseObject(jsonDict, key, parseFunc, out T value, options)) {
      return value;
    } else {
      return null;
    }
  }

  // Tries to convert the found List of objects to a List of strings.
  public static bool TryParseStringList(this JsonDict jsonDict, string key,
      out List<string> values,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    if (jsonDict.TryParseValue(key, out List<object> list, options)) {
      values = list.OfType<string>().ToList();
      return true;
    } else {
      values = null;
      return false;
    }
  }

  // Casts the found List to a string List, otherwise returns default (or throws)
  public static List<string> ParseStringList(this JsonDict jsonDict, string key,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    TryParseStringList(jsonDict, key, out List<string> values, options);
    return values;
  }

  // Tries to convert the found List of Dictionaries, using the given function.
  public static bool TryParseObjectList<T>(this JsonDict jsonDict, string key,
      Func<JsonDict, T> parseFunc, out List<T> values,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    if (jsonDict.TryParseValue(key, out List<object> list, options)) {
      values = list.ConvertJsonList(parseFunc);
      return true;
    } else {
      values = null;
      return false;
    }
  }

  // Casts the found List to an object List, otherwise returns default (or throws)
  public static List<T> ParseObjectList<T>(this JsonDict jsonDict, string key,
      Func<JsonDict, T> parseFunc,
      JsonParseOptions options = JsonParseOptions.ThrowInvalidCast) {
    TryParseObjectList(jsonDict, key, parseFunc, out List<T> values, options);
    return values;
  }

  // Converts the given List of Dictionaries into a list of T, using the converter.
  public static List<T> ConvertJsonList<T>(this List<object> list,
      Func<JsonDict, T> converter) {
    return list
        .Select(o => o as JsonDict)
        .Where(dict => dict != null)
        .Select(converter)
        .ToList();
  }

  public static ModelContent ConvertRole(this ModelContent content, string role) {
    if (content.Role == role) {
      return content;
    } else {
      return new ModelContent(role, content.Parts);
    }
  }

  public static ModelContent ConvertToUser(this ModelContent content) {
    return content.ConvertRole("user");
  }

  public static ModelContent ConvertToModel(this ModelContent content) {
    return content.ConvertRole("model");
  }

  public static ModelContent ConvertToSystem(this ModelContent content) {
    return content.ConvertRole("system");
  }
  
  public static void AddIfHasValue<T>(this JsonDict jsonDict, string key,
      T? value) where T : struct {
    if (value.HasValue) {
      jsonDict.Add(key, value.Value);
    }
  }
  
  public static void AddIfHasValue<T>(this JsonDict jsonDict, string key,
      T value) where T : class {
    if (value != null) {
      jsonDict.Add(key, value);
    }
  }
}
  
}
