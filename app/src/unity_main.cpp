/*
 * Copyright 2016 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
//
// This file is used as part of a complete shared library for use with Unity.
// When this is built as part of a shared library, and that library is loaded
// the JNI environment is automatically extracted and exposed for the rest of
// the C++ module.

#include <jni.h>

#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/util.h"
#include "app/src/util_android.h"

#ifndef FIREBASE_TESTAPP_NAME
#define FIREBASE_TESTAPP_NAME "unity_main"
#endif  // FIREBASE_TESTAPP_NAME

// This is used by the swig generated code that wraps our android calls so that
// the public interface does not expose any details of the JavaVM
// implementation.
namespace firebase {
JavaVM *g_jvm = nullptr;
jobject g_activity = nullptr;

// Guards g_activity and g_modules_initialized. The activity is read from
// arbitrary managed threads and can be replaced by FirebaseSetAndroidActivity.
static Mutex g_activity_mutex;  // NOLINT
static bool g_modules_initialized = false;

static const char *kUnityPlayerClass = "com/unity3d/player/UnityPlayer";
static const char *kUnityPlayerActivityProperty = "currentActivity";

// Enable the module initializers of the Firebase libraries whose Java classes
// are present in the application. Runs once per process.
static void InitializeAndroidModuleCallbacks(JNIEnv *jni_env,
                                             jobject activity_local_ref) {
  if (!jni_env || !activity_local_ref) {
    return;
  }
  MutexLock lock(g_activity_mutex);
  if (g_modules_initialized) {
    return;
  }
  static const struct {
    const char *java_class;
    const char *module_name;
  } kJavaClassModuleMap[] = {
      {"com/google/firebase/analytics/FirebaseAnalytics", "analytics"},
      {"com/google/firebase/appcheck/FirebaseAppCheck", "app_check"},
      {"com/google/firebase/auth/FirebaseAuth", "auth"},
      {"com/google/firebase/crashlytics/FirebaseCrashlytics", "crashlytics"},
      {"com/google/firebase/database/FirebaseDatabase", "database"},
      {"com/google/firebase/functions/FirebaseFunctions", "functions"},
      {"com/google/firebase/installations/FirebaseInstallations",
       "installations"},
      {"com/google/firebase/messaging/FirebaseMessaging", "messaging"},
      {"com/google/firebase/perf/FirebasePerformance", "performance"},
      {"com/google/firebase/remoteconfig/FirebaseRemoteConfigInfo",
       "remote_config"},
      {"com/google/firebase/storage/FirebaseStorage", "storage"},
  };
  // TODO(smiles): Fix this when all plugins are split into separate shared
  // libraries.
  // Use the presence of Java classes to enable module initializers.  The
  // way this is supposed to work is where the end user links the library
  // they want to use and includes the Java dependencies but since our
  // Unity library currently packs all C++ code into a single shared library
  // module initializers need to be enabled manually.
  ::firebase::util::InitializeActivityClasses(jni_env, activity_local_ref);
  for (const auto &entry : kJavaClassModuleMap) {
    jclass found_class = ::firebase::util::FindClass(jni_env, entry.java_class);
    ::firebase::LogDebug("Dependency of %s %s", entry.module_name,
                         found_class ? "found" : "not found");
    if (found_class) {
      jni_env->DeleteLocalRef(found_class);
      ::firebase::AppCallback::SetEnabledByName(entry.module_name, true);
    }
  }
  ::firebase::util::TerminateActivityClasses(jni_env);
  g_modules_initialized = true;
}

// Returns a JNIEnv for the calling thread, or nullptr if the JavaVM is not
// known yet (the library was loaded without JNI_OnLoad being called).
static JNIEnv *GetJNIEnvOrNull() {
  if (!::firebase::g_jvm) {
    return nullptr;
  }
  return ::firebase::util::GetThreadsafeJNIEnv(::firebase::g_jvm);
}

// Set (or with a null activity, clear) the activity used to initialize
// Firebase, replacing the lookup through the UnityPlayer.
void SetAndroidActivity(JNIEnv *jni_env, jobject activity) {
  if (!jni_env) {
    jni_env = GetJNIEnvOrNull();
  }
  if (!jni_env) {
    ::firebase::LogError(
        "Unable to set the Android activity: no JNI environment. Call "
        "FirebaseSetAndroidActivity with a valid JNIEnv.");
    return;
  }
  // Held until the module initializers are enabled, so that no other thread
  // can pick up the new activity and create an App before then. The mutex is
  // recursive.
  MutexLock lock(g_activity_mutex);
  if (g_activity) {
    jni_env->DeleteGlobalRef(g_activity);
    g_activity = nullptr;
  }
  if (activity) {
    g_activity = jni_env->NewGlobalRef(activity);
  }
  InitializeAndroidModuleCallbacks(jni_env, activity);
}

// Get a local reference to the activity registered with
// FirebaseSetAndroidActivity, falling back to the activity of the UnityPlayer.
// Returns nullptr (and may set *jni_env to nullptr) if neither is available.
static jobject GetActivity(JNIEnv **jni_env, bool log_errors) {
  static const char *kGetActivityFailedMessage =
      "This is required to fetch the Android activity used to "
      "initialize Firebase.\n"
      "Try a clean build, if that fails contact Firebase support.\n";
  *jni_env = GetJNIEnvOrNull();
  if (*jni_env == nullptr) {
    if (log_errors) {
      ::firebase::LogError("Unable to get JNI environment.\n%s",
                           kGetActivityFailedMessage);
    }
    return nullptr;
  }

  MutexLock lock(g_activity_mutex);
  if (g_activity) {
    return (*jni_env)->NewLocalRef(g_activity);
  }

  jclass unity_player_class = (*jni_env)->FindClass(kUnityPlayerClass);
  if ((*jni_env)->ExceptionCheck()) {
    (*jni_env)->ExceptionClear();
  }
  if (!unity_player_class) {
    if (log_errors) {
      ::firebase::LogError(
          "Unable to find class %s, and no activity was registered with "
          "FirebaseSetAndroidActivity.\n%s",
          kUnityPlayerClass, kGetActivityFailedMessage);
    }
    return nullptr;
  }
  jfieldID current_activity_field = (*jni_env)->GetStaticFieldID(
      unity_player_class, kUnityPlayerActivityProperty,
      "Landroid/app/Activity;");
  if ((*jni_env)->ExceptionCheck()) {
    (*jni_env)->ExceptionClear();
  }
  if (!current_activity_field) {
    (*jni_env)->DeleteLocalRef(unity_player_class);
    if (log_errors) {
      ::firebase::LogError(
          "Failed to retrieve the %s.%s field from class %s.\n%s",
          kUnityPlayerClass, kUnityPlayerActivityProperty, kUnityPlayerClass,
          kGetActivityFailedMessage);
    }
    return nullptr;
  }
  jobject activity_local_ref = (*jni_env)->GetStaticObjectField(
      unity_player_class, current_activity_field);
  if ((*jni_env)->ExceptionCheck()) {
    (*jni_env)->ExceptionClear();
  }
  (*jni_env)->DeleteLocalRef(unity_player_class);
  if (!activity_local_ref) {
    if (log_errors) {
      ::firebase::LogError(
          "Failed to get a reference to the activity from "
          "%s.%s.\n%s",
          kUnityPlayerClass, kUnityPlayerActivityProperty,
          kGetActivityFailedMessage);
    }
    return nullptr;
  }

  g_activity = (*jni_env)->NewGlobalRef(activity_local_ref);

  return activity_local_ref;
}

jobject UnityGetActivity(JNIEnv **jni_env) {
  return GetActivity(jni_env, /*log_errors=*/true);
}
}  // namespace firebase

