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

#include "app/src/util.h"
#include "app/src/util_android.h"

#ifndef FIREBASE_TESTAPP_NAME
#define FIREBASE_TESTAPP_NAME "unity_main"
#endif  // FIREBASE_TESTAPP_NAME

// This is used by the swig generated code that wraps our android calls so that
// the public interface does not expose any details of the JavaVM
// implementation.
namespace firebase {
JavaVM *g_jvm;
jobject g_activity = nullptr;

static const char *kUnityPlayerClass = "com/unity3d/player/UnityPlayer";
static const char *kUnityPlayerActivityProperty = "currentActivity";

// Get the activity from the UnityPlayer.
jobject UnityGetActivity(JNIEnv **jni_env) {
  static const char *kGetActivityFailedMessage =
      "This is required to fetch the Android activity used to "
      "initialize Firebase.\n"
      "Try a clean build, if that fails contact Firebase support.\n";
  *jni_env = ::firebase::util::GetThreadsafeJNIEnv(::firebase::g_jvm);
  if (*jni_env == nullptr) {
    ::firebase::LogError("Unable to get JNI environment.\n%s",
                         kGetActivityFailedMessage);
    return nullptr;
  }

  if (g_activity) {
    return (*jni_env)->NewLocalRef(g_activity);
  }

  jclass unity_player_class = (*jni_env)->FindClass(kUnityPlayerClass);
  if (!unity_player_class) {
    ::firebase::LogError("Unable to find class %s.\n%s", kUnityPlayerClass,
                         kGetActivityFailedMessage);
    return nullptr;
  }
  jfieldID current_activity_field = (*jni_env)->GetStaticFieldID(
      unity_player_class, kUnityPlayerActivityProperty,
      "Landroid/app/Activity;");
  if (!current_activity_field) {
    ::firebase::LogError(
        "Failed to retrieve the %s.%s field from class %s.\n%s",
        kUnityPlayerClass, kUnityPlayerActivityProperty, kUnityPlayerClass,
        kGetActivityFailedMessage);
    return nullptr;
  }
  jobject activity_local_ref = (*jni_env)->GetStaticObjectField(
      unity_player_class, current_activity_field);
  if (!activity_local_ref) {
    ::firebase::LogError(
        "Failed to get a reference to the activity from "
        "%s.%s.\n%s",
        kUnityPlayerClass, kUnityPlayerActivityProperty,
        kGetActivityFailedMessage);
    return nullptr;
  }

  g_activity = (*jni_env)->NewGlobalRef(activity_local_ref);

  return activity_local_ref;
}
}  // namespace firebase

extern "C" {

jint JNI_OnLoad(JavaVM *jvm, void *reserved) {
  ::firebase::LogDebug("JNI_OnLoad");
  ::firebase::g_jvm = jvm;
  JNIEnv *jni_env;
  jobject activity_local_ref = ::firebase::UnityGetActivity(&jni_env);
  ::firebase::LogDebug(
      "%s.%s = 0x%08x", ::firebase::kUnityPlayerClass,
      ::firebase::kUnityPlayerActivityProperty,
      static_cast<int>(reinterpret_cast<intptr_t>(activity_local_ref)));
  if (activity_local_ref) {
    static const struct {
      const char *java_class;
      const char *module_name;
    } kJavaClassModuleMap[] = {
        {"com/google/firebase/analytics/FirebaseAnalytics", "analytics"},
        {"com/google/firebase/appcheck/FirebaseAppCheck", "app_check"},
        {"com/google/firebase/auth/FirebaseAuth", "auth"},
        {"com/google/firebase/crashlytics/FirebaseCrashlytics", "crashlytics"},
        {"com/google/firebase/database/FirebaseDatabase", "database"},
        {"com/google/firebase/dynamiclinks/FirebaseDynamicLinks",
         "dynamic_links"},
        {"com/google/firebase/functions/FirebaseFunctions", "functions"},
        {"com/google/firebase/installations/FirebaseInstallations",
         "installations"},
        {"com/google/android/gms/appinvite/AppInvite", "invites"},
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
    for (size_t i = 0;
         i < sizeof(kJavaClassModuleMap) / sizeof(kJavaClassModuleMap[0]);
         ++i) {
      jclass found_class = ::firebase::util::FindClass(
          jni_env, kJavaClassModuleMap[i].java_class);
      ::firebase::LogDebug("Dependency of %s %s",
                           kJavaClassModuleMap[i].module_name,
                           found_class ? "found" : "not found");
      if (found_class) {
        jni_env->DeleteLocalRef(found_class);
        ::firebase::AppCallback::SetEnabledByName(
            kJavaClassModuleMap[i].module_name, true);
      }
    }
    jni_env->DeleteLocalRef(activity_local_ref);
    ::firebase::util::TerminateActivityClasses(jni_env);
  }

  return JNI_VERSION_1_6;  // minimum JNI
}
}
