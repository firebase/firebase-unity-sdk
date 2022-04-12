#!/usr/bin/python
#
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
"""Build SDK for certain platform into zip file


Example usage:
  python build_zips.py --platform=macos --targets=auth --targets=firestore
"""
import os
import re
import subprocess
import shutil

from absl import app
from absl import flags
from absl import logging

SUPPORT_PLATFORMS = ("linux", "macos", "windows", "ios", "android")
SUPPORT_TARGETS = [
    "analytics", "auth", "crashlytics", "database", "dynamic_links",
    "firestore", "functions", "installations", "messaging", "remote_config",
    "storage"
]
SUPPORT_DEVICE = ["device", "simulator"]

IOS_SUPPORT_ARCHITECTURE = ["arm64", "armv7", "x86_64", "i386"]
IOS_DEVICE_ARCHITECTURE = ["arm64", "armv7"]
IOS_SIMULATOR_ARCHITECTURE = ["arm64", "x86_64", "i386"]

IOS_CONFIG_DICT = {
    "device": {
        "architecture": ["arm64", "armv7"],
        "ios_platform_location": "iPhoneOS.platform",
        "osx_sysroot": "iphoneos",
    },
    "simulator": {
        "architecture": ["arm64", "x86_64", "i386"],
        "ios_platform_location": "iPhoneSimulator.platform",
        "osx_sysroot": "iphonesimulator",
    }
}

ANDROID_SUPPORT_ARCHITECTURE = ["armeabi-v7a", "arm64-v8a", "x86", "x86_64"]

FLAGS = flags.FLAGS
flags.DEFINE_string(
    'platform', None,
    'Which platform to build SDK on. Required one entry from ({})'.format(
        ",".join(SUPPORT_PLATFORMS)))
flags.DEFINE_string(
    'unity_root', None,
    "The root dir for Unity Engine. If not set, cmake will try to guess in the default unity installation location."
)
flags.DEFINE_multi_string(
    "targets", None,
    ("Target product to includes in the build. List items pick from"
     "({})".format(",".join(SUPPORT_TARGETS))))
flags.DEFINE_multi_string(
    "device", None,
    "To build on device or simulator. If not set, built on both. Only take affect for ios and android build"
)
flags.DEFINE_multi_string(
    "architecture", None, "Which architectures in build on.\n"
    "For iOS device ({}).\n"
    "For iOS simulator ({}).\n"
    "For android ({}).".format(",".join(IOS_CONFIG_DICT["device"]["architecture"]),
                               ",".join(
        IOS_CONFIG_DICT["simulator"]["architecture"]),
        ",".join(ANDROID_SUPPORT_ARCHITECTURE)))
flags.DEFINE_multi_string('cmake_extras', None,
                          "Any extra arguments wants to pass into cmake.")
flags.DEFINE_bool("clean_build", False, "Whether to clean the build folder")


def get_build_path(platform, clean_build=False):
  """Get the folder that cmake configure and build in.

    Args:
      platform: linux, macos, windows, ios, android.
      clean_build: If True, delete the build folder and build from clean.

    Returns:
      The folder path to build sdk inside.
  """
  platform_path = os.path.join(os.getcwd(), platform + "_unity")
  if os.path.exists(platform_path) and clean_build:
    shutil.rmtree(platform_path)
  if not os.path.exists(platform_path):
    os.makedirs(platform_path)
  return platform_path


def get_cpp_folder_args():
  """Get the cmake args to pass in local Firebase C++ SDK folder.
    If not found, will download from Firebase C++ git repo.
    
    Returns:
      cmake args with the folder path of local Firebase C++ SDK. 
      Empty string if not found.
  """
  cpp_folder = os.path.join(os.getcwd(), "..", "firebase-cpp-sdk")
  if os.path.exists(cpp_folder):
    return "-DFIREBASE_CPP_SDK_DIR=" + os.path.realpath(cpp_folder)
  else:
    return ""


def get_unity_engine_folder_args(unity_root):
  """Get the cmake args to pass in Unity engine folder. If not passed in 
     through the parameter, cmake will try to find using logic in 
     cmake/FindUnity.cmake

    Args:
      unity_root: folder path of the Unity Engine.
    Returns:
      camke args with the folder path of Unity Engine. Empty string if not set.
  """
  if unity_root and os.path.exists(unity_root):
    return "-DUNITY_ROOT_DIR=" + unity_root
  else:
    return ""


