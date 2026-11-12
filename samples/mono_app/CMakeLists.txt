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

# Compiles the Mono sample app and the unified MonoTestApp desktop runner
# containing all enabled product automated testapps.

if(FIREBASE_INCLUDE_STORAGE)
  mono_add_executable(mono_sample_app_cs
    MODULE
      MonoSampleApp
    SOURCES
      sample.cs
    REFERENCES
      firebase_app_cs
      firebase_storage_cs
  )
endif()

if(FIREBASE_UNITY_BUILD_TESTS)
  set(FTL_DIR "${FIREBASE_UNITY_DIR}/scripts/gha/integration_testing/automated_testapp")

  set(MONO_TESTAPP_SRCS
    MonoTestAppRunner.cs
    ${FTL_DIR}/AutomatedTestRunner.cs
    ${FTL_DIR}/ftl_testapp_files/TestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/DesktopTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/DummyTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/AndroidTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/AppleTestLabManager.cs
    ${FTL_DIR}/ftl_testapp_files/ApplePluginWrapper.cs
    ${FTL_DIR}/ftl_testapp_files/LibcWrapper.cs
  )

  set(MONO_TESTAPP_REFS
    ${FIREBASE_PLATFORM_REF}
  )
  foreach(opt_ref
      firebase_app_internal_cs
      google_minijson_cs
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
    if(TARGET ${opt_ref})
      list(APPEND MONO_TESTAPP_REFS ${opt_ref})
    endif()
  endforeach()

  macro(append_product_testapp_sources product_dir sample_subdir)
    file(GLOB product_testapp_srcs
      "${FIREBASE_UNITY_DIR}/${product_dir}/testapp/Assets/Firebase/Sample/${sample_subdir}/*.cs"
    )
    list(APPEND MONO_TESTAPP_SRCS ${product_testapp_srcs})
  endmacro()

  if(FIREBASE_INCLUDE_ANALYTICS)
    append_product_testapp_sources(analytics Analytics)
  endif()
  if(FIREBASE_INCLUDE_APP_CHECK)
    append_product_testapp_sources(app_check AppCheck)
  endif()
  if(FIREBASE_INCLUDE_AUTH)
    append_product_testapp_sources(auth Auth)
  endif()
  if(FIREBASE_INCLUDE_CRASHLYTICS)
    append_product_testapp_sources(crashlytics Crashlytics)
    # Crashlytics testapp includes a duplicate, unused Firebase.Sample.Functions.Utils class.
    list(REMOVE_ITEM MONO_TESTAPP_SRCS
      "${FIREBASE_UNITY_DIR}/crashlytics/testapp/Assets/Firebase/Sample/Crashlytics/Utils.cs"
    )
  endif()
  if(FIREBASE_INCLUDE_DATABASE)
    append_product_testapp_sources(database Database)
  endif()
  if(FIREBASE_INCLUDE_FIREBASEAI AND FIREBASE_INCLUDE_APP_CHECK)
    append_product_testapp_sources(firebaseai FirebaseAI)
  endif()
  if(FIREBASE_INCLUDE_FIRESTORE AND FIREBASE_INCLUDE_AUTH)
    append_product_testapp_sources(firestore Firestore)
  endif()
  if(FIREBASE_INCLUDE_FUNCTIONS AND FIREBASE_INCLUDE_AUTH AND FIREBASE_INCLUDE_APP_CHECK)
    append_product_testapp_sources(functions Functions)
  endif()
  if(FIREBASE_INCLUDE_INSTALLATIONS)
    append_product_testapp_sources(installations Installations)
  endif()
  if(FIREBASE_INCLUDE_MESSAGING AND FIREBASE_INCLUDE_FUNCTIONS)
    append_product_testapp_sources(messaging Messaging)
  endif()
  if(FIREBASE_INCLUDE_REMOTE_CONFIG)
    append_product_testapp_sources(remote_config RemoteConfig)
  endif()
  if(FIREBASE_INCLUDE_STORAGE)
    append_product_testapp_sources(storage Storage)
  endif()

  mono_add_executable(mono_testapp_cs
    MODULE
      MonoTestApp
    SOURCES
      ${MONO_TESTAPP_SRCS}
    DEFINES
      UNITY_STANDALONE
    SYSTEM_REFERENCES
      System.Net.Http
    REFERENCES
      ${MONO_TESTAPP_REFS}
  )
endif()
