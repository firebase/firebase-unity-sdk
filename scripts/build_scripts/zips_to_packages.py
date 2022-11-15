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

"""Collects built zips(built by build_zips.py) under current directory
and packages them into Unity Packages using build_packages.py.

Example usage:
  python zips_to_packages.py --ouput unity_packages
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

FLAGS = flags.FLAGS
flags.DEFINE_string(
    'output', 'unity_packages',
    'Relative directory to save the generated unity packages')

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  output = FLAGS.output

  if os.path.exists(output):
    shutil.rmtree(output)
  if not os.path.exists(output):
    os.makedirs(output)
  logging.info("Ready to build Unity packages to {}".format(output))
	
  zip_temp_dir = tempfile.mkdtemp()

  try:
    candidates = glob.glob('./*_unity/firebase_unity*.zip')
    for candidate in candidates:
      shutil.copy(candidate, zip_temp_dir)

    if len(candidates) > 0:
      logging.info("Found zip files:\n {}".format("\n".join(candidates)))
      subprocess.call(["python", "scripts/build_scripts/build_package.py", 
			  "--zip_dir", zip_temp_dir, "-output", output])
  finally:
    shutil.rmtree(zip_temp_dir)
	
if __name__ == '__main__':
  app.run(main)
