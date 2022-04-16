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
"""Merge a list of srcaar files into one

Single srcaar file usually looks like this
├── AndroidManifest.xml
├── R.txt
├── classes.jar
├── jni
│   └── x86_64
│       └── libFirebaseCppRemoteConfig.so
├── proguard.txt
└── res

And a merged one will look like this
├── AndroidManifest.xml
├── R.txt
├── classes.jar
├── jni
│   ├── armeabi-v7a
│   │   └── libFirebaseCppRemoteConfig.so
│   └── x86_64
│       └── libFirebaseCppRemoteConfig.so
├── proguard.txt
└── res

Example usage:
  python merge_aar.py --inputs=<srcaar1> --inputs=<srcaar2> --output=<srcaar output>
"""
import os
import zipfile
import tempfile
from absl import app, flags, logging

FLAGS = flags.FLAGS

flags.DEFINE_multi_string(
    "inputs", None,
    "A list of srcaars to merge together.")
flags.DEFINE_string(
    'output', None,
    "Output file location for merged srcaar file."
)


def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  input_srcaars = FLAGS.inputs
  if len(input_srcaars) <= 1:
    raise app.UsageError(
        'Input srcaars needs more than 1 entry, currently only %d.'.format(len(input_srcaars)))
  # temp folder to exact input srcaar files in
  base_temp_dir = tempfile.mkdtemp()
  for input in input_srcaars:
    if os.stat(input).st_size == 0:
      # Ignore empty aar file
      logging.debug("input %s is empty.", input)
      continue
    # Extacting each input srcaar files into the same temp folder
    # For same files that already extracted from previous srcaar file,
    # the extract operation will just keep one copy.
    with zipfile.ZipFile(input) as zip_aar:
      logging.debug("Extracting %s.", input)
      zip_aar.extractall(base_temp_dir)

  # Create the output AAR.
  output_aar_file = FLAGS.output
  if os.path.exists(output_aar_file) and os.path.isfile(output_aar_file):
    # remove the existing srcaar with the output name.
    os.remove(output_aar_file)

  # Write the temp folder as the output srcaar file.
  with zipfile.ZipFile(output_aar_file, "w", allowZip64=True) as zip_file:
    for current_root, folders, filenames in os.walk(base_temp_dir):
      for folder in folders:
        fullpath = os.path.join(current_root, folder)
        zip_file.write(fullpath, os.path.relpath(fullpath, base_temp_dir))
      for filename in filenames:
        fullpath = os.path.join(current_root, filename)
        zip_file.write(fullpath, os.path.relpath(fullpath, base_temp_dir))
  logging.debug("Archived directory %s to %s", base_temp_dir, output_aar_file)


if __name__ == '__main__':
  flags.mark_flag_as_required("inputs")
  flags.mark_flag_as_required("output")
  app.run(main)
