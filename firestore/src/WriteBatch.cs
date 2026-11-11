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
using System.Threading.Tasks;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A batch of write operations, used to perform multiple writes as a single atomic unit.
  /// </summary>
  /// <remarks>
  /// <para>A <c>WriteBatch</c> object can be acquired by calling
  /// <see cref="FirebaseFirestore.StartBatch"/>. It provides methods for adding writes to the write
  /// batch. None of the writes will be committed (or visible locally) until
  /// <see cref="CommitAsync"/> is called.</para>
  ///
  /// <para>Unlike transactions, write batches are persisted offline and therefore are preferable
  /// when you don't need to condition your writes on read data.</para>
  /// </remarks>
  public sealed class WriteBatch {
    private readonly FirebaseFirestore _firestore;
    private readonly object _lock = new object();
    private readonly List<MutationSpec> _mutations = new List<MutationSpec>();
    private bool _committed;

    internal WriteBatch(FirebaseFirestore firestore) {
      _firestore = Preconditions.CheckNotNull(firestore, nameof(firestore));
    }

    private void ValidateWritableAndRef(DocumentReference docRef) {
      Preconditions.CheckNotNull(docRef, nameof(docRef));
      if (!ReferenceEquals(docRef.Firestore, _firestore)) {
        throw new ArgumentException(
            "Provided DocumentReference belongs to a different FirebaseFirestore instance.",
            nameof(docRef));
      }
      lock (_lock) {
        if (_committed) {
          throw new InvalidOperationException("A WriteBatch can no longer be used after CommitAsync has been called.");
        }
      }
    }

    /// <summary>
    /// Deletes the document referenced by the provided <c>DocumentReference</c>.
    /// </summary>
    /// <param name="documentReference">The document to delete. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Delete(DocumentReference documentReference) {
      ValidateWritableAndRef(documentReference);
      lock (_lock) {
        _mutations.Add(MutationSpec.CreateDelete(documentReference.ResourcePath));
      }
      return this;
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields can contain dots
    /// to reference nested fields in the document.  Fields not present in this dictionary are not
    /// updated. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(
        DocumentReference documentReference, IDictionary<string, object> updates) {
      ValidateWritableAndRef(documentReference);
      Preconditions.CheckNotNull(updates, nameof(updates));

      var fpUpdates = new List<KeyValuePair<FieldPath, object>>(updates.Count);
      foreach (KeyValuePair<string, object> kv in updates) {
        FieldPath fp = FieldPath.FromDotSeparatedString(kv.Key);
        fpUpdates.Add(new KeyValuePair<FieldPath, object>(fp, kv.Value));
      }
      MutationSpec spec = MutationSpec.ParseUpdate(
          SerializationContext.Default,
          documentReference.ResourcePath,
          fpUpdates,
          _firestore.ProjectId,
          _firestore.DatabaseId);
      lock (_lock) {
        _mutations.Add(spec);
      }
      return this;
    }

    /// <summary>
    /// Updates the field in the document referred to by the provided <c>DocumentReference</c>. If
    /// no document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="field">The dot-separated name of the field to update. Must not be <c>null</c>.
    /// </param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(DocumentReference documentReference, string field, object value) {
      ValidateWritableAndRef(documentReference);
      Preconditions.CheckNotNullOrEmpty(field, nameof(field));
      return Update(documentReference, new Dictionary<string, object>(1, StringComparer.Ordinal) {
        { field, value },
      });
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields not present in
    /// this dictionary are not updated. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(
        DocumentReference documentReference, IDictionary<FieldPath, object> updates) {
      ValidateWritableAndRef(documentReference);
      Preconditions.CheckNotNull(updates, nameof(updates));
      foreach (KeyValuePair<FieldPath, object> kv in updates) {
        if (kv.Key == null) {
          throw new ArgumentNullException(nameof(updates), "FieldPath key cannot be null.");
        }
      }
      MutationSpec spec = MutationSpec.ParseUpdate(
          SerializationContext.Default,
          documentReference.ResourcePath,
          updates,
          _firestore.ProjectId,
          _firestore.DatabaseId);
      lock (_lock) {
        _mutations.Add(spec);
      }
      return this;
    }

    /// <summary>
    /// Writes to the document referred to by the provided <c>DocumentReference</c>. If the document
    /// does not yet exist, it will be created. If you pass <paramref name="options"/>, the provided
    /// data can be merged into an existing document.
    /// </summary>
    /// <param name="documentReference">The document in which to set the data. Must not be
    /// <c>null</c>.</param>
    /// <param name="documentData">The data for the document. Must not be <c>null</c>.</param>
    /// <param name="options">The options to use when updating the document. May be <c>null</c>,
    /// which is equivalent to <see cref="SetOptions.Overwrite"/>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Set(
        DocumentReference documentReference, object documentData, SetOptions options = null) {
      ValidateWritableAndRef(documentReference);
      Preconditions.CheckNotNull(documentData, nameof(documentData));

      FirestoreValue data = ValueSerializer.Serialize(SerializationContext.Default, documentData);
      if (data.Kind != FirestoreValueKind.Map) {
        throw new ArgumentException(
            "documentData must be either an IDictionary or a POCO. Instead we get " +
            documentData.GetType().FullName);
      }
      var rawMap = data.MapValue as Dictionary<string, FirestoreValue>
          ?? FirestoreValue.CloneMapDeep(data.MapValue);
      MutationSpec spec = MutationSpec.ParseSet(
          SerializationContext.Default,
          documentReference.ResourcePath,
          rawMap,
          options ?? SetOptions.Overwrite,
          _firestore.ProjectId,
          _firestore.DatabaseId);
      lock (_lock) {
        _mutations.Add(spec);
      }
      return this;
    }

    /// <summary>
    /// Commits all of the writes in this write batch as a single atomic unit.
    /// </summary>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task CommitAsync() {
      List<MutationSpec> snapshot;
      lock (_lock) {
        if (_committed) {
          return Util.TaskFromException<bool>(new InvalidOperationException(
              "A WriteBatch can no longer be used after CommitAsync has been called."));
        }
        _committed = true;
        snapshot = new List<MutationSpec>(_mutations);
      }
      return _firestore.Core.EnqueueMutationsAsync(snapshot);
    }
  }
}
