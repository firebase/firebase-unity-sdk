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

#include "crashlytics/src/cpp/android/crashlytics_android.h"

#include <assert.h>
#include <jni.h>

#include "app/src/include/firebase/app.h"
#include "app/src/log.h"
#include "app/src/util_android.h"

namespace firebase {
namespace crashlytics {
namespace internal {

// clang-format off
#define CRASHLYTICS_METHODS(X)                                         \
  X(GetInstance, "getInstance",                                        \
    "()Lcom/google/firebase/crashlytics/FirebaseCrashlytics;",         \
    util::kMethodTypeStatic),                                          \
  X(Log, "log",                                                        \
    "(Ljava/lang/String;)V",                                           \
    util::kMethodTypeInstance),                                        \
  X(SetCustomKey, "setCustomKey",                                      \
    "(Ljava/lang/String;Ljava/lang/String;)V",                         \
    util::kMethodTypeInstance),                                        \
  X(SetUserId, "setUserId",                                            \
    "(Ljava/lang/String;)V",                                           \
    util::kMethodTypeInstance),                                        \
  X(RecordException, "recordException",                                \
    "(Ljava/lang/Throwable;)V",                                        \
    util::kMethodTypeInstance),                                        \
  X(SetCrashlyticsDataCollectionEnabled,                               \
    "setCrashlyticsCollectionEnabled",                                 \
    "(Z)V",                                                            \
    util::kMethodTypeInstance)

#define CRASHLYTICS_FIELDS(X)                                          \
  X(Core, "core",                                                      \
  "Lcom/google/firebase/crashlytics/internal/common/CrashlyticsCore;", \
  util::kFieldTypeInstance)

// clang-format on
METHOD_LOOKUP_DECLARATION(firebase_crashlytics,
                          CRASHLYTICS_METHODS,
                          CRASHLYTICS_FIELDS)
METHOD_LOOKUP_DEFINITION(firebase_crashlytics,
                         PROGUARD_KEEP_CLASS
                         "com/google/firebase/crashlytics/FirebaseCrashlytics",
                         CRASHLYTICS_METHODS, CRASHLYTICS_FIELDS)

// clang-format off
#define CRASHLYTICS_CORE_METHODS(X)                                          \
  X(LogFatalException, "logFatalException",                                  \
    "(Ljava/lang/Throwable;)V",                                              \
    util::kMethodTypeInstance)

#define CRASHLYTICS_CORE_FIELDS(X)                                           \
  X(DataCollectionArbiter, "dataCollectionArbiter",                          \
  "Lcom/google/firebase/crashlytics/internal/common/DataCollectionArbiter;", \
  util::kFieldTypeInstance)

// clang-format on
METHOD_LOOKUP_DECLARATION(crashlytics_core,
                          CRASHLYTICS_CORE_METHODS,
                          CRASHLYTICS_CORE_FIELDS)
METHOD_LOOKUP_DEFINITION(
    crashlytics_core,
    PROGUARD_KEEP_CLASS
    "com/google/firebase/crashlytics/internal/common/CrashlyticsCore",
    CRASHLYTICS_CORE_METHODS, CRASHLYTICS_CORE_FIELDS)

// clang-format off
#define CRASHLYTICS_DATA_COLLECTION_METHODS(X)                         \
  X(IsDataCollectionEnabled, "isAutomaticDataCollectionEnabled",       \
    "()Z",                                                             \
    util::kMethodTypeInstance),                                        \
  X(SetCrashlyticsDataCollectionEnabled,                               \
    "setCrashlyticsDataCollectionEnabled",                             \
    "(Ljava/lang/Boolean;)V",                                          \
    util::kMethodTypeInstance)
// clang-format on
METHOD_LOOKUP_DECLARATION(crashlytics_data_collection,
                          CRASHLYTICS_DATA_COLLECTION_METHODS)
METHOD_LOOKUP_DEFINITION(
    crashlytics_data_collection,
    PROGUARD_KEEP_CLASS
    "com/google/firebase/crashlytics/internal/common/DataCollectionArbiter",
    CRASHLYTICS_DATA_COLLECTION_METHODS)
// clang-format off
#define JAVA_EXCEPTION_METHODS(X) \
  X(Constructor, "<init>", "(Ljava/lang/String;)V"),                   \
  X(SetStackTrace, "setStackTrace", "([Ljava/lang/StackTraceElement;)V")
// clang-format on
METHOD_LOOKUP_DECLARATION(java_exception, JAVA_EXCEPTION_METHODS);
METHOD_LOOKUP_DEFINITION(java_exception, "java/lang/Exception",
                         JAVA_EXCEPTION_METHODS);

// clang-format off
#define JAVA_STACK_TRACE_ELEMENT(X) \
  X(Constructor, "<init>",                                             \
    "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)V")
// clang-format on
METHOD_LOOKUP_DECLARATION(java_stack_trace_element, JAVA_STACK_TRACE_ELEMENT);
METHOD_LOOKUP_DEFINITION(java_stack_trace_element,
                         "java/lang/StackTraceElement",
                         JAVA_STACK_TRACE_ELEMENT);

// clang-format off
#define CRASHLYTICS_NDK_METHODS(X)                                     \
  X(GetInstance, "getInstance",                                        \
    "()Lcom/google/firebase/crashlytics/ndk/FirebaseCrashlyticsNdk;",  \
    util::kMethodTypeStatic),                                          \
  X(InstallSignalHandler, "installSignalHandler",                      \
    "()V",                                                             \
    util::kMethodTypeInstance)
// clang-format on
METHOD_LOOKUP_DECLARATION(firebase_crashlytics_ndk, CRASHLYTICS_NDK_METHODS);
METHOD_LOOKUP_DEFINITION(
    firebase_crashlytics_ndk,
    PROGUARD_KEEP_CLASS
    "com/google/firebase/crashlytics/ndk/FirebaseCrashlyticsNdk",
    CRASHLYTICS_NDK_METHODS);

static const int ANDROID_LOG_DEBUG = 3;
static const char* EXCEPTION_MESSAGE_SEPARATOR = " : ";

Mutex CrashlyticsInternal::init_mutex_;  // NOLINT
int CrashlyticsInternal::initialize_count_ = 0;

Mutex CrashlyticsInternal::data_collection_mutex_;  // NOLINT
JavaVM* CrashlyticsInternal::java_vm_ = nullptr;
// TODO(b/128917408) we need to cache this value in the C++ layer in case the
//                   Android SDK has turned data collection off because
//                   otherwise the Android SDK will throw an exception when
//                   we call methods on it.
bool cached_data_collection_enabled_ = false;

CrashlyticsInternal::CrashlyticsInternal(App* app) {
  data_collection_obj_ = nullptr;
  core_ = nullptr;
  obj_ = nullptr;
  java_vm_ = app->java_vm();

  jobject activity = app->activity();
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);

