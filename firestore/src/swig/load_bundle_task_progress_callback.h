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

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_LOAD_BUNDLE_TASK_PROGRESS_CALLBACK_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_LOAD_BUNDLE_TASK_PROGRESS_CALLBACK_H_

#include <stdint.h>

#include <string>

#include "firestore/src/include/firebase/firestore.h"
#include "firestore/src/include/firebase/firestore/load_bundle_task_progress.h"

namespace firebase {
namespace firestore {
namespace csharp {

// Add this to make this header compile when SWIG is not involved.
#ifndef SWIGSTDCALL
#if !defined(SWIG) && \
    (defined(_WIN32) || defined(__WIN32__) || defined(__CYGWIN__))
#define SWIGSTDCALL __stdcall
#else
#define SWIGSTDCALL
#endif
#endif

// The callbacks that are used by the listener, that need to reach back to C#
// callbacks.
typedef void(SWIGSTDCALL* LoadBundleTaskProgressCallback)(
    int32_t callback_id, LoadBundleTaskProgress* progress);

// This method is a proxy to Firestore::LoadBundle()
// that can be easily called from C#. It allows our C# wrapper to
// track user callbacks in a dictionary keyed off of a unique int
// for each user callback and then raise the correct one later.
void LoadBundleWithCallback(Firestore* firestore,
                            const std::string& bundle_data, int32_t callback_id,
                            LoadBundleTaskProgressCallback callback);

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_LOAD_BUNDLE_TASK_PROGRESS_CALLBACK_H_
