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

# CMake file for the firebase_unity plugin

cmake_minimum_required(VERSION 3.22)

# Turn on virtual folders for visual studio
set_property(GLOBAL PROPERTY USE_FOLDERS ON)

option(FIREBASE_INCLUDE_UNITY "Build unity libraries" ON)
option(FIREBASE_INCLUDE_MONO "Build mono libraries" OFF)

# Different options to enable/disable each library being included during
# configuration.
option(FIREBASE_INCLUDE_LIBRARY_DEFAULT
       "Should each library be included by default." ON)
option(FIREBASE_INCLUDE_ANALYTICS
       "Include the Google Analytics for Firebase library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_APP_CHECK
       "Include the Firebase App Check library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_AUTH
       "Include the Firebase Authentication library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_CRASHLYTICS
       "Include the Firebase Crashlytics library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_DATABASE
       "Include the Firebase Realtime Database library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_INSTALLATIONS
       "Include the Firebase Installations library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_FIRESTORE
       "Include the Firebase Firestore library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_FUNCTIONS
       "Include the Cloud Functions for Firebase library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_MESSAGING
       "Include the Firebase Cloud Messaging library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_REMOTE_CONFIG
       "Include the Firebase Remote Config library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_STORAGE
       "Include the Cloud Storage for Firebase library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})
option(FIREBASE_INCLUDE_FIREBASEAI
       "Include the Firebase AI library."
       ${FIREBASE_INCLUDE_LIBRARY_DEFAULT})

option(FIREBASE_INCLUDE_EDITOR_TOOL
       "Include Unity editor tools for Firebase library." ON)
option(FIREBASE_UNITY_BUILD_TESTS
       "Enable the Firebase Unity Build Tests." OFF)

# This should only be enabled by the GitHub Action build script.
option(FIREBASE_GITHUB_ACTION_BUILD
       "Indicates that this build was created from a GitHub Action" OFF)

option(FIREBASE_PACK_DOTNET "Add CSharp libraries to CPack output" ON)

if (FIREBASE_INCLUDE_MONO AND FIREBASE_INCLUDE_UNITY)
  message(WARNING "Both Mono and Unity build set to ON. Disabling Unity.")
  set(FIREBASE_INCLUDE_UNITY OFF)
endif()

list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_LIST_DIR}/cmake)

project(firebase_unity NONE)

set(FIREBASE_64BIT_BUILD ON)
if(FIREBASE_ANDROID_BUILD)
  set(ANDROID ON)
  set(APPLE OFF)
  set(WIN32 OFF)
endif()

if(NOT ANDROID AND NOT IOS)
  set(DESKTOP ON)
else()
  set(DESKTOP OFF)
endif()

# Define the Python executable before including the subprojects
find_program(FIREBASE_PYTHON_EXECUTABLE
  NAMES python3 python
  REQUIRED
)

include(android_dependencies)
include(external_rules)
include(firebase_unity_version)
include(play_services_resolver_deps)
include(unity_mono)
include(unity_pack)

# Set variables that are used by download_external_sources.
set(FIREBASE_SOURCE_DIR ${PROJECT_SOURCE_DIR})
set(FIREBASE_BINARY_DIR ${PROJECT_BINARY_DIR})
set(FIREBASE_INSTALL_DIR ${PROJECT_BINARY_DIR}/opt)
set(FIREBASE_DOWNLOAD_DIR ${PROJECT_BINARY_DIR}/downloads)

# Run the CMake build logic that will download all the external dependencies.
download_external_sources()

set(FIREBASE_UNITY_DIR ${CMAKE_CURRENT_SOURCE_DIR})

if(FIREBASE_INCLUDE_UNITY)
  add_external_library(google_unity_jar_resolver)
endif()

add_external_library(minijson)
add_external_library(parse)

# External mono library and dll setup for CSharp projects
add_subdirectory(external)

# Includes platform which needs to be invoked first
add_subdirectory(app)

# Include Firebase editor tools.
if (DESKTOP AND (APPLE OR EXISTS "${UNITY_EDITOR_IOS_XCODE_DLL}") AND FIREBASE_INCLUDE_EDITOR_TOOL)
  add_subdirectory(editor)
endif()

if (FIREBASE_INCLUDE_ANALYTICS)
  add_subdirectory(analytics)
endif()
if (FIREBASE_INCLUDE_APP_CHECK)
  add_subdirectory(app_check)
endif()
if (FIREBASE_INCLUDE_AUTH)
  add_subdirectory(auth)
endif()
if (FIREBASE_INCLUDE_CRASHLYTICS AND FIREBASE_INCLUDE_UNITY)
  add_subdirectory(crashlytics)
endif()
if (FIREBASE_INCLUDE_DATABASE)
  add_subdirectory(database)
endif()
if (FIREBASE_INCLUDE_INSTALLATIONS)
  add_subdirectory(installations)
endif()
if (FIREBASE_INCLUDE_FIRESTORE)
  add_subdirectory(firestore)
endif()
if (FIREBASE_INCLUDE_FUNCTIONS)
  add_subdirectory(functions)
endif()
if (FIREBASE_INCLUDE_MESSAGING)
  add_subdirectory(messaging)
endif()
if (FIREBASE_INCLUDE_REMOTE_CONFIG)
  add_subdirectory(remote_config)
endif()
if (FIREBASE_INCLUDE_STORAGE)
  add_subdirectory(storage)
endif()
if (FIREBASE_INCLUDE_FIREBASEAI)
  add_subdirectory(firebaseai)
endif()

if(FIREBASE_INCLUDE_MONO)
  add_subdirectory(samples/mono_app)
endif()

# add release files
add_subdirectory(release_build_files)
