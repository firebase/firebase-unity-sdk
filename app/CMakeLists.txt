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

# CMake file for the firebase app library

if(FIREBASE_INCLUDE_UNITY)
  set(FIREBASE_PLATFORM_NAME "Unity" PARENT_SCOPE)
  set(firebase_platform_task_lib "unity_engine_cs")

else()
  set(FIREBASE_PLATFORM_NAME "Mono" PARENT_SCOPE)
  set(firebase_platform_task_lib "")
endif()

if (NOT FIREBASE_GENERATE_SWIG_ONLY)
  add_subdirectory(platform)
  add_subdirectory(task_extension)
endif()

set(FIREBASE_PLATFORM_REF
  "firebase_app_cs"
  "firebase_platform_cs"
  "firebase_taskextension_cs"
  ${firebase_platform_task_lib}
  PARENT_SCOPE
)

# Firebase App Swig input files
set(firebase_app_swig
  src/swig/app.i
)

# Firebase App CSharp files that should be included in reference docs
set(firebase_app_src_documented
  src/AppOptions.cs
  src/DependencyStatus.cs
  src/FirebaseException.cs
  src/InitializationException.cs
)

# Firebase App CSharp files
set(firebase_app_src
  ${firebase_app_src_documented}
  src/ErrorMessages.cs
  src/LogUtil.cs
  src/VariantExtension.cs
  src/TaskCompletionSourceCompat.cs
  src/MonoPInvokeCallbackAttribute.cs
  platform/Unity/AssemblyInfo.cs
  platform/FirebaseAppUtils.cs
  platform/FirebaseAppPlatform.cs
)

# Generate Version file
set(version_info_file ${CMAKE_CURRENT_BINARY_DIR}/VersionInfo.cs)

configure_file(src/VersionInfoTemplate.cs ${version_info_file}
  @ONLY
  NEWLINE_STYLE CRLF
)

firebase_swig_add_library(firebase_app_swig
  NAMESPACE
    Firebase
  MODULE
    FirebaseCppApp
  MODULE_OUT
    Firebase.App
  SOURCES
    ${firebase_app_swig}
  DEPENDS
    firebase_app
)

unity_pack_documentation_sources(app
  DOCUMENTATION_SOURCES
    ${firebase_app_src_documented}
    ${firebase_app_swig_gen_src}
    task_extension/TaskExtension.cs
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(app
    DOCUMENTATION_SOURCES
      ${firebase_app_swig_gen_cpp_src}
  )
  return()
endif()

if(FIREBASE_UNI_LIBRARY AND NOT FIREBASE_IOS_BUILD)
  set(firebase_native_library firebase_app_uni)
else()
  set(firebase_native_library firebase_app_swig)
endif()

mono_add_library(firebase_app_cs
  MODULE
    Firebase.App
  SOURCES
    ${firebase_app_src}
    ${firebase_app_swig_gen_src}
    ${version_info_file}
  REFERENCES
    ${firebase_platform_task_lib}
    firebase_platform_cs
    firebase_taskextension_cs
  DEPENDS
    ${firebase_native_library}
)

unity_pack_cs(firebase_app_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(App
    IOS_DEPS
      "Firebase/Core"
    ANDROID_DEPS
      ${FIREBASE_APP_ANDROID_DEPS}
    ANDROID_SPEC
      "app"
  )
endif()

set_property(TARGET firebase_app_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
