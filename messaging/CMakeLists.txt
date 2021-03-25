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

# CMake file for the firebase messaging library

include(build_shared)

# Firebase Messaging Swig input files
set(firebase_messaging_swig
  src/swig/messaging.i
)

# Firebase Messaging CSharp files
set(firebase_messaging_src
  src/MessagingEventArgs.cs
)

firebase_swig_add_library(firebase_messaging_swig
  NAMESPACE
    Firebase.Messaging
  MODULE
    FirebaseCppMessaging
  SOURCES
    ${firebase_messaging_swig}
  DEPENDS
    firebase_messaging
)

mono_add_library(firebase_messaging_cs
  MODULE
    Firebase.Messaging
  SOURCES
    ${firebase_messaging_src}
    ${firebase_messaging_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_messaging_swig
)

build_firebase_shared(
  messaging
  messaging
  FirebaseCppMessaging
)

unity_pack_native(firebase_messaging_swig)
unity_pack_cs(firebase_messaging_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Messaging
    IOS_DEPS
      "Firebase/Messaging"
    ANDROID_DEPS
      ${FIREBASE_MESSAGING_ANDROID_DEPS}
    ANDROID_SPEC
      "messaging"
  )
endif()

set_property(TARGET firebase_messaging_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