  if (!Initialize(env, activity)) return;

  // Create the Crashlytics instance
  jobject crashlytics_obj = env->CallStaticObjectMethod(
      firebase_crashlytics::GetClass(),
      firebase_crashlytics::GetMethodId(firebase_crashlytics::kGetInstance));
  util::CheckAndClearJniExceptions(env);
  assert(crashlytics_obj != nullptr);
  obj_ = env->NewGlobalRef(crashlytics_obj);
  env->DeleteLocalRef(crashlytics_obj);

  // Create the DataCollectionArbiter instance
  jobject application_context = env->CallObjectMethod(
      activity,
      util::activity::GetMethodId(util::activity::kGetApplicationContext));
  if (!application_context) {
    ::firebase::LogError(
        "Crashlytics failed to get the Application Context from the main "
        "activity");
    return;
  }

  // isDataCollectionEnabled is not currently a public API on Android; we can
  // only access the value we need via the DataCollectionArbiter, which is a
  // private field of the CrashlyticsCore instance, which in turn is a private
  // field of the FirebaseCrashlytics instance. So we must access it via
  // reflection for now.
  jobject core =  env->GetObjectField(obj_,
                                      firebase_crashlytics::GetFieldId(
                                      firebase_crashlytics::kCore));
  jobject data_collection_obj = env->GetObjectField(
      core,
      crashlytics_core::GetFieldId(crashlytics_core::kDataCollectionArbiter));

  util::CheckAndClearJniExceptions(env);
  env->DeleteLocalRef(application_context);
  assert(data_collection_obj != nullptr);
  data_collection_obj_ = env->NewGlobalRef(data_collection_obj);
  core_ = env->NewGlobalRef(core);
  env->DeleteLocalRef(data_collection_obj);
  env->DeleteLocalRef(core);

  // We cache this value in case it was set to false on a previous run
  cached_data_collection_enabled_ =
      GetCrashlyticsCollectionEnabled(java_vm_, data_collection_obj_);

