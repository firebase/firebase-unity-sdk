

using Firebase.Firestore.Internal;
using Firebase.Platform;
using System.Threading.Tasks;

namespace Firebase.Firestore {
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

        public Task<AggregateQuerySnapshot> GetSnapshotAsync(AggregateSource source) {
            var sourceProxy = Enums.Convert(source);
            return Util.MapResult(_proxy.GetAsync(sourceProxy),
                taskResult => { return new AggregateQuerySnapshot(taskResult, _firestore); });
        }

        public override bool Equals(object obj) => Equals(obj as Query);
        public bool Equals(AggregateQuery other) => other != null && FirestoreCpp.AggregateQueryEquals(_proxy, other._proxy);

        public override int GetHashCode() {
            return FirestoreCpp.AggregateQueryHashCode(_proxy);
        }
    }
}