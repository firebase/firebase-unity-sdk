# Copyright 2022 Google LLC
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

add_executable(firestore_cpp_test
  firestore_cpp_test.cc
)

target_link_libraries(firestore_cpp_test
  PUBLIC
    firebase_app
    firebase_firestore
    firebase_firestore_swig
)

# Public C++ headers all refer to each other relative to the src/include
# directory, while private headers are relative to the entire C++ SDK
# directory.
target_include_directories(firestore_cpp_test
  PUBLIC
    ${CMAKE_CURRENT_LIST_DIR}
  PRIVATE
    ${FIREBASE_CPP_SDK_DIR}
    ${FIREBASE_UNITY_DIR}
)

set(FIRESTORE_SOURCE_DIR ${PROJECT_BINARY_DIR}/bin/external/src/firestore)

# Additional public headers come from the Firestore core
target_include_directories(firestore_cpp_test
  PUBLIC
    ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
    ${FIRESTORE_SOURCE_DIR}/Firestore/Protos/nanopb
  PRIVATE
    ${FIRESTORE_SOURCE_DIR}
)
set(FIREBASE_FIRESTORE_CORE_HEADER_DIR
  ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
)

set(firestore_generated_headers_dir
    "${CMAKE_BINARY_DIR}/generated/firestore/src/include/firebase/firestore")

add_dependencies(firestore_cpp_test FIREBASE_FIRESTORE_SWIG_CPP_HEADERS)
