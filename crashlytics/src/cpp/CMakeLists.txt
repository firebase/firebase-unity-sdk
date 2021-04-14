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

# CMake file for the firebase cpp crashlytics library

set(firebase_crashlytics_cpp_ios
  ios/CrashlyticsiOSWrapper.m
  ios/CrashlyticsiOSWrapper.h
  ios/Crashlytics_PrivateHeaders/Crashlytics_Platform.h
)

set(firebase_crashlytics_cpp_android
  android/crashlytics_android.cc
  android/crashlytics_android.h
)

set(firebase_crashlytics_cpp_stub
  stub/crashlytics_stub.cc
  stub/crashlytics_stub.h
)

set(firebase_crashlytics_cpp_src
  common/crashlytics.cc
  include/firebase/crashlytics.h
)

if(IOS)
  list(APPEND firebase_crashlytics_cpp_src
    ${firebase_crashlytics_cpp_ios}
    ${firebase_crashlytics_cpp_stub}
  )
elseif(ANDROID)
  list(APPEND firebase_crashlytics_cpp_src ${firebase_crashlytics_cpp_android})
else()
  list(APPEND firebase_crashlytics_cpp_src ${firebase_crashlytics_cpp_stub})
endif()

add_library(firebase_crashlytics STATIC
    ${firebase_crashlytics_cpp_src}
)

set_property(TARGET firebase_crashlytics PROPERTY FOLDER "Firebase Cpp")

target_link_libraries(firebase_crashlytics firebase_app)

target_include_directories(firebase_crashlytics
  PUBLIC
    ${CMAKE_CURRENT_LIST_DIR}/include
  PRIVATE
    ${FIREBASE_CPP_SDK_DIR}
    ${FIREBASE_UNITY_DIR}
)

target_compile_definitions(firebase_crashlytics
  PRIVATE
    -DINTERNAL_EXPERIMENTAL=1
)

if(IOS)
 # Enable Automatic Reference Counting (ARC).
  set_property(
    TARGET firebase_crashlytics
    APPEND_STRING PROPERTY
    COMPILE_FLAGS "-fobjc-arc")

  setup_pod_headers(
    firebase_crashlytics
    POD_NAMES
      FirebaseCrashlytics
  )

  # Crashlytics pods use 'iOS' dir instead of 'Frameworks' dir
  symlink_pod_headers(firebase_crashlytics Crashlytics CrashlyticsiOS
    FRAMEWORK_DIR_NAME iOS
  )
  symlink_pod_headers(firebase_crashlytics Fabric FabriciOS
    FRAMEWORK_DIR_NAME iOS
  )
endif()
