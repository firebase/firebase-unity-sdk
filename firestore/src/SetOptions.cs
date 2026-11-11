// Copyright 2017 Google LLC
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
using System.Linq;
using System.Text;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// An options object that configures the behavior of <c>SetAsync</c> calls.
  /// </summary>
  /// <remarks>
  /// By providing one of the <c>SetOptions</c> objects returned by
  /// <see cref="SetOptions.MergeAll"/> and <see cref="SetOptions.MergeFields(string[])"/>
  /// the <c>SetAsync</c> calls in <c>DocumentReference</c>, <c>WriteBatch</c> and
  /// <c>Transaction</c> can be configured to perform granular merges instead of overwriting the
  /// target documents in their entirety.
  /// </remarks>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class SetOptions {
    internal enum SetKind : byte {
      Overwrite = 0,
      MergeAll = 1,
      MergeFields = 2,
    }

    private enum DisplayType { Overwrite, Merge }

    internal SetKind Kind { get; }
    internal IReadOnlyList<FieldPath> FieldMask { get; }
    private readonly DisplayType _displayType;

    private SetOptions(SetKind kind, FieldPath[] fieldMask, DisplayType displayType) {
      Kind = kind;
      FieldMask = fieldMask ?? Array.Empty<FieldPath>();
      _displayType = displayType;
    }

    /// <summary>
    /// Returns an instance that overwrites the target object. This is the default when no options
    /// are provided.
    /// </summary>
    public static SetOptions Overwrite { get; } =
        new SetOptions(SetKind.Overwrite, Array.Empty<FieldPath>(), DisplayType.Overwrite);

    /// <summary>
    /// Changes the behavior of <c>SetAsync</c> calls to only replace the values specified in its
    /// <c>documentData</c> argument. Fields omitted from the <c>SetAsync</c> call will remain
    /// untouched.
    /// </summary>
    public static SetOptions MergeAll { get; } =
        new SetOptions(SetKind.MergeAll, Array.Empty<FieldPath>(), DisplayType.Merge);

    /// <summary>
    /// Changes the behavior of <c>SetAsync</c> calls to only replace the given fields. Any field
    /// that is not specified in <paramref name="fields"/> is ignored and remains untouched.
    /// </summary>
    /// <remarks>
    /// It is an error to pass a <c>SetOptions</c> object to a <c>set()</c> call that is missing a
    /// value for any of the fields specified here.
    /// </remarks>
    /// <param name="fields">The fields to merge. An empty array is equivalent to using
    /// <see cref="MergeAll"/>. Must not be <c>null</c> or contain any empty or <c>null</c>
    /// elements. Each field is treated as a dot-separated list of segments. </param>
    /// <returns>An instance that merges the given fields.</returns>
    public static SetOptions MergeFields(params string[] fields) {
      if (fields == null) {
        throw new ArgumentException("fields must not be null", nameof(fields));
      }
      var fieldPaths = new FieldPath[fields.Length];
      for (int i = 0; i < fields.Length; i++) {
        if (string.IsNullOrEmpty(fields[i])) {
          throw new ArgumentException("fields must not contain any empty or null elements", nameof(fields));
        }
        fieldPaths[i] = FieldPath.FromDotSeparatedString(fields[i]);
      }
      return new SetOptions(SetKind.MergeFields, fieldPaths, DisplayType.Merge);
    }

    /// <summary>
    /// Changes the behavior of <c>SetAsync</c> calls to only replace the given fields. Any field
    /// that is not specified in <paramref name="fields"/> is ignored and remains untouched.
    /// </summary>
    /// <remarks>
    /// It is an error to pass a <c>SetOptions</c> object to a <c>SetAsync</c> call that is missing
    /// a value for any of the fields specified here in its data argument.
    /// </remarks>
    /// <param name="fields">The fields to merge. An empty array is equivalent to using
    /// <see cref="MergeAll"/>.  Must not be <c>null</c> or contain any <c>null</c>
    /// elements.</param>
    /// <returns>An instance that merges the given fields.</returns>
    public static SetOptions MergeFields(params FieldPath[] fields) {
      Preconditions.CheckNotNull(fields, nameof(fields));
      var copy = new FieldPath[fields.Length];
      for (int i = 0; i < fields.Length; i++) {
        if (fields[i] == null) {
          throw new ArgumentException("fields must not contain any null elements", nameof(fields));
        }
        copy[i] = fields[i];
      }
      return new SetOptions(SetKind.MergeFields, copy, DisplayType.Merge);
    }

    /// <inheritdoc />
    public override string ToString() {
      var stringBuilder = new StringBuilder();
      stringBuilder.Append("SetOptions{ Type=").Append(_displayType).Append(';');
      if (_displayType == DisplayType.Merge) {
        stringBuilder.Append(" Fields=[ ");
        stringBuilder.Append(String.Join(",", FieldMask.Select(f => f.ToString()).ToArray()));
        stringBuilder.Append(" ]");
      }
      stringBuilder.Append('}');
      return stringBuilder.ToString();
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as SetOptions);

    /// <inheritdoc />
    public override int GetHashCode() {
      unchecked {
        int hashCode = 47893;
        hashCode = hashCode * 755707 + (int)_displayType;
        // The contract ensures when type is not `Merge`, `fields` will be empty, so we
        // return early here.
        if (_displayType != DisplayType.Merge) return hashCode;
        hashCode = hashCode * 31 + (int)Kind;
        return FieldMask.Aggregate(hashCode, (current, f) => current * 755707 + f.GetHashCode());
      }
    }

    private bool Equals(SetOptions other) =>
        other != null &&
        Kind == other.Kind &&
        _displayType == other._displayType &&
        Enumerable.SequenceEqual(FieldMask, other.FieldMask);
  }
}
