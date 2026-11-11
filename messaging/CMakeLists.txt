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

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/messaging/src/"
  PACK_PATH "Firebase/FirebaseMessaging"
)

file(GLOB_RECURSE firebase_messaging_doc_src "src/*.cs")
list(FILTER firebase_messaging_doc_src EXCLUDE REGEX "src/internal/")
unity_pack_documentation_sources(messaging
  DOCUMENTATION_SOURCES
    ${firebase_messaging_doc_src}
)

if (FIREBASE_INCLUDE_UNITY)
  set(MESSAGING_ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${FIREBASE_MESSAGING_ANDROID_DEPS})
    string(CONCAT MESSAGING_ANDROID_PACKAGES "${MESSAGING_ANDROID_PACKAGES}"
      "    <androidPackage spec=\"${ANDROID_DEP}\">\n"
      "    </androidPackage>\n")
  endforeach()

  set(MESSAGING_DEPS_XML "${CMAKE_CURRENT_BINARY_DIR}/MessagingDependencies.xml")
  file(WRITE "${MESSAGING_DEPS_XML}"
"<!-- Copyright (C) 2019 Google Inc. All Rights Reserved.\n"
"\n"
"FirebaseMessaging iOS and Android Dependencies.\n"
"-->\n"
"\n"
"<dependencies>\n"
"  <remoteSwiftPackage url=\"https://github.com/firebase/firebase-ios-sdk.git\" version=\"${FIREBASE_SPM_VERSION}\">\n"
"    <swiftPackage name=\"FirebaseMessaging\"/>\n"
"  </remoteSwiftPackage>\n"
"  <androidPackages>\n"
"${MESSAGING_ANDROID_PACKAGES}"
"  </androidPackages>\n"
"</dependencies>\n"
  )
  install(
    FILES "${MESSAGING_DEPS_XML}"
    DESTINATION "Firebase/Editor/"
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
endif()
