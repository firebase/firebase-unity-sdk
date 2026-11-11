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
using System.Collections;
using System.Collections.Generic;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A <c>QuerySnapshot</c> contains the results of a query. It can contain zero or more
  /// <see cref="DocumentSnapshot"/> objects.
  /// </summary>
  // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
  public sealed class QuerySnapshot : IEnumerable<DocumentSnapshot> {
    private readonly Query _query;
    private readonly IReadOnlyList<DocumentSnapshot> _documents;
    private readonly IReadOnlyList<DocumentChange> _excludeChanges;
    private readonly IReadOnlyList<DocumentChange> _includeChanges;
    private readonly SnapshotMetadata _metadata;
    private readonly bool _hadCommittedMutations;

    internal QuerySnapshot(
        Query query,
        IReadOnlyList<DocumentSnapshot> documents,
        IReadOnlyList<DocumentChange> excludeChanges,
        IReadOnlyList<DocumentChange> includeChanges,
        SnapshotMetadata metadata,
        bool hadCommittedMutations = false) {
      _query = Preconditions.CheckNotNull(query, nameof(query));
      _documents = documents ?? Array.Empty<DocumentSnapshot>();
      _excludeChanges = excludeChanges ?? Array.Empty<DocumentChange>();
      _includeChanges = includeChanges ?? _excludeChanges;
      _metadata = metadata ?? new SnapshotMetadata(false, false);
      _hadCommittedMutations = hadCommittedMutations;
    }

    /// <summary>
    /// The query producing this snapshot.
    /// </summary>
    public Query Query => _query;

    /// <summary>
    /// The metadata for this <c>QuerySnapshot</c>.
    /// </summary>
    public SnapshotMetadata Metadata => _metadata;

    /// <summary>
    /// The documents in the snapshot.
    /// </summary>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentSnapshot> Documents => _documents;

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
        if (index < 0 || index >= _documents.Count) {
          throw new ArgumentOutOfRangeException(nameof(index));
        }
        return _documents[index];
      }
    }

    /// <summary>
    /// Returns the number of documents in this query snapshot.
    /// </summary>
    /// <value>The number of documents in this query snapshot.</value>
    public int Count => _documents.Count;

    /// <summary>
    /// The list of documents that changed since the last snapshot. If it's the first snapshot all
    /// documents will be in the list as added changes.
    /// </summary>
    /// <remarks>
    /// <para>Documents with changes only to their metadata will not be included.</para>
    /// </remarks>
    /// <returns>The list of document changes since the last snapshot.</returns>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentChange> GetChanges() => GetChanges(MetadataChanges.Exclude);

    /// <summary>
    /// The list of documents that changed since the last snapshot. If it's the first snapshot all
    /// documents will be in the list as added changes.
    /// </summary>
    /// <param name="metadataChanges">Indicates whether metadata-only changes (i.e. only
    /// <see cref="Firebase.Firestore.DocumentSnapshot.Metadata" /> changed) should be included.</param>
    /// <returns>The list of document changes since the last snapshot.</returns>
    // TODO(zxu): change to IReadOnlyList type, which needs .net 4.5 while current tool-chain is .net 4.0.
    public IEnumerable<DocumentChange> GetChanges(MetadataChanges metadataChanges) =>
        metadataChanges == MetadataChanges.Include ? _includeChanges : _excludeChanges;

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as QuerySnapshot);

    /// <inheritdoc />
    public bool Equals(QuerySnapshot other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null) return false;
      if (!_query.Equals(other._query) ||
          !_metadata.Equals(other._metadata) ||
          _hadCommittedMutations != other._hadCommittedMutations ||
          _documents.Count != other._documents.Count ||
          _includeChanges.Count != other._includeChanges.Count) {
        return false;
      }
      for (int i = 0; i < _documents.Count; i++) {
        if (!_documents[i].Equals(other._documents[i])) return false;
      }
      for (int i = 0; i < _includeChanges.Count; i++) {
        if (!_includeChanges[i].Equals(other._includeChanges[i])) return false;
      }
      return true;
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      int h = _query.GetHashCode();
      h = h * 31 + _metadata.GetHashCode();
      h = h * 31 + (_hadCommittedMutations ? 1 : 0);
      for (int i = 0; i < _documents.Count; i++) {
        h = h * 31 + _documents[i].GetHashCode();
      }
      for (int i = 0; i < _includeChanges.Count; i++) {
        h = h * 31 + _includeChanges[i].GetHashCode();
      }
      return h;
    }

    /// <inheritdoc />
    public IEnumerator<DocumentSnapshot> GetEnumerator() => _documents.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  }
}
