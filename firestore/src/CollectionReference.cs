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
using System.Threading.Tasks;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A reference to a collection in a Firestore database. The existence of this object does not
  /// imply that the collection currently exists in storage.
  /// </summary>
  /// <remarks>
  /// A <c>CollectionReference</c> can be used for adding documents, getting document references,
  /// and querying for documents (using the methods inherited from <c>Query</c>).
  /// </remarks>
  public sealed class CollectionReference : Query, IEquatable<CollectionReference> {
    private readonly ResourcePath _collectionPath;

    internal CollectionReference(ResourcePath collectionPath, FirebaseFirestore firestore)
        : base(new QuerySpec(collectionPath, collectionGroup: null), firestore) {
      _collectionPath = Preconditions.CheckNotNull(collectionPath, nameof(collectionPath));
    }

    internal ResourcePath CollectionPath => _collectionPath;

    /// <summary>
    /// The final part of the complete collection path; this is the identity of the collection
    /// relative to its parent document.
    /// </summary>
    public string Id => Firestore.IsDisposed ? string.Empty : _collectionPath.Id;

    /// <summary>
    /// The complete collection path relative to the database root.
    /// </summary>
    public string Path => Firestore.IsDisposed ? string.Empty : _collectionPath.CanonicalString;

    /// <summary>
    /// The parent document, or null if this is a root collection.
    /// </summary>
    public DocumentReference Parent {
      get {
        ResourcePath parent = _collectionPath.Parent;
        return parent == null || parent.IsEmpty
            ? null
            : new DocumentReference(parent, Firestore);
      }
    }

    /// <summary>
    /// Creates a <see cref="DocumentReference"/> for a direct child document of this collection
    /// with a random ID. This performs no server-side operations; it only generates the appropriate
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <returns>A <see cref="DocumentReference"/> to a child document of this collection with a
    /// random ID.</returns>
    public DocumentReference Document() =>
        new DocumentReference(_collectionPath.Child(ResourcePath.AutoId()), Firestore);

    /// <summary>
    /// Creates a <see cref="DocumentReference"/> for a child document of this reference.
    /// </summary>
    /// <param name="path">The path to the document, relative to this collection. Must not be null,
    /// and must contain an odd number of slash-separated path elements.</param>
    /// <returns>A <see cref="DocumentReference"/> for the specified document.</returns>
    public DocumentReference Document(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      ResourcePath rel = ResourcePath.ParseRelative(path, nameof(path));
      if (rel.Count % 2 == 0) {
        throw new ArgumentException(
            $"Invalid document reference. Relative document paths must have an odd number of segments, but {path} has {rel.Count}.",
            nameof(path));
      }
      return new DocumentReference(_collectionPath.Append(rel), Firestore);
    }

    /// <summary>
    /// Asynchronously creates a document with the given data in this collection. The document has a
    /// randomly generated ID.
    /// </summary>
    /// <param name="documentData">The data for the document. Must not be null.</param>
    /// <returns>The reference for the newly-created document.</returns>
    public Task<DocumentReference> AddAsync(object documentData) {
      Preconditions.CheckNotNull(documentData, nameof(documentData));
      DocumentReference docRef = Document();
      Task setAsyncTask = docRef.SetAsync(documentData);
      return Util.MapResult(setAsyncTask, docRef);
    }

    /// <inheritdoc />
    public override int GetHashCode() =>
        Firestore.GetHashCode() * 31 + _collectionPath.GetHashCode();

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as CollectionReference);

    /// <inheritdoc />
    public bool Equals(CollectionReference other) =>
        other != null &&
        ReferenceEquals(Firestore, other.Firestore) &&
        _collectionPath.Equals(other._collectionPath);

    /// <inheritdoc />
    public override string ToString() => Path;
  }
}
