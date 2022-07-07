# Copyright 2016 Google LLC
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
"""Groups scattered swig generated classes under a single class.
"""

import os
import re

from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string("csharp_dir", None,
                    "Path with generated csharp proxy classes to be processed.")

NAMESPACE_RE = re.compile(r"^\s*namespace\s+(.*)\s+{\s*$", re.MULTILINE)


class NamespaceException(Exception):
  pass


class TargetMismatchException(Exception):
  pass


class TargetMissingException(Exception):
  pass


def reparent_files(target_class_filename, extra_files_list):
  """Moves the implemenation of C# classes, structs, and enums.

  SWIG is hard-coded to extract all classes in the c++ headers to separate
  files, and instead we want those classes to be nested under the created
  target class which contains otherwise global functions and members.
  This provides better isolated scope that's more natural for C# APIs in many
  cases.

  Here we'll parse all of the C# ".cs" files produced from the swig generation,
  and strip off the surrounding namespace:
  namespace Firebase {
    // Capture from here...
    public class Goodies {
    }
    // To here.
  }

  If there is no outter namespace, we'll assume the whole file contains the
  class.

  Then, we'll put all of these classes at the end of the module class which swig
  generates to contain any global scope functions and variables (or the target
  class if the module is intended to be empty).

  Depending on whether or not we expect a namespace, we can simply count
  trailing }'s to find out where to insert everything, because C# files should
  always just contain one top level class.

  Args:
    target_class_filename: This is the name of the target file to be modified.
    extra_files_list: This is a list of filenames that should be absorbed
      into the target_class_filename.

  Raises:
    NamespaceException: This is raised if there are namespaces in any of the
      files that are inconsistent with each other.
  """
  target_extras = ""
  common_namespace = ""

  for cs_filename in extra_files_list:
    with open(cs_filename, "r") as cs_file:
      file_buffer = cs_file.read()

    # cut off the surrounding brackets from the namespace, if there is one.
    ns_start = file_buffer.find("{")
    match = NAMESPACE_RE.search(file_buffer[:ns_start+1])
    if match:
      namespace_name = match.groups()[0]
      common_namespace = common_namespace or namespace_name
      if common_namespace != namespace_name:
        raise NamespaceException(
            "Inconsistent namespace in file %s vs %s.  Expected %s found %s" %
            (cs_filename, str(extra_files_list), common_namespace,
             namespace_name))

      file_buffer = file_buffer[ns_start+1:file_buffer.rfind("}")]

    # Split into lines to indent everything one level.
    file_buffer = "\n".join(["  " + line for line in file_buffer.splitlines()])
    target_extras += file_buffer + "\n"

  with open(target_class_filename, "r") as target_class_file:
    target_class = target_class_file.read()

  match = NAMESPACE_RE.search(target_class[:target_class.find("{")+1])
  if match:
    namespace_name = match.groups()[0]
    common_namespace = common_namespace or namespace_name
    if common_namespace != namespace_name:
      raise NamespaceException(
          "Inconsistent namespace in file %s vs %s.  Expected %s found %s" %
          (target_class_filename, str(extra_files_list), common_namespace,
           namespace_name))

    end_namespace_pos = target_class.rfind("}")
  else:
    end_namespace_pos = len(target_class)

  end_class_pos = target_class.rfind("}", 0, end_namespace_pos)

  target_class = (target_class[:end_class_pos] +
                  target_extras +
                  target_class[end_class_pos:])

  with open(target_class_filename, "w") as target_class_file:
    target_class_file.write(target_class)

  # We'll cleanup here, in a second pass, so that there's less risk of losing
  # content if things break.
  for cs_filename in extra_files_list:
    os.remove(cs_filename)


def main(unused_argv):
  """Moves swig generated classes to a common container class.

  The .csproj filename is used to find the .cs file (with the same name) which
  will contain all of the other SWIG generated classes.

  Args:
    unused_argv: Extra arguments not consumed by the config flags.

  Returns:
    The exit code status; 1 for error, 0 for success.

  Raises:
    TargetMismatchException: This is raised if the .csproj name does not match
    the basename of a .cs file in the folder.  For example, App.csproj must have
    an App.cs file present.
    TargetMissingException: This is raised if files are missing that are used
    for determining the target container class.
  """
  cs_dir = FLAGS.csharp_dir
  # Get all of the files in the proxy dir.
  files = [f for f in os.listdir(cs_dir)
           if not os.path.isdir(os.path.join(cs_dir, f))]

  # Find the name of the target file by finding the .csproj file.
  # Find the name of the moudle file by looking for the PINVOKE file.
  module_name = ""
  target_name = ""
  for f in files:
    filename, extension = os.path.splitext(f)
    if extension == ".csproj":
      target_name = filename

    if filename.endswith("PINVOKE"):
      module_name = filename[:-7]

  if not target_name:
    raise TargetMissingException(
        "No \".csproj\" file found in the csharp_dir.")

  if not module_name:
    raise TargetMissingException(
        "No \"*PINVOKE.cs\" file found in the csharp_dir.")

  # Now remove the target name related files, and what's left should all be
  # classes, enums, and structs stripped out of the C++ API, we want to fix.
  if (target_name + ".cs") not in files:
    raise TargetMismatchException(
        ("%s.cs does not exist.\n"
         "Make sure that the -n argument of build_plugin.sh (currently:%s) "
         "matches either the %%module property in your SWIG file or a class "
         "name you're exporting and want to be the primary interface." %
         (target_name, target_name)))

  files.remove(target_name + ".csproj")
  files.remove(target_name + ".cs")
  files.remove(module_name + "PINVOKE.cs")
  files.remove("AssemblyInfo.cs")

  logging.info(("The contents of the following files %s are being moved to "
                "%s.cs."), str(files), target_name)

  # Make the list into full paths.
  paths = [os.path.join(FLAGS.csharp_dir, f) for f in files]

  if paths:
    reparent_files(os.path.join(FLAGS.csharp_dir, target_name + ".cs"),
                   paths)

    with open(os.path.join(FLAGS.csharp_dir, target_name + ".csproj"),
              "r+") as csproj:
      csproj_lines = csproj.readlines()
      csproj.seek(0)
      for line in csproj_lines:
        if not any([i in line for i in files]):
          csproj.write(line)
      csproj.truncate()

    # For the files moved, update the PInvoke file so that references to the
    # classes as parameters include the target class with the path.
    classes = []
    for f in files:
      base, ext = os.path.splitext(f)
      if ext == ".cs":
        classes.append(base)
    class_re = re.compile(r"((%s)( |\.\S* )[a-zA-Z_][a-zA-Z_0-9]*)" %
                          "|".join(classes))
    replacement = target_name + r".\1"

    with open(os.path.join(FLAGS.csharp_dir, module_name +"PINVOKE.cs"),
              "r+") as pinvoke:
      pinvoke_lines = pinvoke.readlines()
      pinvoke.seek(0)
      for line in pinvoke_lines:
        line = class_re.sub(replacement, line)
        pinvoke.write(line)
      pinvoke.truncate()

  return 0

if __name__ == "__main__":
  flags.mark_flag_as_required("csharp_dir")
  app.run(main)
