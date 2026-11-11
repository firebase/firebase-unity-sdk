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
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Google.MiniJSON;

namespace Firebase {

#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
internal static class AppConfigLoader {
  private static readonly string[] DefaultConfigFileNames = {
    "google-services-desktop.json",
    "google-services.json",
    "GoogleService-Info.plist",
  };

  private static volatile string _customConfigPath;
  private static volatile string _streamingAssetsPath;
  private static volatile string _dataPath;
  private static volatile string _bundleIdentifier;

  internal static void SetDefaultConfigPath(string path) =>
      _customConfigPath = string.IsNullOrEmpty(path) ? null : path;

  internal static void TryWarmUnityPathsOnMainThread() {
    try {
      ReadUnityPathsCore();
    } catch (Exception) {
      // Running off the Unity main thread or in a headless .NET process without UnityEngine ECalls.
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void ReadUnityPathsCore() {
    _streamingAssetsPath = UnityEngine.Application.streamingAssetsPath;
    _dataPath = UnityEngine.Application.dataPath;
    _bundleIdentifier = UnityEngine.Application.identifier;
  }

  internal static void SetUnityPathsForTesting(string streamingAssets, string dataPath, string bundleId = null) {
    _streamingAssetsPath = streamingAssets;
    _dataPath = dataPath;
    _bundleIdentifier = bundleId;
  }

  internal static AppOptions TryLoadDefaultOptions() {
    if (_customConfigPath != null) return TryLoadFromDirectory(_customConfigPath, _bundleIdentifier);
    AppOptions mobileOptions = MobileAppBridge.TryLoadDefaultOptions();
    if (mobileOptions != null) return mobileOptions;

    if (_streamingAssetsPath == null && _dataPath == null && Platform.FirebaseHandler.DefaultInstance?.IsMainThread() == true) {
      TryWarmUnityPathsOnMainThread();
    }

    string preferredPkg = _bundleIdentifier;
    if (!string.IsNullOrEmpty(_streamingAssetsPath)) {
      AppOptions fromStreaming = TryLoadFromDirectory(_streamingAssetsPath, preferredPkg);
      if (fromStreaming != null) return fromStreaming;
    }
    if (!string.IsNullOrEmpty(_dataPath)) {
      AppOptions fromData = TryLoadFromDirectory(_dataPath, preferredPkg)
          ?? TrySearchDataSubdirectories(_dataPath, preferredPkg, maxDepth: 4);
      if (fromData != null) return fromData;
    }
    return TryLoadSafe(Directory.GetCurrentDirectory, preferredPkg)
        ?? TryLoadSafe(() => AppDomain.CurrentDomain.BaseDirectory, preferredPkg);
  }

  private static AppOptions TryLoadSafe(Func<string> dirProvider, string preferredPkg) {
    try {
      return TryLoadFromDirectory(dirProvider(), preferredPkg);
    } catch (Exception) {
      return null;
    }
  }

  internal static AppOptions TryLoadFromDirectory(string directory, string preferredPackageName = null) {
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
    foreach (string fileName in DefaultConfigFileNames) {
      AppOptions loaded = TryLoadFromFile(Path.Combine(directory, fileName), preferredPackageName);
      if (loaded != null) return loaded;
    }
    return null;
  }

  internal static AppOptions TryLoadFromFile(string path, string preferredPackageName = null) {
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
    try {
      var info = new FileInfo(path);
      if (info.Length <= 0 || info.Length > 512 * 1024) return null;
      string content = File.ReadAllText(path, Encoding.UTF8);
      return path.EndsWith(".plist", StringComparison.OrdinalIgnoreCase)
          ? LoadFromPlistConfig(content)
          : LoadFromJsonConfig(content, preferredPackageName);
    } catch (Exception) {
      return null;
    }
  }

  private static AppOptions TrySearchDataSubdirectories(string rootDir, string preferredPkg, int maxDepth) {
    if (maxDepth <= 0 || !Directory.Exists(rootDir)) return null;
    var queue = new Queue<(string Dir, int Depth)>();
    queue.Enqueue((rootDir, 0));
    while (queue.Count > 0) {
      var (dir, depth) = queue.Dequeue();
      if (depth >= maxDepth) continue;
      string[] subdirs;
      try {
        subdirs = Directory.GetDirectories(dir);
      } catch (Exception) {
        continue;
      }
      Array.Sort(subdirs, StringComparer.Ordinal);
      foreach (string sub in subdirs) {
        string name = Path.GetFileName(sub);
        if (string.IsNullOrEmpty(name) || name[0] == '.' ||
            name.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Obj", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Logs", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        AppOptions found = TryLoadFromDirectory(sub, preferredPkg);
        if (found != null) return found;
        queue.Enqueue((sub, depth + 1));
      }
    }
    return null;
  }

  internal static AppOptions LoadFromJsonConfig(string jsonConfig, string preferredPackageName = null) {
    if (string.IsNullOrWhiteSpace(jsonConfig)) return null;
    Dictionary<string, object> root;
    try {
      root = Json.Deserialize(jsonConfig.TrimStart('\uFEFF')) as Dictionary<string, object>;
    } catch (Exception) {
      return null;
    }
    var projectInfo = GetMap(root, "project_info");
    var clients = root != null && root.TryGetValue("client", out object cObj) ? cObj as List<object> : null;
    if (projectInfo == null || clients == null || clients.Count == 0) {
      LogUtil.LogMessage(LogLevel.Error, "Failed to parse Firebase JSON config: missing 'project_info' or 'client'.");
      return null;
    }

    Dictionary<string, object> selectedClient = SelectClient(clients, preferredPackageName);
    var clientInfo = GetMap(selectedClient, "client_info");
    if (clientInfo == null) {
      LogUtil.LogMessage(LogLevel.Error, "Failed to parse Firebase JSON config: missing 'client_info'.");
      return null;
    }

    var options = new AppOptions {
      DatabaseUrl = FirebaseApp.UrlStringToUri(GetString(projectInfo, "firebase_url")),
      MessageSenderId = GetString(projectInfo, "project_number"),
      StorageBucket = GetString(projectInfo, "storage_bucket"),
      ProjectId = GetString(projectInfo, "project_id"),
      AppId = GetString(clientInfo, "mobilesdk_app_id"),
      PackageName = GetString(GetMap(clientInfo, "android_client_info"), "package_name"),
    };
    if (selectedClient.TryGetValue("api_key", out object apiKeysObj) && apiKeysObj is List<object> apiKeys) {
      foreach (object item in apiKeys) {
        if (item is Dictionary<string, object> keyMap && !string.IsNullOrEmpty(GetString(keyMap, "current_key"))) {
          options.ApiKey = GetString(keyMap, "current_key");
          break;
        }
      }
    }
    return options;
  }

  private static Dictionary<string, object> SelectClient(List<object> clients, string preferredPackageName) {
    Dictionary<string, object> firstWithPackage = null, firstWithClientInfo = null;
    foreach (object entry in clients) {
      if (!(entry is Dictionary<string, object> client)) continue;
      var clientInfo = GetMap(client, "client_info");
      if (clientInfo == null) continue;
      firstWithClientInfo ??= client;
      string pkg = GetString(GetMap(clientInfo, "android_client_info"), "package_name");
      if (!string.IsNullOrEmpty(pkg)) {
        firstWithPackage ??= client;
        if (!string.IsNullOrEmpty(preferredPackageName) && string.Equals(pkg, preferredPackageName, StringComparison.Ordinal)) {
          return client;
        }
      }
    }
    return firstWithPackage ?? firstWithClientInfo;
  }

  internal static AppOptions LoadFromPlistConfig(string plistConfig) {
    if (string.IsNullOrWhiteSpace(plistConfig)) return null;
    string trimmed = plistConfig.TrimStart('\uFEFF').Trim();
    Dictionary<string, string> entries = trimmed.StartsWith("<", StringComparison.Ordinal)
        ? ParseXmlPlistTopDict(trimmed)
        : ParseOpenStepPlistTopDict(trimmed);
    if (entries == null || entries.Count == 0) return null;

    var options = new AppOptions();
    bool any = false;
    if (TryGetNonEmpty(entries, "GOOGLE_APP_ID", out string appId)) { options.AppId = appId; any = true; }
    if (TryGetNonEmpty(entries, "API_KEY", out string apiKey)) { options.ApiKey = apiKey; any = true; }
    if (TryGetNonEmpty(entries, "PROJECT_ID", out string projId)) { options.ProjectId = projId; any = true; }
    if (TryGetNonEmpty(entries, "STORAGE_BUCKET", out string bucket)) { options.StorageBucket = bucket; any = true; }
    if (TryGetNonEmpty(entries, "GCM_SENDER_ID", out string sender)) { options.MessageSenderId = sender; any = true; }
    if (TryGetNonEmpty(entries, "DATABASE_URL", out string dbUrl)) { options.DatabaseUrl = FirebaseApp.UrlStringToUri(dbUrl); any = true; }
    if (TryGetNonEmpty(entries, "BUNDLE_ID", out string bundleId)) { options.PackageName = bundleId; any = true; }
    return any ? options : null;
  }

  private static Dictionary<string, string> ParseXmlPlistTopDict(string xml) {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    int i = 0, n = xml.Length, dictDepth = 0, arrayDepth = 0;
    string pendingKey = null;
    while (i < n) {
      int lt = xml.IndexOf('<', i);
      if (lt < 0 || lt + 1 >= n) break;
      if (StartsAt(xml, lt, "<!--")) {
        int end = xml.IndexOf("-->", lt + 4, StringComparison.Ordinal);
        if (end < 0) break;
        i = end + 3;
        continue;
      }
      if (xml[lt + 1] == '?' || xml[lt + 1] == '!') {
        int gtSkip = xml.IndexOf('>', lt + 2);
        if (gtSkip < 0) break;
        i = gtSkip + 1;
        continue;
      }
      int gt = xml.IndexOf('>', lt + 1);
      if (gt < 0) break;
      string rawTag = xml.Substring(lt + 1, gt - lt - 1).Trim();
      bool closing = rawTag.Length > 0 && rawTag[0] == '/';
      bool selfClose = !closing && rawTag.Length > 0 && rawTag[rawTag.Length - 1] == '/';
      string tag = (closing ? rawTag.Substring(1) : selfClose ? rawTag.Substring(0, rawTag.Length - 1) : rawTag).Trim();
      int sp = tag.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
      if (sp >= 0) tag = tag.Substring(0, sp);
      i = gt + 1;

      if (tag == "dict") {
        if (closing) { if (dictDepth == 2 && arrayDepth == 0) pendingKey = null; dictDepth--; }
        else if (!selfClose) dictDepth++;
        else if (dictDepth == 1 && arrayDepth == 0) pendingKey = null;
      } else if (tag == "array") {
        if (closing) { if (dictDepth == 1 && arrayDepth == 1) pendingKey = null; arrayDepth--; }
        else if (!selfClose) arrayDepth++;
        else if (dictDepth == 1 && arrayDepth == 0) pendingKey = null;
      } else if (dictDepth == 1 && arrayDepth == 0 && !closing) {
        if (tag == "key") pendingKey = selfClose ? string.Empty : ReadXmlText(xml, ref i, "key");
        else if (pendingKey != null) {
          result[pendingKey] = tag == "true" ? "true" : tag == "false" ? "false" : selfClose ? string.Empty : ReadXmlText(xml, ref i, tag);
          pendingKey = null;
        }
      }
    }
    return result;
  }

  private static string ReadXmlText(string xml, ref int pos, string tag) {
    string endTag = "</" + tag + ">";
    int closeIdx = xml.IndexOf(endTag, pos, StringComparison.Ordinal);
    if (closeIdx < 0) { pos = xml.Length; return string.Empty; }
    string raw = xml.Substring(pos, closeIdx - pos);
    pos = closeIdx + endTag.Length;
    if (raw.IndexOf('<') < 0 && raw.IndexOf('&') < 0) return raw;
    var sb = new StringBuilder(raw.Length);
    for (int i = 0; i < raw.Length;) {
      if (StartsAt(raw, i, "<![CDATA[")) {
        int end = raw.IndexOf("]]>", i + 9, StringComparison.Ordinal);
        if (end < 0) { sb.Append(raw, i + 9, raw.Length - i - 9); break; }
        sb.Append(raw, i + 9, end - i - 9);
        i = end + 3;
      } else if (raw[i] == '&') {
        int semi = raw.IndexOf(';', i + 1);
        if (semi <= i) { sb.Append(raw[i++]); continue; }
        string ent = raw.Substring(i + 1, semi - i - 1);
        sb.Append(ent switch {
          "amp" => "&", "lt" => "<", "gt" => ">", "quot" => "\"", "apos" => "'",
          _ when ent.Length > 2 && ent[0] == '#' && (ent[1] == 'x' || ent[1] == 'X') &&
                 int.TryParse(ent.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hx) => char.ConvertFromUtf32(hx),
          _ when ent.Length > 1 && ent[0] == '#' &&
                 int.TryParse(ent.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int dc) => char.ConvertFromUtf32(dc),
          _ => "&" + ent + ";",
        });
        i = semi + 1;
      } else sb.Append(raw[i++]);
    }
    return sb.ToString();
  }

  private static Dictionary<string, string> ParseOpenStepPlistTopDict(string text) {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    int i = 0, n = text.Length;
    SkipOpenStepTrivia(text, ref i);
    bool hasBraces = i < n && text[i] == '{';
    if (hasBraces) i++;
    while (i < n) {
      SkipOpenStepTrivia(text, ref i);
      if (i >= n || (hasBraces && text[i] == '}')) break;
      string key = ReadOpenStepScalar(text, ref i);
      if (key == null) break;
      SkipOpenStepTrivia(text, ref i);
      if (i >= n || text[i] != '=') break;
      i++;
      SkipOpenStepTrivia(text, ref i);
      if (i < n && (text[i] == '{' || text[i] == '(')) {
        SkipBalancedGroup(text, ref i, text[i], text[i] == '{' ? '}' : ')');
      } else {
        string val = ReadOpenStepScalar(text, ref i);
        if (val != null) result[key] = val;
      }
      SkipOpenStepTrivia(text, ref i);
      if (i < n && (text[i] == ';' || text[i] == ',')) i++;
    }
    return result;
  }

  private static void SkipOpenStepTrivia(string s, ref int i) {
    while (i < s.Length) {
      if (char.IsWhiteSpace(s[i])) { i++; continue; }
      if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; }
      else if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*') {
        int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
        i = end < 0 ? s.Length : end + 2;
      } else break;
    }
  }

  private static void SkipBalancedGroup(string s, ref int i, char open, char close) {
    int depth = 0;
    while (i < s.Length) {
      SkipOpenStepTrivia(s, ref i);
      if (i >= s.Length) break;
      if (s[i] == '"' || s[i] == '\'') { ReadOpenStepScalar(s, ref i); continue; }
      if (s[i] == open) depth++;
      else if (s[i] == close && --depth == 0) { i++; return; }
      i++;
    }
  }

  private static string ReadOpenStepScalar(string s, ref int i) {
    if (i >= s.Length) return null;
    if (s[i] == '"' || s[i] == '\'') {
      char quote = s[i++];
      var sb = new StringBuilder();
      while (i < s.Length && s[i] != quote) {
        if (s[i] == '\\' && i + 1 < s.Length) {
          char esc = s[++i];
          sb.Append(esc switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => esc });
          i++;
        } else sb.Append(s[i++]);
      }
      if (i < s.Length && s[i] == quote) i++;
      return sb.ToString();
    }
    int start = i;
    while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '=' && s[i] != ';' && s[i] != ',' && s[i] != '}' && s[i] != ')') i++;
    return i > start ? s.Substring(start, i - start) : null;
  }

  private static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key) =>
      map != null && map.TryGetValue(key, out object v) ? v as Dictionary<string, object> : null;

  private static string GetString(Dictionary<string, object> map, string key) =>
      map == null || !map.TryGetValue(key, out object v) || v == null ? null
      : (v is string s ? s : Convert.ToString(v, CultureInfo.InvariantCulture));

  private static bool TryGetNonEmpty(Dictionary<string, string> map, string key, out string value) {
    if (map.TryGetValue(key, out value) && !string.IsNullOrEmpty(value)) return true;
    value = null;
    return false;
  }

  private static bool StartsAt(string s, int index, string pattern) =>
      index >= 0 && index + pattern.Length <= s.Length &&
      string.CompareOrdinal(s, index, pattern, 0, pattern.Length) == 0;
}

}  // namespace Firebase
