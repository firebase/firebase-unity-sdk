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

# CMake file for the Firebase Crashlytics library (shipped as C# source with .asmdef)

# Only for unity builds
if(NOT FIREBASE_INCLUDE_UNITY)
  return()
endif()

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/crashlytics/src/"
  PACK_PATH "Firebase/FirebaseCrashlytics"
)

unity_pack_documentation_sources(crashlytics
  DOCUMENTATION_SOURCES
    src/Crashlytics.cs
)

if (FIREBASE_INCLUDE_UNITY)
  set(CRASHLYTICS_ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${FIREBASE_CRASHLYTICS_ANDROID_DEPS})
    string(CONCAT CRASHLYTICS_ANDROID_PACKAGES "${CRASHLYTICS_ANDROID_PACKAGES}"
      "    <androidPackage spec=\"${ANDROID_DEP}\">\n"
      "    </androidPackage>\n")
  endforeach()

  set(CRASHLYTICS_DEPS_XML "${CMAKE_CURRENT_BINARY_DIR}/CrashlyticsDependencies.xml")
  file(WRITE "${CRASHLYTICS_DEPS_XML}"
"<!-- Copyright (C) 2019 Google Inc. All Rights Reserved.\n"
"\n"
"FirebaseCrashlytics iOS and Android Dependencies.\n"
"-->\n"
"\n"
"<dependencies>\n"
"  <remoteSwiftPackage url=\"https://github.com/firebase/firebase-ios-sdk.git\" version=\"${FIREBASE_SPM_VERSION}\">\n"
"    <swiftPackage name=\"FirebaseCrashlytics\"/>\n"
"  </remoteSwiftPackage>\n"
"  <androidPackages>\n"
"${CRASHLYTICS_ANDROID_PACKAGES}"
"  </androidPackages>\n"
"</dependencies>\n"
  )
  install(
    FILES "${CRASHLYTICS_DEPS_XML}"
    DESTINATION "Firebase/Editor/"
  )
endif()
