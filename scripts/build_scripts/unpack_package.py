#!/usr/bin/python
#
# Copyright 2021 Google LLC
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

# Lint as: python
"""Unpack Unity SDK package file.

Example usage:
  python scripts/build_scripts/unpack_package.py --folder=<sdk folder 1>
"""

import os
import sys
import subprocess
import shutil

from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string('folder', None,
                    'Directory of unziped SDK')
flags.DEFINE_string("output", "output_unpack",
                    "Output folder for unpacked SDK.")
flags.DEFINE_spaceseplist(
    "dotnet_paths", "dotnet3 dotnet4",
    ("List of subfolders of dotnet selection inside the SDK folder"))

def find_unpack_script():
  """Get the unpack script either from folder passed from arg or download from unity-jar-resolver.

    Returns:
      path of the unpack script. None if not found.
  """
  built_folder_ext = "_unity"
  built_folder_postion = os.path.join("external", "src", "google_unity_jar_resolver")
  built_folder = None
  resolver_root_folder = "unity-jar-resolver"
  for folder in os.listdir("."):
    if folder.endswith(built_folder_ext):
      built_folder = folder
      break
  if built_folder != None:
    resolver_root_folder = os.path.join(built_folder, built_folder_postion)
  elif not os.path.exists(resolver_root_folder):
    git_clone_script = ["git", "clone",
      "--depth", "1",
      "https://github.com/googlesamples/unity-jar-resolver.git"]
    subprocess.call(git_clone_script)

  if resolver_root_folder != None:
    script_path = os.path.join(resolver_root_folder, "source", "ImportUnityPackage", "import_unity_package.py")
    return script_path
  return None

def clean_create_folder(folder):
  if os.path.exists(folder):
    shutil.rmtree(folder)
  os.makedirs(folder)

def unpack_one_package(script, product_path):
  if not os.path.exists(product_path):
    logging.error("Product (%s) doesn't exist.", product_path)
    return
  product_name = os.path.basename(os.path.splitext(product_path)[0])
  dotnet_name = os.path.basename(os.path.split(product_path)[0])
  sdk_name = os.path.basename(os.path.split(os.path.split(product_path)[0])[0])
  unpack_folder = os.path.join(FLAGS.output, sdk_name, dotnet_name + "_" + product_name)
  clean_create_folder(unpack_folder)

  cmd_args = [
      sys.executable,
      script,
      "--projects=" + unpack_folder,
      "--packages=" + product_path,
  ]
  subprocess.call(cmd_args)

  output_path = os.path.join(os.getcwd(), FLAGS.output, sdk_name, "tree_lists")
  if not os.path.exists(output_path):
    os.makedirs(output_path)
  output_file = os.path.join(output_path, dotnet_name+"_"+product_name+".txt")
  if os.path.exists(output_file):
    os.remove(output_file)
  subprocess.run(["tree","-h",unpack_folder,"-o", output_file])
  logging.info("%s is unpacked", product_path)

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')

  if FLAGS.folder == None or not os.path.exists(FLAGS.folder):
    logging.error("SDK folder %s doesn't exist", FLAGS.folder)
    return

  unpack_script_path = find_unpack_script()
  if unpack_script_path == None:
    raise app.UsageError('Cannot find unpack script. Please build the project first.')

  clean_create_folder(FLAGS.output)

  for dotnet_path in FLAGS.dotnet_paths:
    unpack_path = os.path.join(FLAGS.folder, dotnet_path)
    if not os.path.exists(unpack_path):
      logging.error("(%s) not exists", unpack_path)
    else:
      for f in os.listdir(unpack_path):
        product_path = os.path.join(unpack_path, f)
        if os.path.isfile(product_path):
          unpack_one_package(unpack_script_path, product_path)
  logging.info("Unpack is done, please find result in %s", os.path.join(os.getcwd(), FLAGS.output))

if __name__ == '__main__':
  app.run(main)
