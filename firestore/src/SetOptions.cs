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

using Firebase.Firestore.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

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
  public sealed class SetOptions {
    // Since there are a few combinations how SetOptions can be created, we
    // simply convert it to internal type and store it instead of storing all
    // the constructor parameters.
    private readonly SetOptionsProxy _proxy;

    // Type, setType and fields exist only for the purpose to implement equality, ToString and
    // GetHashCode. They are not from underlying C++ values, and will not be passed to C++.
    private enum Type { Overwrite, Merge }
    private Type setType = Type.Overwrite;
    private FieldPath[] fields = {};

    internal SetOptionsProxy Proxy {
      get {
        return _proxy;
      }
    }

    private SetOptions(SetOptionsProxy proxy) {
      _proxy = Util.NotNull(proxy);
    }

    /// <summary>
    /// Returns an instance that overwrites the target object. This is the default when no options
    /// are provided.
    /// </summary>
    public static SetOptions Overwrite {
      get {
        var options = new SetOptions(new SetOptionsProxy());
        options.fields = new FieldPath[] {};
        options.setType = Type.Overwrite;
        return options;
      }
    }

    /// <summary>
    /// Changes the behavior of <c>SetAsync</c> calls to only replace the values specified in its
    /// <c>documentData</c> argument. Fields omitted from the <c>SetAsync</c> call will remain
    /// untouched.
    /// </summary>
    public static SetOptions MergeAll {
      get {
        var options = new SetOptions(SetOptionsProxy.Merge());
        options.fields = new FieldPath[] {};
        options.setType = Type.Merge;
        return options;
      }
    }

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
        throw new ArgumentException("fields must not be null");
      }

      FieldPath[] fieldPaths =
          fields
              .Select((fieldPath => {
                if (String.IsNullOrEmpty(fieldPath)) {
                  throw new ArgumentException("fields must not contain any empty or null elements");
                }
                return new FieldPath(fieldPath);
              }))
              .ToArray();

      StringList fieldList = new StringList((ICollection)fields);
      var options = new SetOptions(SetOptionsProxy.MergeFields(fieldList));

      options.fields = fieldPaths;
      options.setType = Type.Merge;
      return options;
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
      var fieldVector = new FieldPathVector();
      foreach (FieldPath fieldPath in fields) {
        fieldVector.PushBack(fieldPath.ConvertToProxy());
      }
      var options = new SetOptions(FirestoreCpp.SetOptionsMergeFieldPaths(fieldVector));

      options.fields = fields;
      options.setType = Type.Merge;
      return options;
    }

    /// <inheritdoc />
    public override string ToString() {
      var stringBuilder = new StringBuilder();
      stringBuilder.Append("SetOptions{ Type=" + setType + ";");

      if (setType == Type.Merge) {
        stringBuilder.Append(" Fields=[ ");
        stringBuilder.Append(String.Join(",", fields.Select((f) => f.ToString()).ToArray()));
        stringBuilder.Append(" ]");
      }

      stringBuilder.Append("}");
      return stringBuilder.ToString();
    }

    /// <inheritdoc />
    public override bool Equals(object obj) {
      return Equals(obj as SetOptions);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      unchecked {
        var hashCode = 47893;
        hashCode = (int)(hashCode * 755707 + setType);
        // The contract ensures when type is not `Merge`, `fields` will be empty, so we
        // return early here.
        if (setType != Type.Merge)
          return hashCode;

        return fields.Aggregate(hashCode, (current, f) => current * 755707 + f.GetHashCode());
      }
    }

    private bool Equals(SetOptions other) {
      return other != null && setType == other.setType &&
             Enumerable.SequenceEqual(fields, other.fields);
    }
  }
}
