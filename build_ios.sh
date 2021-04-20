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

# Enable utf8 output
export LANG=en_US.UTF-8

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
  REAL_PATH=`python -c "import os; print(os.path.realpath('../firebase-cpp-sdk'))"`
  CMAKE_OPTIONS="-DFIREBASE_CPP_SDK_DIR=$REAL_PATH "
fi

CMAKE_OPTIONS="${CMAKE_OPTIONS}-DUNITY_ROOT_DIR=${UNITY_ROOT_DIR}"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_UNITY_BUILD_TESTS=ON"

printf "#################################################################\n"
date
printf "Building config 'unity' on platform 'ios' with option ''.\n"
printf "#################################################################\n"

DIR=ios_unity

# Display commands being run.
set -x

# Make a directory to work in (if doesn't exist)
mkdir -p "$DIR"

pushd "$DIR"

  # Configure cmake with option value
  cmake -DCMAKE_TOOLCHAIN_FILE=../cmake/unity_ios.cmake .. ${CMAKE_OPTIONS}
  check_exit_code $?

  # Build the SDK
  # using make -j <nprocs> is having some issues where the build hangs
  # and continues on pressing return only to stop sometime later.
  # Disabling parallel builds for now.
  # TODO: Enable parallel builds after finding the reason
  make
  check_exit_code $?

  # Package build output into zip
  cpack .
  check_exit_code $?

  # Stop display commands being run.
  set +x

popd

exit $FIRST_FAILED_EXITCODE
