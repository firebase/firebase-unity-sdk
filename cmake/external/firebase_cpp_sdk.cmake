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

include(ExternalProject)

if(TARGET firebase_cpp_sdk OR NOT DOWNLOAD_FIREBASE_CPP_SDK)
  return()
endif()

if("${FIREBASE_CPP_GIT_REPO}" STREQUAL "")
  set(FIREBASE_CPP_GIT_REPO "https://github.com/firebase/firebase-cpp-sdk")
endif()

if("${FIREBASE_CPP_SDK_VERSION}" STREQUAL "")
  set(FIREBASE_CPP_SDK_VERSION ${FIREBASE_CPP_SDK_PRESET_VERSION})
endif()

ExternalProject_Add(
  firebase_cpp_sdk

  GIT_REPOSITORY ${FIREBASE_CPP_GIT_REPO}
  GIT_TAG        ${FIREBASE_CPP_SDK_VERSION}
  GIT_SHALLOW    1

  PREFIX ${PROJECT_BINARY_DIR}

  CONFIGURE_COMMAND ""
  BUILD_COMMAND ""
  INSTALL_COMMAND ""
  TEST_COMMAND ""
)