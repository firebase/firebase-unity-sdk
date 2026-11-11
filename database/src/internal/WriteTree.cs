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

namespace Firebase.Database.Internal {
  internal sealed class UserWriteRecord {
    internal long WriteId { get; }
    internal RtdbPath Path { get; }
    internal bool IsMerge { get; }
    internal RtdbNode UnresolvedOverwrite { get; }
    internal RtdbNode ResolvedOverwrite { get; set; }
    internal List<KeyValuePair<RtdbPath, RtdbNode>> UnresolvedMerge { get; }
    internal List<KeyValuePair<RtdbPath, RtdbNode>> ResolvedMerge { get; set; }
    internal bool Visible { get; }
    internal bool Persist { get; }

    internal UserWriteRecord(
        long writeId,
        RtdbPath path,
        RtdbNode unresolvedOverwrite,
        RtdbNode resolvedOverwrite,
        bool visible,
        bool persist) {
      WriteId = writeId;
      Path = path;
      IsMerge = false;
      UnresolvedOverwrite = unresolvedOverwrite ?? EmptyNode.Instance;
      ResolvedOverwrite = resolvedOverwrite ?? EmptyNode.Instance;
      Visible = visible;
      Persist = persist;
    }

    internal UserWriteRecord(
        long writeId,
        RtdbPath path,
        List<KeyValuePair<RtdbPath, RtdbNode>> unresolvedMerge,
        List<KeyValuePair<RtdbPath, RtdbNode>> resolvedMerge,
        bool visible,
        bool persist) {
      WriteId = writeId;
      Path = path;
      IsMerge = true;
      UnresolvedMerge = unresolvedMerge;
      ResolvedMerge = resolvedMerge;
      Visible = visible;
      Persist = persist;
    }
  }

  internal sealed class WriteTree {
    private readonly List<UserWriteRecord> _writes = new List<UserWriteRecord>();

    internal IReadOnlyList<UserWriteRecord> AllWrites => _writes;

    internal void AddOverwrite(
        long writeId,
        RtdbPath path,
        RtdbNode unresolved,
        RtdbNode resolved,
        bool visible,
        bool persist) {
      _writes.Add(new UserWriteRecord(writeId, path, unresolved, resolved, visible, persist));
    }

    internal void AddMerge(
        long writeId,
        RtdbPath path,
        List<KeyValuePair<RtdbPath, RtdbNode>> unresolved,
        List<KeyValuePair<RtdbPath, RtdbNode>> resolved,
        bool visible,
        bool persist) {
      _writes.Add(new UserWriteRecord(writeId, path, unresolved, resolved, visible, persist));
    }

    internal bool RemoveWrite(long writeId, out UserWriteRecord removed) {
      for (int i = 0; i < _writes.Count; i++) {
        if (_writes[i].WriteId == writeId) {
          removed = _writes[i];
          _writes.RemoveAt(i);
          return true;
        }
      }
      removed = null;
      return false;
    }

    internal List<UserWriteRecord> RemoveAllWrites() {
      var copy = new List<UserWriteRecord>(_writes);
      _writes.Clear();
      return copy;
    }

