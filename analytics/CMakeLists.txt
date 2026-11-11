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

# CMake file for the firebase analytics library

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/analytics/src/"
  PACK_PATH "Firebase/FirebaseAnalytics"
)

unity_pack_file(
  "${CMAKE_CURRENT_LIST_DIR}/plugins/iOS/FirebaseUnityAppleTransactionVerifier.swift"
  PACK_PATH "Firebase/Plugins/iOS/"
)
unity_pack_file(
  "${CMAKE_CURRENT_LIST_DIR}/plugins/iOS/FirebaseUnityAppleTransactionVerifier.swift.meta"
  PACK_PATH "Firebase/Plugins/iOS/"
)

file(GLOB_RECURSE firebase_analytics_doc_src "src/*.cs")
list(FILTER firebase_analytics_doc_src EXCLUDE REGEX "src/internal/")
unity_pack_documentation_sources(analytics
  DOCUMENTATION_SOURCES
    ${firebase_analytics_doc_src}
)

if (FIREBASE_INCLUDE_UNITY)
  set(ANALYTICS_ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${FIREBASE_ANALYTICS_ANDROID_DEPS})
    string(CONCAT ANALYTICS_ANDROID_PACKAGES "${ANALYTICS_ANDROID_PACKAGES}"
      "    <androidPackage spec=\"${ANDROID_DEP}\">\n"
      "    </androidPackage>\n")
  endforeach()

  set(ANALYTICS_DEPS_XML "${CMAKE_CURRENT_BINARY_DIR}/AnalyticsDependencies.xml")
  file(WRITE "${ANALYTICS_DEPS_XML}"
"<!-- Copyright (C) 2019 Google Inc. All Rights Reserved.\n"
"\n"
"FirebaseAnalytics iOS and Android Dependencies.\n"
"-->\n"
"\n"
"<dependencies>\n"
"  <remoteSwiftPackage url=\"https://github.com/firebase/firebase-ios-sdk.git\" version=\"${FIREBASE_SPM_VERSION}\">\n"
"    <swiftPackage name=\"FirebaseAnalytics\"/>\n"
"  </remoteSwiftPackage>\n"
"  <androidPackages>\n"
"${ANALYTICS_ANDROID_PACKAGES}"
"  </androidPackages>\n"
"</dependencies>\n"
  )
  install(
    FILES "${ANALYTICS_DEPS_XML}"
    DESTINATION "Firebase/Editor/"
  )
endif()
