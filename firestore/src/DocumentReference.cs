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
  /// A <c>DocumentReference</c> refers to a document location in a Cloud Firestore database and can
  /// be used to write, read, or listen to the location. There may or may not exist a document at
  /// the referenced location. A <c>DocumentReference</c> can also be used to create a
  /// <see cref="CollectionReference"/> to a subcollection.
  /// </summary>
  public sealed class DocumentReference : IEquatable<DocumentReference> {
    private readonly ResourcePath _path;
    private readonly FirebaseFirestore _firestore;

    internal ResourcePath ResourcePath => _path;

    internal DocumentReference(ResourcePath path, FirebaseFirestore firestore) {
      _path = Preconditions.CheckNotNull(path, nameof(path));
      _firestore = Preconditions.CheckNotNull(firestore, nameof(firestore));
    }

    /// <summary>
    /// The database which contains the document.
    /// </summary>
    public FirebaseFirestore Firestore => _firestore;

    /// <summary>
    /// The final part of the complete document path; this is the identity of the document relative
    /// to its parent collection.
    /// </summary>
    public string Id => _firestore.IsDisposed ? string.Empty : _path.Id;

    /// <summary>
    /// The complete document path, not including project and database ID.
    /// </summary>
    public string Path => _firestore.IsDisposed ? string.Empty : _path.CanonicalString;

    /// <summary>
    /// The parent collection. Never <c>null</c>.
    /// </summary>
    public CollectionReference Parent => new CollectionReference(_path.Parent, _firestore);

    /// <summary>
    /// Creates a <see cref="CollectionReference"/> for a child collection of this document.
    /// </summary>
    /// <param name="path">The path to the collection, relative to this document. Must not be
    /// <c>null</c>, and must contain an odd number of slash-separated path elements.</param>
    /// <returns>A <see cref="CollectionReference"/> for the specified collection.</returns>
    public CollectionReference Collection(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      ResourcePath rel = ResourcePath.ParseRelative(path, nameof(path));
      if (rel.Count % 2 == 0) {
        throw new ArgumentException(
            $"Invalid collection reference. Relative collection paths must have an odd number of segments, but {path} has {rel.Count}.",
            nameof(path));
      }
      return new CollectionReference(_path.Append(rel), _firestore);
    }

    /// <inheritdoc />
    public override int GetHashCode() => _firestore.GetHashCode() * 31 + _path.GetHashCode();

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DocumentReference);

    /// <inheritdoc />
    public bool Equals(DocumentReference other) =>
        other != null &&
        ReferenceEquals(_firestore, other._firestore) &&
        _path.Equals(other._path);

    /// <inheritdoc />
    public override string ToString() =>
        $"/projects/{_firestore.ProjectId}/databases/{_firestore.DatabaseId}/{Path}";

    /// <summary>
    /// Asynchronously deletes the document referred to by this <c>DocumentReference</c>.
    /// </summary>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task DeleteAsync() {
      return _firestore.Core.EnqueueMutationsAsync(
          new[] { MutationSpec.CreateDelete(_path) });
    }

    /// <summary>
    /// Asynchronously performs a set of updates on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="updates">The updates to perform on the document, keyed by the field path to
    /// update. Fields not present in this dictionary are not updated. Must not be <c>null</c>.
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(IDictionary<string, object> updates) {
      Preconditions.CheckNotNull(updates, nameof(updates));
      try {
        var fpUpdates = new List<KeyValuePair<FieldPath, object>>(updates.Count);
        foreach (KeyValuePair<string, object> kv in updates) {
          FieldPath fp = FieldPath.FromDotSeparatedString(kv.Key);
          fpUpdates.Add(new KeyValuePair<FieldPath, object>(fp, kv.Value));
        }
        MutationSpec spec = MutationSpec.ParseUpdate(
            SerializationContext.Default,
            _path,
            fpUpdates,
            _firestore.ProjectId,
            _firestore.DatabaseId);
        return _firestore.Core.EnqueueMutationsAsync(new[] { spec });
      } catch (Exception ex) {
        return Util.TaskFromException<bool>(ex);
      }
    }

    /// <summary>
    /// Asynchronously performs a single field update on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="field">The dot-separated name of the field to update. Must not be
    /// <c>null</c>.</param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(string field, object value) {
      Preconditions.CheckNotNull(field, nameof(field));
      // Validate value serializability synchronously if non-null, while empty field string faults Task
      // (matching DocumentReference_UpdateAsync_EmptyStringField in InvalidArgumentsTest.cs).
      if (value != null) {
        ValueSerializer.Serialize(SerializationContext.Default, value);
      }
      return UpdateAsync(new Dictionary<string, object>(1, StringComparer.Ordinal) {
        { field, value },
      });
    }

    /// <summary>
    /// Asynchronously performs a set of updates on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="updates">The updates to perform on the document, keyed by the field path to
    /// update. Fields not present in this dictionary are not updated. Must not be <c>null</c>.
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(IDictionary<FieldPath, object> updates) {
      Preconditions.CheckNotNull(updates, nameof(updates));
      // Null FieldPath key throws synchronously; other validation errors fault the returned Task.
      foreach (KeyValuePair<FieldPath, object> kv in updates) {
        if (kv.Key == null) {
          throw new ArgumentNullException(nameof(updates), "Update dictionary cannot contain a null FieldPath key.");
        }
      }
      try {
        MutationSpec spec = MutationSpec.ParseUpdate(
            SerializationContext.Default,
            _path,
            updates,
            _firestore.ProjectId,
            _firestore.DatabaseId);
        return _firestore.Core.EnqueueMutationsAsync(new[] { spec });
      } catch (Exception ex) {
        return Util.TaskFromException<bool>(ex);
      }
    }

    /// <summary>
    /// Asynchronously sets data in the document, either replacing it completely or merging fields.
    /// </summary>
    /// <param name="documentData">The data to store in the document. Must not be <c>null</c>.
    /// </param>
    /// <param name="options">
    /// <para>The options to use when updating the document. May be <c>null</c>, which is equivalent
    /// to <see cref="SetOptions.Overwrite"/>.</para>
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task SetAsync(object documentData, SetOptions options = null) {
      Preconditions.CheckNotNull(documentData, nameof(documentData));
      options ??= SetOptions.Overwrite;

      FirestoreValue serialized =
          ValueSerializer.Serialize(SerializationContext.Default, documentData);
      if (serialized.Kind != FirestoreValueKind.Map) {
        throw new ArgumentException(
            "documentData must be either an IDictionary or a POCO. Instead we got " +
            documentData.GetType().FullName);
      }

      try {
        var rawMap = serialized.MapValue as Dictionary<string, FirestoreValue>
            ?? FirestoreValue.CloneMapDeep(serialized.MapValue);
        MutationSpec spec = MutationSpec.ParseSet(
            SerializationContext.Default,
            _path,
            rawMap,
            options,
            _firestore.ProjectId,
            _firestore.DatabaseId);
        return _firestore.Core.EnqueueMutationsAsync(new[] { spec });
      } catch (Exception ex) {
        return Util.TaskFromException<bool>(ex);
      }
    }

    /// <summary>
    /// Asynchronously fetches a snapshot of the document.
    /// </summary>
    /// <remarks>
    /// <para>By default, <c>GetSnapshotAsync</c> attempts to provide up-to-date data when possible
    /// by waiting for data from the server, but it may return cached data or fail if you are
    /// offline and the server cannot be reached. This behavior can be altered via the <c>source</c>
    /// parameter.</para>
    /// </remarks>
    /// <param name="source">indicates whether the results should be fetched from the cache only
    /// (<c>Source.Cache</c>), the server only (<c>Source.Server</c>), or to attempt the server and
    /// fall back to the cache (<c>Source.Default</c>).</param>
    /// <returns>A snapshot of the document. The snapshot may represent a missing document.</returns>
    public Task<DocumentSnapshot> GetSnapshotAsync(Source source = Source.Default) {
      return _firestore.Core.GetDocumentSnapshotAsync(this, source);
    }

    /// <summary>
    /// Starts listening to changes to the document referenced by this <c>DocumentReference</c>.
    /// </summary>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(Action<DocumentSnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      return Listen(MetadataChanges.Exclude, callback);
    }

    /// <summary>
    /// Starts listening to changes to the document referenced by this <c>DocumentReference</c>.
    /// </summary>
    /// <param name="metadataChanges">Indicates whether metadata-only changes (i.e. only
    /// <c>DocumentSnapshot.Metadata</c> changed) should trigger snapshot events.</param>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(
        MetadataChanges metadataChanges, Action<DocumentSnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      return _firestore.Core.ListenDocument(this, metadataChanges, callback);
    }
  }
}
