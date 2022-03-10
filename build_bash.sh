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
  CMAKE_OPTIONS="-DFIREBASE_CPP_SDK_DIR=`python -c "import os;print(os.path.realpath('../firebase-cpp-sdk'))"` "
fi

CMAKE_OPTIONS="${CMAKE_OPTIONS} -DUNITY_ROOT_DIR=${UNITY_ROOT_DIR}"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_UNITY_BUILD_TESTS=ON"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_CPP_BUILD_STUB_TESTS=ON" # enable a stub gtest target to get abseil-cpp working.

# TODO: Fix mono build to not need unity deps
build_options=(
  "unity:-DFIREBASE_INCLUDE_UNITY=ON"
#  "mono:-DFIREBASE_INCLUDE_MONO=ON"
#  "unity_separate:-DFIREBASE_INCLUDE_UNITY=ON -DFIREBASE_UNI_LIBRARY=OFF"
#  "mono_separate:-DFIREBASE_INCLUDE_MONO=ON -DFIREBASE_UNI_LIBRARY=OFF"
)

for option in "${build_options[@]}" ; do
  NAME=${option%%:*}
  EXTRA_OPTIONS=${option#*:}

  printf "#################################################################\n"
  date
  printf "Building config '%s' on platform '%s' with option '%s'.\n" \
    "$NAME" "$PLATFORM" "$EXTRA_OPTIONS"
  printf "#################################################################\n"

  DIR=${PLATFORM}_${NAME}

  # Display commands being run.
  set -x

  # Make a directory to work in (if doesn't exist)
  mkdir -p "$DIR"

  pushd "$DIR"

    # Configure cmake with option value
    cmake .. ${CMAKE_OPTIONS} ${EXTRA_OPTIONS} "$@"
    check_exit_code $?

    # Build the SDK
    make
    check_exit_code $?

    # Package build output into zip
    cpack .
    check_exit_code $?

    # Stop display commands being run.
    set +x

  popd
done

exit $FIRST_FAILED_EXITCODE