    internal RtdbNode CalcCompleteNode(
        RtdbPath queryPath,
        RtdbNode serverNode,
        bool serverComplete,
        HashSet<long> ignoreWriteIds,
        out bool isComplete) {
      bool coveredByFullWrite = false;
      for (int i = 0; i < _writes.Count; i++) {
        UserWriteRecord w = _writes[i];
        if (!w.Visible || (ignoreWriteIds != null && ignoreWriteIds.Contains(w.WriteId))) {
          continue;
        }
        if (!w.IsMerge) {
          if (queryPath.StartsWith(w.Path)) {
            coveredByFullWrite = true;
            break;
          }
        } else {
          for (int m = 0; m < w.ResolvedMerge.Count; m++) {
            RtdbPath fullMergePath = w.Path.Child(w.ResolvedMerge[m].Key);
            if (queryPath.StartsWith(fullMergePath)) {
              coveredByFullWrite = true;
              break;
            }
          }
          if (coveredByFullWrite) break;
        }
      }

      if (!serverComplete && !coveredByFullWrite) {
        isComplete = false;
        return EmptyNode.Instance;
      }

      isComplete = true;
      RtdbNode current = serverComplete ? (serverNode ?? EmptyNode.Instance) : EmptyNode.Instance;

      for (int i = 0; i < _writes.Count; i++) {
        UserWriteRecord w = _writes[i];
        if (!w.Visible || (ignoreWriteIds != null && ignoreWriteIds.Contains(w.WriteId))) {
          continue;
        }
        if (!w.IsMerge) {
          current = ApplySingleOverwrite(queryPath, current, w.Path, w.ResolvedOverwrite);
        } else {
          for (int m = 0; m < w.ResolvedMerge.Count; m++) {
            KeyValuePair<RtdbPath, RtdbNode> entry = w.ResolvedMerge[m];
            RtdbPath targetPath = w.Path.Child(entry.Key);
            current = ApplySingleOverwrite(queryPath, current, targetPath, entry.Value);
          }
        }
      }

      return current;
    }

    private static RtdbNode ApplySingleOverwrite(
        RtdbPath queryPath,
        RtdbNode current,
        RtdbPath writePath,
        RtdbNode writeNode) {
      if (RtdbPath.TryGetRelative(writePath, queryPath, out RtdbPath relToQuery)) {
        return writeNode.GetChild(relToQuery);
      }
      if (RtdbPath.TryGetRelative(queryPath, writePath, out RtdbPath relToWrite)) {
        return current.UpdateChild(relToWrite, writeNode);
      }
      return current;
    }
  }

  internal sealed class SparseSnapshotTree {
    private RtdbNode _value;
    private Dictionary<string, SparseSnapshotTree> _children;

    internal void Remember(RtdbPath path, RtdbNode data) {
      if (path.IsEmpty) {
        _value = data ?? EmptyNode.Instance;
        _children = null;
        return;
      }
      if (_value != null) {
        _value = _value.UpdateChild(path, data);
        return;
      }
      _children ??= new Dictionary<string, SparseSnapshotTree>(StringComparer.Ordinal);
      string front = path.Front;
      if (!_children.TryGetValue(front, out SparseSnapshotTree childTree)) {
        childTree = new SparseSnapshotTree();
        _children[front] = childTree;
      }
      childTree.Remember(path.PopFront(), data);
    }

    internal bool Forget(RtdbPath path) {
      if (path.IsEmpty) {
        _value = null;
        _children = null;
        return true;
      }
      if (_value != null) {
        if (_value.IsLeaf || _value.IsEmpty) {
          return false;
        }
        var childrenNode = (ChildrenNode)_value;
        _value = null;
        foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
          Remember(RtdbPath.Empty.ChildKey(kv.Key), kv.Value);
        }
        return Forget(path);
      }
      if (_children != null) {
        string front = path.Front;
        if (_children.TryGetValue(front, out SparseSnapshotTree childTree)) {
          bool emptied = childTree.Forget(path.PopFront());
          if (emptied) {
            _children.Remove(front);
          }
        }
        if (_children.Count == 0) {
          _children = null;
          return true;
        }
      }
      return false;
    }

    internal void ForEachTree(RtdbPath prefixPath, Action<RtdbPath, RtdbNode> action) {
      if (_value != null) {
        action(prefixPath, _value);
        return;
      }
      if (_children == null) return;
      foreach (KeyValuePair<string, SparseSnapshotTree> kv in _children) {
        kv.Value.ForEachTree(prefixPath.ChildKey(kv.Key), action);
      }
    }

    internal void Clear() {
      _value = null;
      _children = null;
    }
  }
}
