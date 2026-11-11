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

# Firebase App CSharp files that should be included in reference docs
set(firebase_app_src_documented
  src/AppOptions.cs
  src/DependencyStatus.cs
  src/FirebaseApp.cs
  src/FirebaseException.cs
  src/InitializationException.cs
  src/LogLevel.cs
)

# Firebase App CSharp files
set(firebase_app_src
  ${firebase_app_src_documented}
  src/AppConfigLoader.cs
  src/AssemblyInfo.cs
  src/ErrorMessages.cs
  src/LogUtil.cs
  src/MobileAppBridge.cs
  src/MonoPInvokeCallbackAttribute.cs
  platform/FirebaseAppUtils.cs
  platform/FirebaseAppPlatform.cs
)

# Generate Version file
set(version_info_file ${CMAKE_CURRENT_BINARY_DIR}/VersionInfo.cs)

configure_file(src/VersionInfoTemplate.cs ${version_info_file}
  @ONLY
  NEWLINE_STYLE CRLF
)

unity_pack_documentation_sources(app
  DOCUMENTATION_SOURCES
    ${firebase_app_src_documented}
    task_extension/TaskExtension.cs
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  return()
endif()

if(FIREBASE_IOS_BUILD)
  if(FIREBASE_TVOS_VARIANT)
    set(firebase_app_cs_defines UNITY_TVOS)
  else()
    set(firebase_app_cs_defines UNITY_IOS)
  endif()
else()
  set(firebase_app_cs_defines FIREBASE_APP_ANDROID_BRIDGE)
endif()

mono_add_library(firebase_app_cs
  MODULE
    Firebase.App
  SOURCES
    ${firebase_app_src}
    ${version_info_file}
  REFERENCES
    ${firebase_platform_task_lib}
    firebase_platform_cs
    firebase_taskextension_cs
    google_minijson_cs
  DEFINES
    ${firebase_app_cs_defines}
)

unity_pack_cs(firebase_app_cs)

# Pack Internal C# files as raw source for use by other Firebase products
unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/app/src/internal/"
  PACK_PATH "Firebase/FirebaseApp/Internal"
)

if (FIREBASE_INCLUDE_UNITY)
  set(FIREBASE_APP_ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${FIREBASE_APP_ANDROID_DEPS})
    string(CONCAT FIREBASE_APP_ANDROID_PACKAGES "${FIREBASE_APP_ANDROID_PACKAGES}"
      "    <androidPackage spec=\"${ANDROID_DEP}\">\r\n"
      "    </androidPackage>\r\n")
  endforeach()

  set(FIREBASE_APP_DEPS_XML "${PROJECT_BINARY_DIR}/AppDependencies.xml")
  file(WRITE "${FIREBASE_APP_DEPS_XML}"
"<!-- Copyright (C) 2019 Google Inc. All Rights Reserved.\r\n"
"\r\n"
"FirebaseApp iOS and Android Dependencies.\r\n"
"-->\r\n"
"\r\n"
"<dependencies>\r\n"
"  <remoteSwiftPackage url=\"https://github.com/firebase/firebase-ios-sdk.git\" version=\"${FIREBASE_SPM_VERSION}\">\r\n"
"    <swiftPackage name=\"FirebaseCore\"/>\r\n"
"  </remoteSwiftPackage>\r\n"
"  <androidPackages>\r\n"
"${FIREBASE_APP_ANDROID_PACKAGES}"
"  </androidPackages>\r\n"
"</dependencies>\r\n"
  )
  install(
    FILES "${FIREBASE_APP_DEPS_XML}"
    DESTINATION "Firebase/Editor/"
  )
endif()

set_property(TARGET firebase_app_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
