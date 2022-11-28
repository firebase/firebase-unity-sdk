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

#ifndef FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_INCLUDE_FIREBASE_CRASHLYTICS_H_
#define FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_INCLUDE_FIREBASE_CRASHLYTICS_H_

#include <string>
#include <vector>

#include "app/src/include/firebase/app.h"

namespace firebase {

/// Namespace for the Firebase C++ SDK for Crashlytics.
namespace crashlytics {

/// @cond FIREBASE_APP_INTERNAL
namespace internal {
class CrashlyticsInternal;
}  // namespace internal
/// @endcond

class CrashlyticsReference;

typedef struct Frame {
  const char* library;
  const char* symbol;
  const char* fileName;
  const char* lineNumber;
} Frame;

#ifndef SWIG
/// @brief Entry point for the Firebase C++ SDK for Crashlytics.
///
/// To use the SDK, call firebase::crashlytics::Crashlytics::GetInstance() to
/// obtain an instance of Crashlytics.
#endif  // SWIG
class Crashlytics {
 public:
  /// @brief Destructor. You may delete an instance of Crashlytics when
  /// you are finished using it, to shut down the Crashlytics library.
  ~Crashlytics();

  /// @brief Get an instance of Crashlytics corresponding to the given App.
  ///
  /// @param[in] app An instance of firebase::App.
  /// @param[out] init_result_out Optional: If provided, write the init result
  /// here. Will be set to kInitResultSuccess if initialization succeeded, or
  /// kInitResultFailedMissingDependency on Android if Google Play services is
  /// not available on the current device.
  ///
  /// @return An instance of Crashlytics corresponding to the given App.
  static Crashlytics* GetInstance(::firebase::App* app,
                                  InitResult* init_result_out = nullptr);

  void Log(const char* message);
  void SetCustomKey(const char* key, const char* value);
  void SetUserId(const char* id);
  void LogException(const char* name, const char* reason,
                    std::vector<Frame> frames);
  void LogExceptionAsFatal(const char* name, const char* reason,
                           std::vector<Frame> frames);
  bool IsCrashlyticsCollectionEnabled();
  void SetCrashlyticsCollectionEnabled(bool enabled);

 private:
  /// @cond FIREBASE_APP_INTERNAL
  Crashlytics(::firebase::App* app);
  Crashlytics(const Crashlytics& src);
  Crashlytics& operator=(const Crashlytics& src);

  // Delete the internal_ data.
  void DeleteInternal();

  internal::CrashlyticsInternal* internal_;
  /// @endcond
};

}  // namespace crashlytics
}  // namespace firebase

#endif  // FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_INCLUDE_FIREBASE_CRASHLYTICS_H_
