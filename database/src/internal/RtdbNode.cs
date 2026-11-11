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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Firebase.Database.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class ChildKeyComparer : IComparer<string> {
    internal const string MinKey = "[MIN_KEY]";
    internal const string MaxKey = "[MAX_KEY]";
    internal static readonly ChildKeyComparer Instance = new ChildKeyComparer();

    private ChildKeyComparer() { }

    public int Compare(string left, string right) {
      if (ReferenceEquals(left, right) || string.Equals(left, right, StringComparison.Ordinal)) {
        return 0;
      }
      if (left == null || string.Equals(left, MinKey, StringComparison.Ordinal) ||
          string.Equals(right, MaxKey, StringComparison.Ordinal)) {
        return -1;
      }
      if (right == null || string.Equals(right, MinKey, StringComparison.Ordinal) ||
          string.Equals(left, MaxKey, StringComparison.Ordinal)) {
        return 1;
      }

      bool leftIsInt = TryParseCanonicalInt32(left, out int leftInt);
      bool rightIsInt = TryParseCanonicalInt32(right, out int rightInt);
      if (leftIsInt) {
        if (rightIsInt) {
          int cmp = leftInt.CompareTo(rightInt);
          return cmp != 0 ? cmp : left.Length.CompareTo(right.Length);
        }
        return -1;
      }
      if (rightIsInt) {
        return 1;
      }
      return string.CompareOrdinal(left, right);
    }

    internal static bool TryParseCanonicalInt32(string s, out int value) {
      value = 0;
      if (string.IsNullOrEmpty(s) || s.Length > 11) return false;
      bool negative = false;
      int idx = 0;
      if (s[0] == '-') {
        if (s.Length == 1) return false;
        negative = true;
        idx = 1;
      }
      if (s[idx] == '0') {
        if (negative || s.Length != 1) return false;
        value = 0;
        return true;
      }
      long acc = 0;
      for (int i = idx; i < s.Length; i++) {
        char c = s[i];
        if (c < '0' || c > '9') return false;
        acc = acc * 10 + (c - '0');
      }
      if (negative) {
        acc = -acc;
        if (acc < int.MinValue) return false;
      } else if (acc > int.MaxValue) {
        return false;
      }
      value = (int)acc;
      return true;
    }

    internal static bool TryParseArrayIndex(string s, out int index) {
      if (TryParseCanonicalInt32(s, out int val) && val >= 0) {
        index = val;
        return true;
      }
      index = -1;
      return false;
    }
  }

  internal enum LeafKind : byte {
    Bool = 1,
    Long = 2,
    Double = 3,
    String = 4,
    DeferredValue = 5,
  }

  internal abstract class RtdbNode {
    private volatile string _cachedHash;

    internal abstract bool IsEmpty { get; }
    internal abstract bool IsLeaf { get; }
    internal abstract RtdbNode Priority { get; }
    internal abstract int ChildrenCount { get; }

    internal abstract RtdbNode GetImmediateChild(string key);
    internal abstract RtdbNode UpdateImmediateChild(string key, RtdbNode child);
    internal abstract RtdbNode WithPriority(RtdbNode priority);
    internal abstract object GetValue(bool useExportFormat);
    internal abstract object ToWireValue();
    internal abstract bool EqualsNode(RtdbNode other);
    protected abstract void AppendHashRepresentation(StringBuilder sb);

    internal RtdbNode GetChild(RtdbPath path) {
      RtdbNode current = this;
      for (int i = 0; i < path.Count && !current.IsEmpty; i++) {
        current = current.GetImmediateChild(path[i]);
      }
      return current;
    }

    internal RtdbNode UpdateChild(RtdbPath path, RtdbNode newChild) {
      newChild ??= EmptyNode.Instance;
      if (path.IsEmpty) {
        return newChild;
      }
      string front = path.Front;
      if (front == ".priority") {
        return WithPriority(newChild);
      }
      RtdbNode existingChild = GetImmediateChild(front);
      RtdbNode updatedChild = existingChild.UpdateChild(path.PopFront(), newChild);
      return UpdateImmediateChild(front, updatedChild);
    }

    internal string GetHash() {
      string cached = _cachedHash;
      if (cached != null) return cached;
      if (IsEmpty) {
        _cachedHash = string.Empty;
        return string.Empty;
      }
      var sb = new StringBuilder(128);
      AppendHashRepresentation(sb);
      if (sb.Length == 0) {
        _cachedHash = string.Empty;
        return string.Empty;
      }
      byte[] utf8 = Encoding.UTF8.GetBytes(sb.ToString());
      byte[] digest;
      using (SHA1 sha1 = SHA1.Create()) {
        digest = sha1.ComputeHash(utf8);
      }
      string computed = Convert.ToBase64String(digest);
      _cachedHash = computed;
      return computed;
    }

    internal static void AppendPriorityHash(StringBuilder sb, RtdbNode priority) {
      if (priority is LeafNode leaf && !leaf.IsEmpty) {
        sb.Append("priority:");
        leaf.AppendScalarHash(sb);
        sb.Append(':');
      }
    }

    internal static void AppendDoubleHex(StringBuilder sb, double val) {
      if (val == 0.0) val = 0.0; // Normalize -0.0 to +0.0
      ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(val));
      for (int i = 7; i >= 0; i--) {
        byte b = (byte)((bits >> (8 * i)) & 0xFF);
        int hi = (b >> 4) & 0x0F;
        int lo = b & 0x0F;
        sb.Append((char)(hi < 10 ? '0' + hi : 'a' + hi - 10));
        sb.Append((char)(lo < 10 ? '0' + lo : 'a' + lo - 10));
      }
    }
  }

