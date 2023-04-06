#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_EQUALITY_COMPARE_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_EQUALITY_COMPARE_H_

#include "firestore/src/include/firebase/firestore/aggregate_query.h"
#include "firestore/src/include/firebase/firestore/aggregate_query_snapshot.h"
#include "firestore/src/include/firebase/firestore/document_change.h"
#include "firestore/src/include/firebase/firestore/document_snapshot.h"
#include "firestore/src/include/firebase/firestore/query.h"
#include "firestore/src/include/firebase/firestore/query_snapshot.h"

namespace firebase {
namespace firestore {
namespace csharp {

// Compares two `AggregateQuery` objects for equality using their `==` operator, handling
// one or both of them being `nullptr`.
bool AggregateQueryEquals(const AggregateQuery* lhs, const AggregateQuery* rhs);

// Compares two `AggregateQuerySnapshot` objects for equality using their `==` operator,
// handling one or both of them being `nullptr`.
bool AggregateQuerySnapshotEquals(const AggregateQuerySnapshot* lhs, const AggregateQuerySnapshot* rhs);

    // Compares two `Query` objects for equality using their `==` operator, handling
// one or both of them being `nullptr`.
bool QueryEquals(const Query* lhs, const Query* rhs);

// Compares two `QuerySnapshot` objects for equality using their `==` operator,
// handling one or both of them being `nullptr`.
bool QuerySnapshotEquals(const QuerySnapshot* lhs, const QuerySnapshot* rhs);

// Compares two `DocumentSnapshot` objects for equality using their `==`
// operator, handling one or both of them being `nullptr`.
bool DocumentSnapshotEquals(const DocumentSnapshot* lhs,
                            const DocumentSnapshot* rhs);

// Compares two `DocumentChange` objects for equality using their `==` operator,
// handling one or both of them being `nullptr`.
bool DocumentChangeEquals(const DocumentChange* lhs, const DocumentChange* rhs);

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_EQUALITY_COMPARE_H_
