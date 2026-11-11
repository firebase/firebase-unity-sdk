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

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/app_check/src/"
  PACK_PATH "Firebase/FirebaseAppCheck"
)

file(GLOB_RECURSE firebase_app_check_doc_src "src/*.cs")
list(FILTER firebase_app_check_doc_src EXCLUDE REGEX "src/internal/")
unity_pack_documentation_sources(app_check
  DOCUMENTATION_SOURCES
    ${firebase_app_check_doc_src}
)

if (FIREBASE_INCLUDE_UNITY)
  set(APP_CHECK_ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${FIREBASE_APP_CHECK_ANDROID_DEPS})
    string(CONCAT APP_CHECK_ANDROID_PACKAGES "${APP_CHECK_ANDROID_PACKAGES}"
      "    <androidPackage spec=\"${ANDROID_DEP}\">\n"
      "    </androidPackage>\n")
  endforeach()

  set(APP_CHECK_DEPS_XML "${CMAKE_CURRENT_BINARY_DIR}/AppCheckDependencies.xml")
  file(WRITE "${APP_CHECK_DEPS_XML}"
"<!-- Copyright (C) 2023 Google Inc. All Rights Reserved.\n"
"\n"
"FirebaseAppCheck iOS and Android Dependencies.\n"
"-->\n"
"\n"
"<dependencies>\n"
"  <remoteSwiftPackage url=\"https://github.com/firebase/firebase-ios-sdk.git\" version=\"${FIREBASE_SPM_VERSION}\">\n"
"    <swiftPackage name=\"FirebaseAppCheck\"/>\n"
"  </remoteSwiftPackage>\n"
"  <androidPackages>\n"
"${APP_CHECK_ANDROID_PACKAGES}"
"  </androidPackages>\n"
"</dependencies>\n"
  )
  install(
    FILES "${APP_CHECK_DEPS_XML}"
    DESTINATION "Firebase/Editor/"
  )
endif()