#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class EmptyNode : RtdbNode {
    internal static readonly EmptyNode Instance = new EmptyNode();

    private EmptyNode() { }

    internal override bool IsEmpty => true;
    internal override bool IsLeaf => false;
    internal override RtdbNode Priority => Instance;
    internal override int ChildrenCount => 0;

    internal override RtdbNode GetImmediateChild(string key) => Instance;

    internal override RtdbNode UpdateImmediateChild(string key, RtdbNode child) {
      if (child == null || child.IsEmpty || key == ".priority") {
        return Instance;
      }
      var map = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance) {
        [key] = child,
      };
      return new ChildrenNode(map, Instance);
    }

    internal override RtdbNode WithPriority(RtdbNode priority) => Instance;
    internal override object GetValue(bool useExportFormat) => null;
    internal override object ToWireValue() => null;
    internal override bool EqualsNode(RtdbNode other) => other != null && other.IsEmpty;
    protected override void AppendHashRepresentation(StringBuilder sb) { }
  }

  internal sealed class LeafNode : RtdbNode {
    private readonly bool _boolVal;
    private readonly long _longVal;
    private readonly double _doubleVal;
    private readonly string _strVal;
    private readonly Dictionary<string, object> _deferredVal;
    private readonly RtdbNode _priority;

    internal LeafKind Kind { get; }

    internal LeafNode(bool value, RtdbNode priority = null) {
      Kind = LeafKind.Bool;
      _boolVal = value;
      _priority = NormalizePriorityNode(priority);
    }

    internal LeafNode(long value, RtdbNode priority = null) {
      Kind = LeafKind.Long;
      _longVal = value;
      _priority = NormalizePriorityNode(priority);
    }

    internal LeafNode(double value, RtdbNode priority = null) {
      Kind = LeafKind.Double;
      _doubleVal = value;
      _priority = NormalizePriorityNode(priority);
    }

    internal LeafNode(string value, RtdbNode priority = null) {
      Kind = LeafKind.String;
      _strVal = value ?? string.Empty;
      _priority = NormalizePriorityNode(priority);
    }

    internal LeafNode(Dictionary<string, object> deferredValue, RtdbNode priority = null) {
      Kind = LeafKind.DeferredValue;
      _deferredVal = deferredValue;
      _priority = NormalizePriorityNode(priority);
    }

    internal bool BoolValue => _boolVal;
    internal long LongValue => _longVal;
    internal double DoubleValue => Kind == LeafKind.Long ? _longVal : _doubleVal;
    internal string StringValue => _strVal;
    internal Dictionary<string, object> DeferredMap => _deferredVal;

    internal override bool IsEmpty => false;
    internal override bool IsLeaf => true;
    internal override RtdbNode Priority => _priority;
    internal override int ChildrenCount => 0;

    internal object ScalarObject => Kind switch {
      LeafKind.Bool => _boolVal,
      LeafKind.Long => _longVal,
      LeafKind.Double => _doubleVal,
      LeafKind.String => _strVal,
      LeafKind.DeferredValue => CloneDeferredMap(_deferredVal),
      _ => null,
    };

    internal override RtdbNode GetImmediateChild(string key) =>
        key == ".priority" ? _priority : EmptyNode.Instance;

    internal override RtdbNode UpdateImmediateChild(string key, RtdbNode child) {
      child ??= EmptyNode.Instance;
      if (key == ".priority") {
        return WithPriority(child);
      }
      if (child.IsEmpty) {
        return this;
      }
      var map = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance) {
        [key] = child,
      };
      return new ChildrenNode(map, _priority);
    }

    internal override RtdbNode WithPriority(RtdbNode priority) {
      RtdbNode norm = NormalizePriorityNode(priority);
      if (_priority.EqualsNode(norm)) return this;
      return Kind switch {
        LeafKind.Bool => new LeafNode(_boolVal, norm),
        LeafKind.Long => new LeafNode(_longVal, norm),
        LeafKind.Double => new LeafNode(_doubleVal, norm),
        LeafKind.String => new LeafNode(_strVal, norm),
        LeafKind.DeferredValue => new LeafNode(_deferredVal, norm),
        _ => this,
      };
    }

    internal override object GetValue(bool useExportFormat) {
      object val = ScalarObject;
      if (!useExportFormat || _priority.IsEmpty) {
        return val;
      }
      return new Dictionary<string, object>(2) {
        [".value"] = val,
        [".priority"] = _priority.GetValue(false),
      };
    }

    internal override object ToWireValue() {
      object val = ScalarObject;
      if (_priority.IsEmpty) {
        return val;
      }
      return new Dictionary<string, object>(2) {
        [".value"] = val,
        [".priority"] = _priority.ToWireValue(),
      };
    }

    internal override bool EqualsNode(RtdbNode other) {
      if (ReferenceEquals(this, other)) return true;
      if (other is not LeafNode o) return false;
      if (!_priority.EqualsNode(o._priority)) return false;
      if (Kind == o.Kind) {
        return Kind switch {
          LeafKind.Bool => _boolVal == o._boolVal,
          LeafKind.Long => _longVal == o._longVal,
          LeafKind.Double => BitConverter.DoubleToInt64Bits(_doubleVal == 0.0 ? 0.0 : _doubleVal)
              == BitConverter.DoubleToInt64Bits(o._doubleVal == 0.0 ? 0.0 : o._doubleVal),
          LeafKind.String => string.Equals(_strVal, o._strVal, StringComparison.Ordinal),
          LeafKind.DeferredValue => ReferenceEquals(_deferredVal, o._deferredVal),
          _ => false,
        };
      }
      // Note: long(42) vs double(42.0) have distinct C# types in DataSnapshot.Value
      // (as verified by TestServerValuePriority), so keep Long and Double distinct in EqualsNode!
      return false;
    }

    internal void AppendScalarHash(StringBuilder sb) {
      switch (Kind) {
        case LeafKind.Bool:
          sb.Append("boolean:").Append(_boolVal ? "true" : "false");
          break;
        case LeafKind.Long:
          sb.Append("number:");
          AppendDoubleHex(sb, (double)_longVal);
          break;
        case LeafKind.Double:
          sb.Append("number:");
          AppendDoubleHex(sb, _doubleVal);
          break;
        case LeafKind.String:
          sb.Append("string:").Append(_strVal);
          break;
      }
    }

    protected override void AppendHashRepresentation(StringBuilder sb) {
      AppendPriorityHash(sb, _priority);
      AppendScalarHash(sb);
    }

    internal static RtdbNode NormalizePriorityNode(RtdbNode priority) {
      if (priority == null || priority.IsEmpty) return EmptyNode.Instance;
      if (priority is LeafNode leaf) {
        if (!leaf.Priority.IsEmpty) {
          return leaf.WithPriority(EmptyNode.Instance);
        }
        return leaf;
      }
      return EmptyNode.Instance;
    }

    private static Dictionary<string, object> CloneDeferredMap(Dictionary<string, object> src) {
      if (src == null) return null;
      var copy = new Dictionary<string, object>(src.Count);
      foreach (KeyValuePair<string, object> kv in src) {
        copy[kv.Key] = kv.Value is Dictionary<string, object> inner
            ? CloneDeferredMap(inner)
            : kv.Value;
      }
      return copy;
    }
  }

  internal sealed class ChildrenNode : RtdbNode {
    private const int MaxDenseArrayIndex = 100000;
    private readonly SortedDictionary<string, RtdbNode> _children;
    private readonly RtdbNode _priority;

    internal ChildrenNode(SortedDictionary<string, RtdbNode> children, RtdbNode priority = null) {
      _children = children;
      _priority = children.Count == 0 ? EmptyNode.Instance : LeafNode.NormalizePriorityNode(priority);
    }

    internal SortedDictionary<string, RtdbNode> Children => _children;
    internal override bool IsEmpty => _children.Count == 0;
    internal override bool IsLeaf => false;
    internal override RtdbNode Priority => _priority;
    internal override int ChildrenCount => _children.Count;

    internal override RtdbNode GetImmediateChild(string key) {
      if (key == ".priority") return _priority;
      if (string.IsNullOrEmpty(key)) return EmptyNode.Instance;
      return _children.TryGetValue(key, out RtdbNode child) ? child : EmptyNode.Instance;
    }

    internal override RtdbNode UpdateImmediateChild(string key, RtdbNode child) {
      child ??= EmptyNode.Instance;
      if (key == ".priority") {
        return WithPriority(child);
      }
      if (child.IsEmpty) {
        if (!_children.ContainsKey(key)) return this;
        if (_children.Count == 1) return EmptyNode.Instance;
        var nextRemoved = new SortedDictionary<string, RtdbNode>(_children, ChildKeyComparer.Instance);
        nextRemoved.Remove(key);
        return new ChildrenNode(nextRemoved, _priority);
      }
      if (_children.TryGetValue(key, out RtdbNode cur) && cur.EqualsNode(child)) {
        return this;
      }
      var next = new SortedDictionary<string, RtdbNode>(_children, ChildKeyComparer.Instance) {
        [key] = child,
      };
      return new ChildrenNode(next, _priority);
    }

    internal override RtdbNode WithPriority(RtdbNode priority) {
      if (_children.Count == 0) return EmptyNode.Instance;
      RtdbNode norm = LeafNode.NormalizePriorityNode(priority);
      return _priority.EqualsNode(norm) ? this : new ChildrenNode(_children, norm);
    }

    internal override object GetValue(bool useExportFormat) {
      if (_children.Count == 0) return null;

      if (!useExportFormat) {
        int maxIndex = -1;
        bool canConvertArray = true;
        foreach (KeyValuePair<string, RtdbNode> kv in _children) {
          if (!ChildKeyComparer.TryParseArrayIndex(kv.Key, out int idx)) {
            canConvertArray = false;
            break;
          }
          if (idx > maxIndex) maxIndex = idx;
        }
        if (canConvertArray && maxIndex >= 0 && maxIndex < 2L * _children.Count && maxIndex <= MaxDenseArrayIndex) {
          var list = new List<object>(maxIndex + 1);
          for (int i = 0; i <= maxIndex; i++) {
            list.Add(null);
          }
          foreach (KeyValuePair<string, RtdbNode> kv in _children) {
            ChildKeyComparer.TryParseArrayIndex(kv.Key, out int idx);
            list[idx] = kv.Value.GetValue(false);
          }
          return list;
        }
      }

      var dict = new Dictionary<string, object>(_children.Count + (!_priority.IsEmpty && useExportFormat ? 1 : 0));
      if (useExportFormat && !_priority.IsEmpty) {
        dict[".priority"] = _priority.GetValue(false);
      }
      foreach (KeyValuePair<string, RtdbNode> kv in _children) {
        dict[kv.Key] = kv.Value.GetValue(useExportFormat);
      }
      return dict;
    }

    internal override object ToWireValue() {
      if (_children.Count == 0) return null;
      var dict = new Dictionary<string, object>(_children.Count + (_priority.IsEmpty ? 0 : 1));
      if (!_priority.IsEmpty) {
        dict[".priority"] = _priority.ToWireValue();
      }
      foreach (KeyValuePair<string, RtdbNode> kv in _children) {
        dict[kv.Key] = kv.Value.ToWireValue();
      }
      return dict;
    }

    internal override bool EqualsNode(RtdbNode other) {
      if (ReferenceEquals(this, other)) return true;
      if (other is not ChildrenNode o) return false;
      if (_children.Count != o._children.Count || !_priority.EqualsNode(o._priority)) {
        return false;
      }
      using SortedDictionary<string, RtdbNode>.Enumerator e1 = _children.GetEnumerator();
      using SortedDictionary<string, RtdbNode>.Enumerator e2 = o._children.GetEnumerator();
      while (e1.MoveNext() && e2.MoveNext()) {
        if (!string.Equals(e1.Current.Key, e2.Current.Key, StringComparison.Ordinal) ||
            !e1.Current.Value.EqualsNode(e2.Current.Value)) {
          return false;
        }
      }
      return true;
    }

    protected override void AppendHashRepresentation(StringBuilder sb) {
      AppendPriorityHash(sb, _priority);

      bool sawChildPriority = false;
      foreach (KeyValuePair<string, RtdbNode> kv in _children) {
        if (!kv.Value.Priority.IsEmpty) {
          sawChildPriority = true;
          break;
        }
      }

      if (!sawChildPriority) {
        foreach (KeyValuePair<string, RtdbNode> kv in _children) {
          string childHash = kv.Value.GetHash();
          if (childHash.Length > 0) {
            sb.Append(':').Append(kv.Key).Append(':').Append(childHash);
          }
        }
      } else {
        var sorted = new SortedSet<NamedNode>(RtdbIndex.Priority);
        foreach (KeyValuePair<string, RtdbNode> kv in _children) {
          sorted.Add(new NamedNode(kv.Key, kv.Value));
        }
        foreach (NamedNode item in sorted) {
          string childHash = item.Node.GetHash();
          if (childHash.Length > 0) {
            sb.Append(':').Append(item.Key).Append(':').Append(childHash);
          }
        }
      }
    }
  }

  internal static class RtdbNodeFactory {
    internal static RtdbNode NodeFromObject(object value, RtdbNode explicitPriority = null) =>
        NodeFromCore(value, explicitPriority, fromServerWire: false);

    internal static RtdbNode NodeFromServerWire(object wireValue) =>
        NodeFromCore(wireValue, null, fromServerWire: true);

    internal static RtdbNode ParsePriority(object priority, bool fromServerWire = false) {
      if (priority == null) return EmptyNode.Instance;
      return priority switch {
        double d => ValidatePriorityDouble(d),
        float f => ValidatePriorityDouble(f),
        long l => ValidatePriorityDouble(l),
        int i => ValidatePriorityDouble(i),
        short s => ValidatePriorityDouble(s),
        byte b => ValidatePriorityDouble(b),
        sbyte sb => ValidatePriorityDouble(sb),
        ushort us => ValidatePriorityDouble(us),
        uint ui => ValidatePriorityDouble(ui),
        ulong ul => ValidatePriorityDouble(ul),
        decimal dec => ValidatePriorityDouble((double)dec),
        string str => new LeafNode(str),
        IDictionary dict when TryParseDeferredPlaceholder(dict, out Dictionary<string, object> sv) &&
            IsTimestampPlaceholder(sv) => new LeafNode(sv),
        _ => fromServerWire
            ? EmptyNode.Instance
            : throw new DatabaseException(
                "Invalid Firebase Database priority (must be a string, double, ServerValue, or null)"),
      };
    }

    private static RtdbNode ValidatePriorityDouble(double d) {
      if (double.IsNaN(d) || double.IsInfinity(d)) {
        throw new DatabaseException("Invalid Firebase Database priority: NaN or Infinity is not allowed.");
      }
      return new LeafNode(d);
    }

    private static RtdbNode NodeFromCore(object value, RtdbNode explicitPriority, bool fromServerWire) {
      RtdbNode prio = explicitPriority ?? EmptyNode.Instance;
      if (value == null) return EmptyNode.Instance;

      switch (value) {
        case RtdbNode existing:
          return prio.IsEmpty ? existing : existing.WithPriority(prio);
        case bool b:
          return new LeafNode(b, prio);
        case long l:
          return new LeafNode(l, prio);
        case int i:
          return new LeafNode((long)i, prio);
        case short s:
          return new LeafNode((long)s, prio);
        case sbyte sb:
          return new LeafNode((long)sb, prio);
        case byte by:
          return new LeafNode((long)by, prio);
        case ushort us:
          return new LeafNode((long)us, prio);
        case uint ui:
          return new LeafNode((long)ui, prio);
        case ulong ul:
          return ul <= (ulong)long.MaxValue
              ? new LeafNode((long)ul, prio)
              : new LeafNode((double)ul, prio);
        case float f:
          return ValidateLeafDouble(f, prio);
        case double d:
          return ValidateLeafDouble(d, prio);
        case decimal dec:
          return ValidateLeafDouble((double)dec, prio);
        case string str:
          return new LeafNode(str, prio);
        case IDictionary<string, object> genericDict:
          return NodeFromStringDictionary(genericDict, prio, fromServerWire);
        case IDictionary legacyDict:
          return NodeFromLegacyDictionary(legacyDict, prio, fromServerWire);
        case IList list:
          return NodeFromList(list, prio, fromServerWire);
        default:
          throw new DatabaseException($"Failed to parse object of unsupported type {value.GetType().FullName}");
      }
    }

    private static RtdbNode ValidateLeafDouble(double d, RtdbNode prio) {
      if (double.IsNaN(d) || double.IsInfinity(d)) {
        throw new DatabaseException("Invalid double value (NaN or Infinity) in Firebase Database.");
      }
      return new LeafNode(d, prio);
    }

    private static RtdbNode NodeFromStringDictionary(
        IDictionary<string, object> dict,
        RtdbNode explicitPriority,
        bool fromServerWire) {
      if (dict.Count == 0) return EmptyNode.Instance;

      if (TryParseDeferredStringMap(dict, out Dictionary<string, object> svMap)) {
        return new LeafNode(svMap, explicitPriority);
      }

      RtdbNode prio = explicitPriority;
      if (dict.TryGetValue(".priority", out object rawPrio)) {
        prio = ParsePriority(rawPrio, fromServerWire);
      }

      if (dict.TryGetValue(".value", out object rawVal)) {
        if (rawVal == null) return EmptyNode.Instance;
        return NodeFromCore(rawVal, prio, fromServerWire);
      }

      var children = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance);
      foreach (KeyValuePair<string, object> kv in dict) {
        if (kv.Key == null) {
          throw new DatabaseException("Dictionary key in Firebase Database cannot be null.");
        }
        if (kv.Key == ".priority") continue;
        if (!fromServerWire && !RtdbPath.IsValidKey(kv.Key, allowMeta: false)) {
          throw new DatabaseException($"Invalid Firebase Database key '{kv.Key}'.");
        }
        RtdbNode child = NodeFromCore(kv.Value, null, fromServerWire);
        if (!child.IsEmpty) {
          children[kv.Key] = child;
        }
      }

      return children.Count == 0 ? EmptyNode.Instance : new ChildrenNode(children, prio);
    }

    private static RtdbNode NodeFromLegacyDictionary(
        IDictionary dict,
        RtdbNode explicitPriority,
        bool fromServerWire) {
      if (dict.Count == 0) return EmptyNode.Instance;
      var typed = new Dictionary<string, object>(dict.Count);
      foreach (DictionaryEntry entry in dict) {
        if (entry.Key is not string keyStr) {
          throw new DatabaseException($"Firebase Database map keys must be strings, got {entry.Key?.GetType().FullName ?? "null"}.");
        }
        typed[keyStr] = entry.Value;
      }
      return NodeFromStringDictionary(typed, explicitPriority, fromServerWire);
    }

    private static RtdbNode NodeFromList(IList list, RtdbNode prio, bool fromServerWire) {
      if (list.Count == 0) return EmptyNode.Instance;
      var children = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance);
      for (int i = 0; i < list.Count; i++) {
        object elem = list[i];
        if (elem == null) continue;
        RtdbNode child = NodeFromCore(elem, null, fromServerWire);
        if (!child.IsEmpty) {
          children[i.ToString(CultureInfo.InvariantCulture)] = child;
        }
      }
      return children.Count == 0 ? EmptyNode.Instance : new ChildrenNode(children, prio);
    }

    private static bool TryParseDeferredPlaceholder(IDictionary dict, out Dictionary<string, object> normalized) {
      normalized = null;
      if (dict == null || dict.Count != 1 || !dict.Contains(".sv")) return false;
      return ValidateServerValueOp(dict[".sv"], out normalized);
    }

    private static bool TryParseDeferredStringMap(IDictionary<string, object> dict, out Dictionary<string, object> normalized) {
      normalized = null;
      if (dict == null || dict.Count != 1 || !dict.TryGetValue(".sv", out object op)) return false;
      return ValidateServerValueOp(op, out normalized);
    }

    private static bool ValidateServerValueOp(object op, out Dictionary<string, object> normalized) {
      normalized = null;
      if (op is string s && string.Equals(s, "timestamp", StringComparison.Ordinal)) {
        normalized = new Dictionary<string, object>(1) { [".sv"] = "timestamp" };
        return true;
      }
      if (op is IDictionary<string, object> gMap && gMap.Count == 1 && gMap.TryGetValue("increment", out object delta)) {
        object normDelta = NormalizeIncrementDelta(delta);
        normalized = new Dictionary<string, object>(1) {
          [".sv"] = new Dictionary<string, object>(1) { ["increment"] = normDelta },
        };
        return true;
      }
      if (op is IDictionary lMap && lMap.Count == 1 && lMap.Contains("increment")) {
        object normDelta = NormalizeIncrementDelta(lMap["increment"]);
        normalized = new Dictionary<string, object>(1) {
          [".sv"] = new Dictionary<string, object>(1) { ["increment"] = normDelta },
        };
        return true;
      }
      return false;
    }

    private static object NormalizeIncrementDelta(object delta) => delta switch {
      long l => l,
      int i => (long)i,
      short s => (long)s,
      sbyte sb => (long)sb,
      byte b => (long)b,
      ushort us => (long)us,
      uint ui => (long)ui,
      double d when !double.IsNaN(d) && !double.IsInfinity(d) => d,
      float f when !float.IsNaN(f) && !float.IsInfinity(f) => (double)f,
      _ => throw new DatabaseException("Invalid ServerValue.Increment delta; must be a finite number."),
    };

    private static bool IsTimestampPlaceholder(Dictionary<string, object> sv) =>
        sv != null && sv.TryGetValue(".sv", out object v) && v is string s && s == "timestamp";
  }

  internal static class ServerValues {
    internal static RtdbNode ResolveNode(RtdbNode unresolved, RtdbNode existing, long serverTimeOffsetMs) {
      if (unresolved == null || unresolved.IsEmpty) return EmptyNode.Instance;
      long nowServerMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + serverTimeOffsetMs;

      RtdbNode resolvedPriority = ResolvePriority(unresolved.Priority, nowServerMs);
      if (unresolved is LeafNode leaf) {
        if (leaf.Kind != LeafKind.DeferredValue) {
          return leaf.WithPriority(resolvedPriority);
        }
        return ResolveDeferredLeaf(leaf, existing, nowServerMs, resolvedPriority);
      }

      if (unresolved is ChildrenNode childrenNode) {
        var resolvedChildren = new SortedDictionary<string, RtdbNode>(ChildKeyComparer.Instance);
        foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
          RtdbNode existingChild = existing == null ? EmptyNode.Instance : existing.GetImmediateChild(kv.Key);
          RtdbNode resolvedChild = ResolveNode(kv.Value, existingChild, serverTimeOffsetMs);
          if (!resolvedChild.IsEmpty) {
            resolvedChildren[kv.Key] = resolvedChild;
          }
        }
        return resolvedChildren.Count == 0
            ? EmptyNode.Instance
            : new ChildrenNode(resolvedChildren, resolvedPriority);
      }

      return unresolved;
    }

    private static RtdbNode ResolvePriority(RtdbNode prio, long nowServerMs) {
      if (prio is LeafNode leaf && leaf.Kind == LeafKind.DeferredValue) {
        return new LeafNode((double)nowServerMs);
      }
      return prio;
    }

    private static RtdbNode ResolveDeferredLeaf(
        LeafNode deferredLeaf,
        RtdbNode existing,
        long nowServerMs,
        RtdbNode resolvedPriority) {
      Dictionary<string, object> map = deferredLeaf.DeferredMap;
      if (map != null && map.TryGetValue(".sv", out object op)) {
        if (op is string s && s == "timestamp") {
          return new LeafNode(nowServerMs, resolvedPriority);
        }
        if (op is Dictionary<string, object> opMap && opMap.TryGetValue("increment", out object deltaObj)) {
          return ResolveIncrement(deltaObj, existing, resolvedPriority);
        }
      }
      return deferredLeaf.WithPriority(resolvedPriority);
    }

    private static RtdbNode ResolveIncrement(object deltaObj, RtdbNode existing, RtdbNode priority) {
      if (existing is LeafNode existingLeaf && (existingLeaf.Kind == LeafKind.Long || existingLeaf.Kind == LeafKind.Double)) {
        if (deltaObj is long deltaLong && existingLeaf.Kind == LeafKind.Long) {
          long baseLong = existingLeaf.LongValue;
          // Overflow check matching Firebase JS/Android SDKs: fall back to double on Int64 overflow.
          long sum = unchecked(baseLong + deltaLong);
          bool overflow = ((baseLong ^ sum) & (deltaLong ^ sum)) < 0;
          return overflow
              ? new LeafNode((double)baseLong + (double)deltaLong, priority)
              : new LeafNode(sum, priority);
        }
        double dDelta = deltaObj is long dl ? dl : (double)deltaObj;
        return new LeafNode(existingLeaf.DoubleValue + dDelta, priority);
      }

      return deltaObj is long l
          ? new LeafNode(l, priority)
          : new LeafNode((double)deltaObj, priority);
    }
  }
}
