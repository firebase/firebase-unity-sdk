using System.Threading.Tasks;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A query that calculates aggregations over an underlying query.
  /// </summary>
  public sealed class AggregateQuery {
    private readonly Query _query;

    internal AggregateQuery(Query query) {
      _query = Preconditions.CheckNotNull(query, nameof(query));
    }

    /// <summary>
    /// The query of aggregations that will be calculated.
    /// </summary>
    public Query Query => _query;

    /// <summary>
    /// Asynchronously executes the query.
    /// </summary>
    /// <param name="source">The source from which to acquire the aggregate results.</param>
    /// <returns>The results of the query.</returns>
    public Task<AggregateQuerySnapshot> GetSnapshotAsync(AggregateSource source) {
      return _query.Firestore.Core.GetAggregateCountSnapshotAsync(this);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as AggregateQuery);

    /// <summary>
    /// Compares this aggregate query with another for equality.
    /// </summary>
    /// <param name="other">The aggregate query to compare this one with.</param>
    /// <returns><c>true</c> if this aggregate query is equal to <paramref name="other"/>;
    /// <c>false</c> otherwise.</returns>
    public bool Equals(AggregateQuery other) =>
        other != null && _query.Equals(other._query);

    /// <inheritdoc />
    public override int GetHashCode() => _query.GetHashCode() * 31 + 17;
  }
}
