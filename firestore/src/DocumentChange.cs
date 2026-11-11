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

namespace Firebase.Firestore {
  /// <summary>
  /// A DocumentChange represents a change to the documents matching a query. It contains the document
  /// affected and the type of change that occurred (added, modified, or removed).
  /// </summary>
  public sealed class DocumentChange {
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
      Removed = 2,
    }

    /// <summary>Returns the type of the <c>DocumentChange</c>.</summary>
    public Type ChangeType { get; }

    /// <summary>
    /// Returns the newly added or modified document if this DocumentChange is for an updated
    /// document.  Returns the deleted document if this document change represents a removal.
    /// </summary>
    public DocumentSnapshot Document { get; }

    /// <summary>
    /// The index of the changed document in the result set immediately prior to this DocumentChange
    /// (i.e. supposing that all prior DocumentChange objects have been applied). Returns -1 for
    /// 'added' events.
    /// </summary>
    public int OldIndex { get; }

    /// <summary>
    /// The index of the changed document in the result set immediately after this DocumentChange
    /// (i.e.  supposing that all prior DocumentChange objects and the current DocumentChange object
    /// have been applied). Returns -1 for 'removed' events.
    /// </summary>
    public int NewIndex { get; }

    internal DocumentChange(DocumentSnapshot document, Type changeType, int oldIndex, int newIndex) {
      Document = Preconditions.CheckNotNull(document, nameof(document));
      ChangeType = changeType;
      OldIndex = oldIndex;
      NewIndex = newIndex;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DocumentChange);

    /// <inheritdoc />
    public bool Equals(DocumentChange other) =>
        other != null &&
        ChangeType == other.ChangeType &&
        OldIndex == other.OldIndex &&
        NewIndex == other.NewIndex &&
        Document.Equals(other.Document);

    /// <inheritdoc />
    public override int GetHashCode() {
      int h = (int)ChangeType;
      h = h * 31 + Document.GetHashCode();
      h = h * 31 + OldIndex;
      return h * 31 + NewIndex;
    }
  }
}
