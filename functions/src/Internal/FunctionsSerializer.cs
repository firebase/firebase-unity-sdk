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
using System.Text;
using System.Threading;
using Google.MiniJSON;

namespace Firebase.Functions.Internal
{
  // Helper functions to help encode and decode firebase functions data.
  internal static class FunctionsSerializer
  {
    internal static string Serialize(object data)
    {
      var encodedData = Encode(data);
      // The request body must be a JSON object with a "data" field.
      // See: https://firebase.google.com/docs/functions/callable-reference#request_body
      return Json.Serialize(new Dictionary<string, object>() { ["data"] = encodedData });
    }

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
        newDict[entry.Key.ToString()] = Encode(entry.Value);
      }
      return newDict;
    }

    private static object WrapLong(long value)
    {
      return new Dictionary<string, object>
      {
        { "@type", "type.googleapis.com/google.protobuf.Int64Value" },
        { "value", value.ToString() }
      };
    }

    private static object WrapBytes(byte[] value)
    {
      return new Dictionary<string, object>
      {
        { "@type", "type.googleapis.com/google.protobuf.BytesValue" },
        { "value", Convert.ToBase64String(value) }
      };
    }

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