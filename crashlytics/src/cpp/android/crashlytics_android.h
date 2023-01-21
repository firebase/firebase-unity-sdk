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

#ifndef FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_ANDROID_CRASHLYTICS_ANDROID_H_
#define FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_ANDROID_CRASHLYTICS_ANDROID_H_

#include <jni.h>

#include <map>
#include <set>

#include "crashlytics/src/cpp/include/firebase/crashlytics.h"
#include "app/src/future_manager.h"
#include "app/src/include/firebase/app.h"
#include "app/src/include/firebase/internal/common.h"
#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/util_android.h"

namespace firebase {
namespace crashlytics {
namespace internal {

class CrashlyticsInternal {
 public:
  // Build a Crashlytics.
  CrashlyticsInternal(App* app);
  ~CrashlyticsInternal();

  // Whether this object was successfully initialized by the constructor.
  bool initialized() const { return java_vm_ != nullptr; }

  void Log(const char* message);
  void SetCustomKey(const char* key, const char* value);
  void SetUserId(const char* id);
  void LogException(const char* name, const char* reason,
                    std::vector<firebase::crashlytics::Frame> frames);
  void LogExceptionAsFatal(const char* name, const char* reason,
                           std::vector<firebase::crashlytics::Frame> frames);
  bool IsCrashlyticsCollectionEnabled();
  void SetCrashlyticsCollectionEnabled(bool enabled);

 private:
  // Initialize JNI for all classes.
  static bool Initialize(JNIEnv* env, jobject activity);
  static void Terminate();

  static bool GetCrashlyticsCollectionEnabled(JavaVM* java_vm,
                                              jobject data_collection_obj);
  jobject BuildJavaException(std::string message,
                             std::vector<firebase::crashlytics::Frame>& frames);
  jobject BuildJavaStackTrace(std::vector<Frame>& frames);

  void InstallNdkSignalHandler();

  static Mutex init_mutex_;
  static Mutex data_collection_mutex_;
  static int initialize_count_;

  static JavaVM* java_vm_;

  // Java FirebaseCrashlytics global ref.
  jobject obj_;

  // Java DataCollectionArbiter global ref.
  jobject data_collection_obj_;

  // Java CrashlyticsCore global ref.
  jobject core_;
};

}  // namespace internal
}  // namespace crashlytics
}  // namespace firebase

#endif  // FIREBASE_CRASHLYTICS_CLIENT_CPP_SRC_ANDROID_CRASHLYTICS_ANDROID_H_
