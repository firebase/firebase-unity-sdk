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

usage() {
    echo "Usage: $0 [options]
 options:
   -b, build path              default: ios_unity
   -s, source path             default: .
   -p, framework platform      default: ${SUPPORTED_PLATFORMS[@]}
   -a, framework architecture  default: ${SUPPORTED_ARCHITECTURES[@]}
   -c, cmake extra             default: ""
 example: 
   build_scripts/ios/build.sh -b ios_build -s . -a arm64"
}

set -e

readonly SUPPORTED_PLATFORMS=(device simulator)
readonly SUPPORTED_ARCHITECTURES=(arm64 armv7 x86_64 i386)
readonly DEVICE_ARCHITECTURES=(arm64 armv7)
readonly SIMULATOR_ARCHITECTURES=(arm64 x86_64 i386)
readonly DEVICE_IOS_PLATFORM_LOCATION="iPhoneOS.platform"
readonly SIMULATOR_IOS_PLATFORM_LOCATION="iPhoneSimulator.platform"
readonly DEVICE_OSX_SYSROOT="iphoneos"
readonly SIMULATOR_OSX_SYSROOT="iphonesimulator"

# build default value
buildpath="ios_unity"
sourcepath="."
platforms=("${SUPPORTED_PLATFORMS[@]}")
architectures=("${SUPPORTED_ARCHITECTURES[@]}")
cmake_extra=""

# Enable utf8 output
export LANG=en_US.UTF-8

# check options
IFS=',' # split options on ',' characters
while getopts "hb:s:p:a:c:" opt; do
    case $opt in
    h)
        usage
        exit 0
        ;;
    b)
        buildpath=$OPTARG
        ;;
    s)
        sourcepath=$OPTARG
        if [[ ! -d "${sourcepath}" ]]; then
            echo "Source path ${sourcepath} not found."
            exit 2
        fi
        ;;
    p)
        platforms=($OPTARG)
        for platform in ${platforms[@]}; do
            if [[ ! " ${SUPPORTED_PLATFORMS[@]} " =~ " ${platform} " ]]; then
                echo "invalid platform: ${platform}"
                echo "Supported platforms are: ${SUPPORTED_PLATFORMS[@]}"
                exit 2
            fi
        done
        ;;
    a)
        architectures=($OPTARG)
        for arch in ${architectures[@]}; do
            if [[ ! " ${SUPPORTED_ARCHITECTURES[@]} " =~ " ${arch} " ]]; then
                echo "invalid architecture: ${arch}"
                echo "Supported architectures are: ${SUPPORTED_ARCHITECTURES[@]}"
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
echo "*********************** Build Unity iOS SDK *******************************"
echo "build path: ${buildpath}"
echo "source path: ${sourcepath}"
echo "build platforms: ${platforms[@]}"
echo "architectures: ${architectures[@]}"
echo "cmake extras: ${cmake_extra}"
echo "***************************************************************************"
sourcepath=$(cd ${sourcepath} && pwd)                        #full path
buildpath=$(mkdir -p ${buildpath} && cd ${buildpath} && pwd) #full path

ios_location=""
osx_sysroot=""
xcode_platforms=""
cmake_archs=""
# build necessary cmake args
if [[ " ${platforms[@]} " =~ " device " ]]; then
    echo "Build cmake args for device"
    ios_location=${DEVICE_IOS_PLATFORM_LOCATION}
    osx_sysroot=${DEVICE_OSX_SYSROOT}
    xcode_platforms="-${DEVICE_OSX_SYSROOT}"
    for arch in ${architectures[@]}; do
        if [[ " ${DEVICE_ARCHITECTURES[@]} " =~ " ${arch} " ]]; then
            if [ "$cmake_archs" == "" ]; then
                cmake_archs=${arch}
            else
                cmake_archs="${cmake_archs};${arch}"
            fi
        fi
    done
fi

if [[ " ${platforms[@]} " =~ " simulator " ]]; then
    echo "Build cmake args for simulator"
    if [ "$ios_location" == "" ]; then
        ios_location=${SIMULATOR_IOS_PLATFORM_LOCATION}
    else
        ios_location="${ios_location};${SIMULATOR_IOS_PLATFORM_LOCATION}"
    fi
    if [ "$osx_sysroot" == "" ]; then
        osx_sysroot=${SIMULATOR_OSX_SYSROOT}
    else
        osx_sysroot="${osx_sysroot};${SIMULATOR_OSX_SYSROOT}"
    fi
    if [ "$xcode_platforms" == "" ]; then
        xcode_platforms="-${SIMULATOR_OSX_SYSROOT}"
    else
        xcode_platforms="${xcode_platforms};-${SIMULATOR_OSX_SYSROOT}"
    fi
    
    for arch in ${architectures[@]}; do
        if [[ " ${SIMULATOR_ARCHITECTURES[@]} " =~ " ${arch} " ]]; then
            if [ "$cmake_archs" == "" ]; then
                cmake_archs=${arch}
            else
                if [[ ! " ${DEVICE_ARCHITECTURES[@]} " =~ " ${arch} " ]]; then
                    cmake_archs="${cmake_archs};${arch}"
                fi
            fi
        fi
    done
fi

echo "*********************** CMake Args *******************************"
echo "ios_location: ${ios_location}"
echo "osx_sysroot: ${osx_sysroot}"
echo "xcode_platforms: ${xcode_platforms}"
echo "cmake_archs: ${cmake_archs}"
echo "***************************************************************************"

# Stop display commands being run.
set +x

# Set exit code to first failure
FIRST_FAILED_EXITCODE=0

function check_exit_code {
    if [ "$1" -ne "0" ] && [ "$FIRST_FAILED_EXITCODE" -eq 0 ]; then
        FIRST_FAILED_EXITCODE=$1
    fi
}

CMAKE_OPTIONS="-DFIREBASE_UNITY_BUILD_TESTS=ON"
CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_CPP_BUILD_STUB_TESTS=ON" # enable a stub gtest target to get abseil-cpp working.

if [ -d "../firebase-cpp-sdk" ]; then
    REAL_PATH=$(python -c "import os; print(os.path.realpath('../firebase-cpp-sdk'))")
    CMAKE_OPTIONS="${CMAKE_OPTIONS} -DFIREBASE_CPP_SDK_DIR=$REAL_PATH"
fi

# Display commands being run.
set -x

# Make a directory to work in (if doesn't exist)
mkdir -p "$buildpath"

pushd "$buildpath"

# Configure cmake with option value
cmake $sourcepath \
    -DCMAKE_TOOLCHAIN_FILE=$sourcepath/cmake/unity_ios.cmake \
    -DCMAKE_OSX_ARCHITECTURES=$cmake_archs \
    -DCMAKE_OSX_SYSROOT=$osx_sysroot \
    -DCMAKE_XCODE_EFFECTIVE_PLATFORMS=$xcode_platforms \
    -DIOS_PLATFORM_LOCATION=$ios_location \
    -DUNITY_ROOT_DIR=${UNITY_ROOT_DIR} \
    $CMAKE_OPTIONS \
    $cmake_extra
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

exit $FIRST_FAILED_EXITCODE