def get_targets_args(targets):
  """Get the cmake args to pass in built targets of Firebase products.

    Args:
      targets: list of target names defined in SUPPORT_TARGETS.
    Returns:
      camke args included targets.
  """
  result_args = []
  if targets:
    # check if all the entries are valid
    for target in targets:
      if target not in SUPPORT_TARGETS:
        raise app.UsageError(
            'Wrong target "{}", please pick from {}'.format(
                target, ",".join(SUPPORT_TARGETS)))
    for target in SUPPORT_TARGETS:
      if target in targets:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=ON")
      else:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=OFF")
  logging.debug("get target args are:" + ",".join(result_args))
  return result_args


def get_ios_args(source_path):
  """Get the cmake args for iOS platform specific.

    Args:
      source_path: root source folder to find toolchain file.
    Returns:
      camke args for iOS platform.
  """
  result_args = []
  toolchain_path = os.path.join(source_path, "cmake", "unity_ios.cmake")
  # toolchain args is required
  result_args.append("-DCMAKE_TOOLCHAIN_FILE=" + toolchain_path)
  # check device input
  if FLAGS.device:
    for device in FLAGS.device:
      if device not in SUPPORT_DEVICE:
        raise app.UsageError(
            'Wrong device type {}, please pick from {}'.format(
                device, ",".join(SUPPORT_DEVICE)))
    devices = FLAGS.device
  else:
    devices = SUPPORT_DEVICE

  # check architecture input
  if (len(devices) > 1):
    archs_to_check = IOS_SUPPORT_ARCHITECTURE
  else:
    archs_to_check = IOS_CONFIG_DICT[devices[0]]["architecture"]
  if FLAGS.architecture:
    for arch in FLAGS.architecture:
      if arch not in archs_to_check:
        raise app.UsageError(
            'Wrong architecture "{}" for device type {}, please pick from {}'.format(
                arch, ",".join(devices), ",".join(archs_to_check)))
    archs = FLAGS.architecture
  else:
    archs = archs_to_check

  if len(archs) != len(IOS_SUPPORT_ARCHITECTURE):
    # Need to override only if the archs are not default
    result_args.append("-DCMAKE_OSX_ARCHITECTURES=" + ";".join(archs))

  if len(devices) != len(SUPPORT_DEVICE):
    # Need to override if only passed in device or simulator
    result_args.append("-DCMAKE_OSX_SYSROOT=" +
                       IOS_CONFIG_DICT[devices[0]]["osx_sysroot"])
    result_args.append("-DCMAKE_XCODE_EFFECTIVE_PLATFORMS=" +
                       "-"+IOS_CONFIG_DICT[devices[0]]["osx_sysroot"])
    result_args.append("-DIOS_PLATFORM_LOCATION=" +
                       IOS_CONFIG_DICT[devices[0]]["ios_platform_location"])
  return result_args


def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  platform = FLAGS.platform
  if platform not in SUPPORT_PLATFORMS:
    raise app.UsageError('Wrong platform "{}", please pick from {}'.format(
        platform, ",".join(SUPPORT_PLATFORMS)))

  cmake_cpp_folder_args = get_cpp_folder_args()
  build_path = get_build_path(platform, FLAGS.clean_build)

  source_path = os.getcwd()

  os.chdir(build_path)
  cmake_setup_args = [
      "cmake",
      "..",
      "-DFIREBASE_INCLUDE_UNITY=ON",
      "-DFIREBASE_UNITY_BUILD_TESTS=ON",
      "-DFIREBASE_CPP_BUILD_STUB_TESTS=ON",
  ]

  unity_root_args = get_unity_engine_folder_args(FLAGS.unity_root)
  if unity_root_args:
    cmake_setup_args.append(unity_root_args)
  if cmake_cpp_folder_args:
    cmake_setup_args.append(cmake_cpp_folder_args)

  target_arg_list = get_targets_args(FLAGS.targets)
  if target_arg_list:
    cmake_setup_args.extend(target_arg_list)

  if FLAGS.cmake_extras:
    cmake_setup_args.extend(FLAGS.cmake_extras)

  if platform == "ios":
    cmake_setup_args.extend(get_ios_args(source_path))

  logging.info("cmake_setup_args is: " + " ".join(cmake_setup_args))

  subprocess.call(cmake_setup_args)
  subprocess.call("make")

  cmake_pack_args = [
      "cpack",
      ".",
  ]
  subprocess.call(cmake_pack_args)

  os.chdir(source_path)


if __name__ == '__main__':
  flags.mark_flag_as_required("platform")
  app.run(main)
