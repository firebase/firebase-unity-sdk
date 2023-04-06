#include "firestore/src/swig/hash.h"

namespace firebase {
namespace firestore {

// The following functions are declared as friend functions in the corresponding
// classes in the C++ SDK headers.

size_t AggregateQueryHash(const AggregateQuery& query) { return query.Hash(); }

size_t AggregateQuerySnapshotHash(const AggregateQuerySnapshot& snapshot) {
  return snapshot.Hash();
}
  
size_t QueryHash(const Query& query) { return query.Hash(); }

size_t QuerySnapshotHash(const QuerySnapshot& snapshot) {
  return snapshot.Hash();
}

size_t DocumentSnapshotHash(const DocumentSnapshot& snapshot) {
  return snapshot.Hash();
}

size_t DocumentChangeHash(const DocumentChange& change) {
  return change.Hash();
}

namespace csharp {

int32_t AggregateQueryHashCode(const AggregateQuery* query) {
  if (query == nullptr) {
    return 0;
  }
  return AggregateQueryHash(*query);
}

int32_t AggregateQuerySnapshotHashCode(const AggregateQuerySnapshot* snapshot) {
  if (snapshot == nullptr) {
    return 0;
  }
  return AggregateQuerySnapshotHash(*snapshot);
}

int32_t QueryHashCode(const Query* query) {
  if (query == nullptr) {
    return 0;
  }
  return QueryHash(*query);
}

int32_t QuerySnapshotHashCode(const QuerySnapshot* snapshot) {
  if (snapshot == nullptr) {
    return 0;
  }
  return QuerySnapshotHash(*snapshot);
}

int32_t DocumentSnapshotHashCode(const DocumentSnapshot* snapshot) {
  if (snapshot == nullptr) {
    return 0;
  }
  return DocumentSnapshotHash(*snapshot);
}

int32_t DocumentChangeHashCode(const DocumentChange* change) {
  if (change == nullptr) {
    return 0;
  }
  return DocumentChangeHash(*change);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase
