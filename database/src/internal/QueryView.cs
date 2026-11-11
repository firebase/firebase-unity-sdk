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
using System.Threading.Tasks;

namespace Firebase.Database.Internal {
  internal enum EventKind : byte {
    ValueChanged = 0,
    ChildAdded = 1,
    ChildChanged = 2,
    ChildMoved = 3,
    ChildRemoved = 4,
  }

  internal sealed class ListenerRegistration {
    internal QuerySpec Spec { get; }
    internal EventKind Kind { get; }
    internal EventHandler<ValueChangedEventArgs> ValueHandler { get; }
    internal EventHandler<ChildChangedEventArgs> ChildHandler { get; }
    internal bool IsOneShot { get; }
    internal volatile bool Active = true;
    internal bool OneShotDispatched;

    internal ListenerRegistration(
        QuerySpec spec,
        EventHandler<ValueChangedEventArgs> valueHandler,
        bool isOneShot = false) {
      Spec = spec;
      Kind = EventKind.ValueChanged;
      ValueHandler = valueHandler;
      IsOneShot = isOneShot;
    }

    internal ListenerRegistration(
        QuerySpec spec,
        EventKind kind,
        EventHandler<ChildChangedEventArgs> childHandler) {
      Spec = spec;
      Kind = kind;
      ChildHandler = childHandler;
      IsOneShot = false;
    }

    internal bool Matches(EventKind kind, Delegate handler) {
      if (Kind != kind) return false;
      return kind == EventKind.ValueChanged
          ? Equals(ValueHandler, handler)
          : Equals(ChildHandler, handler);
    }
  }

  internal readonly struct DispatchedEvent {
    internal ListenerRegistration Registration { get; }
    internal DataSnapshot Snapshot { get; }
    internal string PreviousChildName { get; }
    internal DatabaseError Error { get; }

    internal DispatchedEvent(
        ListenerRegistration registration,
        DataSnapshot snapshot,
        string previousChildName = null,
        DatabaseError error = null) {
      Registration = registration;
      Snapshot = snapshot;
      PreviousChildName = previousChildName;
      Error = error;
    }
  }

  internal sealed class QueryView {
    private readonly FirebaseDatabase _database;
    private readonly List<ListenerRegistration> _registrations = new List<ListenerRegistration>();

