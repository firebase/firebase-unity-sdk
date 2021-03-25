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

# CMake file for the firebase instance_id library

include(build_shared)

# Firebase InstanceId Swig input files
set(firebase_instance_id_swig
  src/swig/instance_id.i
)

# Firebase InstanceId CSharp files
set(firebase_instance_id_src
)

if(NOT FIREBASE_UNI_LIBRARY AND APPLE AND NOT IOS)
  set(EXTERNAL_LIB_NAMES
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(EXTERNAL_LIB_NAMES)
endif()

firebase_swig_add_library(firebase_instance_id_swig
  NAMESPACE
    Firebase.InstanceId
  MODULE
    FirebaseCppInstanceId
  SOURCES
    ${firebase_instance_id_swig}
  DEPENDS
    firebase_instance_id
  SYSTEM_DEPS
    ${EXTERNAL_LIB_NAMES}
)

mono_add_library(firebase_instance_id_cs
  MODULE
    Firebase.InstanceId
  SOURCES
    ${firebase_instance_id_src}
    ${firebase_instance_id_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_instance_id_swig
)

build_firebase_shared(
  instance_id
  instance-id
  FirebaseCppInstanceId
)

unity_pack_native(firebase_instance_id_swig)
unity_pack_cs(firebase_instance_id_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(InstanceId
    IOS_DEPS
      "FirebaseInstanceId,${FIREBASE_INSTANCE_ID_POD_VERSION}"
    ANDROID_DEPS
      ${FIREBASE_INSTANCE_ID_ANDROID_DEPS}
    ANDROID_SPEC
      "instance-id"
  )
endif()

set_property(TARGET firebase_instance_id_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
