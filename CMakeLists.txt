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

# Higher CMake version needed for improvments in UseSwig.cmake and string join
cmake_minimum_required (VERSION 3.13.4)
set (CMAKE_CXX_STANDARD 11)

# Turn on virtual folders for visual studio
set_property(GLOBAL PROPERTY USE_FOLDERS ON)

# Disable adding "-d" to debug dll filename as CSharp uses dll name for pinvoke
set(CMAKE_DEBUG_POSTFIX "" CACHE STRING "Set debug library postfix")

option(FIREBASE_INCLUDE_UNITY "Build unity libraries" ON)
option(FIREBASE_INCLUDE_MONO "Build mono libraries" OFF)
option(FIREBASE_UNI_LIBRARY "Link all firebase libraries into one shared object" ON)

# Different options to enable/disable each library being included during
# configuration.
option(FIREBASE_INCLUDE_LIBRARY_DEFAULT
       "Should each library be included by default." ON)
option(FIREBASE_INCLUDE_ANALYTICS
       "Include the Google Analytics for Firebase library."
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
option(FIREBASE_INCLUDE_DYNAMIC_LINKS
       "Include the Firebase Dynamic Links library."
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

option(FIREBASE_INCLUDE_EDITOR_TOOL
       "Include Unity editor tools for Firebase library." ON)
option(FIREBASE_UNITY_BUILD_TESTS
       "Enable the Firebase Unity Build Tests." OFF)

# This should only be enabled by the GitHub Action build script.
option(FIREBASE_GITHUB_ACTION_BUILD
       "Indicates that this build was created from a GitHub Action" OFF)

# These options allow selecting what built outputs go into the CPack zip file
# as we merge the different platform zip's together for unity package
# For example: only packing dotnet libraries on linux builds
option(FIREBASE_PACK_NATIVE "Add C++ libraries to CPack output" ON)
option(FIREBASE_PACK_DOTNET "Add CSharp libraries to CPack output" ON)

set(FIREBASE_CPP_SDK_DIR "" CACHE STRING "Directory to the firebase cpp sdk")

set(FIREBASE_ANDROID_STL "" CACHE STRING "STL implementation to use.")
if (NOT FIREBASE_ANDROID_STL STREQUAL "")
  set(ANDROID_STL ${FIREBASE_ANDROID_STL})
endif()

# UseSWIG generates standard target names.
cmake_policy(SET CMP0078 NEW)

if (FIREBASE_INCLUDE_MONO AND FIREBASE_INCLUDE_UNITY)
  message(WARNING "Both Mono and Unity build set to ON. Disabling Unity.")
  set(FIREBASE_INCLUDE_UNITY OFF)
endif()

# Darwin library for security
if(APPLE AND NOT FIREBASE_IOS_BUILD AND NOT FIREBASE_ANDROID_BUILD)
  set(FIREBASE_SYSTEM_DEPS
    "-framework Foundation"
    "-framework Security"
  )
else()
  set(FIREBASE_SYSTEM_DEPS)
endif()

if(NOT FIREBASE_IOS_BUILD) # besides iOS build, don't need lib prefix
  set(CMAKE_SHARED_LIBRARY_PREFIX "")
  set(CMAKE_STATIC_LIBRARY_PREFIX "")
endif()

list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_LIST_DIR}/cmake)

if(DEFINED CMAKE_TOOLCHAIN_FILE AND NOT ANDROID)
  # Force using the toolchain file, as otherwise it loads it later in the
  # process, after several of the checks against it have failed.
  if(IS_ABSOLUTE ${CMAKE_TOOLCHAIN_FILE})
    message("Using Toolchain File: ${CMAKE_TOOLCHAIN_FILE}")
    include("${CMAKE_TOOLCHAIN_FILE}")
  else()
    message("Using Toolchain File: ${CMAKE_BINARY_DIR}/${CMAKE_TOOLCHAIN_FILE}")
    include("${CMAKE_BINARY_DIR}/${CMAKE_TOOLCHAIN_FILE}")
  endif()
