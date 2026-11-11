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
using System.Threading;
using System.Threading.Tasks;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A transaction, as created by
  /// <see cref="FirebaseFirestore.RunTransactionAsync{T}(System.Func{Transaction, Task{T}})"/>
  /// (and overloads) and passed to user code.
  /// </summary>
  public sealed class Transaction {
    private readonly FirebaseFirestore _firestore;
    private readonly FirestoreRpcClient _rpc;
    private readonly string _transactionId;
    private readonly CancellationToken _ct;
    private readonly object _lock = new object();
    private readonly List<MutationSpec> _mutations = new List<MutationSpec>();
    private volatile bool _disposed;

    internal List<MutationSpec> PendingMutations {
      get { lock (_lock) return new List<MutationSpec>(_mutations); }
    }

    internal void MarkDisposed() => _disposed = true;

    private bool IsInvalid =>
        _disposed || _firestore.IsDisposed || _firestore.Core.IsDisposed || _firestore.Core.IsTerminated;

    private void EnsureValid() {
      if (IsInvalid) {
        throw new InvalidOperationException("Transaction is no longer valid.");
      }
    }

    internal Transaction(
        FirebaseFirestore firestore,
        FirestoreRpcClient rpc,
        string transactionId,
        CancellationToken ct) {
      _firestore = Preconditions.CheckNotNull(firestore, nameof(firestore));
      _rpc = Preconditions.CheckNotNull(rpc, nameof(rpc));
      _transactionId = transactionId;
      _ct = ct;
    }

    /// <summary>
    /// The database for this transaction.
    /// </summary>
    public FirebaseFirestore Firestore => _firestore;

    private void ValidateDocumentReference(DocumentReference docRef) {
      Preconditions.CheckNotNull(docRef, nameof(docRef));
      if (!ReferenceEquals(docRef.Firestore, _firestore)) {
        throw new ArgumentException(
            "Provided DocumentReference belongs to a different FirebaseFirestore instance.",
            nameof(docRef));
      }
    }

    /// <summary>
    /// Read a snapshot of the document specified by <paramref name="documentReference"/>, with
    /// respect to this transaction. This method cannot be called after any write operations have
    /// been created.
    /// </summary>
    /// <param name="documentReference">The document reference to read. Must not be <c>null</c>.
    /// </param>
    /// <returns>A snapshot of the given document with respect to this transaction.</returns>
    public Task<DocumentSnapshot> GetSnapshotAsync(DocumentReference documentReference) {
      ValidateDocumentReference(documentReference);
      if (IsInvalid) {
        return Util.TaskFromException<DocumentSnapshot>(
            new InvalidOperationException("Transaction is no longer valid."));
      }
      lock (_lock) {
        if (_mutations.Count > 0) {
          return Util.TaskFromException<DocumentSnapshot>(new InvalidOperationException(
              "Firestore transactions require all reads to be executed before all writes."));
        }
      }
      FirestoreException invalidPath = documentReference.ResourcePath.ValidateForBackend();
      if (invalidPath != null) {
        return Util.TaskFromException<DocumentSnapshot>(invalidPath);
      }
      return ReadTransactionalDocAsync(documentReference);
    }

    private async Task<DocumentSnapshot> ReadTransactionalDocAsync(DocumentReference docRef) {
      EnsureValid();
      Dictionary<ResourcePath, MutableDocument> res =
          await _rpc.BatchGetAsync(new[] { docRef.ResourcePath }, _transactionId, _ct)
              .ConfigureAwait(false);
      EnsureValid();
      res.TryGetValue(docRef.ResourcePath, out MutableDocument doc);
      bool exists = doc != null && doc.Exists;
      return new DocumentSnapshot(
          docRef,
          exists ? doc.Fields : null,
          exists,
          new SnapshotMetadata(hasPendingWrites: false, isFromCache: false));
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
    public void Set(
        DocumentReference documentReference, object documentData, SetOptions options = null) {
      ValidateDocumentReference(documentReference);
      Preconditions.CheckNotNull(documentData, nameof(documentData));
      EnsureValid();
      FirestoreException pathErr = documentReference.ResourcePath.ValidateForBackend();
      if (pathErr != null) {
        throw new ArgumentException(pathErr.Message, nameof(documentReference));
      }

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
        EnsureValid();
        _mutations.Add(spec);
      }
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields can contain dots
    /// to reference nested fields in the document.  Fields not present in this dictionary are not
    /// updated. Must not be <c>null</c>.</param>
    public void Update(DocumentReference documentReference, IDictionary<string, object> updates) {
      ValidateDocumentReference(documentReference);
      Preconditions.CheckNotNull(updates, nameof(updates));
      EnsureValid();
      FirestoreException pathErr = documentReference.ResourcePath.ValidateForBackend();
      if (pathErr != null) {
        throw new ArgumentException(pathErr.Message, nameof(documentReference));
      }

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
        EnsureValid();
        _mutations.Add(spec);
      }
    }

    /// <summary>
    /// Updates the field in the document referred to by the provided <c>DocumentReference</c>. If
    /// no document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="field">The dot-separated name of the field to update. Must not be <c>null</c>.
    /// </param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    public void Update(DocumentReference documentReference, string field, object value) {
      ValidateDocumentReference(documentReference);
      Preconditions.CheckNotNullOrEmpty(field, nameof(field));
      EnsureValid();
      Update(documentReference, new Dictionary<string, object>(1, StringComparer.Ordinal) {
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
    public void Update(DocumentReference documentReference, IDictionary<FieldPath, object> updates) {
      ValidateDocumentReference(documentReference);
      Preconditions.CheckNotNull(updates, nameof(updates));
      EnsureValid();
      FirestoreException pathErr = documentReference.ResourcePath.ValidateForBackend();
      if (pathErr != null) {
        throw new ArgumentException(pathErr.Message, nameof(documentReference));
      }

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
        EnsureValid();
        _mutations.Add(spec);
      }
    }

    /// <summary>
    /// Deletes the document referenced by the provided <c>DocumentReference</c>.
    /// </summary>
    /// <param name="documentReference">The document to delete. Must not be <c>null</c>.</param>
    public void Delete(DocumentReference documentReference) {
      ValidateDocumentReference(documentReference);
      EnsureValid();
      lock (_lock) {
        EnsureValid();
        _mutations.Add(MutationSpec.CreateDelete(documentReference.ResourcePath));
      }
    }
  }
}
