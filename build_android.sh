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
# Builds the Android version of the Firebase Unity SDK.

help() {
  echo "Usage: $(basename "$0") -a arch_list -c cmake_extra -d -h
Builds the Android SDK, possibly merging multiple architectures.
-a arch_list
  Space separated list of Android architectures to build for.
  These are passed to CMake with the ANDROID_ABI flag.
  If not provided (and -m not used), defaults to (armeabi-v7a).
-c cmake_extra
  Additional flags to pass to the CMake configure step.
-d
  Use the default set of architecture flags,
    armeabi-v7a arm64-v8a x86 x86_64
  If set, the -a flag is ignored.
-h
  Display this help message.
" >&1
  exit 1
}

main() {
  local -a arch_list=(armeabi-v7a)
  local -a cmake_extra=
  local build_default=false
  while getopts "a:c:dh" option "$@"; do
    case "${option}" in
      a ) arch_list="${OPTARG}";;
      c ) cmake_extra="${OPTARG}";;
      d ) build_default=true;;
      h ) help;;
      * ) help;;
    esac
  done

  
  if $build_default; then
    arch_list=(armeabi-v7a arm64-v8a x86 x86_64)
  fi

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

  if [[ -z "${ANDROID_NDK_HOME}" ]]; then #ANDROID_NDK_HOME not set
    echo "Using ANDROID_NDK_HOME: ${ANDROID_NDK_HOME} android tool chain"
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
  else
    echo "Using ANDROID_NDK_HOME: ${ANDROID_NDK_HOME} android tool chain"
    shopt -s nullglob
    list=(${ANDROID_NDK_HOME}/build/cmake/android.toolchain.cmake)
    shopt -u nullglob
  fi
  echo "Using android toolchain: ${list[0]}"

  if [ -n "$UNITY_ROOT_DIR" ]; then
    CMAKE_OPTIONS="${CMAKE_OPTIONS} -DUNITY_ROOT_DIR=${UNITY_ROOT_DIR}"
  fi
  CMAKE_OPTIONS="${CMAKE_OPTIONS} -DANDROID_NDK=$ANDROID_NDK_HOME"
  CMAKE_OPTIONS="${CMAKE_OPTIONS} -DCMAKE_TOOLCHAIN_FILE=${list[0]}"
  CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_ANDROID_BUILD=true"
  CMAKE_OPTIONS="${CMAKE_OPTIONS} -DCMAKE_BUILD_TYPE=release"

  # Make a directory to work in (if doesn't exist)
  mkdir -p android_build

  pushd android_build

  multi_arch=false
  path_up=".."
  if [ "${#arch_list[@]}" -gt 1 ]; then
    multi_arch=true
    path_up="../.."
  fi
  for arch in "${arch_list[@]}"; do
    # If building more than one architecture, move into a subdirectory
    if $multi_arch; then
      mkdir -p "${arch}"
      pushd "${arch}"
    fi

    # Configure cmake with option value
    cmake $path_up ${CMAKE_OPTIONS} -DANDROID_ABI="${arch}" ${cmake_extra}
    check_exit_code $?

    # Build the SDK
    make -j 8
    check_exit_code $?

    # Package build output into zip
    cpack .
    check_exit_code $?

    if $multi_arch; then
      popd
    fi
  done

  # If building for multiple architectures, merge the results
  if $multi_arch; then
    local -r unzip_dir="$(mktemp -d)"
    local -r remove_temp_dir="rm -rf \"${unzip_dir}\""
    trap "${remove_temp_dir}" SIGKILL SIGTERM SIGQUIT EXIT

    zip_basename=
    srcaar_list=
    for arch in "${arch_list[@]}"; do
      zip_name=$(find ${arch} -name '*Android.zip' -depth 1)

      if [ -z "${zip_basename}" ]; then
        # If this is the first srcaar being handled, unzip everything
        # to the primary unzip_dir.
        unzip -q -d "${unzip_dir}" "${zip_name}"

        # Get the list of srcaar files, to merge into later
        srcaar_list=$(find ${unzip_dir} -type f -name '*.srcaar')
        zip_basename=$(basename ${zip_name})
      else
        # Unzip the other architectures into a different temp directory
        tmp_dir="$(mktemp -d)"
        echo "TMPDIR:  ${tmp_dir}"
        trap "rm -rf ${tmp_dir}" SIGKILL SIGTERM SIGQUIT EXIT
        unzip -q -d "${tmp_dir}" "${zip_name}" *.srcaar
        for srcaar in "${srcaar_list[@]}"; do
          # Get the path to the new srcaar that matches the original
          srcaar_name=$(basename ${srcaar})
          echo "Looking for ${srcaar_name}"
          new_srcaar=$(find ${tmp_dir} | grep ${srcaar_name})
          # Merge into the first srcaar
          ../aar_builder/merge_aar.sh -i "${srcaar} ${new_srcaar}" -o ${srcaar}
        done
      fi
    done

    # Zip up the result into a new zipfile with the correct name
    final_zip="$(pwd)/${zip_basename}"
    pushd ${unzip_dir}
    zip -q -X -r ${final_zip} .
    popd
  fi

  # popd android_build
  popd

  exit $FIRST_FAILED_EXITCODE
}

main "$@"
