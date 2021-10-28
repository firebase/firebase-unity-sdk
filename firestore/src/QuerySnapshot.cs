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

namespace Firebase.Firestore {
  /// <summary>
  /// A <c>QuerySnapshot</c> contains the results of a query. It can contain zero or more
  /// <see cref="DocumentSnapshot"/> objects.
  /// </summary>
  // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
  public sealed class QuerySnapshot : IEnumerable<DocumentSnapshot> {
    private readonly QuerySnapshotProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    // We cache documents / changes so we only materialize each at most once.
    private DocumentSnapshot[] _documentsCached;

    internal QuerySnapshot(QuerySnapshotProxy proxy, FirebaseFirestore firestore) {
      _proxy = Util.NotNull(proxy);
      _firestore = Util.NotNull(firestore);
      _documentsCached = null;
    }

    /// <summary>
    /// The query producing this snapshot.
    /// </summary>
    public Query Query => new Query(_proxy.query(), _firestore);

    /// <summary>
    /// The metadata for this <c>QuerySnapshot</c>.
    /// </summary>
    public SnapshotMetadata Metadata {
      get {
        return SnapshotMetadata.ConvertFromProxy(_proxy.metadata());
      }
    }

    /// <summary>
    /// The documents in the snapshot.
    /// </summary>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentSnapshot> Documents {
      get {
        LoadDocumentsCached();
        return _documentsCached;
      }
    }

    /// <summary>
    /// Returns the document snapshot with the specified index within this query snapshot.
    /// </summary>
    /// <param name="index">The index of the document to return.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is less than 0, or
    /// greater than or equal to <see cref="Count"/>.</exception>
    /// <returns>The document snapshot with the specified index within this query snapshot.
    /// </returns>
    public DocumentSnapshot this[int index] {
      get {
        if (index < 0 || index >= Count) {
          throw new ArgumentOutOfRangeException();
        }

        LoadDocumentsCached();
        return _documentsCached[index];
      }
    }

    /// <summary>
    /// Returns the number of documents in this query snapshot.
    /// </summary>
    /// <value>The number of documents in this query snapshot.</value>
    public int Count {
      get {
        uint size = _proxy.size();
        if (size > Int32.MaxValue) {
          throw new ArgumentException("number of documents is larger than " + Int32.MaxValue);
        }
        return (int)size;
      }
    }

    /// <summary>
    /// The list of documents that changed since the last snapshot. If it's the first snapshot all
    /// documents will be in the list as added changes.
    /// </summary>
    /// <remarks>
    /// <para>Documents with changes only to their metadata will not be included.</para>
    /// </remarks>
    /// <returns>The list of document changes since the last snapshot.</returns>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentChange> GetChanges() {
      return GetChanges(MetadataChanges.Exclude);
    }

    /// <summary>
    /// The list of documents that changed since the last snapshot. If it's the first snapshot all
    /// documents will be in the list as added changes.
    /// </summary>
    /// <param name="metadataChanges">Indicates whether metadata-only changes (i.e. only
    /// <see cref="Firebase.Firestore.DocumentSnapshot.Metadata" /> changed) should be included.</param>
    /// <returns>The list of document changes since the last snapshot.</returns>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentChange> GetChanges(MetadataChanges metadataChanges) {
      var changes = FirestoreCpp.QuerySnapshotDocumentChanges(_proxy,
          Enums.Convert(metadataChanges));
      uint size = changes.Size();

      if (size > Int32.MaxValue) {
        throw new ArgumentException("Number of document changes is larger than " + Int32.MaxValue);
      }

      DocumentChange[] result = new DocumentChange[(int)size];

      for (int i = 0; i < (int)size; ++i) {
        result[i] = new DocumentChange(changes.GetCopy((uint)i), _firestore);
      }

      return result;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as QuerySnapshot);

    /// <inheritdoc />
    public bool Equals(QuerySnapshot other) => other != null
                                               && FirestoreCpp.QuerySnapshotEquals(_proxy,
                                                                                   other._proxy);

    /// <inheritdoc />
    public override int GetHashCode() {
      return FirestoreCpp.QuerySnapshotHashCode(_proxy);
    }

    /// <inheritdoc />
    public IEnumerator<DocumentSnapshot> GetEnumerator() {
      LoadDocumentsCached();
      return ((IEnumerable<DocumentSnapshot>)_documentsCached).GetEnumerator();
    }

    /// <inheritdoc />
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
      return GetEnumerator();
    }

    private void LoadDocumentsCached() {
      if (_documentsCached != null) {
        return;
      }

      DocumentSnapshot[] documents = new DocumentSnapshot[Count];
      var result = FirestoreCpp.QuerySnapshotDocuments(_proxy);
      for (int i = 0; i < Count; ++i) {
        documents[i] = new DocumentSnapshot(result.GetCopy((uint)i), _firestore);
      }

      _documentsCached = documents;
    }
  }
}
