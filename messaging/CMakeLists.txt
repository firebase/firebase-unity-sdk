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

unity_pack_documentation_sources(messaging
  DOCUMENTATION_SOURCES
    ${firebase_messaging_src}
    ${firebase_messaging_swig_gen_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(messaging
    DOCUMENTATION_SOURCES
      ${firebase_messaging_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_messaging_cs
  MODULE
    Firebase.Messaging
  SOURCES
    ${firebase_messaging_src}
    ${firebase_messaging_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_messaging_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_messaging libFirebaseCppMessaging DEPS firebase_messaging firebase_messaging_swig)
else()
build_firebase_shared(
  messaging
  messaging
  FirebaseCppMessaging
  ANDROID_MANIFEST
    "${CMAKE_CURRENT_LIST_DIR}/AndroidManifest.xml"
)
endif()

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

if (ANDROID)
  # Add the additional Messaging files needed for Android
  unity_pack_file(
    "activity/FirebaseMessagingActivityGenerator.cs"
    PACK_PATH
      "Firebase/Editor/Messaging/"
  )
  unity_pack_file(
    "activity/FirebaseMessagingActivityGenerator.asmdef"
    PACK_PATH
      "Firebase/Editor/Messaging/"
  )
  # Get the Messaging Java AAR file from the C++ SDK.
  add_custom_target(
    firebase_messaging_java_aar_unity
    ALL
    DEPENDS firebase_messaging_java_aar
  )
  get_target_property(messaging_aar_file firebase_messaging_java_aar OBJECT_OUTPUTS)
  unity_pack_file(
    ${messaging_aar_file}
    PACK_PATH
      "Firebase/Plugins/Android/"
    RENAME_TO
      "firebase-messaging-cpp.aar"
  )
endif()

set_property(TARGET firebase_messaging_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
