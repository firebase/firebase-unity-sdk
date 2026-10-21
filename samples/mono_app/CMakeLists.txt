# Copyright 2019 Google
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# This file compiles a mono sample app and per-product automated test apps
# that use the firebase mono libraries.

mono_add_executable(mono_sample_app_cs
  MODULE
    MonoSampleApp
  SOURCES
    sample.cs
  REFERENCES
    firebase_app_cs
    firebase_storage_cs
)

set(FTL_DIR "${FIREBASE_UNITY_DIR}/scripts/gha/integration_testing/automated_testapp")

mono_add_library(mono_testapp_runner_cs
  MODULE
    MonoTestAppRunner
  SOURCES
    MonoTestAppRunner.cs
    ${FTL_DIR}/AutomatedTestRunner.cs
    ${FTL_DIR}/ftl_testapp_files/TestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/DesktopTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/DummyTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/AndroidTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/AppleTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/ApplePluginWrapper.cs
    ${FTL_DIR}/ftl_testapp_files/LibcWrapper.cs
  SYSTEM_REFERENCES
    System.Net.Http
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
)

set(MONO_TESTAPP_ALL_REFS
  ${FIREBASE_PLATFORM_REF}
  firebase_app_internal_cs
  google_minijson_cs
  mono_testapp_runner_cs
)
foreach(prod_target
    firebase_analytics_cs
    firebase_app_check_cs
    firebase_auth_cs
    firebase_crashlytics_cs
    firebase_database_cs
    firebase_firebaseai_cs
    firebase_firestore_cs
    firebase_functions_cs
    firebase_installations_cs
    firebase_messaging_cs
    firebase_remote_config_cs
    firebase_storage_cs)
  if(TARGET ${prod_target})
    list(APPEND MONO_TESTAPP_ALL_REFS ${prod_target})
  endif()
endforeach()

macro(add_mono_product_testapp target_suffix module_name product_dir sample_subdir)
  file(GLOB testapp_srcs "${FIREBASE_UNITY_DIR}/${product_dir}/testapp/Assets/Firebase/Sample/${sample_subdir}/*.cs")
  mono_add_executable(mono_testapp_${target_suffix}_cs
    MODULE
      ${module_name}
    SOURCES
      MonoTestAppMain.cs
      ${testapp_srcs}
    DEFINES
      UNITY_STANDALONE
    SYSTEM_REFERENCES
      System.Net.Http
    REFERENCES
      ${MONO_TESTAPP_ALL_REFS}
  )
endmacro()

if(FIREBASE_INCLUDE_ANALYTICS)
  add_mono_product_testapp(analytics MonoAnalyticsTestApp analytics Analytics)
endif()
if(FIREBASE_INCLUDE_APP_CHECK)
  add_mono_product_testapp(app_check MonoAppCheckTestApp app_check AppCheck)
endif()
if(FIREBASE_INCLUDE_AUTH)
  add_mono_product_testapp(auth MonoAuthTestApp auth Auth)
endif()
if(FIREBASE_INCLUDE_CRASHLYTICS)
  add_mono_product_testapp(crashlytics MonoCrashlyticsTestApp crashlytics Crashlytics)
endif()
if(FIREBASE_INCLUDE_DATABASE)
  add_mono_product_testapp(database MonoDatabaseTestApp database Database)
endif()
if(FIREBASE_INCLUDE_FIREBASEAI)
  add_mono_product_testapp(firebaseai MonoFirebaseAITestApp firebaseai FirebaseAI)
endif()
if(FIREBASE_INCLUDE_FIRESTORE)
  add_mono_product_testapp(firestore MonoFirestoreTestApp firestore Firestore)
endif()
if(FIREBASE_INCLUDE_FUNCTIONS)
  add_mono_product_testapp(functions MonoFunctionsTestApp functions Functions)
endif()
if(FIREBASE_INCLUDE_INSTALLATIONS)
  add_mono_product_testapp(installations MonoInstallationsTestApp installations Installations)
endif()
if(FIREBASE_INCLUDE_MESSAGING)
  add_mono_product_testapp(messaging MonoMessagingTestApp messaging Messaging)
endif()
if(FIREBASE_INCLUDE_REMOTE_CONFIG)
  add_mono_product_testapp(remote_config MonoRemoteConfigTestApp remote_config RemoteConfig)
endif()
if(FIREBASE_INCLUDE_STORAGE)
  add_mono_product_testapp(storage MonoStorageTestApp storage Storage)
endif()
