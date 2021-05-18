#!/bin/bash
#
# Copyright 2019 Google LLC
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
#
# Builds and runs the tests, meant to be used on a bash environment.

# Stop display commands being run.
set +x

# Set exit code to first failure
FIRST_FAILED_EXITCODE=0

function check_exit_code {
  if [ "$1" -ne "0" ] && [ "$FIRST_FAILED_EXITCODE" -eq 0 ]; then
    FIRST_FAILED_EXITCODE=$1
 fi
}

CMAKE_OPTIONS=

if [ -d "../firebase-cpp-sdk" ]; then
  CMAKE_OPTIONS="-DFIREBASE_CPP_SDK_DIR=`realpath ../firebase-cpp-sdk` "
  cd ../firebase-cpp-sdk
  ./gradlew
  cd ../firebase-unity-sdk
fi

shopt -s nullglob
list=(${ANDROID_HOME}/**/build/cmake/android.toolchain.cmake)
shopt -u nullglob

if [ ! -f "${list[0]}" ]; then
  # Some installations of the NDK have it in NDK/version, instead of just
  # ndk-bundle, and ** sometimes does not recurse correctly, so check that case.
  shopt -s nullglob
  list=(${ANDROID_HOME}/*/*/build/cmake/android.toolchain.cmake)
  shopt -u nullglob

  if [ ! -f "${list[0]}" ]; then
    echo "Failed to find android.toolchain.cmake. Please ensure ANDROID_HOME is set to a valid ndk directory."
    exit -1
  fi
fi

echo "Using android toolchain: ${list[0]}"

CMAKE_OPTIONS="${CMAKE_OPTIONS} -DUNITY_ROOT_DIR=${UNITY_ROOT_DIR}"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DANDROID_NDK=$ANDROID_NDK"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DCMAKE_TOOLCHAIN_FILE=${list[0]}"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DANDROID_ABI=armeabi-v7a"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_ANDROID_BUILD=true"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DCMAKE_BUILD_TYPE=release"

# Display commands being run.
set -x

# Make a directory to work in (if doesn't exist)
mkdir -p android_build

pushd android_build

# Configure cmake with option value
cmake .. ${CMAKE_OPTIONS} -DANDROID_ABI=armeabi-v7a
check_exit_code $?

# Build the SDK
make # -j 8
check_exit_code $?

# Package build output into zip
cpack .
check_exit_code $?

# Stop display commands being run.
set +x

popd

exit $FIRST_FAILED_EXITCODE
