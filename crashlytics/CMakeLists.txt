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

# CMake file for the firebase crashlytics library

include(build_shared)

# Only for unity builds
if(NOT FIREBASE_INCLUDE_UNITY)
  return()
endif()

add_subdirectory(src/cpp)

# Firebase Crashlytics Swig input files
set(firebase_crashlytics_swig
  src/swig/crashlytics.i
)

# Firebase Crashlytics CSharp files that should be included in reference docs
set(firebase_crashlytics_src_documented
  src/Crashlytics.cs
)

# Firebase Crashlytics CSharp files
set(firebase_crashlytics_src
  ${firebase_crashlytics_src_documented}
  src/ExceptionHandler.cs
  src/Impl.cs
  src/LoggedException.cs
  src/Metadata.cs
  src/MetadataBuilder.cs
  src/StackTraceParser.cs
)

set(firebase_crashlytics_src_android
  src/AndroidImpl.cs
)

set(firebase_crashlytics_src_ios
  src/IOSImpl.cs
)

if(IOS)
  list(APPEND firebase_crashlytics_src ${firebase_crashlytics_src_ios})
else()
  list(APPEND firebase_crashlytics_src ${firebase_crashlytics_src_android})
endif()

if(NOT FIREBASE_UNI_LIBRARY AND APPLE AND NOT IOS)
  set(EXTERNAL_LIB_NAMES
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(EXTERNAL_LIB_NAMES)
endif()

firebase_swig_add_library(firebase_crashlytics_swig
  NAMESPACE
    Firebase.Crashlytics
  MODULE
    FirebaseCppCrashlytics
  SOURCES
    ${firebase_crashlytics_swig}
  DEPENDS
    firebase_crashlytics
  SYSTEM_DEPS
    ${EXTERNAL_LIB_NAMES}
)

unity_pack_documentation_sources(crashlytics
  DOCUMENTATION_SOURCES
    ${firebase_crashlytics_src_documented}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(crashlytics
    DOCUMENTATION_SOURCES
      ${firebase_crashlytics_swig_gen_src}
      ${firebase_crashlytics_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_crashlytics_cs
  MODULE
    Firebase.Crashlytics
  SOURCES
    ${firebase_crashlytics_src}
    ${firebase_crashlytics_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_crashlytics_swig
  DEFINES
    FIREBASE_OPENSOURCE
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_crashlytics libFirebaseCppCrashlytics DEPS firebase_crashlytics firebase_crashlytics_swig)
else()
  build_firebase_shared(
    crashlytics
    crashlytics
    FirebaseCppCrashlytics
  )
endif()

unity_pack_cs(firebase_crashlytics_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Crashlytics
    IOS_DEPS
      "Firebase/Crashlytics"
    ANDROID_DEPS
      ${FIREBASE_CRASHLYTICS_ANDROID_DEPS}
    ANDROID_SPEC
      "crashlytics"
  )
endif()

set_property(TARGET firebase_crashlytics_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
