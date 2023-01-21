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

#include "crashlytics/src/cpp/stub/crashlytics_stub.h"

#include "app/src/include/firebase/app.h"

namespace firebase {
namespace crashlytics {
namespace internal {

CrashlyticsInternal::CrashlyticsInternal(App* app) {
  app_ = nullptr;
  app_ = app;
}

CrashlyticsInternal::~CrashlyticsInternal() {
  // If initialization failed, there is nothing to clean up.
  if (app_ == nullptr) return;
  app_ = nullptr;
}

void CrashlyticsInternal::Log(const char* message) {}

void CrashlyticsInternal::SetCustomKey(const char* key, const char* value) {}

void CrashlyticsInternal::SetUserId(const char* id) {}

void CrashlyticsInternal::LogException(
    const char* name, const char* reason,
    std::vector<firebase::crashlytics::Frame> frames) {}

void CrashlyticsInternal::LogExceptionAsFatal(
    const char* name, const char* reason,
    std::vector<firebase::crashlytics::Frame> frames) {}

bool CrashlyticsInternal::IsCrashlyticsCollectionEnabled() { return false; }

void CrashlyticsInternal::SetCrashlyticsCollectionEnabled(bool enabled) {}

}  // namespace internal
}  // namespace crashlytics
}  // namespace firebase
