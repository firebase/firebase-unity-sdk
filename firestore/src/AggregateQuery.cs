

using Firebase.Firestore.Internal;
using System.Threading.Tasks;

namespace Firebase.Firestore {
    /// <summary>
    /// A query that calculates aggregations over an underlying query.
    /// </summary>
    public sealed class AggregateQuery
    {
        private readonly AggregateQueryProxy _proxy;
        private readonly FirebaseFirestore _firestore;

        internal AggregateQuery(AggregateQueryProxy proxy, FirebaseFirestore firestore) {
            _proxy = Util.NotNull(proxy);
            _firestore = Util.NotNull(firestore);
        }

        /// <summary>
        /// The query of aggregations that will be calculated.
        /// </summary>
        public Query Query => new Query(_proxy.query(), _firestore);

        /// <summary>
        /// Asynchronously executes the query.
        /// </summary>
        /// <param name="source">The source from which to acquire the aggregate results.</param>
        /// <returns>The results of the query.</returns>
        public Task<AggregateQuerySnapshot> GetSnapshotAsync(AggregateSource source) {
            var sourceProxy = Enums.Convert(source);
            return Util.MapResult(_proxy.GetAsync(sourceProxy),
                taskResult => { return new AggregateQuerySnapshot(taskResult, _firestore); });
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as Query);
    
        /// <summary>
        /// Compares this aggregate query with another for equality.
        /// </summary>
        /// <param name="other">The aggregate query to compare this one with.</param>
        /// <returns><c>true</c> if this aggregate query is equal to <paramref name="other"/>;
        /// <c>false</c> otherwise.</returns>
        public bool Equals(AggregateQuery other) => other != null && FirestoreCpp.AggregateQueryEquals(_proxy, other._proxy);

        /// <inheritdoc />
        public override int GetHashCode() {
            return FirestoreCpp.AggregateQueryHashCode(_proxy);
        }
    }
}