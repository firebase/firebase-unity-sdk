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
"""Combines some base files and the given files into an aar file."""

import os
import zipfile
from absl import app
from absl import flags

FLAGS = flags.FLAGS

flags.DEFINE_string("output_file", None, "Location of the output aar to write")
flags.DEFINE_string("library_file", None,
                    "Location of the library file to include in the aar")
flags.mark_flag_as_required("library_file")
flags.DEFINE_string("architecture", None,
                    "The build architecture of the given library")
flags.mark_flag_as_required("architecture")
flags.DEFINE_string("proguard_file", None,
                    "Location of the proguard file to include in the aar")
flags.mark_flag_as_required("proguard_file")


def main(unused_argv):
  output_file = FLAGS.output_file
  if not output_file:
    output_file = "generated.srcaar"
  else:
    output_file = os.path.normcase(output_file)
  library_file = os.path.normcase(FLAGS.library_file)
  library_name = os.path.basename(library_file)
  proguard_file = os.path.normcase(FLAGS.proguard_file)

  file_dir = os.path.dirname(os.path.realpath(__file__))

  # Delete the aar file, if it already exists
  if os.path.exists(output_file):
    os.remove(output_file)

  with zipfile.ZipFile(output_file, "w") as myzip:
    # Write the generic base files that are required in an aar file.
    myzip.write(
        os.path.join(file_dir, "AndroidManifest.xml"), "AndroidManifest.xml")
    myzip.write(os.path.join(file_dir, "classes.jar"), "classes.jar")
    myzip.write(os.path.join(file_dir, "R.txt"), "R.txt")
    myzip.writestr("res/", "")
    # Write the provided library to the proper architecture, and proguard file.
    myzip.write(library_file, "jni/" + FLAGS.architecture + "/" + library_name)
    myzip.write(proguard_file, "proguard.txt")


if __name__ == "__main__":
  app.run(main)
