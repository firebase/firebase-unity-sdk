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

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_MAP_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_MAP_H_

#include <string>
#include <vector>
#include <utility>

#include "firestore/src/common/futures.h"
#include "firestore/src/include/firebase/firestore/document_reference.h"
#include "firestore/src/include/firebase/firestore/document_snapshot.h"
#include "firestore/src/include/firebase/firestore/field_path.h"
#include "firestore/src/include/firebase/firestore/field_value.h"
#include "firestore/src/include/firebase/firestore/filter.h"
#include "firestore/src/include/firebase/firestore/query.h"
#include "firestore/src/include/firebase/firestore/set_options.h"
#include "firestore/src/include/firebase/firestore/write_batch.h"

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

// Wraps `std::unordered_map<K, V>` for use in C# code.
// Note: `V` has to be default-constructible with a sensible default value.
template <typename K, typename V>
class Map {
#ifdef STLPORT
#define FIRESTORE_STD_NS std::tr1
#else
#define FIRESTORE_STD_NS std
#endif
  using ContainerT = FIRESTORE_STD_NS::unordered_map<K, V>;
  using IterT = typename ContainerT::const_iterator;
#undef FIRESTORE_STD_NS

 public:
  Map() = default;

  std::size_t Size() const { return container_.size(); }

  // The returned reference is only valid as long as this `Map` is
  // valid. In C#, declare the map with a `using` statement to ensure its
  // lifetime exceeds the lifetime of the reference.
  const V& GetUnsafeView(const K& key) const {
    auto found = container_.find(key);
    if (found != container_.end()) {
      return found->second;
    }

    return InvalidValue();
  }

  V GetCopy(const K& key) const {
    // Note: this is safe because the reference returned by `GetUnsafeView` is
    // copied into the return value.
    return GetUnsafeView(key);
  }

  void Insert(const K& key, const V& value) { container_[key] = value; }

  class MapIterator {
   public:
    bool HasMore() const { return cur_ != end_; }

    void Advance() { ++cur_; }

    const K& UnsafeKeyView() const { return cur_->first; }
    const V& UnsafeValueView() const { return cur_->second; }

    K KeyCopy() const { return cur_->first; }
    V ValueCopy() const { return cur_->second; }

   private:
    friend class Map;
    explicit MapIterator(const Map& wrapper)
        : cur_{wrapper.Unwrap().begin()}, end_{wrapper.Unwrap().end()} {}

    Map::IterT cur_, end_;
  };

  MapIterator Iterator() const { return MapIterator(*this); }

  // Note: this is a named function and not a constructor to make it easier to
  // ignore in SWIG.
  static Map Wrap(const std::unordered_map<K, V>& container) {
    return Map(container);
  }

  const std::unordered_map<K, V>& Unwrap() const { return container_; }

 private:
  explicit Map(const std::unordered_map<K, V>& container)
      : container_(container) {}

  static const V& InvalidValue() {
    static V value;
    return value;
  }

  ContainerT container_;
};

inline Map<std::string, FieldValue> ConvertFieldValueToMap(
    const FieldValue& field_value) {
  return Map<std::string, FieldValue>::Wrap(field_value.map_value());
}

inline FieldValue ConvertMapToFieldValue(
    const Map<std::string, FieldValue>& wrapper) {
  return FieldValue::Map(wrapper.Unwrap());
}

inline FieldValue ConvertSnapshotToFieldValue(
    const DocumentSnapshot& snapshot,
    DocumentSnapshot::ServerTimestampBehavior stb) {
  return FieldValue::Map(snapshot.GetData(stb));
}

inline void WriteBatchUpdate(WriteBatch* batch,
                                  const DocumentReference& doc,
                                  const FieldValue& field_value) {
  batch->Update(doc, field_value.map_value());
}

inline void WriteBatchUpdate(
    WriteBatch* batch, const DocumentReference& doc,
    const Map<std::string, FieldValue>& wrapper) {
  batch->Update(doc, wrapper.Unwrap());
}

inline void WriteBatchUpdate(
    WriteBatch* batch, const DocumentReference& doc,
    const Map<FieldPath, FieldValue>& wrapper) {
  batch->Update(doc, wrapper.Unwrap());
}

inline Future<void> DocumentReferenceSet(DocumentReference& doc,
                                              const FieldValue& field_value,
                                              const SetOptions& options) {
  return doc.Set(field_value.map_value(), options);
}

inline Future<void> DocumentReferenceUpdate(
    DocumentReference& doc, const FieldValue& field_value) {
  return doc.Update(field_value.map_value());
}

inline Future<void> DocumentReferenceUpdate(
    DocumentReference& doc, const Map<FieldPath, FieldValue>& wrapper) {
  return doc.Update(wrapper.Unwrap());
}

inline Filter FilterArrayContainsAny(const std::string& field, const FieldValue& values) {
  return Filter::ArrayContainsAny(field, values.array_value());
}

inline Filter FilterArrayContainsAny(const FieldPath& field, const FieldValue& values) {
  return Filter::ArrayContainsAny(field, values.array_value());
}

inline Filter FilterIn(const std::string& field, const FieldValue& values) {
  return Filter::In(field, values.array_value());
}

inline Filter FilterIn(const FieldPath& field, const FieldValue& values) {
  return Filter::In(field, values.array_value());
}

inline Filter FilterNotIn(const std::string& field, const FieldValue& values) {
  return Filter::NotIn(field, values.array_value());
}

inline Filter FilterNotIn(const FieldPath& field, const FieldValue& values) {
  return Filter::NotIn(field, values.array_value());
}

inline Query QueryWhereArrayContainsAny(Query& query,
                                             const std::string& field,
                                             const FieldValue& values) {
  // TODO(b/158342776): prevent an avoidable copy. SWIG would allocate a new
  // `Query` on the heap and initialize it with a copy of this return value.
  return query.WhereArrayContainsAny(field, values.array_value());
}

inline Query QueryWhereArrayContainsAny(Query& query,
                                             const FieldPath& field,
                                             const FieldValue& values) {
  return query.WhereArrayContainsAny(field, values.array_value());
}

inline Query QueryWhereIn(Query& query, const std::string& field,
                               const FieldValue& values) {
  return query.WhereIn(field, values.array_value());
}

inline Query QueryWhereIn(Query& query, const FieldPath& field,
                               const FieldValue& values) {
  return query.WhereIn(field, values.array_value());
}

inline Query QueryWhereNotIn(Query& query, const std::string& field,
                             const FieldValue& values) {
  return query.WhereNotIn(field, values.array_value());
}

inline Query QueryWhereNotIn(Query& query, const FieldPath& field,
                             const FieldValue& values) {
  return query.WhereNotIn(field, values.array_value());
}

inline Query QueryStartAt(Query& query, const FieldValue& values) {
  return query.StartAt(values.array_value());
}

inline Query QueryStartAfter(Query& query, const FieldValue& values) {
  return query.StartAfter(values.array_value());
}

inline Query QueryEndBefore(Query& query, const FieldValue& values) {
  return query.EndBefore(values.array_value());
}

inline Query QueryEndAt(Query& query, const FieldValue& values) {
  return query.EndAt(values.array_value());
}

inline void WriteBatchSet(WriteBatch& write_batch,
                               const DocumentReference& document,
                               const FieldValue& data,
                               const SetOptions& options) {
  write_batch.Set(document, data.map_value(), options);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_MAP_H_
