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
using System.Security.Cryptography;
using System.Text;
using Firebase.Internal;

namespace Firebase.Database.Internal {
  internal sealed class PersistedCacheEntry {
    internal RtdbPath Path { get; set; }
    internal RtdbNode Node { get; set; }
    internal long LastAccessTick { get; set; }
    internal long ApproxBytes { get; set; }
  }

  internal sealed class PersistedStateSnapshot {
    internal List<PersistedCacheEntry> CacheEntries { get; } = new List<PersistedCacheEntry>();
    internal List<UserWriteRecord> PendingWrites { get; } = new List<UserWriteRecord>();
  }

  internal sealed class RtdbPersistence {
    internal const long DefaultCacheSizeBytes = 10L * 1024L * 1024L;

    private readonly AtomicFileStore _store;
    private readonly string _stateKey;
    private readonly object _ioLock = new object();
    private long _lastSavedSeq = -1;

    internal RtdbPersistence(string appName, string databaseCacheKey, AtomicFileStore customStore = null) {
      _store = customStore ?? AtomicFileStore.Default;
      _stateKey = $"rtdb/{ComputeStableId(appName + "|" + databaseCacheKey)}/state_v1.json";
    }

    internal PersistedStateSnapshot Load() {
      string raw;
      lock (_ioLock) {
        raw = _store.ReadAllText(_stateKey);
      }
      var result = new PersistedStateSnapshot();
      if (string.IsNullOrEmpty(raw)) {
        return result;
      }

      Dictionary<string, object> root = JsonHelper.ParseObject(raw);
      if (root == null) {
        return result;
      }

      List<object> cacheList = JsonHelper.GetList(root, "cache");
      if (cacheList != null) {
        for (int i = 0; i < cacheList.Count; i++) {
          if (cacheList[i] is not Dictionary<string, object> item) continue;
          string pathStr = JsonHelper.GetString(item, "p", string.Empty);
          long tick = JsonHelper.GetLong(item, "t", i + 1);
          long bytes = JsonHelper.GetLong(item, "b", 0);
          item.TryGetValue("d", out object rawData);
          RtdbNode node = RtdbNodeFactory.NodeFromServerWire(rawData);
          result.CacheEntries.Add(new PersistedCacheEntry {
            Path = RtdbPath.ParseUnchecked(pathStr),
            Node = node,
            LastAccessTick = tick,
            ApproxBytes = bytes > 0 ? bytes : EstimateBytes(rawData),
          });
        }
      }

      List<object> writeList = JsonHelper.GetList(root, "writes");
      if (writeList != null) {
        for (int i = 0; i < writeList.Count; i++) {
          if (writeList[i] is not Dictionary<string, object> item) continue;
          long writeId = JsonHelper.GetLong(item, "id", i + 1);
          string pathStr = JsonHelper.GetString(item, "p", string.Empty);
          bool isMerge = JsonHelper.GetBool(item, "m", false);
          RtdbPath path = RtdbPath.ParseUnchecked(pathStr);

          if (!isMerge) {
            item.TryGetValue("d", out object rawData);
            RtdbNode unresolved = RtdbNodeFactory.NodeFromServerWire(rawData);
            RtdbNode resolved = ServerValues.ResolveNode(unresolved, EmptyNode.Instance, 0);
            result.PendingWrites.Add(new UserWriteRecord(writeId, path, unresolved, resolved, visible: true, persist: true));
          } else if (item.TryGetValue("d", out object rawMergeObj) && rawMergeObj is Dictionary<string, object> mergeMap) {
            var unresolvedMerge = new List<KeyValuePair<RtdbPath, RtdbNode>>(mergeMap.Count);
            var resolvedMerge = new List<KeyValuePair<RtdbPath, RtdbNode>>(mergeMap.Count);
            foreach (KeyValuePair<string, object> kv in mergeMap) {
              RtdbPath childRel = RtdbPath.ParseUnchecked(kv.Key);
              RtdbNode unresolved = RtdbNodeFactory.NodeFromServerWire(kv.Value);
              RtdbNode resolved = ServerValues.ResolveNode(unresolved, EmptyNode.Instance, 0);
              unresolvedMerge.Add(new KeyValuePair<RtdbPath, RtdbNode>(childRel, unresolved));
              resolvedMerge.Add(new KeyValuePair<RtdbPath, RtdbNode>(childRel, resolved));
            }
            result.PendingWrites.Add(new UserWriteRecord(writeId, path, unresolvedMerge, resolvedMerge, visible: true, persist: true));
          }
        }
      }

      return result;
    }

