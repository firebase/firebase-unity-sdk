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
      return Json.Serialize(new Dictionary<string, object>() { ["data"] = encodedData });
    }

    internal static object Encode(object obj)
    {
      if (obj is long longValue)
      {
        // Wrap Int64 in special protobuf object
        return new Dictionary<string, object>
        {
      { "@type", "type.googleapis.com/google.protobuf.Int64Value" },
      { "value", longValue.ToString() }
    };
      }
      else if (obj is IDictionary dict)
      {
        // Recursively encode dictionary values
        var newDict = new Dictionary<string, object>();
        foreach (DictionaryEntry entry in dict)
        {
          newDict[entry.Key.ToString()] = Encode(entry.Value);
        }
        return newDict;
      }
      else if (obj is IEnumerable list && !(obj is string))
      {
        // Recursively encode enumerable elements
        var newList = new List<object>();
        foreach (var item in list)
        {
          newList.Add(Encode(item));
        }
        return newList;
      }

      // Return other primitives (string, bool, double) as-is
      return obj;
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
        // Check for the Int64 signature
        if (dict.ContainsKey("@type") &&
          dict["@type"] as string == "type.googleapis.com/google.protobuf.Int64Value")
          {
          if (dict.TryGetValue("value", out var valueStr) && valueStr is string s)
          {
            if (long.TryParse(s, out long longValue))
            {
              return longValue;
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