endif()

project(firebase_unity NONE)
enable_language(C)
enable_language(CXX)

if(NOT MSVC AND NOT APPLE)
  # The Linux Unity Editor seems to require using the CXX11 ABI for Firestore,
  # so we turn the option on, and set the flag to true for the C++ SDK to pick
  # it up as well.
  add_definitions(-D_GLIBCXX_USE_CXX11_ABI=1)
  set(CMAKE_C_FLAGS "${CMAKE_C_FLAGS} -D_GLIBCXX_USE_CXX11_ABI=1")
  set(CMAKE_CXX_FLAGS "${CMAKE_CXX_FLAGS} -D_GLIBCXX_USE_CXX11_ABI=1")
  set(FIREBASE_LINUX_USE_CXX11_ABI TRUE)
endif()

if(NOT ANDROID AND NOT IOS)
  set(DESKTOP ON)
else()
  set(DESKTOP OFF)
endif()

include(FindPkgConfig)
include(android_dependencies)
include(build_universal)
include(external_rules)
include(firebase_swig)
include(firebase_unity_version)
include(ios_pack)
include(play_services_resolver_deps)
include(unity_mono)
include(unity_pack)

# Set variables that are used by download_external_sources.
set(FIREBASE_SOURCE_DIR ${PROJECT_SOURCE_DIR})
set(FIREBASE_BINARY_DIR ${PROJECT_BINARY_DIR})
set(FIREBASE_INSTALL_DIR ${PROJECT_BINARY_DIR}/opt)
set(FIREBASE_DOWNLOAD_DIR ${PROJECT_BINARY_DIR}/downloads)

# Try to read the path to the Firebase C++ SDK from an environment variable.
# Do this before downloading external sources, as if it is missing, it will be
# downloaded as part of that step.
if (NOT "$ENV{FIREBASE_CPP_SDK_DIR}" STREQUAL "")
  set(DEFAULT_FIREBASE_CPP_SDK_DIR "$ENV{FIREBASE_CPP_SDK_DIR}")
else()
  set(DEFAULT_FIREBASE_CPP_SDK_DIR "firebase_cpp_sdk")
endif()

if ("${FIREBASE_CPP_SDK_DIR}" STREQUAL "")
  set(FIREBASE_CPP_SDK_DIR ${DEFAULT_FIREBASE_CPP_SDK_DIR})
endif()
if (NOT IS_ABSOLUTE ${FIREBASE_CPP_SDK_DIR})
  set(FIREBASE_CPP_SDK_DIR "${PROJECT_BINARY_DIR}/${FIREBASE_CPP_SDK_DIR}")
endif()

# Run the CMake build logic that will download all the external dependencies.
download_external_sources()

# Shared libraries on linux and mac need position independent code to link
# properly. Force it here for all projects.
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT MSVC AND NOT ANDROID)
  if("${CMAKE_GENERATOR_PLATFORM}" STREQUAL "x64" OR
     "${CMAKE_GENERATOR_PLATFORM}" STREQUAL "")
    add_compile_options(-m64)
  else()
    add_compile_options(-m32)
  endif()
endif()

if(UNIX AND NOT APPLE) # we only need this for linux
  # Report undefined symbols
  set(CMAKE_SHARED_LINKER_FLAGS "-Wl,--no-undefined")
endif()

if(NOT EXISTS ${FIREBASE_CPP_SDK_DIR})
  message(FATAL_ERROR "The Firebase C++ SDK directory does not exist: \
  ${FIREBASE_CPP_SDK_DIR}. See the readme.md for more information")
endif()

# Add the Firebase libraries to the target using the function from the SDK.
add_subdirectory(${FIREBASE_CPP_SDK_DIR} bin/ EXCLUDE_FROM_ALL)

# Windows runtime mode, either MD or MT depending on whether you are using
# /MD or /MT. For more information see:
# https://msdn.microsoft.com/en-us/library/2kzt1wy3.aspx
set(MSVC_RUNTIME_MODE MD)