    internal void Save(
        long seq,
        List<PersistedCacheEntry> cacheEntries,
        List<UserWriteRecord> persistedWrites) {
      var cacheOut = new List<object>(cacheEntries.Count);
      for (int i = 0; i < cacheEntries.Count; i++) {
        PersistedCacheEntry c = cacheEntries[i];
        object wire = c.Node.ToWireValue();
        cacheOut.Add(new Dictionary<string, object>(4) {
          ["p"] = c.Path.ToRelativeString(),
          ["t"] = c.LastAccessTick,
          ["b"] = c.ApproxBytes,
          ["d"] = wire,
        });
      }

      var writesOut = new List<object>(persistedWrites.Count);
      for (int i = 0; i < persistedWrites.Count; i++) {
        UserWriteRecord w = persistedWrites[i];
        if (!w.Persist) continue;
        if (!w.IsMerge) {
          writesOut.Add(new Dictionary<string, object>(4) {
            ["id"] = w.WriteId,
            ["p"] = w.Path.ToRelativeString(),
            ["m"] = false,
            ["d"] = w.UnresolvedOverwrite.ToWireValue(),
          });
        } else {
          var mergeDict = new Dictionary<string, object>(w.UnresolvedMerge.Count);
          for (int m = 0; m < w.UnresolvedMerge.Count; m++) {
            KeyValuePair<RtdbPath, RtdbNode> kv = w.UnresolvedMerge[m];
            mergeDict[kv.Key.ToRelativeString()] = kv.Value.ToWireValue();
          }
          writesOut.Add(new Dictionary<string, object>(4) {
            ["id"] = w.WriteId,
            ["p"] = w.Path.ToRelativeString(),
            ["m"] = true,
            ["d"] = mergeDict,
          });
        }
      }

      string json = JsonHelper.Serialize(new Dictionary<string, object>(2) {
        ["cache"] = cacheOut,
        ["writes"] = writesOut,
      });

      lock (_ioLock) {
        if (seq < _lastSavedSeq) {
          return;
        }
        _lastSavedSeq = seq;
        _store.WriteAllText(_stateKey, json);
      }
    }

    internal void Clear(long seq) {
      lock (_ioLock) {
        if (seq < _lastSavedSeq) {
          return;
        }
        _lastSavedSeq = seq;
        _store.Delete(_stateKey);
      }
    }

    internal static long EstimateNodeBytes(RtdbNode node) {
      if (node == null || node.IsEmpty) return 4;
      string json = JsonHelper.Serialize(node.ToWireValue());
      return json == null ? 4 : Encoding.UTF8.GetByteCount(json);
    }

    private static long EstimateBytes(object raw) {
      if (raw == null) return 4;
      string json = JsonHelper.Serialize(raw);
      return json == null ? 4 : Encoding.UTF8.GetByteCount(json);
    }

    private static string ComputeStableId(string input) {
      byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
      byte[] hash;
      using (SHA256 sha = SHA256.Create()) {
        hash = sha.ComputeHash(bytes);
      }
      var sb = new StringBuilder(32);
      for (int i = 0; i < 16; i++) {
        sb.Append(hash[i].ToString("x2"));
      }
      return sb.ToString();
    }
  }
}
