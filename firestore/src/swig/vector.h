/*
 * Copyright 2021 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_VECTOR_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_VECTOR_H_

#include "firestore/src/include/firebase/firestore/document_change.h"
#include "firestore/src/include/firebase/firestore/document_snapshot.h"
#include "firestore/src/include/firebase/firestore/field_path.h"
#include "firestore/src/include/firebase/firestore/field_value.h"
#include "firestore/src/include/firebase/firestore/metadata_changes.h"
#include "firestore/src/include/firebase/firestore/query_snapshot.h"
#include "firestore/src/include/firebase/firestore/set_options.h"

namespace firebase {
namespace firestore {
namespace csharp {

// Simple wrappers to avoid exposing C++ standard library containers to SWIG.
//
// While it's normally possible to work with standard library containers in SWIG
// (by instantiating them for each template type used via the `%template`
// directive), issues in the build environment make that approach too
// complicated to be worth it. Instead, use simple wrappers and make sure the
// underlying containers are never exposed to C#.
//
// Note: most of the time, these classes should be declared with a `using`
// statement to ensure predictable lifetime of the object when dealing with
// iterators or unsafe views.

// Wraps `std::vector<T>` for use in C# code.
template <typename T>
class Vector {
 public:
  Vector() = default;

  std::size_t Size() const { return container_.size(); }

  // The returned reference is only valid as long as this `Vector` is
  // valid. In C#, declare the vector with a `using` statement to ensure its
  // lifetime exceeds the lifetime of the reference.
  const T& GetUnsafeView(std::size_t i) const { return container_[i]; }

  T GetCopy(std::size_t i) const { return container_[i]; }

  void PushBack(const T& value) { container_.push_back(value); }

  // Note: this is a named function and not a constructor to make it easier to
  // ignore in SWIG.
  static Vector Wrap(const std::vector<T>& container) {
    return Vector(container);
  }

  const std::vector<T>& Unwrap() const { return container_; }

 private:
  explicit Vector(const std::vector<T>& container) : container_(container) {}

  std::vector<T> container_;
};

Vector<FieldValue> ConvertFieldValueToVector(const FieldValue& value) {
  return Vector<FieldValue>::Wrap(value.array_value());
}

inline FieldValue ConvertVectorToFieldValue(const Vector<FieldValue>& wrapper) {
  return FieldValue::Array(wrapper.Unwrap());
}

inline FieldValue FieldValueArrayUnion(const Vector<FieldValue>& wrapper) {
  return FieldValue::ArrayUnion(wrapper.Unwrap());
}

inline FieldValue FieldValueArrayRemove(const Vector<FieldValue>& wrapper) {
  return FieldValue::ArrayRemove(wrapper.Unwrap());
}

inline Vector<DocumentSnapshot> QuerySnapshotDocuments(
    const QuerySnapshot& snapshot) {
  return Vector<DocumentSnapshot>::Wrap(snapshot.documents());
}

inline Vector<DocumentChange> QuerySnapshotDocumentChanges(
    const QuerySnapshot& snapshot, MetadataChanges metadata_changes) {
  return Vector<DocumentChange>::Wrap(
      snapshot.DocumentChanges(metadata_changes));
}

inline SetOptions SetOptionsMergeFieldPaths(const Vector<FieldPath>& fields) {
  return SetOptions::MergeFieldPaths(fields.Unwrap());
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_VECTOR_H_