  // For Unity apps, the signal handler must be explicitly installed
  InstallNdkSignalHandler();
}

CrashlyticsInternal::~CrashlyticsInternal() {
  // If initialization failed, there is nothing to clean up.
  if (java_vm_ == nullptr) return;

  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  if (obj_) {
    env->DeleteGlobalRef(obj_);
    obj_ = nullptr;
  }
  if (data_collection_obj_) {
    env->DeleteGlobalRef(data_collection_obj_);
    data_collection_obj_ = nullptr;
  }
  if (core_) {
    env->DeleteGlobalRef(core_);
    core_ = nullptr;
  }
  Terminate();
  java_vm_ = nullptr;

  util::CheckAndClearJniExceptions(env);
}

bool CrashlyticsInternal::Initialize(JNIEnv* env, jobject activity) {
  MutexLock init_lock(init_mutex_);
  if (initialize_count_ == 0) {
    if (!firebase::util::Initialize(env, activity)) {
      return false;
    }
    if (!(firebase_crashlytics::CacheMethodIds(env, activity) &&
          firebase_crashlytics::CacheFieldIds(env, activity) &&
          firebase_crashlytics_ndk::CacheMethodIds(env, activity) &&
          crashlytics_core::CacheMethodIds(env, activity) &&
          crashlytics_core::CacheFieldIds(env, activity) &&
          crashlytics_data_collection::CacheMethodIds(env, activity) &&
          java_exception::CacheMethodIds(env, activity) &&
          java_stack_trace_element::CacheMethodIds(env, activity))) {
      return false;
    }
    util::CheckAndClearJniExceptions(env);
  }
  initialize_count_++;
  return true;
}

void CrashlyticsInternal::Terminate() {
  MutexLock init_lock(init_mutex_);
  assert(initialize_count_ > 0);
  initialize_count_--;
  if (initialize_count_ == 0) {
    JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
    firebase_crashlytics::ReleaseClass(env);
    crashlytics_data_collection::ReleaseClass(env);
    crashlytics_core::ReleaseClass(env);

    firebase::util::Terminate(env);

    util::CheckAndClearJniExceptions(env);
  }
}

void CrashlyticsInternal::InstallNdkSignalHandler() {
  firebase::LogDebug("Installing Crashlytics NDK signal handlers...");
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jobject ndk_obj =
      env->CallStaticObjectMethod(firebase_crashlytics_ndk::GetClass(),
                                  firebase_crashlytics_ndk::GetMethodId(
                                      firebase_crashlytics_ndk::kGetInstance));
  env->CallVoidMethod(ndk_obj,
                      firebase_crashlytics_ndk::GetMethodId(
                          firebase_crashlytics_ndk::kInstallSignalHandler));
  env->DeleteLocalRef(ndk_obj);
}

void CrashlyticsInternal::Log(const char* message) {
  if (!cached_data_collection_enabled_) {
    return;
  }
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jobject message_string = env->NewStringUTF(message);
  env->CallVoidMethod(
      obj_,
      firebase_crashlytics::GetMethodId(firebase_crashlytics::kLog),
      message_string);
  util::LogException(env, kLogLevelError,
                     "Crashlytics::Log() (message = %s) failed", message);
  env->DeleteLocalRef(message_string);
}

void CrashlyticsInternal::SetCustomKey(const char* key, const char* value) {
  if (!cached_data_collection_enabled_) {
    return;
  }
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jobject key_string = env->NewStringUTF(key);
  jobject value_string = env->NewStringUTF(value);
  env->CallVoidMethod(
      obj_,
      firebase_crashlytics::GetMethodId(firebase_crashlytics::kSetCustomKey),
      key_string, value_string);
  util::LogException(
      env, kLogLevelError,
      "Crashlytics::SetCustomKey() (key = %s) (value = %s) failed", key, value);
  env->DeleteLocalRef(key_string);
  env->DeleteLocalRef(value_string);
}

void CrashlyticsInternal::SetUserId(const char* id) {
  if (!cached_data_collection_enabled_) {
    return;
  }
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jobject id_string = env->NewStringUTF(id);
    env->CallVoidMethod(obj_,
                        firebase_crashlytics::GetMethodId(
                        firebase_crashlytics::kSetUserId),
                        id_string);
  util::LogException(env, kLogLevelError,
                     "Crashlytics::SetUserIdentifier() (id = %s) failed",
                     id);
  env->DeleteLocalRef(id_string);
}

void CrashlyticsInternal::LogException(
    const char* name, const char* reason,
    std::vector<firebase::crashlytics::Frame> frames) {
  if (!cached_data_collection_enabled_) {
    return;
  }
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);

  std::string message(name);
  message += EXCEPTION_MESSAGE_SEPARATOR;
  message += reason;

  jobject exception_object = BuildJavaException(message, frames);

  env->CallVoidMethod(
      obj_,
      firebase_crashlytics::GetMethodId(firebase_crashlytics::kRecordException),
      exception_object);
  util::LogException(env, kLogLevelError,
                     "Crashlytics::LogException() failed");
  env->DeleteLocalRef(exception_object);
}

void CrashlyticsInternal::LogExceptionAsFatal(
    const char* name, const char* reason,
    std::vector<firebase::crashlytics::Frame> frames) {
  if (!cached_data_collection_enabled_) {
    return;
  }
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);

