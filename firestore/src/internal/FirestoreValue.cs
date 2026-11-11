// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;

namespace Firebase.Firestore.Internal {
  internal enum FirestoreValueKind : byte {
    Null = 0,
    Boolean = 1,
    Integer = 2,
    Double = 3,
    Timestamp = 4,
    ServerTimestamp = 5,
    String = 6,
    Blob = 7,
    Reference = 8,
    GeoPoint = 9,
    Array = 10,
    Map = 11,
    // Write-time FieldValue sentinels (extracted before wire serialization).
    SentinelDelete = 12,
    SentinelServerTimestamp = 13,
    SentinelIncrementInteger = 14,
    SentinelIncrementDouble = 15,
    SentinelArrayUnion = 16,
    SentinelArrayRemove = 17,
  }

  internal sealed class DocumentReferenceValue : IEquatable<DocumentReferenceValue> {
    internal string ProjectId { get; }
    internal string DatabaseId { get; }
    internal ResourcePath Path { get; }

    internal DocumentReferenceValue(string projectId, string databaseId, ResourcePath path) {
      ProjectId = projectId ?? string.Empty;
      DatabaseId = string.IsNullOrEmpty(databaseId) ? "(default)" : databaseId;
      Path = path ?? ResourcePath.Empty;
    }

    internal string FullResourceName => Path.ToFullResourceName(ProjectId, DatabaseId);

    public bool Equals(DocumentReferenceValue other) =>
        other != null &&
        string.Equals(ProjectId, other.ProjectId, StringComparison.Ordinal) &&
        string.Equals(DatabaseId, other.DatabaseId, StringComparison.Ordinal) &&
        Path.Equals(other.Path);

    public override bool Equals(object obj) => Equals(obj as DocumentReferenceValue);

    public override int GetHashCode() {
      int h = StringComparer.Ordinal.GetHashCode(ProjectId);
      h = h * 31 + StringComparer.Ordinal.GetHashCode(DatabaseId);
      return h * 31 + Path.GetHashCode();
    }
  }

  internal sealed class ServerTimestampData {
    internal Timestamp LocalWriteTime { get; }
    internal bool HasPreviousValue { get; }
    internal FirestoreValue PreviousValue { get; }

    internal ServerTimestampData(
        Timestamp localWriteTime, bool hasPreviousValue, FirestoreValue previousValue) {
      LocalWriteTime = localWriteTime;
      HasPreviousValue = hasPreviousValue;
      PreviousValue = previousValue;
    }
  }

  /// <summary>
  /// Zero-P/Invoke immutable union representing any Firestore value or write-time sentinel.
  /// Replaces legacy SWIG <c>FieldValueProxy</c> without native heap allocations.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal readonly struct FirestoreValue : IEquatable<FirestoreValue> {
    internal static readonly FirestoreValue Null = new FirestoreValue(FirestoreValueKind.Null, 0, 0.0, null);
    internal static readonly FirestoreValue True = new FirestoreValue(FirestoreValueKind.Boolean, 1, 0.0, null);
    internal static readonly FirestoreValue False = new FirestoreValue(FirestoreValueKind.Boolean, 0, 0.0, null);
    internal static readonly FirestoreValue DeleteSentinel =
        new FirestoreValue(FirestoreValueKind.SentinelDelete, 0, 0.0, null);
    internal static readonly FirestoreValue ServerTimestampSentinel =
        new FirestoreValue(FirestoreValueKind.SentinelServerTimestamp, 0, 0.0, null);

    internal FirestoreValueKind Kind { get; }
    private readonly long _intBits;
    private readonly double _doubleVal;
    private readonly object _refVal;

    private FirestoreValue(FirestoreValueKind kind, long intBits, double doubleVal, object refVal) {
      Kind = kind;
      _intBits = intBits;
      _doubleVal = doubleVal;
      _refVal = refVal;
    }

    internal bool IsNull => Kind == FirestoreValueKind.Null;
    internal bool IsSentinel => Kind >= FirestoreValueKind.SentinelDelete;
    internal bool IsNumber => Kind == FirestoreValueKind.Integer || Kind == FirestoreValueKind.Double;

    internal bool BooleanValue => _intBits != 0;
    internal long IntegerValue => _intBits;
    internal double DoubleValue => _doubleVal;
    internal Timestamp TimestampValue => new Timestamp(_intBits, (int)_doubleVal);
    internal ServerTimestampData ServerTimestampValue => (ServerTimestampData)_refVal;
    internal string StringValue => (string)_refVal;
    internal Blob BlobValue => Blob.FromTrusted((byte[])_refVal);
    internal byte[] RawBytes => (byte[])_refVal;
    internal DocumentReferenceValue ReferenceValue => (DocumentReferenceValue)_refVal;
    internal GeoPoint GeoPointValue =>
        new GeoPoint(BitConverter.Int64BitsToDouble(_intBits), _doubleVal);
    internal IReadOnlyList<FirestoreValue> ArrayValue => (IReadOnlyList<FirestoreValue>)_refVal;
    internal IDictionary<string, FirestoreValue> MapValue =>
        (IDictionary<string, FirestoreValue>)_refVal;
    internal object[] RawSentinelElements => (object[])_refVal;

    internal static FirestoreValue FromBoolean(bool value) => value ? True : False;

    internal static FirestoreValue FromInteger(long value) =>
        new FirestoreValue(FirestoreValueKind.Integer, value, 0.0, null);

    internal static FirestoreValue FromDouble(double value) =>
        new FirestoreValue(FirestoreValueKind.Double, 0, value, null);

    internal static FirestoreValue FromTimestamp(Timestamp value) =>
        new FirestoreValue(FirestoreValueKind.Timestamp, value.Seconds, value.Nanoseconds, null);

    internal static FirestoreValue FromServerTimestamp(
        Timestamp localWriteTime, bool hasPrevious, FirestoreValue previous) =>
        new FirestoreValue(
            FirestoreValueKind.ServerTimestamp, 0, 0.0,
            new ServerTimestampData(localWriteTime, hasPrevious, previous));

    internal static FirestoreValue FromString(string value) =>
        value == null ? Null : new FirestoreValue(FirestoreValueKind.String, 0, 0.0, value);

    internal static FirestoreValue FromBlob(Blob value) =>
        new FirestoreValue(FirestoreValueKind.Blob, 0, 0.0, value.InternalBytes ?? Array.Empty<byte>());

    internal static FirestoreValue FromOwnedBytes(byte[] bytes) =>
        new FirestoreValue(FirestoreValueKind.Blob, 0, 0.0, bytes ?? Array.Empty<byte>());

