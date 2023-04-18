# Copyright 2023 Google
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

# CMake file for the firebase app check library

include(build_shared)

# Firebase App Check Swig input files
set(firebase_app_check_swig_files
  src/swig/app_check.i
)

# Firebase App Check CSharp files
set(firebase_app_check_src
  src/AppCheckError.cs
  src/AppCheckToken.cs
  src/FirebaseAppCheck.cs
  src/IAppCheckProvider.cs
  src/IAppCheckProviderFactory.cs
  src/TokenChangedEventArgs.cs

  # Built In Providers
  src/BuiltInProviderWrapper.cs
  src/AppAttestProviderFactory.cs
  src/DebugAppCheckProviderFactory.cs
  src/DeviceCheckProviderFactory.cs
  src/PlayIntegrityProviderFactory.cs
)

if(NOT FIREBASE_UNI_LIBRARY AND APPLE AND NOT IOS)
  set(EXTERNAL_LIB_NAMES
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(EXTERNAL_LIB_NAMES)
endif()

firebase_swig_add_library(firebase_app_check_swig
  NAMESPACE
    Firebase.AppCheck
  MODULE
    FirebaseCppAppCheck
  SOURCES
    ${firebase_app_check_swig_files}
  DEPENDS
    firebase_app_check
  SYSTEM_DEPS
    ${EXTERNAL_LIB_NAMES}
)

unity_pack_documentation_sources(app_check
  DOCUMENTATION_SOURCES
    ${firebase_app_check_src}
    ${firebase_app_check_swig_gen_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(app_check
    DOCUMENTATION_SOURCES
      ${firebase_app_check_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_app_check_cs
  MODULE
    Firebase.AppCheck
  SOURCES
    ${firebase_app_check_src}
    ${firebase_app_check_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_app_check_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_app_check libFirebaseCppAppCheck DEPS firebase_app_check firebase_app_check_swig)
else()
build_firebase_shared(
  app_check
  appcheck
  FirebaseCppAppCheck
)
endif()

unity_pack_cs(firebase_app_check_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(AppCheck
    IOS_DEPS
      "Firebase/AppCheck"
    ANDROID_DEPS
      ${FIREBASE_APP_CHECK_ANDROID_DEPS}
    ANDROID_SPEC
      "appcheck"
  )
endif()

set_property(TARGET firebase_app_check_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
