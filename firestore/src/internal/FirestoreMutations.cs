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
  internal enum MutationKind : byte {
    SetOverwrite = 0,
    SetMergeAll = 1,
    SetMergeFields = 2,
    Update = 3,
    Delete = 4,
  }

  internal enum FieldTransformKind : byte {
    ServerTimestamp = 0,
    IncrementInteger = 1,
    IncrementDouble = 2,
    ArrayUnion = 3,
    ArrayRemove = 4,
  }

  internal sealed class FieldTransformSpec {
    internal FieldPath Field { get; }
    internal FieldTransformKind Kind { get; }
    internal FirestoreValue Operand { get; }

    internal FieldTransformSpec(FieldPath field, FieldTransformKind kind, FirestoreValue operand) {
      Field = field;
      Kind = kind;
      Operand = operand;
    }
  }

  internal sealed class MutableDocument {
    internal ResourcePath Path { get; }
    internal bool Exists { get; set; }
    internal Dictionary<string, FirestoreValue> Fields { get; set; }
    internal Timestamp Version { get; set; }
    internal bool HasLocalMutations { get; set; }
    internal bool HasCommittedMutations { get; set; }

    internal MutableDocument(
        ResourcePath path,
        bool exists,
        Dictionary<string, FirestoreValue> fields,
        Timestamp version,
        bool hasLocalMutations = false,
        bool hasCommittedMutations = false) {
      Path = path;
      Exists = exists;
      Fields = fields ?? new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      Version = version;
      HasLocalMutations = hasLocalMutations;
      HasCommittedMutations = hasCommittedMutations;
    }

    internal bool HasPendingWrites => HasLocalMutations;

    internal MutableDocument Clone() => new MutableDocument(
        Path,
        Exists,
        FirestoreValue.CloneMapDeep(Fields),
        Version,
        HasLocalMutations,
        HasCommittedMutations);
  }

  internal sealed class MutationSpec {
    internal ResourcePath Path { get; }
    internal MutationKind Kind { get; }
    internal Dictionary<string, FirestoreValue> Data { get; }
    internal List<FieldPath> Mask { get; }
    internal List<FieldTransformSpec> Transforms { get; }

    internal MutationSpec(
        ResourcePath path,
        MutationKind kind,
        Dictionary<string, FirestoreValue> data,
        List<FieldPath> mask,
        List<FieldTransformSpec> transforms) {
      Path = path;
      Kind = kind;
      Data = data;
      Mask = mask;
      Transforms = transforms ?? new List<FieldTransformSpec>();
    }

    internal static MutationSpec CreateDelete(ResourcePath path) =>
        new MutationSpec(path, MutationKind.Delete, null, null, null);

    internal static MutationSpec ParseSet(
        SerializationContext context,
        ResourcePath path,
        Dictionary<string, FirestoreValue> rawMap,
        SetOptions options,
        string expectedProjectId,
        string expectedDatabaseId) {
      options ??= SetOptions.Overwrite;
      var cleanMap = new Dictionary<string, FirestoreValue>(rawMap.Count, StringComparer.Ordinal);
      var transforms = new List<FieldTransformSpec>();
      var deletePaths = new List<FieldPath>();

      ExtractMapEntryTree(
          context,
          rawMap,
          cleanMap,
          prefixSegments: Array.Empty<string>(),
          allowDeleteSentinel: options.Kind != SetOptions.SetKind.Overwrite,
          transforms,
          deletePaths,
          expectedProjectId,
          expectedDatabaseId);

      if (options.Kind == SetOptions.SetKind.Overwrite) {
        return new MutationSpec(path, MutationKind.SetOverwrite, cleanMap, null, transforms);
      }

      if (options.Kind == SetOptions.SetKind.MergeAll) {
        var mask = new List<FieldPath>();
        CollectLeafMaskPaths(cleanMap, Array.Empty<string>(), mask);
        foreach (FieldPath dp in deletePaths) {
          AddUniqueFieldPath(mask, dp);
        }
        return new MutationSpec(path, MutationKind.SetMergeAll, cleanMap, mask, transforms);
      }

      // MergeFields: keep only fields covered by options.FieldMask.
      IReadOnlyList<FieldPath> requestedMask = options.FieldMask;
      // Verify every specified FieldPath is either present in rawMap/transforms/deletes or omitted:
      // Note that FieldValue.Delete is allowed ONLY if covered by the mask; any unmasked Delete is
      // ignored, while every requested mask entry must exist in input unless it's a transform/delete?
      // Check Firestore rule: in SetOptions.MergeFields, specifying a field path that does not exist
      // in the input data throws ArgumentException!
      foreach (FieldPath reqPath in requestedMask) {
        if (!InputContainsPath(rawMap, reqPath)) {
          throw new ArgumentException(
              $"Field '{reqPath}' is specified in your field mask but missing from your input data.");
        }
      }

      var filteredTransforms = new List<FieldTransformSpec>();
      foreach (FieldTransformSpec tr in transforms) {
        if (IsCoveredByMask(tr.Field, requestedMask)) {
          filteredTransforms.Add(tr);
        }
      }

      var filteredMap = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      var effectiveMask = new List<FieldPath>(requestedMask.Count);
      foreach (FieldPath maskPath in requestedMask) {
        // Transform paths are sent as FieldTransforms, not in updateMask, unless a parent map is masked.
        bool isExactTransform = false;
        for (int i = 0; i < filteredTransforms.Count; i++) {
          if (filteredTransforms[i].Field.Equals(maskPath)) {
            isExactTransform = true;
            break;
          }
        }
        if (!isExactTransform) {
          AddUniqueFieldPath(effectiveMask, maskPath);
        }
        if (FirestoreValue.TryGetFieldByPath(cleanMap, maskPath, out FirestoreValue val)) {
          FirestoreValue.SetFieldByPath(filteredMap, maskPath, val);
        }
      }

      return new MutationSpec(
          path, MutationKind.SetMergeFields, filteredMap, effectiveMask, filteredTransforms);
    }

    internal static MutationSpec ParseUpdate(
        SerializationContext context,
        ResourcePath path,
        IEnumerable<KeyValuePair<FieldPath, object>> updates,
        string expectedProjectId,
        string expectedDatabaseId) {
      var cleanData = new Dictionary<string, FirestoreValue>(StringComparer.Ordinal);
      var mask = new List<FieldPath>();
      var transforms = new List<FieldTransformSpec>();
      var seenPaths = new List<FieldPath>();

      foreach (KeyValuePair<FieldPath, object> entry in updates) {
        FieldPath fieldPath = entry.Key;
        if (fieldPath == null) {
          throw new ArgumentException("Update field path cannot be null.");
        }
        if (fieldPath.IsDocumentId) {
          throw new ArgumentException("FieldPath.DocumentId is not valid in Update.");
        }
        // Check for duplicate or prefix-conflicting field paths (e.g., "a" and "a.b").
        for (int i = 0; i < seenPaths.Count; i++) {
          if (IsPrefixOrEqual(seenPaths[i], fieldPath)) {
            throw new ArgumentException(
                $"Field '{fieldPath}' was specified multiple times or conflicts with '{seenPaths[i]}'.");
          }
        }
        seenPaths.Add(fieldPath);

        FirestoreValue serialized = ValueSerializer.Serialize(context, entry.Value);
        ApplyParsedUpdateValue(
            context,
            fieldPath,
            serialized,
            cleanData,
            mask,
            transforms,
            expectedProjectId,
            expectedDatabaseId);
      }

      return new MutationSpec(path, MutationKind.Update, cleanData, mask, transforms);
    }

    internal MutableDocument ApplyToLocal(MutableDocument existing, Timestamp localWriteTime) {
      if (Kind == MutationKind.Delete) {
        return new MutableDocument(
            Path,
            exists: false,
            fields: new Dictionary<string, FirestoreValue>(StringComparer.Ordinal),
            version: existing?.Version ?? default,
            hasLocalMutations: true,
            hasCommittedMutations: existing?.HasCommittedMutations ?? false);
      }

      if (Kind == MutationKind.Update && (existing == null || !existing.Exists)) {
        // Precondition (exists == true) fails locally; document stays missing.
        return existing ?? new MutableDocument(Path, false, null, default);
      }

      Dictionary<string, FirestoreValue> baseFields;
      if (Kind == MutationKind.SetOverwrite || existing == null || !existing.Exists) {
        baseFields = FirestoreValue.CloneMapDeep(Data);
      } else {
        baseFields = FirestoreValue.CloneMapDeep(existing.Fields);
        if (Mask != null) {
          for (int i = 0; i < Mask.Count; i++) {
            FieldPath fp = Mask[i];
            if (FirestoreValue.TryGetFieldByPath(Data, fp, out FirestoreValue v)) {
              FirestoreValue.SetFieldByPath(baseFields, fp, FirestoreValue.CloneValueDeep(v));
            } else {
              FirestoreValue.DeleteFieldByPath(baseFields, fp);
            }
          }
        }
      }

      for (int i = 0; i < Transforms.Count; i++) {
        FieldTransformSpec tr = Transforms[i];
        FirestoreValue prevVal = FirestoreValue.Null;
        bool hadPrev = existing != null &&
                       existing.Exists &&
                       FirestoreValue.TryGetFieldByPath(existing.Fields, tr.Field, out prevVal);
        if (!hadPrev) {
          prevVal = FirestoreValue.Null;
        }
        FirestoreValue transformed = ApplyLocalTransform(tr, hadPrev, prevVal, localWriteTime);
        FirestoreValue.SetFieldByPath(baseFields, tr.Field, transformed);
      }

      return new MutableDocument(
          Path,
          exists: true,
          fields: baseFields,
          version: existing?.Version ?? default,
          hasLocalMutations: true,
          hasCommittedMutations: existing?.HasCommittedMutations ?? false);
    }

    private static FirestoreValue ApplyLocalTransform(
        FieldTransformSpec tr, bool hasPrev, FirestoreValue prev, Timestamp localWriteTime) {
      switch (tr.Kind) {
        case FieldTransformKind.ServerTimestamp:
          return FirestoreValue.FromServerTimestamp(localWriteTime, hasPrev, prev);

        case FieldTransformKind.IncrementInteger: {
          long delta = tr.Operand.IntegerValue;
          if (!hasPrev || !prev.IsNumber) {
            return FirestoreValue.FromInteger(delta);
          }
          if (prev.Kind == FirestoreValueKind.Double) {
            return FirestoreValue.FromDouble(prev.DoubleValue + delta);
          }
          return FirestoreValue.FromInteger(SaturatingAdd(prev.IntegerValue, delta));
        }

        case FieldTransformKind.IncrementDouble: {
          double delta = tr.Operand.DoubleValue;
          if (!hasPrev || !prev.IsNumber) {
            return FirestoreValue.FromDouble(delta);
          }
          double baseNum = prev.Kind == FirestoreValueKind.Double
              ? prev.DoubleValue
              : prev.IntegerValue;
          return FirestoreValue.FromDouble(baseNum + delta);
        }

        case FieldTransformKind.ArrayUnion: {
          var list = new List<FirestoreValue>();
          if (hasPrev && prev.Kind == FirestoreValueKind.Array) {
            list.AddRange(prev.ArrayValue);
          }
          IReadOnlyList<FirestoreValue> toAdd = tr.Operand.ArrayValue;
          for (int i = 0; i < toAdd.Count; i++) {
            if (!ContainsByValueEquals(list, toAdd[i])) {
              list.Add(toAdd[i]);
            }
          }
          return FirestoreValue.FromArray(list.ToArray());
        }

        case FieldTransformKind.ArrayRemove: {
          var list = new List<FirestoreValue>();
          if (hasPrev && prev.Kind == FirestoreValueKind.Array) {
            IReadOnlyList<FirestoreValue> toRemove = tr.Operand.ArrayValue;
            IReadOnlyList<FirestoreValue> cur = prev.ArrayValue;
            for (int i = 0; i < cur.Count; i++) {
              if (!ContainsByValueEquals(toRemove, cur[i])) {
                list.Add(cur[i]);
              }
            }
          }
          return FirestoreValue.FromArray(list.ToArray());
        }

        default:
          return FirestoreValue.Null;
      }
    }

    private static long SaturatingAdd(long a, long b) {
      if (b > 0 && a > long.MaxValue - b) return long.MaxValue;
      if (b < 0 && a < long.MinValue - b) return long.MinValue;
      return a + b;
    }

    private static bool ContainsByValueEquals(
        IReadOnlyList<FirestoreValue> list, FirestoreValue candidate) {
      for (int i = 0; i < list.Count; i++) {
        if (FirestoreValue.ValueEquals(list[i], candidate)) return true;
      }
      return false;
    }

    private static void ApplyParsedUpdateValue(
        SerializationContext context,
        FieldPath fieldPath,
        FirestoreValue value,
        Dictionary<string, FirestoreValue> cleanData,
        List<FieldPath> mask,
        List<FieldTransformSpec> transforms,
        string expectedProjectId,
        string expectedDatabaseId) {
      if (value.Kind == FirestoreValueKind.SentinelDelete) {
        AddUniqueFieldPath(mask, fieldPath);
        return;
      }
      if (value.IsSentinel) {
        transforms.Add(ConvertSentinelToTransform(
            context, fieldPath, value, expectedProjectId, expectedDatabaseId));
        return;
      }
      if (value.Kind == FirestoreValueKind.Map) {
        IDictionary<string, FirestoreValue> subRaw = value.MapValue;
        if (subRaw.Count == 0) {
          FirestoreValue.SetFieldByPath(
              cleanData, fieldPath,
              FirestoreValue.FromMap(new Dictionary<string, FirestoreValue>(StringComparer.Ordinal)));
          AddUniqueFieldPath(mask, fieldPath);
          return;
        }
        var subClean = new Dictionary<string, FirestoreValue>(subRaw.Count, StringComparer.Ordinal);
        var subDeletes = new List<FieldPath>();
        ExtractMapEntryTree(
            context,
            subRaw,
            subClean,
            fieldPath.Segments,
            allowDeleteSentinel: true,
            transforms,
            subDeletes,
            expectedProjectId,
            expectedDatabaseId);
        if (subDeletes.Count > 0) {
          throw new ArgumentException(
              "FieldValue.Delete cannot be nested inside a map in an Update call; use a FieldPath instead.");
        }
        FirestoreValue.SetFieldByPath(cleanData, fieldPath, FirestoreValue.FromMap(subClean));
        AddUniqueFieldPath(mask, fieldPath);
        return;
      }

      FirestoreValue cleaned = ValidateNonSentinelValue(value, expectedProjectId, expectedDatabaseId);
      FirestoreValue.SetFieldByPath(cleanData, fieldPath, cleaned);
      AddUniqueFieldPath(mask, fieldPath);
    }

    private static void ExtractMapEntryTree(
        SerializationContext context,
        IDictionary<string, FirestoreValue> source,
        Dictionary<string, FirestoreValue> dest,
        string[] prefixSegments,
        bool allowDeleteSentinel,
        List<FieldTransformSpec> transforms,
        List<FieldPath> deletePaths,
        string expectedProjectId,
        string expectedDatabaseId) {
      foreach (KeyValuePair<string, FirestoreValue> kv in source) {
        if (string.IsNullOrEmpty(kv.Key)) {
          throw new ArgumentException("Document field names must not be empty.");
        }
        string[] childSegments = AppendSegment(prefixSegments, kv.Key);
        FirestoreValue val = kv.Value;

        if (val.Kind == FirestoreValueKind.SentinelDelete) {
          if (!allowDeleteSentinel) {
            throw new ArgumentException(
                "FieldValue.Delete cannot be used with SetAsync unless MergeOptions are specified.");
          }
          deletePaths.Add(new FieldPath(childSegments));
          continue;
        }

        if (val.IsSentinel) {
          var fieldPath = new FieldPath(childSegments);
          transforms.Add(ConvertSentinelToTransform(
              context, fieldPath, val, expectedProjectId, expectedDatabaseId));
          continue;
        }

        if (val.Kind == FirestoreValueKind.Map) {
          IDictionary<string, FirestoreValue> subSource = val.MapValue;
          var subDest = new Dictionary<string, FirestoreValue>(subSource.Count, StringComparer.Ordinal);
          ExtractMapEntryTree(
              context,
              subSource,
              subDest,
              childSegments,
              allowDeleteSentinel,
              transforms,
              deletePaths,
              expectedProjectId,
              expectedDatabaseId);
          // Only include subDest if the input map was empty (explicit `{}`) or had non-sentinel entries.
          if (subSource.Count == 0 || subDest.Count > 0) {
            dest[kv.Key] = FirestoreValue.FromMap(subDest);
          }
          continue;
        }

        dest[kv.Key] = ValidateNonSentinelValue(val, expectedProjectId, expectedDatabaseId);
      }
    }

    internal static FirestoreValue ValidateNonSentinelValue(
        FirestoreValue value, string expectedProjectId, string expectedDatabaseId) {
      if (value.IsSentinel) {
        throw new ArgumentException("FieldValue sentinels are not supported inside arrays or queries.");
      }
      if (value.Kind == FirestoreValueKind.Reference) {
        ValidateReferenceDatabase(value.ReferenceValue, expectedProjectId, expectedDatabaseId);
        return value;
      }
      if (value.Kind == FirestoreValueKind.Array) {
        IReadOnlyList<FirestoreValue> arr = value.ArrayValue;
        for (int i = 0; i < arr.Count; i++) {
          FirestoreValue el = arr[i];
          if (el.IsSentinel) {
            throw new ArgumentException("FieldValue sentinels are not supported inside arrays.");
          }
          if (el.Kind == FirestoreValueKind.Array) {
            // Firestore does not allow directly nested arrays!
            throw new ArgumentException("Nested arrays are not supported in Firestore.");
          }
          ValidateEntryInsideArray(el, expectedProjectId, expectedDatabaseId);
        }
        return value;
      }
      return value;
    }

    private static void ValidateEntryInsideArray(
        FirestoreValue val, string expectedProjectId, string expectedDatabaseId) {
      if (val.IsSentinel) {
        throw new ArgumentException("FieldValue sentinels are not supported inside arrays.");
      }
      if (val.Kind == FirestoreValueKind.Reference) {
        ValidateReferenceDatabase(val.ReferenceValue, expectedProjectId, expectedDatabaseId);
      } else if (val.Kind == FirestoreValueKind.Array) {
        IReadOnlyList<FirestoreValue> arr = val.ArrayValue;
        for (int i = 0; i < arr.Count; i++) {
          if (arr[i].Kind == FirestoreValueKind.Array) {
            throw new ArgumentException("Nested arrays are not supported in Firestore.");
          }
          ValidateEntryInsideArray(arr[i], expectedProjectId, expectedDatabaseId);
        }
      } else if (val.Kind == FirestoreValueKind.Map) {
        foreach (KeyValuePair<string, FirestoreValue> kv in val.MapValue) {
          if (string.IsNullOrEmpty(kv.Key)) {
            throw new ArgumentException("Map field names must not be empty.");
          }
          ValidateEntryInsideArray(kv.Value, expectedProjectId, expectedDatabaseId);
        }
      }
    }

    internal static void ValidateReferenceDatabase(
        DocumentReferenceValue refVal, string expectedProjectId, string expectedDatabaseId) {
      if (refVal == null) return;
      if (!string.Equals(refVal.ProjectId, expectedProjectId, StringComparison.Ordinal) ||
          !string.Equals(refVal.DatabaseId, expectedDatabaseId, StringComparison.Ordinal)) {
        throw new ArgumentException(
            $"Document reference is for database {refVal.ProjectId}/{refVal.DatabaseId}, " +
            $"which differs from this Firestore instance ({expectedProjectId}/{expectedDatabaseId}).");
      }
      FirestoreException invalid = refVal.Path.ValidateForBackend();
      if (invalid != null) {
        throw new ArgumentException(invalid.Message);
      }
    }

    private static FieldTransformSpec ConvertSentinelToTransform(
        SerializationContext context,
        FieldPath field,
        FirestoreValue sentinel,
        string expectedProjectId,
        string expectedDatabaseId) {
      switch (sentinel.Kind) {
        case FirestoreValueKind.SentinelServerTimestamp:
          return new FieldTransformSpec(field, FieldTransformKind.ServerTimestamp, FirestoreValue.Null);
        case FirestoreValueKind.SentinelIncrementInteger:
          return new FieldTransformSpec(field, FieldTransformKind.IncrementInteger,
              FirestoreValue.FromInteger(sentinel.IntegerValue));
        case FirestoreValueKind.SentinelIncrementDouble:
          return new FieldTransformSpec(field, FieldTransformKind.IncrementDouble,
              FirestoreValue.FromDouble(sentinel.DoubleValue));
        case FirestoreValueKind.SentinelArrayUnion:
        case FirestoreValueKind.SentinelArrayRemove: {
          object[] raw = sentinel.RawSentinelElements;
          var parsed = new FirestoreValue[raw.Length];
          for (int i = 0; i < raw.Length; i++) {
            FirestoreValue el = ValueSerializer.Serialize(context, raw[i]);
            if (el.IsSentinel) {
              throw new ArgumentException("FieldValue sentinels cannot be used inside ArrayUnion or ArrayRemove.");
            }
            if (el.Kind == FirestoreValueKind.Array) {
              throw new ArgumentException("Nested arrays are not supported inside ArrayUnion or ArrayRemove.");
            }
            ValidateEntryInsideArray(el, expectedProjectId, expectedDatabaseId);
            parsed[i] = el;
          }
          FieldTransformKind kind = sentinel.Kind == FirestoreValueKind.SentinelArrayUnion
              ? FieldTransformKind.ArrayUnion
              : FieldTransformKind.ArrayRemove;
          return new FieldTransformSpec(field, kind, FirestoreValue.FromArray(parsed));
        }
        default:
          throw new ArgumentException($"Unsupported sentinel kind: {sentinel.Kind}");
      }
    }

    private static void CollectLeafMaskPaths(
        IDictionary<string, FirestoreValue> map, string[] prefix, List<FieldPath> mask) {
      foreach (KeyValuePair<string, FirestoreValue> kv in map) {
        string[] next = AppendSegment(prefix, kv.Key);
        if (kv.Value.Kind == FirestoreValueKind.Map && kv.Value.MapValue.Count > 0) {
          CollectLeafMaskPaths(kv.Value.MapValue, next, mask);
        } else {
          mask.Add(new FieldPath(next));
        }
      }
    }

    private static bool InputContainsPath(
        IDictionary<string, FirestoreValue> rawMap, FieldPath target) {
      string[] segs = target.Segments;
      IDictionary<string, FirestoreValue> cur = rawMap;
      for (int i = 0; i < segs.Length; i++) {
        if (cur == null || !cur.TryGetValue(segs[i], out FirestoreValue val)) {
          return false;
        }
        if (i == segs.Length - 1) return true;
        cur = val.Kind == FirestoreValueKind.Map ? val.MapValue : null;
      }
      return false;
    }

    private static bool IsCoveredByMask(FieldPath path, IReadOnlyList<FieldPath> mask) {
      for (int i = 0; i < mask.Count; i++) {
        string[] m = mask[i].Segments;
        string[] p = path.Segments;
        if (m.Length <= p.Length) {
          bool match = true;
          for (int j = 0; j < m.Length; j++) {
            if (!string.Equals(m[j], p[j], StringComparison.Ordinal)) {
              match = false;
              break;
            }
          }
          if (match) return true;
        }
      }
      return false;
    }

    private static bool IsPrefixOrEqual(FieldPath a, FieldPath b) {
      string[] sa = a.Segments, sb = b.Segments;
      int min = Math.Min(sa.Length, sb.Length);
      for (int i = 0; i < min; i++) {
        if (!string.Equals(sa[i], sb[i], StringComparison.Ordinal)) return false;
      }
      return true;
    }

    private static void AddUniqueFieldPath(List<FieldPath> list, FieldPath fp) {
      for (int i = 0; i < list.Count; i++) {
        if (list[i].Equals(fp)) return;
      }
      list.Add(fp);
    }

    private static string[] AppendSegment(string[] prefix, string segment) {
      var res = new string[prefix.Length + 1];
      if (prefix.Length > 0) Array.Copy(prefix, 0, res, 0, prefix.Length);
      res[prefix.Length] = segment;
      return res;
    }
  }
}
