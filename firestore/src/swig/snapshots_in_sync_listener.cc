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

#include "firestore/src/swig/snapshots_in_sync_listener.h"

#include "app/src/callback.h"

namespace firebase {
namespace firestore {
namespace csharp {

namespace {

class ListenerCallback {
 public:
  ListenerCallback(SnapshotsInSyncCallback callback, int32_t callback_id)
      : callback_(callback), callback_id_(callback_id) {}

  static void Run(ListenerCallback* listener_callback) {
    listener_callback->Run();
  }

 private:
  void Run() { callback_(callback_id_); }

  SnapshotsInSyncCallback callback_ = nullptr;
  int32_t callback_id_ = -1;
};

}  // namespace

ListenerRegistration AddSnapshotsInSyncListener(
    Firestore* firestore, int32_t callback_id,
    SnapshotsInSyncCallback callback) {
  auto snapshots_in_sync_listener = [callback, callback_id] {
    ListenerCallback listener_callback(callback, callback_id);
    auto* callback = new callback::CallbackMoveValue1<ListenerCallback>(
        std::move(listener_callback), ListenerCallback::Run);
    callback::AddCallback(callback);
  };
  return firestore->AddSnapshotsInSyncListener(snapshots_in_sync_listener);
}

}  // namespace csharp
}  // namespace firestore
}  // namespace firebase
