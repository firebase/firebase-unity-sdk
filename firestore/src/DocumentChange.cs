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

namespace Firebase.Firestore {
  internal static class ChangeTypeConverter {
    public static DocumentChangeProxy.Type ConvertToProxy(this DocumentChange.Type type) {
      switch (type) {
        case DocumentChange.Type.Added:
          return DocumentChangeProxy.Type.Added;
        case DocumentChange.Type.Modified:
          return DocumentChangeProxy.Type.Modified;
        case DocumentChange.Type.Removed:
          return DocumentChangeProxy.Type.Removed;
      }
      throw new ArgumentException("Unsupported DocumentChange.Type:" + type);
    }

    public static DocumentChange.Type ConvertFromProxy(this DocumentChangeProxy.Type type) {
      switch (type) {
        case DocumentChangeProxy.Type.Added:
          return DocumentChange.Type.Added;
        case DocumentChangeProxy.Type.Modified:
          return DocumentChange.Type.Modified;
        case DocumentChangeProxy.Type.Removed:
          return DocumentChange.Type.Removed;
      }
      throw new ArgumentException("Unsupported DocumentChangeProxy.Type:" + type);
    }
  }

  /// <summary>
  /// A DocumentChange represents a change to the documents matching a query. It contains the document
  /// affected and the type of change that occurred (added, modified, or removed).
  /// </summary>
  public sealed class DocumentChange {
    private readonly DocumentChangeProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    internal DocumentChange(DocumentChangeProxy proxy, FirebaseFirestore firestore) {
      _proxy = Util.NotNull(proxy);
      _firestore = Util.NotNull(firestore);
    }

    /// <summary>
    /// An enumeration of <c>DocumentChange</c> types.
    /// </summary>
    public enum Type {
      /// <summary>Indicates a new document was added to the set of documents matching the
      /// query.</summary>
      Added = 0,

      /// <summary>Indicates document within the query was modified.</summary>
      Modified = 1,

      /// <summary>Indicates a document within the query was removed (either deleted or no longer
      /// matches the query).</summary>
      Removed = 2
    }

    /// <summary>Returns the type of the <c>DocumentChange</c>.</summary>
    public Type ChangeType {
      get {
        return _proxy.type().ConvertFromProxy();
      }
    }

    /// <summary>
    /// Returns the newly added or modified document if this DocumentChange is for an updated
    /// document.  Returns the deleted document if this document change represents a removal.
    /// </summary>
    public DocumentSnapshot Document => new DocumentSnapshot(_proxy.document(), _firestore);

    /// <summary>
    /// The index of the changed document in the result set immediately prior to this DocumentChange
    /// (i.e. supposing that all prior DocumentChange objects have been applied). Returns -1 for
    /// 'added' events.
    /// </summary>
    public int OldIndex {
      get {
        return _proxy.old_index() == DocumentChangeProxy.npos ? -1 : (int)_proxy.old_index();
      }
    }

    /// <summary>
    /// The index of the changed document in the result set immediately after this DocumentChange
    /// (i.e.  supposing that all prior DocumentChange objects and the current DocumentChange object
    /// have been applied). Returns -1 for 'removed' events.
    /// </summary>
    public int NewIndex {
      get {
        return _proxy.new_index() == DocumentChangeProxy.npos ? -1 : (int)_proxy.new_index();
      }
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DocumentChange);

    /// <inheritdoc />
    public bool Equals(DocumentChange other) => other != null
                                                && FirestoreCpp.DocumentChangeEquals(_proxy,
                                                                                     other._proxy);

    /// <inheritdoc />
    public override int GetHashCode() {
      return FirestoreCpp.DocumentChangeHashCode(_proxy);
    }
  }
}
