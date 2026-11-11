// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.RemoteConfig.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RemoteConfigStore {
    internal const ulong DefaultFetchTimeoutMs = 30_000UL, DefaultMinimumFetchIntervalMs = 43_200_000UL;
    internal const int MaxCustomSignalKeyLength = 256, MaxCustomSignalValueLength = 500, MaxCustomSignalsCount = 100;
    private static readonly int[] BackoffMinutes = { 2, 4, 8, 16, 32, 64, 128, 256 };
    private static readonly Random BackoffJitter = new Random();

    private sealed class Snapshot {
      internal readonly Dictionary<string, ConfigValue> Values;
      internal readonly IReadOnlyList<string> SortedKeys;
      internal Snapshot(Dictionary<string, ConfigValue> v, IReadOnlyList<string> k) => (Values, SortedKeys) = (v, k);
    }

    private readonly object _lock = new object(), _persistLock = new object();
    private readonly AtomicFileStore _fileStore;
    private readonly string _storageKey;
    private Dictionary<string, string> _fetched = new(StringComparer.Ordinal), _fetchedKeyMeta = new(StringComparer.Ordinal);
    private Dictionary<string, string> _activated = new(StringComparer.Ordinal), _activatedKeyMeta = new(StringComparer.Ordinal);
    private Dictionary<string, string> _defaults = new(StringComparer.Ordinal);
    private Dictionary<string, object> _customSignals = new(StringComparer.Ordinal);
    private long _fetchedSequence, _fetchedTemplateVersion, _activatedSequence, _activatedTemplateVersion;
    private LastFetchStatus _lastFetchStatus = LastFetchStatus.Pending;
    private FetchFailureReason _lastFetchFailureReason = FetchFailureReason.Invalid;
    private long _fetchTimeMs, _lastSuccessfulFetchTimeMs, _throttledEndTimeMs, _realtimeBackoffEndTimeMs;
    private int _backoffCount, _realtimeFailedStreams;
    private string _etag = string.Empty, _fallbackFid = string.Empty;
    private ConfigSettings _settings = new ConfigSettings {
      FetchTimeoutInMilliseconds = DefaultFetchTimeoutMs, MinimumFetchIntervalInMilliseconds = DefaultMinimumFetchIntervalMs,
    };
    private IReadOnlyList<string> _lastActivatedUpdatedKeys = Array.Empty<string>();
    private Snapshot _snapshot = new(new Dictionary<string, ConfigValue>(StringComparer.Ordinal), Array.Empty<string>());
    private long _writeSeq, _persistedSeq;

    internal RemoteConfigStore(string appName, string projectKey, AtomicFileStore fileStore = null) {
      (_fileStore, _storageKey) = (fileStore ?? AtomicFileStore.Default, $"remote_config/{BuildSafeFileId(appName, projectKey)}.json");
      LoadFromDisk();
    }

    internal ConfigValue GetValue(string key) =>
        key != null && Volatile.Read(ref _snapshot).Values.TryGetValue(key, out ConfigValue val) ? val : default;
    internal IReadOnlyList<string> GetKeys() => Volatile.Read(ref _snapshot).SortedKeys;
    internal IDictionary<string, ConfigValue> GetAllValues() =>
        new Dictionary<string, ConfigValue>(Volatile.Read(ref _snapshot).Values, StringComparer.Ordinal);
    internal IReadOnlyList<string> LastActivatedUpdatedKeys { get { lock (_lock) return _lastActivatedUpdatedKeys; } }
    internal long LatestTemplateVersion { get { lock (_lock) return Math.Max(_fetchedTemplateVersion, _activatedTemplateVersion); } }
    internal string GetETag() { lock (_lock) return _etag; }
    internal ConfigSettings GetSettings() { lock (_lock) return _settings; }
    internal ConfigInfo GetInfo() { lock (_lock) return new ConfigInfo(_fetchTimeMs, _throttledEndTimeMs, _lastFetchStatus, _lastFetchFailureReason); }
    internal IReadOnlyList<string> ComputeFetchedVsActivatedDiff() { lock (_lock) return ComputeDiff(_activated, _activatedKeyMeta, _fetched, _fetchedKeyMeta); }
    internal (long BackoffEndMs, int FailedStreams) GetRealtimeBackoff() { lock (_lock) return (_realtimeBackoffEndTimeMs, _realtimeFailedStreams); }

    internal IEnumerable<string> GetKeysByPrefix(string prefix) {
      IReadOnlyList<string> keys = Volatile.Read(ref _snapshot).SortedKeys;
      if (string.IsNullOrEmpty(prefix)) return keys;
      var matched = new List<string>();
      for (int i = 0; i < keys.Count; i++) if (keys[i].StartsWith(prefix, StringComparison.Ordinal)) matched.Add(keys[i]);
      return matched;
    }

    internal Task SetSettingsAsync(ConfigSettings settings) {
      (string Json, long Seq) p;
      lock (_lock) { _settings = settings; p = PreparePersistLocked(); }
      return FlushPersistAsync(p);
    }

    internal Task AdvanceTemplateVersionFloorAsync(long minVersion) {
      if (minVersion <= 0) return Task.CompletedTask;
      (string Json, long Seq) p = default;
      lock (_lock) if (_fetchedTemplateVersion < minVersion) { _fetchedTemplateVersion = minVersion; p = PreparePersistLocked(); }
      return FlushPersistAsync(p);
    }

    internal Task SetDefaultsAsync(IDictionary<string, object> defaults) {
      if (defaults == null) throw new ArgumentNullException(nameof(defaults));
      var converted = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (KeyValuePair<string, object> pair in defaults) {
        if (pair.Key == null) throw new ArgumentException("Key cannot be null.", nameof(defaults));
        converted[pair.Key] = ConvertDefaultToString(pair.Value);
      }
      (string Json, long Seq) p;
      lock (_lock) { _defaults = converted; RebuildSnapshotLocked(); p = PreparePersistLocked(); }
      return FlushPersistAsync(p);
    }

    internal Task SetCustomSignalsAsync(IDictionary<string, object> customSignals) {
      if (customSignals == null) throw new ArgumentNullException(nameof(customSignals));
      var validated = new List<KeyValuePair<string, object>>();
      foreach (KeyValuePair<string, object> pair in customSignals) {
        if (pair.Key == null) throw new ArgumentException("Key cannot be null.", nameof(customSignals));
        if (pair.Key.Length > MaxCustomSignalKeyLength) {
          throw new FirebaseException((int)RemoteConfigError.None, $"Custom signal keys must be {MaxCustomSignalKeyLength} characters or less.");
        }
        validated.Add(new KeyValuePair<string, object>(pair.Key, NormalizeSignalValue(pair.Value)));
      }
      (string Json, long Seq) p;
      lock (_lock) {
        var merged = new Dictionary<string, object>(_customSignals, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> pair in validated) {
          if (pair.Value == null) merged.Remove(pair.Key); else merged[pair.Key] = pair.Value;
        }
        if (merged.Count > MaxCustomSignalsCount) {
          throw new FirebaseException((int)RemoteConfigError.None, $"Custom signals count exceeds the limit of {MaxCustomSignalsCount}.");
        }
        _customSignals = merged;
        p = PreparePersistLocked();
      }
      return FlushPersistAsync(p);
    }

    internal Dictionary<string, object> GetWireCustomSignalsSnapshot() {
      lock (_lock) {
        if (_customSignals.Count == 0) return null;
        var map = new Dictionary<string, object>(_customSignals.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> kv in _customSignals) {
          map[kv.Key] = kv.Value switch {
            string s => s, long l => l.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("G17", CultureInfo.InvariantCulture),
            _ => Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? string.Empty,
          };
        }
        return map;
      }
    }

    internal async Task<bool> ActivateAsync() {
      (string Json, long Seq) p;
      lock (_lock) {
        if (_fetchedSequence == 0 || _fetchedSequence <= _activatedSequence) return false;
        IReadOnlyList<string> updatedKeys = ComputeDiff(_activated, _activatedKeyMeta, _fetched, _fetchedKeyMeta);
        _activated = new Dictionary<string, string>(_fetched, StringComparer.Ordinal);
        _activatedKeyMeta = new Dictionary<string, string>(_fetchedKeyMeta, StringComparer.Ordinal);
        (_activatedSequence, _activatedTemplateVersion, _lastActivatedUpdatedKeys) = (_fetchedSequence, _fetchedTemplateVersion, updatedKeys);
        RebuildSnapshotLocked();
        p = PreparePersistLocked();
      }
      await FlushPersistAsync(p).ConfigureAwait(false);
      return true;
    }

    internal bool IsCacheFresh(TimeSpan cacheExpiration, long nowMs) {
      if (cacheExpiration <= TimeSpan.Zero) return false;
      lock (_lock) return _lastSuccessfulFetchTimeMs > 0 && (nowMs - _lastSuccessfulFetchTimeMs) < cacheExpiration.TotalMilliseconds;
    }

    internal async Task<(bool Allowed, long ThrottledUntilMs, string CurrentEtag, ulong TimeoutMs)> TryBeginNetworkFetchAsync(long nowMs) {
      (string Json, long Seq) p = default;
      bool throttled = false;
      long throttledUntilMs;
      string currentEtag;
      ulong timeoutMs;
      lock (_lock) {
        timeoutMs = _settings.FetchTimeoutInMilliseconds > 0 ? _settings.FetchTimeoutInMilliseconds : DefaultFetchTimeoutMs;
        currentEtag = _etag;
        if (_throttledEndTimeMs > nowMs) {
          (_lastFetchStatus, _lastFetchFailureReason, _fetchTimeMs, throttledUntilMs, throttled) =
              (LastFetchStatus.Failure, FetchFailureReason.Throttled, nowMs, _throttledEndTimeMs, true);
          p = PreparePersistLocked();
        } else { _lastFetchStatus = LastFetchStatus.Pending; throttledUntilMs = 0; }
      }
      if (throttled) await FlushPersistAsync(p).ConfigureAwait(false);
      return (!throttled, throttledUntilMs, currentEtag, timeoutMs);
    }

    internal Task RecordFetchSuccessAsync(
        string state, Dictionary<string, string> entries, Dictionary<string, string> keyMeta,
        long templateVersion, string responseEtag, long nowMs) {
      (string Json, long Seq) p;
      lock (_lock) {
        (_backoffCount, _throttledEndTimeMs, _lastFetchStatus, _lastFetchFailureReason) =
            (0, 0, LastFetchStatus.Success, FetchFailureReason.Invalid);
        _fetchTimeMs = _lastSuccessfulFetchTimeMs = nowMs;
        if (!string.IsNullOrEmpty(responseEtag)) _etag = responseEtag;
        if (templateVersion > 0) _fetchedTemplateVersion = templateVersion;
        string norm = string.IsNullOrEmpty(state) ? "UPDATE" : state;
        if (!string.Equals(norm, "NO_CHANGE", StringComparison.OrdinalIgnoreCase)) {
          bool clear = string.Equals(norm, "EMPTY_CONFIG", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(norm, "NO_TEMPLATE", StringComparison.OrdinalIgnoreCase);
          _fetched = clear || entries == null ? new(StringComparer.Ordinal) : new(entries, StringComparer.Ordinal);
          _fetchedKeyMeta = clear || keyMeta == null ? new(StringComparer.Ordinal) : new(keyMeta, StringComparer.Ordinal);
          _fetchedSequence = Math.Max(_fetchedSequence, _activatedSequence) + 1;
        }
        p = PreparePersistLocked();
      }
      return FlushPersistAsync(p);
    }

    internal async Task<long> RecordFetchFailureAsync(bool isThrottleable, long? retryAfterMs, long nowMs) {
      (string Json, long Seq) p;
      long throttledEnd;
      lock (_lock) {
        (_fetchTimeMs, _lastFetchStatus) = (nowMs, LastFetchStatus.Failure);
        if (isThrottleable) {
          _backoffCount++;
          long backoffMs = retryAfterMs.HasValue && retryAfterMs.Value > 0 ? retryAfterMs.Value : ComputeThrottleBackoffMs(_backoffCount);
          (_throttledEndTimeMs, _lastFetchFailureReason) = (nowMs + backoffMs, FetchFailureReason.Throttled);
        } else _lastFetchFailureReason = FetchFailureReason.Error;
        throttledEnd = _throttledEndTimeMs;
        p = PreparePersistLocked();
      }
      await FlushPersistAsync(p).ConfigureAwait(false);
      return throttledEnd;
    }

    internal async Task<string> GetOrCreateFallbackFidAsync() {
      (string Json, long Seq) p;
      string fid;
      lock (_lock) {
        if (!string.IsNullOrEmpty(_fallbackFid)) return _fallbackFid;
        fid = _fallbackFid = GenerateSpecFid();
        p = PreparePersistLocked();
      }
      await FlushPersistAsync(p).ConfigureAwait(false);
      return fid;
    }

    internal Task UpdateRealtimeBackoffAsync(long backoffEndMs, int failedStreams) {
      (string Json, long Seq) p = default;
      lock (_lock) {
        if (_realtimeBackoffEndTimeMs == backoffEndMs && _realtimeFailedStreams == failedStreams) return Task.CompletedTask;
        (_realtimeBackoffEndTimeMs, _realtimeFailedStreams) = (backoffEndMs, failedStreams);
        p = PreparePersistLocked();
      }
      return FlushPersistAsync(p);
    }

    private void RebuildSnapshotLocked() {
      var values = new Dictionary<string, ConfigValue>(_defaults.Count + _activated.Count, StringComparer.Ordinal);
      foreach (KeyValuePair<string, string> kv in _defaults) values[kv.Key] = ConfigValue.Create(kv.Value, ValueSource.DefaultValue);
      foreach (KeyValuePair<string, string> kv in _activated) values[kv.Key] = ConfigValue.Create(kv.Value, ValueSource.RemoteValue);
      var keys = new string[values.Count];
      int idx = 0;
      foreach (string k in values.Keys) keys[idx++] = k;
      Array.Sort(keys, StringComparer.Ordinal);
      Volatile.Write(ref _snapshot, new Snapshot(values, new ReadOnlyCollection<string>(keys)));
    }

    private static IReadOnlyList<string> ComputeDiff(
        Dictionary<string, string> oldEntries, Dictionary<string, string> oldMeta,
        Dictionary<string, string> newEntries, Dictionary<string, string> newMeta) {
      var changed = new HashSet<string>(StringComparer.Ordinal);
      foreach (KeyValuePair<string, string> kv in newEntries) {
        if (!oldEntries.TryGetValue(kv.Key, out string oldVal) || !string.Equals(kv.Value, oldVal, StringComparison.Ordinal) ||
            !string.Equals(GetOrEmpty(newMeta, kv.Key), GetOrEmpty(oldMeta, kv.Key), StringComparison.Ordinal)) changed.Add(kv.Key);
      }
      foreach (string oldKey in oldEntries.Keys) if (!newEntries.ContainsKey(oldKey)) changed.Add(oldKey);
      if (changed.Count == 0) return Array.Empty<string>();
      var sorted = new string[changed.Count];
      changed.CopyTo(sorted);
      Array.Sort(sorted, StringComparer.Ordinal);
      return new ReadOnlyCollection<string>(sorted);
    }

    private static string GetOrEmpty(Dictionary<string, string> map, string key) =>
        map != null && map.TryGetValue(key, out string v) ? (v ?? string.Empty) : string.Empty;

    private static long ComputeThrottleBackoffMs(int backoffCount) {
      long baseMs = BackoffMinutes[Math.Min(Math.Max(backoffCount, 1), BackoffMinutes.Length) - 1] * 60_000L;
      lock (BackoffJitter) return (baseMs / 2L) + BackoffJitter.Next((int)Math.Min(baseMs, int.MaxValue));
    }

    private static object NormalizeSignalValue(object value) {
      if (value == null) return null;
      if (value is string s) return s.Length <= MaxCustomSignalValueLength ? s : throw new FirebaseException((int)RemoteConfigError.None, $"Custom signal string values must be {MaxCustomSignalValueLength} characters or less.");
      if (value is sbyte or byte or short or ushort or int or uint or long) return Convert.ToInt64(value, CultureInfo.InvariantCulture);
      if (value is float or double) {
        double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return !double.IsNaN(d) && !double.IsInfinity(d) ? (object)d : throw new FirebaseException((int)RemoteConfigError.None, "Custom signal double value must be finite.");
      }
      throw new FirebaseException((int)RemoteConfigError.None, "Invalid value type. Must be String, Int, Double, or Null.");
    }

    private static string ConvertDefaultToString(object value) {
      if (value == null) return string.Empty;
      if (value is string s) return s;
      if (value is byte[] byteArr) return Encoding.UTF8.GetString(byteArr);
      if (value is IEnumerable<byte> byteSeq) return Encoding.UTF8.GetString(new List<byte>(byteSeq).ToArray());
      if (value is IEnumerable enumerable) {
        var sb = new StringBuilder();
        foreach (object item in enumerable) sb.Append(Convert.ToString(item, CultureInfo.InvariantCulture));
        return sb.ToString();
      }
      return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private (string Json, long Seq) PreparePersistLocked() {
      var root = new Dictionary<string, object>(StringComparer.Ordinal) {
        ["fetched"] = ToObjectDict(_fetched), ["fetchedKeyMeta"] = ToObjectDict(_fetchedKeyMeta),
        ["fetchedSeq"] = _fetchedSequence, ["fetchedTemplateVersion"] = _fetchedTemplateVersion,
        ["activated"] = ToObjectDict(_activated), ["activatedKeyMeta"] = ToObjectDict(_activatedKeyMeta),
        ["activatedSeq"] = _activatedSequence, ["activatedTemplateVersion"] = _activatedTemplateVersion,
        ["defaults"] = ToObjectDict(_defaults), ["customSignals"] = new Dictionary<string, object>(_customSignals, StringComparer.Ordinal),
        ["metadata"] = new Dictionary<string, object>(StringComparer.Ordinal) {
          ["lastFetchStatus"] = (long)_lastFetchStatus, ["lastFetchFailureReason"] = (long)_lastFetchFailureReason,
          ["fetchTimeMs"] = _fetchTimeMs, ["lastSuccessfulFetchTimeMs"] = _lastSuccessfulFetchTimeMs,
          ["throttledEndTimeMs"] = _throttledEndTimeMs, ["backoffCount"] = (long)_backoffCount,
          ["etag"] = _etag ?? string.Empty, ["fallbackFid"] = _fallbackFid ?? string.Empty,
          ["fetchTimeoutMs"] = (long)Math.Min(_settings.FetchTimeoutInMilliseconds, long.MaxValue),
          ["minimumFetchIntervalMs"] = (long)Math.Min(_settings.MinimumFetchIntervalInMilliseconds, long.MaxValue),
          ["realtimeBackoffEndTimeMs"] = _realtimeBackoffEndTimeMs, ["realtimeFailedStreams"] = (long)_realtimeFailedStreams,
        },
      };
      return (JsonHelper.Serialize(root), ++_writeSeq);
    }

    private Task FlushPersistAsync((string Json, long Seq) p) => p.Json == null ? Task.CompletedTask : Task.Run(() => {
      lock (_persistLock) {
        if (p.Seq <= _persistedSeq) return;
        try { _fileStore.WriteAllText(_storageKey, p.Json); _persistedSeq = p.Seq; }
        catch (Exception ex) { Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Warning, $"RemoteConfig persist error: {ex.Message}"); }
      }
    });

    private void LoadFromDisk() {
      string raw = _fileStore.ReadAllText(_storageKey);
      if (string.IsNullOrEmpty(raw)) return;
      Dictionary<string, object> root;
      try { root = JsonHelper.ParseObject(raw); }
      catch (Exception ex) { Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Warning, $"RemoteConfig cache ignored: {ex.Message}"); return; }
      if (root == null) return;
      lock (_lock) {
        (_fetched, _fetchedKeyMeta) = (ReadStringMap(JsonHelper.GetMap(root, "fetched")), ReadStringMap(JsonHelper.GetMap(root, "fetchedKeyMeta")));
        (_fetchedSequence, _fetchedTemplateVersion) = (JsonHelper.GetLong(root, "fetchedSeq", 0), JsonHelper.GetLong(root, "fetchedTemplateVersion", 0));
        (_activated, _activatedKeyMeta) = (ReadStringMap(JsonHelper.GetMap(root, "activated")), ReadStringMap(JsonHelper.GetMap(root, "activatedKeyMeta")));
        (_activatedSequence, _activatedTemplateVersion) = (JsonHelper.GetLong(root, "activatedSeq", 0), JsonHelper.GetLong(root, "activatedTemplateVersion", 0));
        (_defaults, _customSignals) = (ReadStringMap(JsonHelper.GetMap(root, "defaults")), ReadCustomSignalsMap(JsonHelper.GetMap(root, "customSignals")));
        if (JsonHelper.GetMap(root, "metadata") is Dictionary<string, object> meta) {
          int st = (int)JsonHelper.GetLong(meta, "lastFetchStatus", (long)LastFetchStatus.Pending);
          _lastFetchStatus = Enum.IsDefined(typeof(LastFetchStatus), st) ? (LastFetchStatus)st : LastFetchStatus.Pending;
          int fr = (int)JsonHelper.GetLong(meta, "lastFetchFailureReason", (long)FetchFailureReason.Invalid);
          _lastFetchFailureReason = Enum.IsDefined(typeof(FetchFailureReason), fr) ? (FetchFailureReason)fr : FetchFailureReason.Invalid;
          _fetchTimeMs = Math.Max(0L, JsonHelper.GetLong(meta, "fetchTimeMs", 0));
          _lastSuccessfulFetchTimeMs = Math.Max(0L, JsonHelper.GetLong(meta, "lastSuccessfulFetchTimeMs", 0));
          if (_lastFetchStatus == LastFetchStatus.Pending && _lastSuccessfulFetchTimeMs > 0) _lastFetchStatus = LastFetchStatus.Success;
          _throttledEndTimeMs = Math.Max(0L, JsonHelper.GetLong(meta, "throttledEndTimeMs", 0));
          _backoffCount = (int)Math.Max(0L, JsonHelper.GetLong(meta, "backoffCount", 0));
          (_etag, _fallbackFid) = (JsonHelper.GetString(meta, "etag", string.Empty) ?? string.Empty, JsonHelper.GetString(meta, "fallbackFid", string.Empty) ?? string.Empty);
          long tMs = JsonHelper.GetLong(meta, "fetchTimeoutMs", (long)DefaultFetchTimeoutMs), mMs = JsonHelper.GetLong(meta, "minimumFetchIntervalMs", (long)DefaultMinimumFetchIntervalMs);
          _settings = new ConfigSettings {
            FetchTimeoutInMilliseconds = tMs >= 0 ? (ulong)tMs : DefaultFetchTimeoutMs,
            MinimumFetchIntervalInMilliseconds = mMs >= 0 ? (ulong)mMs : DefaultMinimumFetchIntervalMs,
          };
          _realtimeBackoffEndTimeMs = Math.Max(0L, JsonHelper.GetLong(meta, "realtimeBackoffEndTimeMs", 0));
          _realtimeFailedStreams = (int)Math.Max(0L, JsonHelper.GetLong(meta, "realtimeFailedStreams", 0));
        }
        RebuildSnapshotLocked();
      }
    }

    private static Dictionary<string, object> ToObjectDict(Dictionary<string, string> src) {
      var dst = new Dictionary<string, object>(src.Count, StringComparer.Ordinal);
      foreach (KeyValuePair<string, string> kv in src) dst[kv.Key] = kv.Value;
      return dst;
    }

    private static Dictionary<string, string> ReadStringMap(Dictionary<string, object> raw) {
      var dst = new Dictionary<string, string>(StringComparer.Ordinal);
      if (raw != null) foreach (KeyValuePair<string, object> kv in raw) if (kv.Key != null) dst[kv.Key] = JsonHelper.GetString(raw, kv.Key, string.Empty) ?? string.Empty;
      return dst;
    }

    private static Dictionary<string, object> ReadCustomSignalsMap(Dictionary<string, object> raw) {
      var dst = new Dictionary<string, object>(StringComparer.Ordinal);
      if (raw == null) return dst;
      foreach (KeyValuePair<string, object> kv in raw) {
        if (kv.Key == null || kv.Value == null) continue;
        try { if (NormalizeSignalValue(kv.Value) is object norm) dst[kv.Key] = norm; }
        catch (Exception) {
          // Ignore malformed persisted signal entry.
        }
      }
      return dst;
    }

    private static string BuildSafeFileId(string appName, string projectKey) {
      using var sha = SHA256.Create();
      byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{appName ?? string.Empty}|{projectKey ?? string.Empty}"));
      var sb = new StringBuilder(32);
      for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
      return sb.ToString();
    }

    private static string GenerateSpecFid() {
      byte[] bytes = new byte[17];
      using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
      bytes[0] = (byte)(0x70 | (bytes[0] & 0x0F));
      return Convert.ToBase64String(bytes).Substring(0, 22).Replace('+', '-').Replace('/', '_');
    }
  }
}
