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

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_QUERY_EVENT_LISTENER_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_QUERY_EVENT_LISTENER_H_

#include "firebase/firestore/firestore_errors.h"
#include "firestore/src/include/firebase/firestore/listener_registration.h"
#include "firestore/src/include/firebase/firestore/query_snapshot.h"

namespace firebase {
namespace firestore {
namespace csharp {

// Add this to make this header compile when SWIG is not involved.
#ifndef SWIGSTDCALL
#if defined(_WIN32) || defined(__WIN32__) || defined(__CYGWIN__)
#define SWIGSTDCALL __stdcall
#else
#define SWIGSTDCALL
#endif
#endif

// The callbacks that are used by the listener, that need to reach back to C#
// callbacks. The error_message pointer is only valid for the duration of the
// callback.
typedef void(SWIGSTDCALL* QueryEventListenerCallback)(
    int32_t callback_id, QuerySnapshot* snapshot, Error error_code,
    const char* error_message);

// This method is a proxy to Query::AddSnapshotsListener()
// that can be easily called from C#. It allows our C# wrapper to
// track user callbacks in a dictionary keyed off of a unique int
// for each user callback and then raise the correct one later.
ListenerRegistration AddQuerySnapshotListener(
    Query* query, MetadataChanges metadata_changes, int32_t callback_id,
    QueryEventListenerCallback callback);

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_QUERY_EVENT_LISTENER_H_