extern "C" {

// Registers the Android activity Firebase should use, for hosts that are not
// a Unity player (where the activity cannot be read from the UnityPlayer
// class). Must be called with a valid JNIEnv for the calling thread and a
// reference to an android.app.Activity before the first FirebaseApp is
// created; a global reference to the activity is retained. Passing a null
// activity clears the registration. Safe to call from any thread.
JNIEXPORT void JNICALL FirebaseSetAndroidActivity(JNIEnv *jni_env,
                                                  jobject activity) {
  if (jni_env && !::firebase::g_jvm) {
    if (jni_env->GetJavaVM(&::firebase::g_jvm) != JNI_OK) {
      ::firebase::g_jvm = nullptr;
    }
  }
  ::firebase::SetAndroidActivity(jni_env, activity);
}

jint JNI_OnLoad(JavaVM *jvm, void *reserved) {
  ::firebase::LogDebug("JNI_OnLoad");
  ::firebase::g_jvm = jvm;
  JNIEnv *jni_env = nullptr;
  jobject activity_local_ref =
      ::firebase::GetActivity(&jni_env, /*log_errors=*/false);
  ::firebase::LogDebug(
      "%s.%s = 0x%08x", ::firebase::kUnityPlayerClass,
      ::firebase::kUnityPlayerActivityProperty,
      static_cast<int>(reinterpret_cast<intptr_t>(activity_local_ref)));
  if (activity_local_ref) {
    ::firebase::InitializeAndroidModuleCallbacks(jni_env, activity_local_ref);
    jni_env->DeleteLocalRef(activity_local_ref);
  }

  return JNI_VERSION_1_6;  // minimum JNI
}
}
