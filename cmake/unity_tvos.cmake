# Copyright 2022 Google Inc. All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

message(STATUS "gcc found at: ${CMAKE_C_COMPILER}")
message(STATUS "g++ found at: ${CMAKE_CXX_COMPILER}")
message(STATUS "Using iOS SDK: ${CMAKE_OSX_SYSROOT}")

set(CMAKE_SYSTEM_NAME "tvOS")

set(CMAKE_OSX_SYSROOT "")
set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "")
set(CMAKE_XCODE_EFFECTIVE_PLATFORMS "-appletvos;-tvsimulator")
set(IOS_PLATFORM_LOCATION "iPhoneOS.platform;iPhoneSimulator.platform")
set(IOS_PLATFORM_LOCATION "AppleTvOS.platform;AppleTvSimulator.platform")

set(PLATFORM_INT "${PLATFORM}")
if(PLATFORM_INT STREQUAL "TVOS")
  set(SDK_NAME appletvos)
  set(ARCHS arm64)
  set(APPLE_TARGET_TRIPLE_INT aarch64-apple-tvos)
  set(CMAKE_XCODE_ATTRIBUTE_ARCHS[sdk=appletvos*] "arm64")
elseif(PLATFORM_INT STREQUAL "SIMULATOR_TVOS")
  set(SDK_NAME appletvsimulator)
  set(ARCHS x86_64)
  set(APPLE_TARGET_TRIPLE_INT x86_64-apple-tvos)
  set(CMAKE_XCODE_ATTRIBUTE_ARCHS[sdk=appletvsimulator*] "x86_64")
endif()

if(NOT DEFINED ENABLE_ARC)
  message(STATUS "ARC not defined, enabling by default")
  set(ENABLE_ARC TRUE)  
endif()

set(ENABLE_ARC_INT ${ENABLE_ARC} CACHE BOOL "Whether or not to enable ARC" FORCE)
set(XCODE_IOS_PLATFORM tvos)

set(CMAKE_IOS_INSTALL_UNIVERSAL_LIBS "YES")
set(CMAKE_XCODE_ATTRIBUTE_ONLY_ACTIVE_ARCH "NO")
set(CMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_REQUIRED "NO")
set(CMAKE_XCODE_ATTRIBUTE_ENABLE_BITCODE "NO")
set(CMAKE_OSX_DEPLOYMENT_TARGET "10.1" CACHE STRING "")

# skip TRY_COMPILE checks
set(CMAKE_CXX_COMPILER_WORKS TRUE)
set(CMAKE_C_COMPILER_WORKS TRUE)

set(FIREBASE_IOS_BUILD ON CACHE BOOL "")
set(IOS ON CACHE BOOL "")
set(FIREBASE_TVOS_VARIANT ON CACHE BOOL "")
