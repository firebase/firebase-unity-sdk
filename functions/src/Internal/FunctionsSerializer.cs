/*
* Copyright 2026 Google LLC
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
using System.Collections;
using System.Collections.Generic;
using Google.MiniJSON;

namespace Firebase.Functions.Internal
{
  /// <summary>
  /// Helper functions to help encode and decode firebase functions data.
  /// A custom serializer is necessary on top of MiniJSON because Cloud Functions requires
  /// specific data formatting for its callable protocol. Specifically, it requires:
  /// 1. Request payloads to be wrapped in a `{"data": ...}` envelope.
  /// 2. Integers (longs) to be explicitly wrapped in `type.googleapis.com/google.protobuf.Int64Value` to avoid precision loss in JavaScript or generic JSON parsers.
  /// 3. Byte arrays (`byte[]`) to be wrapped in `type.googleapis.com/google.protobuf.BytesValue` and base64 encoded.
  /// 4. Response parsing to unwrap the `{"data": ...}` or `{"result": ...}` payload and parse standard error JSON formats into FunctionsException.
  /// </summary>
  internal static class FunctionsSerializer
  {
    /// <summary>
    /// Serializes an object into a JSON string suitable for the Cloud Functions callable protocol.
    /// This includes encoding specific types and wrapping the payload in a `{"data": ...}` envelope.
    /// </summary>
    internal static string Serialize(object data)
    {
      var encodedData = Encode(data);
      // The request body must be a JSON object with a "data" field.
      // See: https://firebase.google.com/docs/functions/callable-reference#request_body
      return Json.Serialize(new Dictionary<string, object>() { ["data"] = encodedData });
    }

    /// <summary>
    /// Recursively encodes objects, lists, and dictionaries into types supported by the Cloud Functions backend.
    /// Specifically encodes longs using WrapLong and byte arrays using WrapBytes.
    /// </summary>
    internal static object Encode(object obj)
    {
      switch (obj)
      {
        case null:
          return null;
        case string s:
          return s;
        case int _:
        case short _:
        case byte _:
        case sbyte _:
        case ushort _:
        case uint _:
        case long _:
          // Consistent with legacy Variant, wrap all integers as Int64Value.
          return WrapLong(Convert.ToInt64(obj));
        case float f:
          return f;
        case double d:
          return d;
        case bool b:
          return b;
        case byte[] blob:
          return WrapBytes(blob);
        case IList list:
          return EncodeList(list);
        case IDictionary dict:
          return EncodeDictionary(dict);
        default:
          throw new ArgumentException($"Invalid type {obj.GetType()} for encoding");
      }
    }

    private static List<object> EncodeList(IList list)
    {
      var newList = new List<object>();
      foreach (var item in list)
      {
        newList.Add(Encode(item));
      }
      return newList;
    }

    private static Dictionary<string, object> EncodeDictionary(IDictionary dict)
    {
      var newDict = new Dictionary<string, object>();
      foreach (DictionaryEntry entry in dict)
      {
        if (entry.Key == null)
        {
          throw new ArgumentException("Dictionary keys cannot be null");
        }
        newDict[entry.Key.ToString()] = Encode(entry.Value);
      }
      return newDict;
    }

    /// <summary>
    /// Wraps an integer (long) in `type.googleapis.com/google.protobuf.Int64Value`
    /// to avoid precision loss in JavaScript or generic JSON parsers.
    /// </summary>
    private static object WrapLong(long value)
    {
      return new Dictionary<string, object>
      {
        { "@type", "type.googleapis.com/google.protobuf.Int64Value" },
        { "value", value.ToString() }
      };
    }

    /// <summary>
    /// Wraps a byte array (`byte[]`) in `type.googleapis.com/google.protobuf.BytesValue`
    /// and base64 encodes the value.
    /// </summary>
    private static object WrapBytes(byte[] value)
    {
      return new Dictionary<string, object>
      {
        { "@type", "type.googleapis.com/google.protobuf.BytesValue" },
        { "value", Convert.ToBase64String(value) }
      };
    }

    /// <summary>
    /// Deserializes a parsed JSON string from the Cloud Functions callable protocol.
    /// Unwraps the `{"data": ...}` or `{"result": ...}` payload and handles parsing
    /// standard error formats into FunctionsException if present.
    /// </summary>
    internal static object Deserialize(string data)
    {
      var deserializedData = Json.Deserialize(data);

      if (deserializedData is Dictionary<string, object> dict)
      {
        if (dict.TryGetValue("error", out var errorValue) && errorValue is Dictionary<string, object> errorDict)
        {
          string message = "INTERNAL";
          if (errorDict.TryGetValue("message", out var msg) && msg is string s) message = s;

          FunctionsErrorCode code = FunctionsErrorCode.Internal;
          if (errorDict.TryGetValue("status", out var statusStr) && statusStr is string status)
          {
            code = FunctionsErrorParser.MapStatusStringToEnum(status);
          }

          throw new FunctionsException(code, message);
        }

        if (dict.TryGetValue("result", out var resultValue))
        {
          return Decode(resultValue);
        }

        if (dict.TryGetValue("data", out var dataValue))
        {
          return Decode(dataValue);
        }

        throw new FunctionsException(FunctionsErrorCode.Internal, "Response is missing data field.");
      }

      throw new FunctionsException(FunctionsErrorCode.Internal, "INTERNAL");
    }

    /// <summary>
    /// Recursively decodes a deserialized JSON object back into regular C# types.
    /// Specifically unwraps `Int64Value` and `BytesValue` Protocol Buffer formats.
    /// </summary>
    internal static object Decode(object obj)
    {
      if (obj is Dictionary<string, object> dict)
      {
        // Check for specific proto3 wrappers
        if (dict.TryGetValue("@type", out var typeObj) && typeObj is string type)
        {
          if (type == "type.googleapis.com/google.protobuf.Int64Value")
          {
            if (dict.TryGetValue("value", out var valueStr) && valueStr is string s)
            {
              if (long.TryParse(s, out long longValue))
              {
                return longValue;
              }
            }
          }
          else if (type == "type.googleapis.com/google.protobuf.BytesValue")
          {
            if (dict.TryGetValue("value", out var valueStr) && valueStr is string s)
            {
              try
              {
                return Convert.FromBase64String(s);
              }
              catch (FormatException)
              {
                // Fallback to returning dictionary if invalid base64
              }
            }
          }
        }

        // Recursively decode dictionary values
        var newDict = new Dictionary<string, object>();
        foreach (var kvp in dict)
        {
          newDict[kvp.Key] = Decode(kvp.Value);
        }
        return newDict;
      }
      else if (obj is List<object> list)
      {
        // Recursively decode list elements
        var newList = new List<object>();
        foreach (var item in list)
        {
          newList.Add(Decode(item));
        }
        return newList;
      }

      // Return primitives as-is
      return obj;
    }
  }
}