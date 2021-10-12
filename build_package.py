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

flags.DEFINE_spaceseplist(
    "enabled_sections", "build_dotnet3 build_dotnet4 asset_package_only",
    ("List of sections to include in the set of packages. "
     "Package specifications that do not specify any sections are always "
     "included."))


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
  zip_file_list = get_zip_files()

  if not zip_file_list:
    raise app.UsageError("No zip files to process.")

  cmd_args = [
      sys.executable,
      packer_script_path,
      "--assets_dir=" + FLAGS.zip_dir,
      "--config_file=" + config_file_path,
      "--guids_file=" + guids_file_path,
      "--enabled_sections=" + " ".join(FLAGS.enabled_sections),
      "--output_dir=" + FLAGS.output,
  ]
  cmd_args.extend(["--assets_zip=" + zip_file for zip_file in zip_file_list])
  last_version = get_last_version(guids_file_path)
  if last_version:
    cmd_args.append("--plugins_version=" + last_version)

  return subprocess.call(cmd_args)


if __name__ == '__main__':
  app.run(main)
