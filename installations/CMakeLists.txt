# Copyright 2020 Google
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

# CMake file for the firebase installations library

include(build_shared)

# Firebase Installations Swig input files
set(firebase_installations_swig
  src/swig/installations.i
)

# Firebase Installations CSharp files
set(firebase_installations_src
)

if(NOT FIREBASE_UNI_LIBRARY AND APPLE AND NOT IOS)
  set(EXTERNAL_LIB_NAMES
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(EXTERNAL_LIB_NAMES)
endif()

firebase_swig_add_library(firebase_installations_swig
  NAMESPACE
    Firebase.Installations
  MODULE
    FirebaseCppInstallations
  SOURCES
    ${firebase_installations_swig}
  DEPENDS
    firebase_installations
  SYSTEM_DEPS
    ${EXTERNAL_LIB_NAMES}
)

unity_pack_documentation_sources(installations
  DOCUMENTATION_SOURCES
    ${firebase_installations_src}
    ${firebase_installations_swig_gen_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(installations
    DOCUMENTATION_SOURCES
      ${firebase_installations_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_installations_cs
  MODULE
    Firebase.Installations
  SOURCES
    ${firebase_installations_src}
    ${firebase_installations_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_installations_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_installations libFirebaseCppInstallations DEPS firebase_installations firebase_installations_swig)
else()
build_firebase_shared(
  installations
  installations
  FirebaseCppInstallations
)
endif()

unity_pack_cs(firebase_installations_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Installations
    IOS_DEPS
      "FirebaseInstallations,${FIREBASE_INSTALLATIONS_POD_VERSION}"
    ANDROID_DEPS
      ${FIREBASE_INSTALLATIONS_ANDROID_DEPS}
    ANDROID_SPEC
      "installations"
  )
endif()

set_property(TARGET firebase_installations_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
