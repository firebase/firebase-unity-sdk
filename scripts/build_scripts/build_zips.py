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
  python build_zips.py --platform=macos --apis=auth --targets=firestore
"""
import glob
import os
import shutil
import subprocess
import zipfile
import tempfile
import threading
import sys

from absl import app, flags, logging

SUPPORT_PLATFORMS = ("linux", "macos", "windows", "ios", "tvos", "android")
SUPPORT_TARGETS = [
    "analytics", "app_check", "auth", "crashlytics", "database",
    "firebaseai", "firestore", "functions", "installations", "messaging", "remote_config",
    "storage"
]
TVOS_SUPPORT_TARGETS = [
    "analytics", "app_check", "auth", "crashlytics", "database", "firebaseai", "firestore",
    "functions", "installations", "messaging", "remote_config", "storage"
]
SUPPORT_DEVICE = ["device", "simulator"]

IOS_CONFIG_DICT = {
    "device": {
        "architecture": ["arm64"],
        "ios_platform_location": "iPhoneOS.platform",
        "osx_sysroot": "iphoneos",
    },
    "simulator": {
        "architecture": ["arm64", "x86_64"],
        "ios_platform_location": "iPhoneSimulator.platform",
        "osx_sysroot": "iphonesimulator",
    }
}

TVOS_CONFIG_DICT = {
    "device": {
        "architecture": ["arm64"],
        "ios_platform_location": "AppleTvOS.platform",
        "osx_sysroot": "appletvos",
        "toolchain_platform": "TVOS",
    },
    "simulator": {
        "architecture": ["arm64", "x86_64"],
        "ios_platform_location": "AppleTvSimulator.platform",
        "osx_sysroot": "appletvsimulator",
        "toolchain_platform": "SIMULATOR_TVOS",
    }
}

ANDROID_SUPPORT_ARCHITECTURE = ["armeabi-v7a", "arm64-v8a", "x86", "x86_64"]

MACOS_SUPPORT_ARCHITECTURE = ["x86_64", "arm64"]

g_target_architectures = []
g_target_devices = []
g_cpp_sdk_realpath = ""

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
flags.DEFINE_string(
    "apis", None,
    ("One single string that has multiple targets combined with ','. \n"
     "Usage example is --apis=auth,firestore,remote_config. \n"
     "Exclusive with parameter targets. List items pick from"
     "({})".format(",".join(SUPPORT_TARGETS))))
flags.DEFINE_multi_string(
    "device", None,
    "To build on device or simulator. If not set, built on both. Only take affect for ios, tvos, and android build"
)
flags.DEFINE_multi_string(
    "architecture", None, "Which architectures to build on.\n"
    "For iOS/tvOS device ({}).\n"
    "For iOS/tvOS simulator ({}).\n"
    "For android ({}).\n"
    "For MacOS ({})".format(",".join(IOS_CONFIG_DICT["device"]["architecture"]),
                               ",".join(
        IOS_CONFIG_DICT["simulator"]["architecture"]),
        ",".join(ANDROID_SUPPORT_ARCHITECTURE),
        ",".join(MACOS_SUPPORT_ARCHITECTURE)))
flags.DEFINE_multi_string('cmake_extras', None,
                          "Any extra arguments wants to pass into cmake.")
flags.DEFINE_bool("clean_build", False, "Whether to clean the build folder")
flags.DEFINE_bool("use_boringssl", False, "Build with BoringSSL instead of openSSL.")
flags.DEFINE_bool("verbose", False, "If verbose, cmake build with DCMAKE_VERBOSE_MAKEFILE=1")
flags.DEFINE_string("swig_dir", None, "If pass in swig dir directly rather than find swig by cmake")
flags.DEFINE_bool("gen_documentation_zip", False, "Also generate a zip file containing files to document")
flags.DEFINE_bool("gha", False, "True if the build is triggered by Github Action.")
flags.DEFINE_bool("gen_swig_only", False, "Should it only generate swig, skipping building libraries")

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


def get_cpp_folder_args(source_path):
  """Pure-C# Unity SDK build no longer uses firebase-cpp-sdk."""
  del source_path
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
  
  support_targets = SUPPORT_TARGETS
  if is_tvos_build():
    support_targets = TVOS_SUPPORT_TARGETS
    if not targets:
      targets = TVOS_SUPPORT_TARGETS

  if targets:
    # check if all the entries are valid
    for target in targets:
      if target not in support_targets:
        raise app.UsageError(
            'Wrong target "{}", please pick from {}'.format(
                target, ",".join(support_targets)))
    for target in SUPPORT_TARGETS:
      if target in targets:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=ON")
      else:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=OFF")
  logging.debug("get target args are:" + ",".join(result_args))
  return result_args


def get_ios_or_tvos_args(source_path):
  """Get the cmake args for iOS and tvOS platforms.

    Args:
      source_path: root source folder to find toolchain file.
    Returns:
      cmake args for iOS or tvOS platform.
  """
  del source_path
  result_args = ["-DFIREBASE_IOS_BUILD=ON", "-DIOS=ON"]
  if is_tvos_build():
    result_args.append("-DFIREBASE_TVOS_VARIANT=ON")
  return result_args


def get_android_args():
  """Get the cmake args for android platform specific.

    Returns:
      cmake args for android platform.
  """
  return ["-DFIREBASE_ANDROID_BUILD=true"]

