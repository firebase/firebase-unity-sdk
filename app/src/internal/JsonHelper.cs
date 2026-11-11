// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using Google.MiniJSON;

namespace Firebase.Internal {
  internal static class JsonHelper {
    internal static object Deserialize(string json) => string.IsNullOrEmpty(json) ? null : Json.Deserialize(json);
    internal static Dictionary<string, object> ParseObject(string json) => Deserialize(json) as Dictionary<string, object>;
    internal static string Serialize(object value) => Json.Serialize(value);
    internal static Dictionary<string, object> GetMap(IDictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;
    internal static List<object> GetList(IDictionary<string, object> map, string key) =>
        map != null && map.TryGetValue(key, out object v) ? v as List<object> : null;
    internal static string GetString(IDictionary<string, object> map, string key, string defaultValue = null) =>
        map == null || !map.TryGetValue(key, out object v) || v == null ? defaultValue
        : (v is string s ? s : (v is IConvertible c ? c.ToString(CultureInfo.InvariantCulture) : v.ToString()));
    internal static bool GetBool(IDictionary<string, object> map, string key, bool defaultValue = false) =>
        map == null || !map.TryGetValue(key, out object v) || v == null ? defaultValue
        : (v is bool b ? b : (v is string s && bool.TryParse(s, out bool p) ? p : defaultValue));
    internal static long GetLong(IDictionary<string, object> map, string key, long defaultValue = 0) =>
        map == null || !map.TryGetValue(key, out object v) || v == null ? defaultValue : v switch {
          long l => l, int i => i, double d => (long)d,
          string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long p) => p,
          _ => defaultValue,
        };
    internal static double GetDouble(IDictionary<string, object> map, string key, double defaultValue = 0.0) =>
        map == null || !map.TryGetValue(key, out object v) || v == null ? defaultValue : v switch {
          double d => d, float f => f, long l => l, int i => i,
          string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) => p,
          _ => defaultValue,
        };
  }
}