set(FIREBASE_UNITY_DIR ${CMAKE_CURRENT_SOURCE_DIR})

if(FIREBASE_INCLUDE_UNITY)
  add_external_library(google_unity_jar_resolver)
endif()

add_external_library(minijson)
add_external_library(parse)

# External mono library and dll setup for CSharp projects
add_subdirectory(external)

string(REPLACE "." "_" version_string ${FIREBASE_UNITY_SDK_VERSION})
set(FIREBASE_APP_UNI_VERSIONED "FirebaseCppApp-${version_string}")

# Setup override of the CSharp import library for uni builds
if(FIREBASE_UNI_LIBRARY)
  set(FIREBASE_SWIG_OVERRIDE_IMPORT_MODULE "${FIREBASE_APP_UNI_VERSIONED}")
endif()

# Includes platform which needs to be invoked first
add_subdirectory(app)
set(TARGET_LINK_LIB_NAMES "firebase_app" "firebase_app_swig")
set(PROJECT_LIST_HEADER "#define PROJECT_LIST(X)")
list(APPEND PROJECT_LIST_HEADER "  X(App)")

# Include Firebase editor tools.
if (DESKTOP AND APPLE AND FIREBASE_INCLUDE_EDITOR_TOOL)
  add_subdirectory(editor)
endif()

if (FIREBASE_INCLUDE_ANALYTICS)
  add_subdirectory(analytics)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_analytics" "firebase_analytics_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Analytics)")
endif()
if (FIREBASE_INCLUDE_AUTH)
  add_subdirectory(auth)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_auth" "firebase_auth_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Auth)")
endif()
if (FIREBASE_INCLUDE_CRASHLYTICS AND FIREBASE_INCLUDE_UNITY)
  add_subdirectory(crashlytics)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_crashlytics" "firebase_crashlytics_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Crashlytics)")
endif()
if (FIREBASE_INCLUDE_DATABASE)
  add_subdirectory(database)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_database" "firebase_database_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Database)")
endif()
if (FIREBASE_INCLUDE_DYNAMIC_LINKS)
  add_subdirectory(dynamic_links)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_dynamic_links" "firebase_dynamic_links_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(DynamicLinks)")
endif()
if (FIREBASE_INCLUDE_INSTALLATIONS)
  add_subdirectory(installations)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_installations" "firebase_installations_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Installations)")
endif()
if (FIREBASE_INCLUDE_FIRESTORE)
  add_subdirectory(firestore)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_firestore" "firebase_firestore_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Firestore)")
endif()
if (FIREBASE_INCLUDE_FUNCTIONS)
  add_subdirectory(functions)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_functions" "firebase_functions_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Functions)")
endif()
if (FIREBASE_INCLUDE_MESSAGING)
  add_subdirectory(messaging)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_messaging" "firebase_messaging_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Messaging)")
endif()
if (FIREBASE_INCLUDE_REMOTE_CONFIG)
  add_subdirectory(remote_config)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_remote_config" "firebase_remote_config_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(RemoteConfig)")
endif()
if (FIREBASE_INCLUDE_STORAGE)
  add_subdirectory(storage)
  list(APPEND TARGET_LINK_LIB_NAMES "firebase_storage" "firebase_storage_swig")
  list(APPEND PROJECT_LIST_HEADER "  X(Storage)")
endif()

if(FIREBASE_UNI_LIBRARY)
  if(FIREBASE_IOS_BUILD)
    ios_pack(firebase_lib_uni libFirebaseCppApp DEPS "firebase_app" "firebase_app_swig")
  else()
    build_uni(
      "${TARGET_LINK_LIB_NAMES}"
      PROJECT_LIST_HEADER
    )
  endif()
endif()

if(FIREBASE_INCLUDE_MONO)
  add_subdirectory(samples/mono_app)
endif()

# add release files
add_subdirectory(release_build_files)
