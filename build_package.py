#!/usr/bin/python
#
# Copyright 2019 Google LLC
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

# Lint as: python2
"""Packages zip files from unity builds into unity packages.

Takes packages produced from the unity sdk builds i.e.:
  * firebase_unity-opensource-unknown-Linux.zip
  * firebase_unity-opensource-unknown-Windows.zip
  * firebase_unity-opensource-unknown-Mac.zip

And converts them into unity packages i.e.:
  * FirebaseApp.unitypackage
  * FirebaseAuth.unitypackage
  * ...

Example usage:
  python build_package.py --zip_dir=<assets_zip_dir>
"""
import collections
import json
import os
import sys
import subprocess
import zipfile
import shutil

from github import Github
from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string('zip_dir', None,
                    'Directory of zip files from build output to package')
flags.DEFINE_string("config_file", "exports.json",
                    ("Config file that describes how to "
                     "pack the unity assets."))
flags.DEFINE_string("guids_file", "guids.json",
                    "Json file with stable guids cache.")

flags.DEFINE_string("script_folder", "unity_packer",
                    "Json file with stable guids cache.")

flags.DEFINE_string("output", "output",
                    "Output folder for unitypackage.")

flags.DEFINE_string("unity_sdk_version", None,
                    "If None, use the version that reads from guid.json."
                    "If not None, will check and upgrad cmake/firebase_unity_version.cmake")

flags.DEFINE_boolean("output_upm", False, "Whether output packages as tgz for"
    "Unity Package Manager.")

def get_zip_files():
  """Get all zip files from FLAGS.zip_dir.

    Returns:
      list of zip file paths.
  """
  if not os.path.exists(FLAGS.zip_dir):
    logging.error("Asset zip dir(%s) doesn't exist.", FLAGS.zip_dir)
    return []

  zip_file_paths = []
  for zip_file in os.listdir(FLAGS.zip_dir):
    zip_path = os.path.join(os.getcwd(), FLAGS.zip_dir, zip_file)
    if zipfile.is_zipfile(zip_path):
      zip_file_paths.append(zip_path)

  return zip_file_paths

def get_last_version(guids_file):
  """Get the last version number in guids file if exists.

    Args:
      guids_file: the file to look the version in.

    Returns:
      version number.
  """
  json_dict = None
  with open(guids_file, "rt") as json_file:
    try:
      json_dict = json.load(json_file,
                             object_pairs_hook=collections.OrderedDict)
    except ValueError as error:
      raise ValueError("Failed to load JSON file %s (%s)" % (guids_file,
                                                             str(error)))
  if json_dict:
    key_list = list(json_dict.keys())
    key_list.sort(key=lambda s: list(map(int, s.split('.'))))
    return key_list[-1]

def find_pack_script():
  """Get the pack script either from intermediate build folder or download from unity-jar-resolver.

    Returns:
      path of the pack script. None if not found.
  """
  built_folder_ext = "_unity"
  built_folder_postion = os.path.join("external", "src", "google_unity_jar_resolver")
  built_folder = None
  resolver_root_folder = None
  for folder in os.listdir("."):
    if folder.endswith(built_folder_ext):
      built_folder = folder
      break
  if built_folder != None:
    resolver_root_folder = os.path.join(built_folder, built_folder_postion)
  else:
    git_clone_script = ["git", "clone",
      "--depth", "1",
      "https://github.com/googlesamples/unity-jar-resolver.git"]
    subprocess.call(git_clone_script)
    resolver_root_folder = "unity-jar-resolver"

  if resolver_root_folder != None:
    script_path = os.path.join(resolver_root_folder, "source", "ExportUnityPackage", "export_unity_package.py")
    return script_path
  return None

def get_latest_repo_tag(repo_url):
  repo = Github().get_repo(repo_url)
  tags = sorted(repo.get_tags(), key=lambda t: t.last_modified)
  return tags[0].name

def get_ios_pod_version_from_cpp():
  pod_path = os.path.join(os.getcwd(), "..", "firebase-cpp-sdk", "ios_pod", "Podfile")
  if not os.path.exists(pod_path):
    return None
  with open(pod_path, "r") as f:
    datafile = f.readlines()
    for line in datafile:
      if 'Firebase/Core' in line:
        result = line.split()
        return result[-1].strip("\'")

def update_unity_version(unity_sdk_version):
  version_cmake_path = os.path.join(os.getcwd(), "cmake", "firebase_unity_version.cmake")
  replacement = ""
  with open(version_cmake_path, "r") as f:
    datafile = f.readlines()
    for line in datafile:
      if "FIREBASE_UNITY_SDK_VERSION" in line:
        newline = "set(FIREBASE_UNITY_SDK_VERSION \"" + unity_sdk_version + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_IOS_POD_VERSION" in line:
        podversion = get_ios_pod_version_from_cpp()
        newline = "set(FIREBASE_IOS_POD_VERSION \"" + podversion + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_UNITY_JAR_RESOLVER_VERSION" in line:
        jar_version = get_latest_repo_tag('googlesamples/unity-jar-resolver')
        jar_version = jar_version.lstrip("v") # jar resolver need to strip "v" from the tag
        newline = "set(FIREBASE_UNITY_JAR_RESOLVER_VERSION \"" + jar_version + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_CPP_SDK_PRESET_VERSION" in line:
        cpp_version = get_latest_repo_tag('firebase/firebase-cpp-sdk')
        newline = "set(FIREBASE_CPP_SDK_PRESET_VERSION \"" + cpp_version + "\""
        replacement = replacement + newline + "\n"
      else:
        replacement = replacement + line
  
  with open(version_cmake_path, "w") as fout:
    fout.write(replacement)

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')

  packer_script_path = find_pack_script()
  if packer_script_path == None:
    raise app.UsageError('Cannot find pack script. Please build the project first.')
   
  packer_script_path = os.path.join(os.getcwd(), packer_script_path)
  config_file_path = os.path.join(os.getcwd(), FLAGS.script_folder,
                                  FLAGS.config_file)
  guids_file_path = os.path.join(os.getcwd(), FLAGS.script_folder,
                                 FLAGS.guids_file)

  last_version = None
  if FLAGS.unity_sdk_version != None:
    last_version = FLAGS.unity_sdk_version
    update_unity_version(last_version)
    return # testing
  else:
    last_version = get_last_version(guids_file_path)

  zip_file_list = get_zip_files()

  if not zip_file_list:
    raise app.UsageError("No zip files to process.")

  output_folder = os.path.join(os.getcwd(), FLAGS.output)
  if os.path.exists(output_folder):
    shutil.rmtree(output_folder)

  cmd_args = [
      sys.executable,
      packer_script_path,
      "--assets_dir=" + FLAGS.zip_dir,
      "--config_file=" + config_file_path,
      "--guids_file=" + guids_file_path,
      "--output_dir=" + output_folder,
      "--output_upm=" + str(FLAGS.output_upm),
  ]
  cmd_args.extend(["--assets_zip=" + zip_file for zip_file in zip_file_list])
  
  if last_version:
    cmd_args.append("--plugins_version=" + last_version)

  if FLAGS.output_upm:
    cmd_args.append("--enabled_sections=build_dotnet4")
    cmd_args.append("--output_unitypackage=False")
  else:
    cmd_args.append("--enabled_sections=build_dotnet3 build_dotnet4")

  return subprocess.call(cmd_args)


if __name__ == '__main__':
  app.run(main)
