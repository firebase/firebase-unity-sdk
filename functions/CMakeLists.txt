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

# CMake file for the firebase functions library

include(build_shared)

# Firebase Functions Swig input files
set(firebase_functions_swig
  src/swig/functions.i
)

# Firebase Functions CSharp files
set(firebase_functions_src
  src/FirebaseFunctions.cs
  src/FunctionsException.cs
  src/HttpsCallableReference.cs
  src/HttpsCallableResult.cs
)

firebase_swig_add_library(firebase_functions_swig
  NAMESPACE
    Firebase.Functions
  MODULE
    FirebaseCppFunctions
  SOURCES
    ${firebase_functions_swig}
  DEPENDS
    firebase_functions
)

mono_add_library(firebase_functions_cs
  MODULE
    Firebase.Functions
  SOURCES
    ${firebase_functions_src}
    ${firebase_functions_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_functions_swig
)

build_firebase_shared(
  functions
  functions
  FirebaseCppFunctions
)

unity_pack_native(firebase_functions_swig)
unity_pack_cs(firebase_functions_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Functions
    IOS_DEPS
      "Firebase/Functions"
    ANDROID_DEPS
      ${FIREBASE_FUNCTIONS_ANDROID_DEPS}
    ANDROID_SPEC
      "functions"
  )
endif()

set_property(TARGET firebase_functions_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
