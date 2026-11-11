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

namespace Firebase.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class FirebaseInstanceCache {
    private static readonly List<Action> _resetHooks = new();
    internal static void RegisterResetHook(Action reset) { lock (_resetHooks) _resetHooks.Add(reset); }
    internal static void ClearAllForReload() {
      Action[] hooks;
      lock (_resetHooks) hooks = _resetHooks.ToArray();
      foreach (Action h in hooks) h();
    }
#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() => ClearAllForReload();
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorHooks() {
      UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
      UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }
    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange s) {
      if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) ClearAllForReload();
    }
#endif
  }

  // Thread-safe (app.Name, subKey) cache. Lock order: FirebaseApp locks -> _lock (leaf lock).
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class FirebaseInstanceCache<T> where T : class {
    private sealed class Entry { internal FirebaseApp App; internal T Instance; internal EventHandler Handler; internal bool Disposed; }
    private readonly object _lock = new();
    private readonly Dictionary<(string, string), Entry> _entries = new();
    private readonly Action<T> _onDisposed;
    private long _epoch;

    internal FirebaseInstanceCache(Action<T> onDisposed = null) { _onDisposed = onDisposed; FirebaseInstanceCache.RegisterResetHook(ClearForReload); }
    internal static void ClearAllForReload() => FirebaseInstanceCache.ClearAllForReload();
    internal T GetOrCreate(FirebaseApp app, Func<T> factory, string subKey = "") {
      if (app == null) throw new ArgumentNullException(nameof(app));
      if (factory == null) throw new ArgumentNullException(nameof(factory));
      (string, string) key = (app.Name, subKey ?? string.Empty);
      while (true) {
        long epoch;
        lock (_lock) {
          if (_entries.TryGetValue(key, out Entry hit) && ReferenceEquals(hit.App, app) && !hit.Disposed) return hit.Instance;
          epoch = _epoch;
        }
        if (!ReferenceEquals(FirebaseApp.GetInstance(key.Item1), app)) throw new ObjectDisposedException(nameof(FirebaseApp));
        var entry = new Entry { App = app, Instance = factory() };
        entry.Handler = (_, __) => EvictEntry(key, entry);
        app.AppDisposed += entry.Handler;
        bool appAlive = ReferenceEquals(FirebaseApp.GetInstance(key.Item1), app), release = false, retry = false, dead = false;
        T winner = entry.Instance;
        lock (_lock) {
          if (!appAlive || entry.Disposed) { dead = true; if (!entry.Disposed) { entry.Disposed = true; release = true; } }
          else if (_epoch != epoch) { entry.Disposed = true; release = true; retry = true; }
          else if (_entries.TryGetValue(key, out Entry hit) && ReferenceEquals(hit.App, app) && !hit.Disposed) { winner = hit.Instance; entry.Disposed = true; release = true; }
          else _entries[key] = entry;
        }
        if (release) ReleaseEntry(entry);
        if (dead) throw new ObjectDisposedException(nameof(FirebaseApp));
        if (!retry) return winner;
      }
    }
    internal void ClearForReload() {
      List<Entry> evicted;
      lock (_lock) {
        _epoch++;
        if (_entries.Count == 0) return;
        evicted = new List<Entry>(_entries.Count);
        foreach (Entry e in _entries.Values) if (!e.Disposed) { e.Disposed = true; evicted.Add(e); }
        _entries.Clear();
      }
      foreach (Entry e in evicted) ReleaseEntry(e);
    }
    private void EvictEntry((string, string) key, Entry entry) {
      lock (_lock) {
        if (entry.Disposed) return;
        entry.Disposed = true;
        if (_entries.TryGetValue(key, out Entry cur) && ReferenceEquals(cur, entry)) _entries.Remove(key);
      }
      ReleaseEntry(entry);
    }
    private void ReleaseEntry(Entry entry) {
      try { entry.App.AppDisposed -= entry.Handler; }
      catch (Exception) {
        // Native app peer may already be destroyed during domain reload.
      }
      _onDisposed?.Invoke(entry.Instance);
    }
  }
}