    internal QuerySpec Spec { get; }
    internal RtdbNode CurrentNode { get; private set; } = EmptyNode.Instance;
    internal bool IsComplete { get; private set; }
    internal bool ServerSynced { get; set; }
    internal bool ReceivedServerDataPush { get; set; }
    internal bool NoIndexFallback { get; set; }
    internal bool KeepSynced { get; set; }
    internal long? Tag { get; set; }
    internal TaskCompletionSource<bool> ServerSyncTcs { get; private set; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<bool> ServerUpdateTcs { get; private set; } =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    internal QueryView(FirebaseDatabase database, QuerySpec spec, long? tag) {
      _database = database;
      Spec = spec;
      Tag = tag;
    }

    internal void MarkServerSyncedUnlocked() {
      ServerSynced = true;
      ServerSyncTcs.TrySetResult(true);
      ServerUpdateTcs.TrySetResult(true);
    }

    internal void ResetServerSyncedUnlocked() {
      ServerSynced = false;
      ReceivedServerDataPush = false;
      NoIndexFallback = false;
      if (ServerSyncTcs.Task.IsCompleted) {
        ServerSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      }
    }

    internal Task WaitNextServerUpdateUnlocked() {
      if (ServerUpdateTcs.Task.IsCompleted) {
        ServerUpdateTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      }
      return ServerUpdateTcs.Task;
    }

    internal bool HasActiveListeners {
      get {
        for (int i = 0; i < _registrations.Count; i++) {
          if (_registrations[i].Active) return true;
        }
        return false;
      }
    }

    internal bool HasOnlyOneShotListeners {
      get {
        if (KeepSynced) return false;
        bool foundAny = false;
        for (int i = 0; i < _registrations.Count; i++) {
          ListenerRegistration reg = _registrations[i];
          if (!reg.Active) continue;
          if (!reg.IsOneShot) return false;
          foundAny = true;
        }
        return foundAny;
      }
    }

    internal bool NeedsNetworkListen => (KeepSynced || HasActiveListeners) && !Spec.Path.IsDotInfo;

    internal void AddRegistration(ListenerRegistration reg, List<DispatchedEvent> initialEvents) {
      _registrations.Add(reg);
      if (!IsComplete || !reg.Active) {
        return;
      }

      if (reg.Kind == EventKind.ValueChanged) {
        if (reg.IsOneShot) {
          if (reg.OneShotDispatched) return;
          reg.OneShotDispatched = true;
        }
        var snap = new DataSnapshot(_database, Spec.Path, CurrentNode, Spec.Params.Index);
        initialEvents.Add(new DispatchedEvent(reg, snap));
      } else if (reg.Kind == EventKind.ChildAdded && CurrentNode is ChildrenNode children && !children.IsEmpty) {
        List<NamedNode> ordered = BuildOrderedChildren(children, Spec.Params.Index);
        string prevKey = null;
        for (int i = 0; i < ordered.Count; i++) {
          NamedNode item = ordered[i];
          var childSnap = new DataSnapshot(
              _database,
              Spec.Path.ChildKey(item.Key),
              item.Node,
              RtdbIndex.Priority);
          initialEvents.Add(new DispatchedEvent(reg, childSnap, prevKey));
          prevKey = item.Key;
        }
      }
    }

    internal bool RemoveRegistration(EventKind kind, Delegate handler) {
      for (int i = _registrations.Count - 1; i >= 0; i--) {
        ListenerRegistration reg = _registrations[i];
        if (reg.Active && reg.Matches(kind, handler)) {
          reg.Active = false;
          _registrations.RemoveAt(i);
          return true;
        }
      }
      return false;
    }

    internal void RemoveSpecificRegistration(ListenerRegistration target) {
      target.Active = false;
      _registrations.Remove(target);
    }

    internal void DeactivateAll() {
      for (int i = 0; i < _registrations.Count; i++) {
        _registrations[i].Active = false;
      }
      _registrations.Clear();
      KeepSynced = false;
    }

    internal void CancelAll(DatabaseError error, List<DispatchedEvent> outEvents) {
      for (int i = 0; i < _registrations.Count; i++) {
        ListenerRegistration reg = _registrations[i];
        if (!reg.Active) continue;
        // Avoid firing duplicate cancel events if a single C# delegate subscribed to multiple Child* events?
        // Note: in Query.cs each += registers one EventKind, so firing per registration matches C#.
        outEvents.Add(new DispatchedEvent(reg, snapshot: null, previousChildName: null, error: error));
        reg.Active = false;
      }
      _registrations.Clear();
      KeepSynced = false;
    }

    internal void UpdateFromCompleteNode(RtdbNode unfilteredCompleteNode, bool complete, List<DispatchedEvent> outEvents) {
      if (!complete) {
        return;
      }

      RtdbNode nextFiltered = Spec.Params.ApplyFilter(unfilteredCompleteNode ?? EmptyNode.Instance);
      bool wasComplete = IsComplete;
      RtdbNode prevFiltered = CurrentNode;

      if (wasComplete && prevFiltered.EqualsNode(nextFiltered)) {
        return;
      }

      CurrentNode = nextFiltered;
      IsComplete = true;

      if (_registrations.Count == 0) {
        return;
      }

      RtdbIndex index = Spec.Params.Index;
      List<NamedNode> newOrdered = nextFiltered is ChildrenNode nextChildren && !nextChildren.IsEmpty
          ? BuildOrderedChildren(nextChildren, index)
          : null;

      Dictionary<string, string> predecessorByKey = null;
      if (newOrdered != null && newOrdered.Count > 0) {
        predecessorByKey = new Dictionary<string, string>(newOrdered.Count, StringComparer.Ordinal);
        string prev = null;
        for (int i = 0; i < newOrdered.Count; i++) {
          string k = newOrdered[i].Key;
          predecessorByKey[k] = prev;
          prev = k;
        }
      }

      if (!wasComplete) {
        if (newOrdered != null) {
          for (int i = 0; i < newOrdered.Count; i++) {
            NamedNode item = newOrdered[i];
            EmitForKind(
                EventKind.ChildAdded,
                new DataSnapshot(_database, Spec.Path.ChildKey(item.Key), item.Node, RtdbIndex.Priority),
                predecessorByKey[item.Key],
                outEvents);
          }
        }
        EmitValueChanged(nextFiltered, outEvents);
        return;
      }

      SortedDictionary<string, RtdbNode> oldMap = (prevFiltered as ChildrenNode)?.Children;
      SortedDictionary<string, RtdbNode> newMap = (nextFiltered as ChildrenNode)?.Children;

      // 1. ChildRemoved (sorted by query index on old child nodes)
      if (oldMap != null && oldMap.Count > 0) {
        List<NamedNode> removed = null;
        foreach (KeyValuePair<string, RtdbNode> kv in oldMap) {
          if (newMap == null || !newMap.ContainsKey(kv.Key)) {
            removed ??= new List<NamedNode>();
            removed.Add(new NamedNode(kv.Key, kv.Value));
          }
        }
        if (removed != null) {
          removed.Sort(index);
          for (int i = 0; i < removed.Count; i++) {
            NamedNode item = removed[i];
            EmitForKind(
                EventKind.ChildRemoved,
                new DataSnapshot(_database, Spec.Path.ChildKey(item.Key), item.Node, RtdbIndex.Priority),
                null,
                outEvents);
          }
        }
      }

      // 2. ChildAdded, 3. ChildMoved, 4. ChildChanged (all in newOrdered query index order!)
      if (newOrdered != null) {
        List<NamedNode> added = null;
        List<NamedNode> moved = null;
        List<NamedNode> changed = null;

        for (int i = 0; i < newOrdered.Count; i++) {
          NamedNode item = newOrdered[i];
          if (oldMap == null || !oldMap.TryGetValue(item.Key, out RtdbNode oldChild)) {
            added ??= new List<NamedNode>();
            added.Add(item);
          } else if (!oldChild.EqualsNode(item.Node)) {
            changed ??= new List<NamedNode>();
            changed.Add(item);
            if (index.IndexValueChanged(oldChild, item.Node)) {
              moved ??= new List<NamedNode>();
              moved.Add(item);
            }
          }
        }

        if (added != null) {
          for (int i = 0; i < added.Count; i++) {
            NamedNode item = added[i];
            EmitForKind(
                EventKind.ChildAdded,
                new DataSnapshot(_database, Spec.Path.ChildKey(item.Key), item.Node, RtdbIndex.Priority),
                predecessorByKey[item.Key],
                outEvents);
          }
        }

        if (moved != null) {
          for (int i = 0; i < moved.Count; i++) {
            NamedNode item = moved[i];
            EmitForKind(
                EventKind.ChildMoved,
                new DataSnapshot(_database, Spec.Path.ChildKey(item.Key), item.Node, RtdbIndex.Priority),
                predecessorByKey[item.Key],
                outEvents);
          }
        }

        if (changed != null) {
          for (int i = 0; i < changed.Count; i++) {
            NamedNode item = changed[i];
            EmitForKind(
                EventKind.ChildChanged,
                new DataSnapshot(_database, Spec.Path.ChildKey(item.Key), item.Node, RtdbIndex.Priority),
                predecessorByKey[item.Key],
                outEvents);
          }
        }
      }

      // 5. ValueChanged
      EmitValueChanged(nextFiltered, outEvents);
    }

    private void EmitForKind(EventKind kind, DataSnapshot snap, string prevName, List<DispatchedEvent> outEvents) {
      for (int i = 0; i < _registrations.Count; i++) {
        ListenerRegistration reg = _registrations[i];
        if (reg.Active && reg.Kind == kind) {
          outEvents.Add(new DispatchedEvent(reg, snap, prevName));
        }
      }
    }

    private void EmitValueChanged(RtdbNode node, List<DispatchedEvent> outEvents) {
      DataSnapshot snap = null;
      for (int i = 0; i < _registrations.Count; i++) {
        ListenerRegistration reg = _registrations[i];
        if (!reg.Active || reg.Kind != EventKind.ValueChanged) continue;
        if (reg.IsOneShot) {
          if (reg.OneShotDispatched) continue;
          reg.OneShotDispatched = true;
        }
        snap ??= new DataSnapshot(_database, Spec.Path, node, Spec.Params.Index);
        outEvents.Add(new DispatchedEvent(reg, snap));
      }
    }

    internal static List<NamedNode> BuildOrderedChildren(ChildrenNode children, RtdbIndex index) {
      var list = new List<NamedNode>(children.ChildrenCount);
      foreach (KeyValuePair<string, RtdbNode> kv in children.Children) {
        list.Add(new NamedNode(kv.Key, kv.Value));
      }
      if (index.OrderBy != QueryOrderBy.Key) {
        list.Sort(index);
      }
      return list;
    }
  }
}