    internal static FirestoreValue FromBytesCopy(byte[] bytes) {
      if (bytes == null) return Null;
      var copy = new byte[bytes.Length];
      Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);
      return new FirestoreValue(FirestoreValueKind.Blob, 0, 0.0, copy);
    }

    internal static FirestoreValue FromReference(DocumentReferenceValue value) =>
        new FirestoreValue(FirestoreValueKind.Reference, 0, 0.0, value);

    internal static FirestoreValue FromGeoPoint(GeoPoint value) =>
        new FirestoreValue(
            FirestoreValueKind.GeoPoint,
            BitConverter.DoubleToInt64Bits(value.Latitude),
            value.Longitude,
            null);

    internal static FirestoreValue FromArray(IReadOnlyList<FirestoreValue> elements) =>
        new FirestoreValue(FirestoreValueKind.Array, 0, 0.0, elements ?? Array.Empty<FirestoreValue>());

    internal static FirestoreValue FromMap(IDictionary<string, FirestoreValue> entries) =>
        new FirestoreValue(
            FirestoreValueKind.Map, 0, 0.0,
            entries ?? new Dictionary<string, FirestoreValue>(StringComparer.Ordinal));

    internal static FirestoreValue SentinelIncrement(long value) =>
        new FirestoreValue(FirestoreValueKind.SentinelIncrementInteger, value, 0.0, null);

    internal static FirestoreValue SentinelIncrement(double value) =>
        new FirestoreValue(FirestoreValueKind.SentinelIncrementDouble, 0, value, null);

    internal static FirestoreValue SentinelArrayUnion(object[] rawElements) =>
        new FirestoreValue(FirestoreValueKind.SentinelArrayUnion, 0, 0.0, rawElements);

    internal static FirestoreValue SentinelArrayRemove(object[] rawElements) =>
        new FirestoreValue(FirestoreValueKind.SentinelArrayRemove, 0, 0.0, rawElements);

    internal FirestoreValue ResolveServerTimestamp(ServerTimestampBehavior behavior) {
      if (Kind == FirestoreValueKind.ServerTimestamp) {
        ServerTimestampData st = ServerTimestampValue;
        switch (behavior) {
          case ServerTimestampBehavior.Estimate:
            return FromTimestamp(st.LocalWriteTime);
          case ServerTimestampBehavior.Previous:
            return st.HasPreviousValue
                ? st.PreviousValue.ResolveServerTimestamp(behavior)
                : Null;
          default:
            return Null;
        }
      }
      if (Kind == FirestoreValueKind.Array) {
        IReadOnlyList<FirestoreValue> arr = ArrayValue;
        FirestoreValue[] copy = null;
        for (int i = 0; i < arr.Count; i++) {
          FirestoreValue resolved = arr[i].ResolveServerTimestamp(behavior);
          if (!ReferenceEquals(copy, null)) {
            copy[i] = resolved;
          } else if (!arr[i].Equals(resolved)) {
            copy = new FirestoreValue[arr.Count];
            for (int j = 0; j < i; j++) copy[j] = arr[j];
            copy[i] = resolved;
          }
        }
        return copy != null ? FromArray(copy) : this;
      }
      if (Kind == FirestoreValueKind.Map) {
        IDictionary<string, FirestoreValue> map = MapValue;
        Dictionary<string, FirestoreValue> copy = null;
        foreach (KeyValuePair<string, FirestoreValue> kv in map) {
          FirestoreValue resolved = kv.Value.ResolveServerTimestamp(behavior);
          if (copy != null) {
            copy[kv.Key] = resolved;
          } else if (!kv.Value.Equals(resolved)) {
            copy = new Dictionary<string, FirestoreValue>(map.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, FirestoreValue> prev in map) {
              if (string.Equals(prev.Key, kv.Key, StringComparison.Ordinal)) break;
              copy[prev.Key] = prev.Value;
            }
            copy[kv.Key] = resolved;
          }
        }
        return copy != null ? FromMap(copy) : this;
      }
      return this;
    }

    internal static bool TryGetFieldByPath(
        IDictionary<string, FirestoreValue> rootMap, FieldPath path, out FirestoreValue result) {
      result = Null;
      if (rootMap == null || path == null) return false;
      string[] segs = path.Segments;
      IDictionary<string, FirestoreValue> cur = rootMap;
      for (int i = 0; i < segs.Length; i++) {
        if (cur == null || !cur.TryGetValue(segs[i], out FirestoreValue val)) {
          result = Null;
          return false;
        }
        if (i == segs.Length - 1) {
          result = val;
          return true;
        }
        if (val.Kind != FirestoreValueKind.Map) {
          result = Null;
          return false;
        }
        cur = val.MapValue;
      }
      return false;
    }

    internal static Dictionary<string, FirestoreValue> CloneMapDeep(
        IDictionary<string, FirestoreValue> source) {
      if (source == null) {
        return new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      }
      var clone = new Dictionary<string, FirestoreValue>(source.Count, StringComparer.Ordinal);
      foreach (KeyValuePair<string, FirestoreValue> kv in source) {
        clone[kv.Key] = CloneValueDeep(kv.Value);
      }
      return clone;
    }

    internal static FirestoreValue CloneValueDeep(FirestoreValue value) {
      if (value.Kind == FirestoreValueKind.Map) {
        return FromMap(CloneMapDeep(value.MapValue));
      }
      if (value.Kind == FirestoreValueKind.Array) {
        IReadOnlyList<FirestoreValue> src = value.ArrayValue;
        if (src.Count == 0) return value;
        var dst = new FirestoreValue[src.Count];
        for (int i = 0; i < src.Count; i++) {
          dst[i] = CloneValueDeep(src[i]);
        }
        return FromArray(dst);
      }
      return value;
    }

    internal static void SetFieldByPath(
        Dictionary<string, FirestoreValue> rootMap, FieldPath path, FirestoreValue newValue) {
      string[] segs = path.Segments;
      Dictionary<string, FirestoreValue> cur = rootMap;
      for (int i = 0; i < segs.Length - 1; i++) {
        string seg = segs[i];
        if (cur.TryGetValue(seg, out FirestoreValue existing) &&
            existing.Kind == FirestoreValueKind.Map) {
          var sub = existing.MapValue as Dictionary<string, FirestoreValue>
              ?? CloneMapDeep(existing.MapValue);
          cur[seg] = FromMap(sub);
          cur = sub;
        } else {
          var sub = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
          cur[seg] = FromMap(sub);
          cur = sub;
        }
      }
      cur[segs[segs.Length - 1]] = newValue;
    }

    internal static void DeleteFieldByPath(
        Dictionary<string, FirestoreValue> rootMap, FieldPath path) {
      string[] segs = path.Segments;
      IDictionary<string, FirestoreValue> cur = rootMap;
      for (int i = 0; i < segs.Length - 1; i++) {
        if (!cur.TryGetValue(segs[i], out FirestoreValue existing) ||
            existing.Kind != FirestoreValueKind.Map) {
          return;
        }
        cur = existing.MapValue;
      }
      cur.Remove(segs[segs.Length - 1]);
    }

    internal static int GetTypeOrder(FirestoreValueKind kind) => kind switch {
      FirestoreValueKind.Null => 0,
      FirestoreValueKind.Boolean => 1,
      FirestoreValueKind.Integer => 2,
      FirestoreValueKind.Double => 2,
      FirestoreValueKind.Timestamp => 3,
      FirestoreValueKind.ServerTimestamp => 3,
      FirestoreValueKind.String => 4,
      FirestoreValueKind.Blob => 5,
      FirestoreValueKind.Reference => 6,
      FirestoreValueKind.GeoPoint => 7,
      FirestoreValueKind.Array => 8,
      FirestoreValueKind.Map => 9,
      _ => 10,
    };

    /// <summary>
    /// Firestore canonical query/value comparison (cross-type + mixed numeric + UTF-8).
    /// </summary>
    internal static int Compare(FirestoreValue left, FirestoreValue right) {
      int leftOrder = GetTypeOrder(left.Kind);
      int rightOrder = GetTypeOrder(right.Kind);
      if (leftOrder != rightOrder) {
        return leftOrder.CompareTo(rightOrder);
      }
      switch (leftOrder) {
        case 0:
          return 0;
        case 1:
          return left.BooleanValue.CompareTo(right.BooleanValue);
        case 2:
          return CompareNumbers(left, right);
        case 3:
          return CompareTimestamps(left, right);
        case 4:
          return CompareUtf8Strings(left.StringValue ?? string.Empty, right.StringValue ?? string.Empty);
        case 5:
          return CompareBytes(left.RawBytes, right.RawBytes);
        case 6:
          return CompareReferences(left.ReferenceValue, right.ReferenceValue);
        case 7:
          return CompareGeoPoints(left.GeoPointValue, right.GeoPointValue);
        case 8:
          return CompareArrays(left.ArrayValue, right.ArrayValue);
        case 9:
          return CompareMaps(left.MapValue, right.MapValue);
        default:
          return 0;
      }
    }

    /// <summary>
    /// Firestore value equality used by query filters and array transforms
    /// (<c>1L</c> equals <c>1.0</c>, <c>NaN</c> equals <c>NaN</c>, <c>+0.0</c> equals <c>-0.0</c>).
    /// </summary>
    internal static bool ValueEquals(FirestoreValue left, FirestoreValue right) {
      if (left.IsNumber && right.IsNumber) {
        return CompareNumbers(left, right) == 0;
      }
      if (left.Kind != right.Kind) return false;
      switch (left.Kind) {
        case FirestoreValueKind.Null:
          return true;
        case FirestoreValueKind.Boolean:
          return left.BooleanValue == right.BooleanValue;
        case FirestoreValueKind.Timestamp:
          return left.TimestampValue.Equals(right.TimestampValue);
        case FirestoreValueKind.ServerTimestamp:
          return left.ServerTimestampValue.LocalWriteTime.Equals(
              right.ServerTimestampValue.LocalWriteTime);
        case FirestoreValueKind.String:
          return string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal);
        case FirestoreValueKind.Blob:
          return CompareBytes(left.RawBytes, right.RawBytes) == 0;
        case FirestoreValueKind.Reference:
          return left.ReferenceValue.Equals(right.ReferenceValue);
        case FirestoreValueKind.GeoPoint:
          return left.GeoPointValue.Equals(right.GeoPointValue);
        case FirestoreValueKind.Array: {
          IReadOnlyList<FirestoreValue> a = left.ArrayValue, b = right.ArrayValue;
          if (a.Count != b.Count) return false;
          for (int i = 0; i < a.Count; i++) {
            if (!ValueEquals(a[i], b[i])) return false;
          }
          return true;
        }
        case FirestoreValueKind.Map: {
          IDictionary<string, FirestoreValue> a = left.MapValue, b = right.MapValue;
          if (a.Count != b.Count) return false;
          foreach (KeyValuePair<string, FirestoreValue> kv in a) {
            if (!b.TryGetValue(kv.Key, out FirestoreValue rv) || !ValueEquals(kv.Value, rv)) {
              return false;
            }
          }
          return true;
        }
        default:
          return false;
      }
    }

    private static int CompareNumbers(FirestoreValue left, FirestoreValue right) {
      if (left.Kind == FirestoreValueKind.Integer && right.Kind == FirestoreValueKind.Integer) {
        return left.IntegerValue.CompareTo(right.IntegerValue);
      }
      if (left.Kind == FirestoreValueKind.Double && right.Kind == FirestoreValueKind.Double) {
        return CompareDoubles(left.DoubleValue, right.DoubleValue);
      }
      if (left.Kind == FirestoreValueKind.Double) {
        return CompareDoubleAndLong(left.DoubleValue, right.IntegerValue);
      }
      return -CompareDoubleAndLong(right.DoubleValue, left.IntegerValue);
    }

    internal static int CompareDoubles(double a, double b) {
      bool aNaN = double.IsNaN(a);
      bool bNaN = double.IsNaN(b);
      if (aNaN || bNaN) {
        return aNaN == bNaN ? 0 : (aNaN ? -1 : 1);
      }
      if (a < b) return -1;
      if (a > b) return 1;
      return 0;
    }

    private static int CompareDoubleAndLong(double d, long l) {
      if (double.IsNaN(d)) return -1;
      const double minLongAsDouble = -9223372036854775808.0; // -2^63 (exact)
      const double maxLongPlusOneAsDouble = 9223372036854775808.0; // +2^63 (exact)
      if (d < minLongAsDouble) return -1;
      if (d >= maxLongPlusOneAsDouble) return 1;
      if (d == minLongAsDouble) return l == long.MinValue ? 0 : -1;
      long trunc = (long)d;
      int cmp = trunc.CompareTo(l);
      if (cmp != 0) return cmp;
      double frac = d - trunc;
      if (frac < 0.0) return -1;
      if (frac > 0.0) return 1;
      return 0;
    }

    private static int CompareTimestamps(FirestoreValue left, FirestoreValue right) {
      if (left.Kind == FirestoreValueKind.Timestamp && right.Kind == FirestoreValueKind.Timestamp) {
        return left.TimestampValue.CompareTo(right.TimestampValue);
      }
      if (left.Kind == FirestoreValueKind.ServerTimestamp &&
          right.Kind == FirestoreValueKind.ServerTimestamp) {
        return left.ServerTimestampValue.LocalWriteTime.CompareTo(
            right.ServerTimestampValue.LocalWriteTime);
      }
      return left.Kind == FirestoreValueKind.Timestamp ? -1 : 1;
    }

    internal static int CompareUtf8Strings(string a, string b) {
      if (ReferenceEquals(a, b)) return 0;
      int ia = 0, ib = 0;
      while (ia < a.Length && ib < b.Length) {
        int ca = a[ia++];
        int cb = b[ib++];
        if (ca == cb) continue;
        if (char.IsHighSurrogate((char)ca) && ia < a.Length && char.IsLowSurrogate(a[ia])) {
          ca = char.ConvertToUtf32((char)ca, a[ia++]);
        }
        if (char.IsHighSurrogate((char)cb) && ib < b.Length && char.IsLowSurrogate(b[ib])) {
          cb = char.ConvertToUtf32((char)cb, b[ib++]);
        }
        return ca.CompareTo(cb);
      }
      return (a.Length - ia).CompareTo(b.Length - ib);
    }

    private static int CompareBytes(byte[] a, byte[] b) {
      a ??= Array.Empty<byte>();
      b ??= Array.Empty<byte>();
      int min = Math.Min(a.Length, b.Length);
      for (int i = 0; i < min; i++) {
        if (a[i] != b[i]) return a[i].CompareTo(b[i]);
      }
      return a.Length.CompareTo(b.Length);
    }

    private static int CompareReferences(DocumentReferenceValue a, DocumentReferenceValue b) {
      int cmp = CompareUtf8Strings(a.ProjectId, b.ProjectId);
      if (cmp != 0) return cmp;
      cmp = CompareUtf8Strings(a.DatabaseId, b.DatabaseId);
      if (cmp != 0) return cmp;
      return a.Path.CompareTo(b.Path);
    }

    private static int CompareGeoPoints(GeoPoint a, GeoPoint b) {
      int cmp = CompareDoubles(a.Latitude, b.Latitude);
      return cmp != 0 ? cmp : CompareDoubles(a.Longitude, b.Longitude);
    }

    private static int CompareArrays(
        IReadOnlyList<FirestoreValue> a, IReadOnlyList<FirestoreValue> b) {
      int min = Math.Min(a.Count, b.Count);
      for (int i = 0; i < min; i++) {
        int cmp = Compare(a[i], b[i]);
        if (cmp != 0) return cmp;
      }
      return a.Count.CompareTo(b.Count);
    }

    private static int CompareMaps(
        IDictionary<string, FirestoreValue> a, IDictionary<string, FirestoreValue> b) {
      if (a.Count == 0 && b.Count == 0) return 0;
      var keysA = new List<string>(a.Keys);
      var keysB = new List<string>(b.Keys);
      keysA.Sort(CompareUtf8Strings);
      keysB.Sort(CompareUtf8Strings);
      int min = Math.Min(keysA.Count, keysB.Count);
      for (int i = 0; i < min; i++) {
        int keyCmp = CompareUtf8Strings(keysA[i], keysB[i]);
        if (keyCmp != 0) return keyCmp;
        int valCmp = Compare(a[keysA[i]], b[keysB[i]]);
        if (valCmp != 0) return valCmp;
      }
      return keysA.Count.CompareTo(keysB.Count);
    }

    public bool Equals(FirestoreValue other) {
      if (Kind != other.Kind) return false;
      switch (Kind) {
        case FirestoreValueKind.Null:
        case FirestoreValueKind.SentinelDelete:
        case FirestoreValueKind.SentinelServerTimestamp:
          return true;
        case FirestoreValueKind.Boolean:
        case FirestoreValueKind.Integer:
        case FirestoreValueKind.SentinelIncrementInteger:
          return _intBits == other._intBits;
        case FirestoreValueKind.Double:
        case FirestoreValueKind.SentinelIncrementDouble:
          return BitConverter.DoubleToInt64Bits(_doubleVal) ==
                 BitConverter.DoubleToInt64Bits(other._doubleVal);
        case FirestoreValueKind.Timestamp:
          return _intBits == other._intBits && (int)_doubleVal == (int)other._doubleVal;
        case FirestoreValueKind.ServerTimestamp: {
          ServerTimestampData a = ServerTimestampValue, b = other.ServerTimestampValue;
          return a.LocalWriteTime.Equals(b.LocalWriteTime) &&
                 a.HasPreviousValue == b.HasPreviousValue &&
                 (!a.HasPreviousValue || a.PreviousValue.Equals(b.PreviousValue));
        }
        case FirestoreValueKind.String:
          return string.Equals(StringValue, other.StringValue, StringComparison.Ordinal);
        case FirestoreValueKind.Blob:
          return CompareBytes(RawBytes, other.RawBytes) == 0;
        case FirestoreValueKind.Reference:
          return ReferenceValue.Equals(other.ReferenceValue);
        case FirestoreValueKind.GeoPoint:
          return _intBits == other._intBits &&
                 BitConverter.DoubleToInt64Bits(_doubleVal) ==
                 BitConverter.DoubleToInt64Bits(other._doubleVal);
        case FirestoreValueKind.Array: {
          IReadOnlyList<FirestoreValue> a = ArrayValue, b = other.ArrayValue;
          if (a.Count != b.Count) return false;
          for (int i = 0; i < a.Count; i++) {
            if (!a[i].Equals(b[i])) return false;
          }
          return true;
        }
        case FirestoreValueKind.Map: {
          IDictionary<string, FirestoreValue> a = MapValue, b = other.MapValue;
          if (a.Count != b.Count) return false;
          foreach (KeyValuePair<string, FirestoreValue> kv in a) {
            if (!b.TryGetValue(kv.Key, out FirestoreValue rv) || !kv.Value.Equals(rv)) {
              return false;
            }
          }
          return true;
        }
        default:
          return ReferenceEquals(_refVal, other._refVal);
      }
    }

    public override bool Equals(object obj) => obj is FirestoreValue other && Equals(other);

    public override int GetHashCode() {
      int h = (int)Kind * 397;
      switch (Kind) {
        case FirestoreValueKind.Boolean:
        case FirestoreValueKind.Integer:
          return h ^ Hash.LongHash(_intBits);
        case FirestoreValueKind.Double:
          return h ^ Hash.DoubleBitwiseHash(_doubleVal);
        case FirestoreValueKind.Timestamp:
          return h ^ Hash.LongHash(_intBits * 37 + (int)_doubleVal);
        case FirestoreValueKind.ServerTimestamp:
          return h ^ ServerTimestampValue.LocalWriteTime.GetHashCode();
        case FirestoreValueKind.String:
          return h ^ (StringValue != null ? StringComparer.Ordinal.GetHashCode(StringValue) : 0);
        case FirestoreValueKind.Blob:
          return h ^ BlobValue.GetHashCode();
        case FirestoreValueKind.Reference:
          return h ^ ReferenceValue.GetHashCode();
        case FirestoreValueKind.GeoPoint:
          return h ^ GeoPointValue.GetHashCode();
        case FirestoreValueKind.Array: {
          IReadOnlyList<FirestoreValue> arr = ArrayValue;
          for (int i = 0; i < arr.Count; i++) h = h * 31 + arr[i].GetHashCode();
          return h;
        }
        case FirestoreValueKind.Map: {
          int sum = 0;
          foreach (KeyValuePair<string, FirestoreValue> kv in MapValue) {
            sum ^= (StringComparer.Ordinal.GetHashCode(kv.Key) * 397) ^ kv.Value.GetHashCode();
          }
          return h ^ sum;
        }
        default:
          return h;
      }
    }
  }
}
