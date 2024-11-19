# Copyright 2024 Google LLC
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

"""Convert C++ headers of Analytics constants to C# files."""

import datetime
import os
import re
import subprocess

from absl import app
from absl import flags

FLAGS = flags.FLAGS

# Required args
flags.DEFINE_string('cpp_header', '', 'C++ header file containing a '
                    'set of constants to convert to C#.')
flags.register_validator(
    'cpp_header',
    lambda x: x and os.path.exists(x),
    message=('Must reference an existing C++ header file'))
flags.DEFINE_string('csharp_file', '', 'Full path of the C# file to write '
                    'out.')
flags.register_validator(
    'csharp_file', lambda x: x, message='Output C# file must be specified')

CPP_NAMESPACE = 'namespace analytics'

DOC_REPLACEMENTS = [
  ('static const char\*const k', 'public static string ')
]


def main(unused_argv):
  """Convert the C++ file into C#, going line-by-line to edit it."""
  with open(FLAGS.cpp_header) as input_file:
    with open(FLAGS.csharp_file, 'w') as output_file:
      # Write the initial lines at the top
      output_file.write('// Copyright %s Google LLC\n\n' %
                        str(datetime.date.today().year))
      output_file.write('namespace Firebase.Analytics {\n\n')
      output_file.write('public static partial class FirebaseAnalytics {\n\n')

      found_namespace = False
      for line in input_file:
        line = line.rstrip()

        # Ignore everything in the C++ file until inside the namespaces
        if not found_namespace:
          if re.search(CPP_NAMESPACE, line):
            found_namespace = True
          continue
        # Stop copying when finding the next namespace (we assume it is closing)
        if re.search(CPP_NAMESPACE, line):
          break

        for replace_from, replace_to in DOC_REPLACEMENTS:
          if (re.search(replace_from, line)):
            line = re.sub(replace_from, replace_to, line)
        output_file.write(line + '\n')

      # Write the lines at the end
      # Close the class
      output_file.write('}\n\n')
      # close the namespace
      output_file.write('}\n')

  return 0

if __name__ == '__main__':
  app.run(main)
