// Copyright 2019 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#include "crashlytics/src/cpp/include/firebase/crashlytics.h"

#include <assert.h>

#include <string>

#include "app/src/assert.h"
#include "app/src/cleanup_notifier.h"
#include "app/src/include/firebase/app.h"
#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/include/firebase/version.h"
#include "app/src/log.h"
#include "app/src/util.h"

#if defined(__ANDROID__)
#include "crashlytics/src/cpp/android/crashlytics_android.h"
#include "app/src/util_android.h"
#else
#include "crashlytics/src/cpp/stub/crashlytics_stub.h"
#endif  // defined(__ANDROID__)

// Register the module initializer.
FIREBASE_APP_REGISTER_CALLBACKS(crashlytics,
                                { return ::firebase::kInitResultSuccess; },
                                {
                                    // Nothing to tear down.
                                },
                                false);

namespace firebase {
namespace crashlytics {

DEFINE_FIREBASE_VERSION_STRING(FirebaseCrashlytics);

Mutex g_crashlytics_lock;  // NOLINT

// TODO(b/130037817) Refactor Crashlytics as a Module instead of a Class
Crashlytics* Crashlytics::GetInstance(::firebase::App* app,
                                      InitResult* init_result_out) {
  Crashlytics* crashlytics = new Crashlytics(app);
  if (!crashlytics->internal_->initialized()) {
    if (init_result_out) *init_result_out = kInitResultFailedMissingDependency;
    delete crashlytics;
    return nullptr;
  }
  if (init_result_out) *init_result_out = kInitResultSuccess;
  return crashlytics;
}

Crashlytics::Crashlytics(::firebase::App* app) {
  internal_ = new internal::CrashlyticsInternal(app);
}

Crashlytics::~Crashlytics() {
  DeleteInternal();
}

void Crashlytics::DeleteInternal() {
  MutexLock lock(g_crashlytics_lock);

  if (!internal_) return;

  // If a Crashlytics is explicitly deleted, remove it from our cache.
  delete internal_;
  internal_ = nullptr;
}

void Crashlytics::Log(const char* message) { internal_->Log(message); }

void Crashlytics::SetCustomKey(const char* key, const char* value) {
  internal_->SetCustomKey(key, value);
}

void Crashlytics::SetUserId(const char* id) { internal_->SetUserId(id); }

void Crashlytics::LogException(const char* name, const char* reason,
                               std::vector<Frame> frames) {
  internal_->LogException(name, reason, frames);
}

void Crashlytics::LogExceptionAsFatal(const char* name, const char* reason,
                                      std::vector<Frame> frames) {
  internal_->LogExceptionAsFatal(name, reason, frames);
}

bool Crashlytics::IsCrashlyticsCollectionEnabled() {
  return internal_->IsCrashlyticsCollectionEnabled();
}

void Crashlytics::SetCrashlyticsCollectionEnabled(bool enabled) {
  internal_->SetCrashlyticsCollectionEnabled(enabled);
}

}  // namespace crashlytics
}  // namespace firebase
