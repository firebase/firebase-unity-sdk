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
  /// The results of executing an <c>AggregateQuerySnapshot</c>.
  /// </summary>
  public sealed class AggregateQuerySnapshot {
    private readonly AggregateQuerySnapshotProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    internal AggregateQuerySnapshot(AggregateQuerySnapshotProxy proxy, FirebaseFirestore firestore) {
      _proxy = Util.NotNull(proxy);
      _firestore = Util.NotNull(firestore);
    }
    /// <summary>
    /// Returns the query that was executed to produce this result.
    /// </summary>
    public AggregateQuery Query {
      get {
        return new AggregateQuery(_proxy.query(), _firestore);
      }
    }

    /// <summary>
    /// Returns the number of documents in the result set of the underlying query.
    /// </summary>
    public long Count {
      get {
        return _proxy.count();
      }
    }
    
    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as AggregateQuerySnapshot);

    /// <summary>
    /// Compares this aggregate snapshot with another for equality.
    /// </summary>
    /// <param name="other">The aggregate snapshot to compare this one with.</param>
    /// <returns><c>true</c> if this aggregate snapshot is equal to <paramref name="other"/>;
    /// <c>false</c> otherwise.</returns>
    public bool Equals(AggregateQuerySnapshot other) =>
      other != null && FirestoreCpp.AggregateQuerySnapshotEquals(_proxy, other._proxy);
    
    /// <inheritdoc />
    public override int GetHashCode() {
      return FirestoreCpp.AggregateQuerySnapshotHashCode(_proxy);
    }
  }
}
