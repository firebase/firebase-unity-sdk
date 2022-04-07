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
import subprocess
import shutil

from absl import app
from absl import flags
from absl import logging

SUPPORT_PLATFORMS = ("linux", "macos", "windows", "ios", "android")
SUPPORT_TARGETS = ["analytics","auth","crashlytics","database","dynamic_links","firestore","functions","installations","messaging","remote_config","storage"]
FLAGS = flags.FLAGS
flags.DEFINE_string('platform', None,
                    'Which platform to build SDK on. Required one entry from ({})'.format(",".join(SUPPORT_PLATFORMS)))
flags.DEFINE_string('unity_root', None,
                    "The root dir for Unity Engine. If not set, cmake will try to guess in the default unity installation location.")
flags.DEFINE_multi_string("targets", None,
                    ("Target product to includes in the build. List items pick from"
                    "({})".format(",".join(SUPPORT_TARGETS))))
flags.DEFINE_multi_string("device", None,
                    "To build on device or simulator or both, only take affect for ios and android build")

flags.DEFINE_multi_string("architecture", None,
                    "Which architectures in build on"
                    "For iOS device ()"
                    "For iOS simulator ()"
                    "For android device ()"
                    "For android simulator ()")
flags.DEFINE_multi_string('cmake_extras', None,
                    "Any extra arguments wants to pass into cmake.")
flags.DEFINE_bool("clean_build", False, "Whether to clean the build folder")

def get_build_path(platform, clean_build=False):
  platform_path = os.path.join(os.getcwd(), platform+"_unity")
  if os.path.exists(platform_path) and clean_build:
    shutil.rmtree(platform_path)
  if not os.path.exists(platform_path):
    os.makedirs(platform_path)
  return platform_path

def get_cpp_folder_args():
  cpp_folder = os.path.join(os.getcwd(), "..", "firebase-cpp-sdk")
  if os.path.exists(cpp_folder):
    return "-DFIREBASE_CPP_SDK_DIR="+os.path.realpath(cpp_folder)
  else:
    return ""

def get_unity_engine_folder_args(unity_root):
  if unity_root and os.path.exists(unity_root):
    return "-DUNITY_ROOT_DIR="+unity_root
  else:
    return ""

def get_targets_args(targets):
  result_args = []
  if targets:
    # check if all the entries are valid
    for target in targets:
      if target not in SUPPORT_TARGETS:
        raise app.UsageError('Wrong target "{}", please pick from {}'.format(target, ",".join(SUPPORT_TARGETS)))
    for target in SUPPORT_TARGETS:
      if target in targets:        
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper()+"=ON")
      else:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper()+"=OFF")
  logging.debug("get target args are:" + ",".join(result_args))
  return result_args

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  platform = FLAGS.platform
  if platform not in SUPPORT_PLATFORMS:
    raise app.UsageError('Wrong platform "{}", please pick from {}'.format(platform, ",".join(SUPPORT_PLATFORMS)))

  cmake_cpp_folder_args=get_cpp_folder_args()
  build_path = get_build_path(platform, FLAGS.clean_build)

  execution_folder = os.getcwd()
  logging.info("Current exection folder is:" + execution_folder)

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

  logging.debug("cmake_setup_args is: " + " ".join(cmake_setup_args))

  subprocess.call(cmake_setup_args)
  subprocess.call("make")

  cmake_pack_args = [
      "cpack",
      ".",
  ]
  subprocess.call(cmake_pack_args)

  os.chdir(execution_folder)

if __name__ == '__main__':
  flags.mark_flag_as_required("platform")
  app.run(main)