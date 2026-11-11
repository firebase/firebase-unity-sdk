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
using System.IO;
using System.Text;

namespace Firebase.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class AtomicFileStore {
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly object WriteLock = new();
    private static volatile string _persistentDataPath = null;
    private static AtomicFileStore _default;

    internal static AtomicFileStore Default => System.Threading.LazyInitializer.EnsureInitialized(ref _default, () => new AtomicFileStore());
    internal string RootDirectory { get; }
    internal AtomicFileStore(string rootDirectory = null) =>
        RootDirectory = Path.GetFullPath(string.IsNullOrEmpty(rootDirectory) ? ResolveDefaultRoot() : rootDirectory);
    internal bool Exists(string key) => File.Exists(ResolvePath(key));
#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnRuntimeInit() => WarmPersistentDataPath();
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void OnEditorInit() => WarmPersistentDataPath();
#endif
#if UNITY_5_3_OR_NEWER || UNITY_EDITOR
    private static void WarmPersistentDataPath() {
      try { _persistentDataPath = UnityEngine.Application.persistentDataPath; }
      catch (Exception) {
        // Engine path unavailable in headless context.
      }
    }
#endif
    internal string ReadAllText(string key) {
      try {
        using var stream = new FileStream(ResolvePath(key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
      } catch (FileNotFoundException) { return null; }
      catch (DirectoryNotFoundException) { return null; }
      catch (IOException) when (!File.Exists(ResolvePath(key))) { return null; }
    }

    internal void WriteAllText(string key, string content) {
      string path = ResolvePath(key), dir = Path.GetDirectoryName(path), tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
      if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
      try {
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
          byte[] bytes = Utf8NoBom.GetBytes(content ?? string.Empty);
          fs.Write(bytes, 0, bytes.Length);
          fs.Flush(true);
        }
        lock (WriteLock) {
          if (!File.Exists(path)) File.Move(tempPath, path);
          else { try { File.Replace(tempPath, path, null); } catch (Exception) { File.Delete(path); File.Move(tempPath, path); } }
        }
      } finally {
        if (File.Exists(tempPath)) {
          try { File.Delete(tempPath); }
          catch (Exception) {
            // Best-effort cleanup of orphaned temp file.
          }
        }
      }
    }

    internal void Delete(string key) {
      string path = ResolvePath(key);
      lock (WriteLock) if (File.Exists(path)) File.Delete(path);
    }
    private string ResolvePath(string key) {
      if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key cannot be empty.", nameof(key));
      string full = Path.GetFullPath(Path.Combine(RootDirectory, key));
      string prefix = RootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? RootDirectory : RootDirectory + Path.DirectorySeparatorChar;
      return full.StartsWith(prefix, StringComparison.Ordinal) ? full : throw new ArgumentException("Key escapes root.", nameof(key));
    }
    private static string ResolveDefaultRoot() {
      if (_persistentDataPath is string cached && cached.Length > 0) return Path.Combine(cached, "firebase");
#if UNITY_5_3_OR_NEWER
      if (Firebase.Platform.FirebaseHandler.DefaultInstance != null) {
        try {
          if (Firebase.Platform.FirebaseHandler.RunOnMainThread(() => UnityEngine.Application.persistentDataPath) is string p && p.Length > 0) return Path.Combine(_persistentDataPath = p, "firebase");
        } catch (Exception) {
          // Headless Mono fallback below.
        }
      }
#endif
      string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
      return Path.Combine(string.IsNullOrEmpty(appData) ? Path.GetTempPath() : appData, "firebase");
    }
  }
}
