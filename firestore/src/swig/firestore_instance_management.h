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

#ifndef FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_FIRESTORE_INSTANCE_MANAGEMENT_H_
#define FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_FIRESTORE_INSTANCE_MANAGEMENT_H_

namespace firebase {

class App;

namespace firestore {

class Firestore;

namespace csharp {

/**
 * Returns the Firestore instance for the given App, creating it if necessary.
 * This method is merely a wrapper around Firestore::GetInstance() that
 * increments a reference count each time a given Firestore pointer is returned.
 * The caller must call ReleaseFirestoreInstance() with the returned pointer
 * once it is no longer referenced to ensure proper garbage collection.
 */
Firestore* GetFirestoreInstance(App* app);

/**
 * Decrements the reference count of the given Firestore, deleting it if the
 * reference count becomes zero. The given Firestore pointer must have been
 * returned by a previous invocation of GetFirestoreInstance().
 */
void ReleaseFirestoreInstance(Firestore* firestore);

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase

#endif  // FIREBASE_FIRESTORE_CLIENT_UNITY_SRC_SWIG_FIRESTORE_INSTANCE_MANAGEMENT_H_
