# Copyright 2018 Google Inc. All rights reserved.
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

set(CMAKE_SYSTEM_NAME "iOS")
# In order to build for both device and simulator, this needs to be empty,
# or the CMAKE_OSX_DEPLOYMENT_TARGET setting below is not handled correctly.
# Note, this seems to cause an issue with repeating builds on some cmake versions,
# the long term fix will likely be to not handle both device and simulator with
# the same toolchain, and instead merge the libraries after the fact.
set(CMAKE_OSX_SYSROOT "")
set(CMAKE_OSX_ARCHITECTURES "arm64;x86_64" CACHE STRING "")
set(CMAKE_XCODE_EFFECTIVE_PLATFORMS "-iphoneos;-iphonesimulator")
set(IOS_PLATFORM_LOCATION "iPhoneOS.platform;iPhoneSimulator.platform")

set(CMAKE_IOS_INSTALL_UNIVERSAL_LIBS "YES")
set(CMAKE_XCODE_ATTRIBUTE_ONLY_ACTIVE_ARCH "NO")
set(CMAKE_XCODE_ATTRIBUTE_CODE_SIGNING_REQUIRED "NO")
set(CMAKE_XCODE_ATTRIBUTE_ENABLE_BITCODE "YES")
set(CMAKE_OSX_DEPLOYMENT_TARGET "8.0" CACHE STRING "")

# skip TRY_COMPILE checks
set(CMAKE_CXX_COMPILER_WORKS TRUE)
set(CMAKE_C_COMPILER_WORKS TRUE)

set(FIREBASE_IOS_BUILD ON CACHE BOOL "")
set(IOS ON CACHE BOOL "")
