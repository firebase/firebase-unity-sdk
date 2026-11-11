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
    private readonly AggregateQuery _query;
    private readonly long _count;

    internal AggregateQuerySnapshot(AggregateQuery query, long count) {
      _query = Preconditions.CheckNotNull(query, nameof(query));
      _count = count;
    }

    /// <summary>
    /// Returns the query that was executed to produce this result.
    /// </summary>
    public AggregateQuery Query => _query;

    /// <summary>
    /// Returns the number of documents in the result set of the underlying query.
    /// </summary>
    public long Count => _count;

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as AggregateQuerySnapshot);

    /// <summary>
    /// Compares this aggregate snapshot with another for equality.
    /// </summary>
    /// <param name="other">The aggregate snapshot to compare this one with.</param>
    /// <returns><c>true</c> if this aggregate snapshot is equal to <paramref name="other"/>;
    /// <c>false</c> otherwise.</returns>
    public bool Equals(AggregateQuerySnapshot other) =>
        other != null && _count == other._count && _query.Equals(other._query);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _query.GetHashCode() * 31 + Hash.LongHash(_count);
  }
}