  std::string message(name);
  message += EXCEPTION_MESSAGE_SEPARATOR;
  message += reason;

  jobject exception_object = BuildJavaException(message, frames);

  env->CallVoidMethod(
      core_,
      crashlytics_core::GetMethodId(crashlytics_core::kLogFatalException),
      exception_object);
  util::LogException(env, kLogLevelError,
                     "Crashlytics::LogExceptionAsFatal() failed");
  env->DeleteLocalRef(exception_object);
}

bool CrashlyticsInternal::GetCrashlyticsCollectionEnabled(
    JavaVM* java_vm, jobject data_collection_obj) {
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm);
  jboolean enabled_bool = env->CallBooleanMethod(
      data_collection_obj,
      crashlytics_data_collection::GetMethodId(
          crashlytics_data_collection::kIsDataCollectionEnabled));
  if (util::LogException(
          env, kLogLevelError,
          "Crashlytics::GetCrashlyticsCollectionEnabled() failed")) {
    return false;
  }

  return enabled_bool != JNI_FALSE;
}

bool CrashlyticsInternal::IsCrashlyticsCollectionEnabled() {
  MutexLock init_lock(data_collection_mutex_);
  bool enabled =
      data_collection_obj_ != nullptr &&
      GetCrashlyticsCollectionEnabled(java_vm_, data_collection_obj_);
  cached_data_collection_enabled_ = enabled;
  return enabled;
}

void CrashlyticsInternal::SetCrashlyticsCollectionEnabled(bool enabled) {
  MutexLock init_lock(data_collection_mutex_);
  if (!data_collection_obj_) return;

  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jboolean enabled_bool = enabled ? JNI_TRUE : JNI_FALSE;
  env->CallVoidMethod(
      obj_,
      firebase_crashlytics::GetMethodId(
          firebase_crashlytics::kSetCrashlyticsDataCollectionEnabled),
      enabled_bool);

  if (util::LogException(env, kLogLevelError,
                         "Crashlytics::SetCrashlyticsCollectionEnabled() "
                         "(enabled = %s) failed",
                         enabled)) {
    return;
  }

  cached_data_collection_enabled_ = enabled;
}

jobject CrashlyticsInternal::BuildJavaException(
    std::string message, std::vector<firebase::crashlytics::Frame>& frames) {
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);

  jobject exception_message = env->NewStringUTF(message.c_str());
  jobject exception_object =
      env->NewObject(java_exception::GetClass(),
                     java_exception::GetMethodId(java_exception::kConstructor),
                     exception_message);
  env->DeleteLocalRef(exception_message);
  util::CheckAndClearJniExceptions(env);

  jobject stack_trace_object = BuildJavaStackTrace(frames);
  env->CallVoidMethod(
      exception_object,
      java_exception::GetMethodId(java_exception::kSetStackTrace),
      stack_trace_object);
  env->DeleteLocalRef(stack_trace_object);

  util::CheckAndClearJniExceptions(env);
  return exception_object;
}

jobject CrashlyticsInternal::BuildJavaStackTrace(std::vector<Frame>& frames) {
  JNIEnv* env = util::GetThreadsafeJNIEnv(java_vm_);
  jobjectArray stack_trace =
      env->NewObjectArray(frames.size(), java_stack_trace_element::GetClass(),
                          /*initialElement=*/nullptr);
  util::CheckAndClearJniExceptions(env);

  for (int i = 0; i < frames.size(); i++) {
    Frame frame = frames[i];

    jobject library_string = env->NewStringUTF(frame.library);
    jobject symbol_string = env->NewStringUTF(frame.symbol);
    jobject filename_string = env->NewStringUTF(frame.fileName);
    int line_number = std::stoi(frame.lineNumber);

    jobject stack_trace_element = env->NewObject(
        java_stack_trace_element::GetClass(),
        java_stack_trace_element::GetMethodId(
            java_stack_trace_element::kConstructor),
        library_string, symbol_string, filename_string, line_number);
    util::CheckAndClearJniExceptions(env);
    env->DeleteLocalRef(filename_string);
    env->DeleteLocalRef(symbol_string);
    env->DeleteLocalRef(library_string);

    env->SetObjectArrayElement(stack_trace, i, stack_trace_element);
    util::CheckAndClearJniExceptions(env);
    env->DeleteLocalRef(stack_trace_element);
  }

  return stack_trace;
}

}  // namespace internal
}  // namespace crashlytics
}  // namespace firebase
