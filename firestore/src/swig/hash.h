#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_HASH_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_HASH_H_

#include <stdint.h>

#include "firestore/src/include/firebase/firestore/aggregate_query.h"
#include "firestore/src/include/firebase/firestore/aggregate_query_snapshot.h"
#include "firestore/src/include/firebase/firestore/document_change.h"
#include "firestore/src/include/firebase/firestore/document_snapshot.h"
#include "firestore/src/include/firebase/firestore/query.h"
#include "firestore/src/include/firebase/firestore/query_snapshot.h"

namespace firebase {
namespace firestore {
namespace csharp {

// Returns the hash code for the given query.
int32_t AggregateQueryHashCode(const AggregateQuery* query);

// Returns the hash code for the given query snapshot.
int32_t AggregateQuerySnapshotHashCode(const AggregateQuerySnapshot* snapshot);

// Returns the hash code for the given query.
int32_t QueryHashCode(const Query* query);

// Returns the hash code for the given query snapshot.
int32_t QuerySnapshotHashCode(const QuerySnapshot* snapshot);

// Returns the hash code for the given document snapshot.
int32_t DocumentSnapshotHashCode(const DocumentSnapshot* snapshot);

// Returns the hash code for the given document change.
int32_t DocumentChangeHashCode(const DocumentChange* change);

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_HASH_H_
