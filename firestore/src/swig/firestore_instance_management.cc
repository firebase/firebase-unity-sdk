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

#include "firestore/src/swig/firestore_instance_management.h"

#include "app/src/cpp_instance_manager.h"
#include "firestore/src/include/firebase/firestore.h"

namespace firebase {
namespace firestore {
namespace csharp {

namespace {

CppInstanceManager<Firestore>& GetFirestoreInstanceManager() {
  // Allocate the CppInstanceManager on the heap to prevent its destructor
  // from executing (go/totw/110#the-fix-safe-initialization-no-destruction).
  static CppInstanceManager<Firestore>* firestore_instance_manager =
      new CppInstanceManager<Firestore>();
  return *firestore_instance_manager;
}

}  // namespace

Firestore* GetFirestoreInstance(App* app) {
  auto& firestore_instance_manager = GetFirestoreInstanceManager();
  // Acquire the lock used internally by CppInstanceManager::ReleaseReference()
  // to avoid racing with deletion of Firestore instances.
  MutexLock lock(firestore_instance_manager.mutex());
  Firestore* instance =
      Firestore::GetInstance(app, /*init_result_out=*/nullptr);
  firestore_instance_manager.AddReference(instance);
  return instance;
}

void ReleaseFirestoreInstance(Firestore* firestore) {
  GetFirestoreInstanceManager().ReleaseReference(firestore);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase
