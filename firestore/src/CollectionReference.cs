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
using System.Threading;
using System.Threading.Tasks;

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
    // _proxy is defined in the base class Query of type Object.

    internal CollectionReference(CollectionReferenceProxy proxy, FirebaseFirestore firestore)
        : base(proxy, firestore) { }

    private CollectionReferenceProxy Proxy {
      get {
        return (CollectionReferenceProxy)_proxy;
      }
    }

    /// <summary>
    /// The final part of the complete collection path; this is the identity of the collection
    /// relative to its parent document.
    /// </summary>
    public string Id => Proxy.id();

    /// <summary>
    /// The complete collection path, including project and database ID.
    /// </summary>
    public string Path => Proxy.path();

    /// <summary>
    /// The parent document, or null if this is a root collection.
    /// </summary>
    public DocumentReference Parent {
      get {
        DocumentReferenceProxy parent = Proxy.Parent();
        if (parent.is_valid()) {
          return new DocumentReference(parent, Firestore);
        } else {
          return null;
        }
      }
    }

    /// <summary>
    /// Creates a <see cref="DocumentReference"/> for a direct child document of this collection
    /// with a random ID. This performs no server-side operations; it only generates the appropriate
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <returns>A <see cref="DocumentReference"/> to a child document of this collection with a
    /// random ID.</returns>
    public DocumentReference Document() => new DocumentReference(Proxy.Document(), Firestore);

    /// <summary>
    /// Creates a <see cref="DocumentReference"/> for a child document of this reference.
    /// </summary>
    /// <param name="path">The path to the document, relative to this collection. Must not be null,
    /// and must contain an odd number of slash-separated path elements.</param>
    /// <returns>A <see cref="DocumentReference"/> for the specified document.</returns>
    public DocumentReference Document(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      return new DocumentReference(Proxy.Document(path), Firestore);
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
    public override int GetHashCode() {
      int hash = Firestore.GetHashCode();
      hash = hash * 31 + base.GetHashCode();
      return hash;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as CollectionReference);

    /// <inheritdoc />
    public bool Equals(CollectionReference other) {
      return other != null && base.Equals(other);
    }

    /// <inheritdoc />
    public override string ToString() => Path;
  }
}
