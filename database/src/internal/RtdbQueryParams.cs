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
  internal enum QueryOrderBy : byte {
    Priority = 0,
    Key = 1,
    Value = 2,
    Child = 3,
  }

  internal readonly struct NamedNode {
    internal string Key { get; }
    internal RtdbNode Node { get; }

    internal NamedNode(string key, RtdbNode node) {
      Key = key;
      Node = node ?? EmptyNode.Instance;
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RtdbIndex : IComparer<NamedNode> {
    internal static readonly RtdbIndex Priority = new RtdbIndex(QueryOrderBy.Priority, RtdbPath.Empty);
    internal static readonly RtdbIndex Key = new RtdbIndex(QueryOrderBy.Key, RtdbPath.Empty);
    internal static readonly RtdbIndex Value = new RtdbIndex(QueryOrderBy.Value, RtdbPath.Empty);

    internal QueryOrderBy OrderBy { get; }
    internal RtdbPath ChildPath { get; }

    internal RtdbIndex(QueryOrderBy orderBy, RtdbPath childPath) {
      OrderBy = orderBy;
      ChildPath = childPath;
    }

    internal static RtdbIndex ForChild(RtdbPath childPath) =>
        new RtdbIndex(QueryOrderBy.Child, childPath);

    public int Compare(NamedNode a, NamedNode b) {
      int valCmp = CompareIndexValue(a.Key, a.Node, b.Key, b.Node);
      if (valCmp != 0) return valCmp;
      return ChildKeyComparer.Instance.Compare(a.Key, b.Key);
    }

    internal int CompareIndexValue(string keyA, RtdbNode nodeA, string keyB, RtdbNode nodeB) {
      return OrderBy switch {
        QueryOrderBy.Priority => CompareNodeValues(nodeA.Priority, nodeB.Priority),
        QueryOrderBy.Key => ChildKeyComparer.Instance.Compare(keyA, keyB),
        QueryOrderBy.Value => CompareNodeValues(nodeA, nodeB),
        QueryOrderBy.Child => CompareNodeValues(nodeA.GetChild(ChildPath), nodeB.GetChild(ChildPath)),
        _ => 0,
      };
    }

    internal bool IndexValueChanged(RtdbNode oldChild, RtdbNode newChild) {
      return OrderBy switch {
        QueryOrderBy.Priority => CompareNodeValues(oldChild.Priority, newChild.Priority) != 0,
        QueryOrderBy.Key => false,
        QueryOrderBy.Value => CompareNodeValues(oldChild, newChild) != 0,
        QueryOrderBy.Child => CompareNodeValues(oldChild.GetChild(ChildPath), newChild.GetChild(ChildPath)) != 0,
        _ => false,
      };
    }

    internal RtdbNode ExtractIndexedValue(string childKey, RtdbNode childNode) {
      return OrderBy switch {
        QueryOrderBy.Priority => childNode.Priority,
        QueryOrderBy.Key => new LeafNode(childKey),
        QueryOrderBy.Value => childNode,
        QueryOrderBy.Child => childNode.GetChild(ChildPath),
        _ => EmptyNode.Instance,
      };
    }

    internal static int CompareNodeValues(RtdbNode a, RtdbNode b) {
      a ??= EmptyNode.Instance;
      b ??= EmptyNode.Instance;

      int precA = GetPrecedence(a);
      int precB = GetPrecedence(b);
      if (precA != precB) {
        return precA.CompareTo(precB);
      }

      return precA switch {
        0 => 0, // Both Empty/Null
        1 => ((LeafNode)a).BoolValue.CompareTo(((LeafNode)b).BoolValue),
        2 => CompareNumbers((LeafNode)a, (LeafNode)b),
        3 => string.CompareOrdinal(((LeafNode)a).StringValue, ((LeafNode)b).StringValue),
        4 => 0, // ChildrenNodes compare equal by value; tie-broken by key
        _ => 0,
      };
    }

    private static int GetPrecedence(RtdbNode node) {
      if (node.IsEmpty) return 0;
      if (node is LeafNode leaf) {
        return leaf.Kind switch {
          LeafKind.Bool => 1,
          LeafKind.Long => 2,
          LeafKind.Double => 2,
          LeafKind.String => 3,
          _ => 0,
        };
      }
      return 4;
    }

    private static int CompareNumbers(LeafNode a, LeafNode b) {
      if (a.Kind == LeafKind.Long && b.Kind == LeafKind.Long) {
        return a.LongValue.CompareTo(b.LongValue);
      }
      return a.DoubleValue.CompareTo(b.DoubleValue);
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RtdbQueryParams : IEquatable<RtdbQueryParams> {
    internal static readonly RtdbQueryParams Default = new RtdbQueryParams(
        orderBy: QueryOrderBy.Priority,
        hasExplicitOrderBy: false,
        orderByChildPath: RtdbPath.Empty,
        hasStart: false,
        startValue: EmptyNode.Instance,
        startKey: null,
        hasEnd: false,
        endValue: EmptyNode.Instance,
        endKey: null,
        limitFirst: 0,
        limitLast: 0);

    internal QueryOrderBy OrderBy { get; }
    internal bool HasExplicitOrderBy { get; }
    internal RtdbPath OrderByChildPath { get; }
    internal bool HasStart { get; }
    internal RtdbNode StartValue { get; }
    internal string StartKey { get; }
    internal bool HasEnd { get; }
    internal RtdbNode EndValue { get; }
    internal string EndKey { get; }
    internal int LimitFirst { get; }
    internal int LimitLast { get; }
    internal RtdbIndex Index { get; }

    private RtdbQueryParams(
        QueryOrderBy orderBy,
        bool hasExplicitOrderBy,
        RtdbPath orderByChildPath,
        bool hasStart,
        RtdbNode startValue,
        string startKey,
        bool hasEnd,
        RtdbNode endValue,
        string endKey,
        int limitFirst,
        int limitLast) {
      OrderBy = orderBy;
      HasExplicitOrderBy = hasExplicitOrderBy;
      OrderByChildPath = orderByChildPath;
      HasStart = hasStart;
      StartValue = startValue ?? EmptyNode.Instance;
      StartKey = startKey;
      HasEnd = hasEnd;
      EndValue = endValue ?? EmptyNode.Instance;
      EndKey = endKey;
      LimitFirst = limitFirst;
      LimitLast = limitLast;
      Index = orderBy switch {
        QueryOrderBy.Priority => RtdbIndex.Priority,
        QueryOrderBy.Key => RtdbIndex.Key,
        QueryOrderBy.Value => RtdbIndex.Value,
        QueryOrderBy.Child => RtdbIndex.ForChild(orderByChildPath),
        _ => RtdbIndex.Priority,
      };
    }

    internal bool HasLimit => LimitFirst > 0 || LimitLast > 0;
    internal bool LoadsAllData => !HasStart && !HasEnd && !HasLimit;
    internal bool IsDefault => LoadsAllData && OrderBy == QueryOrderBy.Priority;

    internal RtdbQueryParams WithOrderBy(QueryOrderBy orderBy, RtdbPath childPath) {
      if (HasExplicitOrderBy) {
        throw new ArgumentException("You can't combine multiple OrderBy calls!");
      }
      var next = new RtdbQueryParams(
          orderBy,
          hasExplicitOrderBy: true,
          childPath,
          HasStart,
          StartValue,
          StartKey,
          HasEnd,
          EndValue,
          EndKey,
          LimitFirst,
          LimitLast);
      next.ValidateEndpoints();
      return next;
    }

    internal RtdbQueryParams WithStartAt(RtdbNode value, string childKey, bool hasKeyArg) {
      if (HasStart) {
        throw new ArgumentException("Can't call StartAt() or EqualTo() multiple times.");
      }
      ValidateBoundKeyArg(childKey, hasKeyArg);
      var next = new RtdbQueryParams(
          OrderBy,
          HasExplicitOrderBy,
          OrderByChildPath,
          hasStart: true,
          startValue: value,
          startKey: childKey,
          HasEnd,
          EndValue,
          EndKey,
          LimitFirst,
          LimitLast);
      next.ValidateEndpoints();
      return next;
    }

    internal RtdbQueryParams WithEndAt(RtdbNode value, string childKey, bool hasKeyArg) {
      if (HasEnd) {
        throw new ArgumentException("Can't call EndAt() or EqualTo() multiple times.");
      }
      ValidateBoundKeyArg(childKey, hasKeyArg);
      var next = new RtdbQueryParams(
          OrderBy,
          HasExplicitOrderBy,
          OrderByChildPath,
          HasStart,
          StartValue,
          StartKey,
          hasEnd: true,
          endValue: value,
          endKey: childKey,
          LimitFirst,
          LimitLast);
      next.ValidateEndpoints();
      return next;
    }

    internal RtdbQueryParams WithEqualTo(RtdbNode value, string childKey, bool hasKeyArg) {
      if (HasStart || HasEnd) {
        throw new ArgumentException("Can't combine EqualTo() with StartAt() or EndAt().");
      }
      ValidateBoundKeyArg(childKey, hasKeyArg);
      var next = new RtdbQueryParams(
          OrderBy,
          HasExplicitOrderBy,
          OrderByChildPath,
          hasStart: true,
          startValue: value,
          startKey: childKey,
          hasEnd: true,
          endValue: value,
          endKey: childKey,
          LimitFirst,
          LimitLast);
      next.ValidateEndpoints();
      return next;
    }

    internal RtdbQueryParams WithLimitToFirst(int limit) {
      if (limit <= 0) {
        throw new ArgumentException("Limit must be a positive integer.", nameof(limit));
      }
      if (HasLimit) {
        throw new ArgumentException("Can't call LimitToFirst() or LimitToLast() multiple times.");
      }
      return new RtdbQueryParams(
          OrderBy,
          HasExplicitOrderBy,
          OrderByChildPath,
          HasStart,
          StartValue,
          StartKey,
          HasEnd,
          EndValue,
          EndKey,
          limitFirst: limit,
          limitLast: 0);
    }

    internal RtdbQueryParams WithLimitToLast(int limit) {
      if (limit <= 0) {
        throw new ArgumentException("Limit must be a positive integer.", nameof(limit));
      }
      if (HasLimit) {
        throw new ArgumentException("Can't call LimitToFirst() or LimitToLast() multiple times.");
      }
      return new RtdbQueryParams(
          OrderBy,
          HasExplicitOrderBy,
          OrderByChildPath,
          HasStart,
          StartValue,
          StartKey,
          HasEnd,
          EndValue,
          EndKey,
          limitFirst: 0,
          limitLast: limit);
    }

    internal bool MatchesBounds(string childKey, RtdbNode childNode) {
      if (HasStart) {
        if (OrderBy == QueryOrderBy.Key) {
          string startStr = (StartValue as LeafNode)?.StringValue ?? string.Empty;
          if (ChildKeyComparer.Instance.Compare(childKey, startStr) < 0) return false;
        } else {
          RtdbNode extracted = Index.ExtractIndexedValue(childKey, childNode);
          int cmp = RtdbIndex.CompareNodeValues(extracted, StartValue);
          if (cmp < 0) return false;
          if (cmp == 0 && StartKey != null && ChildKeyComparer.Instance.Compare(childKey, StartKey) < 0) {
            return false;
          }
        }
      }

      if (HasEnd) {
        if (OrderBy == QueryOrderBy.Key) {
          string endStr = (EndValue as LeafNode)?.StringValue ?? string.Empty;
          if (ChildKeyComparer.Instance.Compare(childKey, endStr) > 0) return false;
        } else {
          RtdbNode extracted = Index.ExtractIndexedValue(childKey, childNode);
          int cmp = RtdbIndex.CompareNodeValues(extracted, EndValue);
          if (cmp > 0) return false;
          if (cmp == 0 && EndKey != null && ChildKeyComparer.Instance.Compare(childKey, EndKey) > 0) {
            return false;
          }
        }
      }

      return true;
    }

    internal RtdbNode ApplyFilter(RtdbNode fullNode) {
      fullNode ??= EmptyNode.Instance;
      if (LoadsAllData) {
        return fullNode;
      }
      if (fullNode is not ChildrenNode childrenNode || childrenNode.IsEmpty) {
        return EmptyNode.Instance;
      }

      if (!HasLimit) {
        var ranged = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance);
        foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
          if (MatchesBounds(kv.Key, kv.Value)) {
            ranged[kv.Key] = kv.Value;
          }
        }
        return ranged.Count == 0 ? EmptyNode.Instance : new ChildrenNode(ranged, EmptyNode.Instance);
      }

      // Bounded SortedSet keeps memory O(limit) and avoids LOH even on huge child maps.
      int cap = LimitFirst > 0 ? LimitFirst : LimitLast;
      bool keepFirst = LimitFirst > 0;
      var window = new SortedSet<NamedNode>(Index);
      foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
        if (!MatchesBounds(kv.Key, kv.Value)) continue;
        var candidate = new NamedNode(kv.Key, kv.Value);
        if (window.Count < cap) {
          window.Add(candidate);
        } else if (keepFirst) {
          NamedNode max = window.Max;
          if (Index.Compare(candidate, max) < 0) {
            window.Remove(max);
            window.Add(candidate);
          }
        } else {
          NamedNode min = window.Min;
          if (Index.Compare(candidate, min) > 0) {
            window.Remove(min);
            window.Add(candidate);
          }
        }
      }

      if (window.Count == 0) return EmptyNode.Instance;
      var limited = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance);
      foreach (NamedNode item in window) {
        limited[item.Key] = item.Node;
      }
      return new ChildrenNode(limited, EmptyNode.Instance);
    }

    internal Dictionary<string, object> ToWireQueryParams() {
      var wire = new Dictionary<string, object>();
      if (HasStart) {
        wire["sp"] = StartValue.ToWireValue();
        if (StartKey != null) {
          wire["sn"] = StartKey;
        }
      }
      if (HasEnd) {
        wire["ep"] = EndValue.ToWireValue();
        if (EndKey != null) {
          wire["en"] = EndKey;
        }
      }
      if (LimitFirst > 0) {
        wire["l"] = (long)LimitFirst;
        wire["vf"] = "l";
      } else if (LimitLast > 0) {
        wire["l"] = (long)LimitLast;
        wire["vf"] = "r";
      }
      if (OrderBy != QueryOrderBy.Priority) {
        wire["i"] = OrderBy switch {
          QueryOrderBy.Key => ".key",
          QueryOrderBy.Value => ".value",
          QueryOrderBy.Child => OrderByChildPath.ToWireString(),
          _ => ".priority",
        };
      }
      return wire;
    }

    private static void ValidateBoundKeyArg(string childKey, bool hasKeyArg) {
      if (!hasKeyArg) return;
      if (string.IsNullOrEmpty(childKey) || !RtdbPath.IsValidKey(childKey, allowMeta: false)) {
        throw new ArgumentException("Invalid key argument passed to StartAt, EndAt, or EqualTo.", nameof(childKey));
      }
    }

    private void ValidateEndpoints() {
      if (OrderBy == QueryOrderBy.Key) {
        if ((HasStart && (StartKey != null || StartValue is not LeafNode { Kind: LeafKind.String })) ||
            (HasEnd && (EndKey != null || EndValue is not LeafNode { Kind: LeafKind.String }))) {
          throw new ArgumentException(
              "You must use StartAt(string), EndAt(string), or EqualTo(string) in combination with OrderByKey().");
        }
      } else if (OrderBy == QueryOrderBy.Priority) {
        if ((HasStart && !IsValidPriorityBound(StartValue)) ||
            (HasEnd && !IsValidPriorityBound(EndValue))) {
          throw new ArgumentException(
              "When using OrderByPriority(), values provided to StartAt(), EndAt(), or EqualTo() must be valid priorities (string, double, or null).");
        }
      }
    }

    private static bool IsValidPriorityBound(RtdbNode node) =>
        node.IsEmpty || (node is LeafNode leaf &&
            (leaf.Kind == LeafKind.Double || leaf.Kind == LeafKind.Long || leaf.Kind == LeafKind.String));

    public bool Equals(RtdbQueryParams other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null) return false;
      return OrderBy == other.OrderBy
          && OrderByChildPath.Equals(other.OrderByChildPath)
          && HasStart == other.HasStart
          && StartValue.EqualsNode(other.StartValue)
          && string.Equals(StartKey, other.StartKey, StringComparison.Ordinal)
          && HasEnd == other.HasEnd
          && EndValue.EqualsNode(other.EndValue)
          && string.Equals(EndKey, other.EndKey, StringComparison.Ordinal)
          && LimitFirst == other.LimitFirst
          && LimitLast == other.LimitLast;
    }

    public override bool Equals(object obj) => obj is RtdbQueryParams other && Equals(other);

    public override int GetHashCode() {
      int h = (int)OrderBy;
      h = unchecked(h * 31 + OrderByChildPath.GetHashCode());
      h = unchecked(h * 31 + (HasStart ? 1 : 0));
      h = unchecked(h * 31 + (StartValue.GetHash()?.GetHashCode() ?? 0));
      h = unchecked(h * 31 + (StartKey != null ? StringComparer.Ordinal.GetHashCode(StartKey) : 0));
      h = unchecked(h * 31 + (HasEnd ? 1 : 0));
      h = unchecked(h * 31 + (EndValue.GetHash()?.GetHashCode() ?? 0));
      h = unchecked(h * 31 + (EndKey != null ? StringComparer.Ordinal.GetHashCode(EndKey) : 0));
      h = unchecked(h * 31 + LimitFirst);
      h = unchecked(h * 31 + LimitLast);
      return h;
    }
  }

  internal readonly struct QuerySpec : IEquatable<QuerySpec> {
    internal RtdbPath Path { get; }
    internal RtdbQueryParams Params { get; }

    internal QuerySpec(RtdbPath path, RtdbQueryParams queryParams = null) {
      Path = path;
      Params = queryParams ?? RtdbQueryParams.Default;
    }

    internal bool LoadsAllData => Params.LoadsAllData;
    internal bool IsDefault => Params.IsDefault;

    public bool Equals(QuerySpec other) => Path.Equals(other.Path) && Params.Equals(other.Params);
    public override bool Equals(object obj) => obj is QuerySpec other && Equals(other);
    public override int GetHashCode() => unchecked(Path.GetHashCode() * 397 ^ Params.GetHashCode());
    public override string ToString() => $"{Path.ToWireString()}|order={Params.OrderBy}|l1={Params.LimitFirst}|l2={Params.LimitLast}";
  }
}