def get_windows_args():
  """Get the cmake args for windows platform specific.

    Returns:
      cmake args for windows platform.
  """
  result_args = []
  result_args.append('-G Visual Studio 17 2022')
  result_args.append('-A x64') # TODO flexibily for x32
  result_args.append("-DFIREBASE_PYTHON_HOST_EXECUTABLE:FILEPATH=%s" % sys.executable)
  if FLAGS.gha:
    result_args.append('-DCMAKE_SYSTEM_VERSION=10.0.20348.0')
  return result_args

def get_macos_args():
  """Get the cmake args for macos platform specific.

    Returns:
      cmake args for macos platform.
  """
  return []

def gen_documentation_zip():
  """If the flag was enabled, builds the zip file containing source files to document.
  """
  if not FLAGS.gen_documentation_zip and not FLAGS.gen_swig_only:
    return
  cpack_args = [
    'cpack',
    '-D', 'CPACK_COMPONENTS_ALL=documentation',
    '-D', 'CPACK_COMPONENTS_ALL_IN_ONE_PACKAGE=ON',
    '-D', 'CPACK_ARCHIVE_FILE_NAME=documentation_sources'
  ]
  subprocess.check_call(cpack_args)

def is_android_build():
  """
    Returns:
      If the build platform is android
  """
  return FLAGS.platform == "android"

def is_ios_build():
  """
    Returns:
      If the build platform is ios
  """
  return FLAGS.platform == "ios"

def is_tvos_build():
  """
    Returns:
      If the build platform is tvos
  """
  return FLAGS.platform == "tvos"

def is_windows_build():
  """
    Returns:
      If the build platform is windows
  """
  return FLAGS.platform == "windows"

def is_macos_build():
  """
    Returns:
      If the build platform is macos
  """
  return FLAGS.platform == "macos"

def is_linux_build():
  """
    Returns:
      If the build platform is linux
  """
  return FLAGS.platform == "linux"


def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  platform = FLAGS.platform
  if platform not in SUPPORT_PLATFORMS:
    raise app.UsageError('Wrong platform "{}", please pick from {}'.format(
        platform, ",".join(SUPPORT_PLATFORMS)))

  source_path = os.getcwd()
  cmake_cpp_folder_args = get_cpp_folder_args(source_path)
  build_path = get_build_path(platform, FLAGS.clean_build)

  os.chdir(build_path)
  cmake_setup_args = [
      "cmake",
      source_path
  ]
  
  if FLAGS.verbose:
    cmake_setup_args.append('-DCMAKE_VERBOSE_MAKEFILE=1')

  if is_windows_build():
    # windows args need to happen right after target path
    cmake_setup_args.extend(get_windows_args())

  cmake_setup_args.extend([
    "-DFIREBASE_INCLUDE_UNITY=ON",
    "-DFIREBASE_UNITY_BUILD_TESTS=ON",
  ])

  if FLAGS.gha:
    cmake_setup_args.append("-DFIREBASE_GITHUB_ACTION_BUILD=ON")

  unity_root_args = get_unity_engine_folder_args(FLAGS.unity_root)
  if unity_root_args:
    cmake_setup_args.append(unity_root_args)
  if cmake_cpp_folder_args:
    cmake_setup_args.append(cmake_cpp_folder_args)
  
  if FLAGS.apis and FLAGS.targets:
    raise app.UsageError('Parameter apis and targets are exclusive, please only pick one.')
  
  api_target_list = []
  if FLAGS.apis:
    api_target_list = FLAGS.apis.split(',')
  elif FLAGS.targets:
    api_target_list = FLAGS.targets
  target_arg_list = get_targets_args(api_target_list)

  if FLAGS.swig_dir:
    cmake_setup_args.append("-DSWIG_DIR="+FLAGS.swig_dir)
  
  if target_arg_list:
    cmake_setup_args.extend(target_arg_list)

  if FLAGS.cmake_extras:
    cmake_setup_args.extend(FLAGS.cmake_extras)

  if is_ios_build() or is_tvos_build():
    cmake_setup_args.extend(get_ios_or_tvos_args(source_path))
  elif is_android_build():
    cmake_setup_args.extend(get_android_args())
  elif is_macos_build():
    cmake_setup_args.extend(get_macos_args())

  if FLAGS.gen_swig_only:
    cmake_setup_args.append("-DFIREBASE_GENERATE_SWIG_ONLY=ON")

  logging.info("cmake_setup_args is: " + " ".join(cmake_setup_args))
  subprocess.check_call(cmake_setup_args)
  if not FLAGS.gen_swig_only:
    if is_windows_build():
      subprocess.check_call(["cmake", "--build", ".", "--config", "Release"])
    else:
      subprocess.check_call(["make"])

    cmake_pack_args = [
      "cpack",
    ]
    if is_windows_build():
      cmake_pack_args.extend(["-C", "Release"])
    cmake_pack_args.append(".")

    subprocess.check_call(cmake_pack_args)
  else:
    subprocess.check_call(["cmake", "--build", ".", "--target", "firebase_swig_targets"])

  gen_documentation_zip()

  os.chdir(source_path)


if __name__ == '__main__':
  flags.mark_flag_as_required("platform")
  app.run(main)
