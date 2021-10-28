#include "firestore/src/swig/equality_compare.h"

namespace {

template <typename T>
bool EqualityCompareHelper(const T* lhs, const T* rhs) {
  return lhs == rhs || (lhs != nullptr && rhs != nullptr && *lhs == *rhs);
}

}  // namespace

namespace firebase {
namespace firestore {
namespace csharp {

bool QueryEquals(const Query* lhs, const Query* rhs) {
  return EqualityCompareHelper(lhs, rhs);
}

bool QuerySnapshotEquals(const QuerySnapshot* lhs, const QuerySnapshot* rhs) {
  return EqualityCompareHelper(lhs, rhs);
}

bool DocumentSnapshotEquals(const DocumentSnapshot* lhs,
                            const DocumentSnapshot* rhs) {
  return EqualityCompareHelper(lhs, rhs);
}

bool DocumentChangeEquals(const DocumentChange* lhs,
                          const DocumentChange* rhs) {
  return EqualityCompareHelper(lhs, rhs);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase
