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

usage() {
    echo "Usage: $0 [options]
 options:
   -t, CMake target            default: ""
   -c, cmake extra             default: ""
 example: 
   build_macos.sh or build_linux.sh -t auth,firestore -c -DCMAKE_BUILD_TYPE=Debug"
}

readonly SUPPORTED_TARGETS=(analytics auth database dynamic_links firestore functions installations messaging remote_config storage)

# build default value
build_target=""
cmake_extra=""

# Enable utf8 output
export LANG=en_US.UTF-8

# check options
IFS=',' # split options on ',' characters
while getopts "ht:c:" opt; do
    case $opt in
    h)
        usage
        exit 0
        ;;
    t)
        build_target=($OPTARG)
        for target in ${build_target[@]}; do
            if [[ ! " ${SUPPORTED_TARGETS[@]} " =~ " ${target} " ]]; then
                echo "invalid target: ${target}"
                echo "Supported target are: ${SUPPORTED_TARGETS[@]}"
                exit 2
            fi
        done
        ;;
    c)
        cmake_extra=$OPTARG
        ;;
    *)
        echo "unknown parameter"
        exit 2
        ;;
    esac
done

CMAKE_OPTIONS=

if [ -d "../firebase-cpp-sdk" ]; then
  CMAKE_OPTIONS="-DFIREBASE_CPP_SDK_DIR=`python -c "import os;print(os.path.realpath('../firebase-cpp-sdk'))"` "
fi

CMAKE_OPTIONS="${CMAKE_OPTIONS} -DUNITY_ROOT_DIR=${UNITY_ROOT_DIR}"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_UNITY_BUILD_TESTS=ON"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_CPP_BUILD_STUB_TESTS=ON" # enable a stub gtest target to get abseil-cpp working.
CMAKE_OPTIONS="${CMAKE_OPTIONS} ${cmake_extra}"

if (( ${#build_target[@]} )); then
  CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_INCLUDE_LIBRARY_DEFAULT=OFF"
  for target in ${build_target[@]}; do
    uppertarget=$(echo $target | tr '[:lower:]' '[:upper:]')
    CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_INCLUDE_$uppertarget=ON"
  done
fi

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

  echo "*********************** Build $PLATFORM *******************************"
  date
  echo "build target: ${build_target[@]}"
  echo "cmake extras: ${cmake_extra}"
  echo "extra options $EXTRA_OPTIONS" 
  echo "***************************************************************************"

  DIR=${PLATFORM}_${NAME}

  # Display commands being run.
  set -x

  # Make a directory to work in (if doesn't exist)
  mkdir -p "$DIR"

  pushd "$DIR"

    # Configure cmake with option value
    cmake .. ${CMAKE_OPTIONS} ${EXTRA_OPTIONS}
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
