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

# CMake file for the firebase auth library

include(build_shared)

# Firebase Auth Swig input files
set(firebase_auth_swig
  src/swig/auth.i
)

# Firebase Auth CSharp files
set(firebase_auth_src
  src/FirebaseAccountLinkException.cs
  src/IUserInfo.cs
  src/PhoneAuthProvider.cs
)

if(NOT FIREBASE_UNI_LIBRARY AND APPLE AND NOT IOS)
  set(EXTERNAL_LIB_NAMES
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(EXTERNAL_LIB_NAMES)
endif()

firebase_swig_add_library(firebase_auth_swig
  NAMESPACE
    Firebase.Auth
  MODULE
    FirebaseCppAuth
  SOURCES
    ${firebase_auth_swig}
  DEPENDS
    firebase_auth
  SYSTEM_DEPS
    ${EXTERNAL_LIB_NAMES}
)

mono_add_library(firebase_auth_cs
  MODULE
    Firebase.Auth
  SOURCES
    ${firebase_auth_src}
    ${firebase_auth_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_auth_swig
)

build_firebase_shared(
  auth
  auth
  FirebaseCppAuth
)

unity_pack_native(firebase_auth_swig)
unity_pack_cs(firebase_auth_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Auth
    IOS_DEPS
      "Firebase/Auth"
    ANDROID_DEPS
      ${FIREBASE_AUTH_ANDROID_DEPS}
    ANDROID_SPEC
      "auth"
  )
endif()

set_property(TARGET firebase_auth_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
