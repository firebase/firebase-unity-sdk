# Copyright 2021 Google
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

# CMake file for the firebase cpp firestore library

set(firebase_firestore_swig_cpp_src
  document_event_listener.cc
  equality_compare.cc
  hash.cc
  load_bundle_task_progress_callback.cc
  query_event_listener.cc
  snapshots_in_sync_listener.cc
  transaction_manager.cc
)

add_library(firebase_firestore_swig_cpp STATIC
    ${firebase_firestore_swig_cpp_src}
)

set_property(TARGET firebase_firestore_swig_cpp PROPERTY FOLDER "Firebase Cpp")

target_link_libraries(firebase_firestore_swig_cpp
  PUBLIC
    firebase_app
    firebase_firestore
)
# Public C++ headers all refer to each other relative to the src/include
# directory, while private headers are relative to the entire C++ SDK
# directory.
target_include_directories(firebase_firestore_swig_cpp
  PUBLIC
    ${CMAKE_CURRENT_LIST_DIR}
  PRIVATE
    ${FIREBASE_CPP_SDK_DIR}
    ${FIREBASE_UNITY_DIR}
)

set(FIRESTORE_SOURCE_DIR ${PROJECT_BINARY_DIR}/bin/external/src/firestore)

# Additional public headers come from the Firestore core
if(IOS)
  target_include_directories(firebase_firestore_swig_cpp
    PUBLIC
      # The Firestore core on iOS comes via the FirebaseFirestore CocoaPod, and
      # public headers should come from there.
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore/Firestore/core/include
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestoreInternal/Firestore/core/include
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore/Firestore/Protos/nanopb
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestoreInternal/Firestore/Protos/nanopb
    PRIVATE
      # Additionally, the core C++ API is not declared publicly within the
      # FirebaseFirestore pod, so depend on headers available in the source
      # distribution.
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestoreInternal
  )
  set(FIREBASE_FIRESTORE_CORE_HEADER_DIR
    ${FIREBASE_POD_DIR}/Pods/FirebaseFirestoreInternal/Firestore/core/include
  )
else()
  # Desktop and Android get their public headers from the CMake build in
  # firebase-ios-sdk.
  target_include_directories(firebase_firestore_swig_cpp
    PUBLIC
      ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
      ${FIRESTORE_SOURCE_DIR}/Firestore/Protos/nanopb
    PRIVATE
      ${FIRESTORE_SOURCE_DIR}
  )
  set(FIREBASE_FIRESTORE_CORE_HEADER_DIR
    ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
  )
endif()

set(firestore_generated_headers_dir
    "${CMAKE_BINARY_DIR}/generated/firestore/src/include/firebase/firestore")

add_custom_target(
  FIREBASE_FIRESTORE_SWIG_CPP_HEADERS
  COMMAND ${CMAKE_COMMAND} -E make_directory
          "${firestore_generated_headers_dir}"
  COMMAND ${CMAKE_COMMAND} -E copy_directory
          ${FIREBASE_FIRESTORE_CORE_HEADER_DIR}
          "${firestore_generated_headers_dir}"
  COMMAND ${CMAKE_COMMAND} -E echo "COPIED INTERNAL HEADER!"
  COMMENT "Copying internal Firestore headers"
)

add_dependencies(firebase_firestore_swig_cpp FIREBASE_FIRESTORE_SWIG_CPP_HEADERS)

if(IOS)
 # Enable Automatic Reference Counting (ARC).
  set_property(
    TARGET firebase_firestore
    APPEND_STRING PROPERTY
    COMPILE_FLAGS "-fobjc-arc")

  setup_pod_headers(
    firebase_firestore
    POD_NAMES
      FirebaseFirestore
      FirebaseFirestoreInternal
  )
endif()

