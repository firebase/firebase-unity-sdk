# Copyright 2016 Google LLC
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
"""Extract comments from C++ classes and inject comments into C# classes.

Basic Usage: swig_commenter.py -i <cpp_sources> -o <cs_sources>

In SWIG, methods can have comments prepened to them by using the feature
%csmethodmodifiers. No analogous feature exists for classes or enums however,
so this script inserts the C++ comments into the C# files directly.

Currently this assumes that the C++ classes and C# classes have the same name.
If this changes in the future this script will have to be updated.
"""

import re
import sys

from absl import app
from absl import flags

FLAGS = flags.FLAGS
flags.DEFINE_spaceseplist(
    'input',
    None,
    'The C++ source files to scan for class comments.',
    short_name='i')
flags.DEFINE_spaceseplist(
    'output',
    None,
    'The C# source files which will have comments inserted.',
    short_name='o')
flags.DEFINE_string(
    'namespace_prefix', '',
    'The prefix used to converted the C++ namespace to a C# class.')

# A regex to extract a C++ enum name from a line.
CPP_ENUM_DECLARATION_REGEX = re.compile(r'^\s*enum\s*(?:class\s*)?(\w+)\s*{')

# A regex to extract a C++ enum value identifier from a line. In other words,
# given the line `  kFoo = 1,` this will extract "kFoo".
CPP_ENUM_VALUE_IDENTIFIER_REGEX = re.compile(r'^\s*(\w+).*$')

# A regex to extract a C++ class or struct name from a line.
CPP_CLASS_DECLARATION_REGEX = re.compile(r'^\s*(?:class|struct) (\w+).*$')

# A regex to extract a C++ namespace name from a line.
CPP_NAMESPACE_DECLARATION_REGEX = re.compile(r'^\s*namespace (\w+).*$')

# A regex to extract a C-style global string identifier from a line. This is
# useful as in some cases these get translated into properties on C# classes.
CPP_GLOBAL_STRING_REGEX = re.compile(
    r'^(?:static )?\s*const\s+char\s*\*\s*(?:const)?\s+(\w+)')

# A regex to extract a C# namespace name from a line.
# b/36068888: we should support symbol renaming properly
CSHARP_CLASS_DECLARATION_REGEX = re.compile(
    r'^\s*(?:public|private) .*(?:class|struct) (?:Firebase)?(\w+).*$')

# A regex to extract a C# enum name from a line.
CSHARP_ENUM_DECLARATION_REGEX = re.compile(
    r'^\s*(?:public|private) enum (\w+).*$')

# A regex to extract a C# enum value identifier from a line. This happens to be
# the same for both C++ and C#.
CSHARP_ENUM_VALUE_IDENTIFIER_REGEX = CPP_ENUM_VALUE_IDENTIFIER_REGEX

# A regex to extract a C# property from a line.
CSHARP_PROPERTY_REGEX = re.compile(r'public (?:static)? string (\w+) {')


def snake_case_to_camel_case(string):
  """Converts a snake case string to a camel case string."""
  result = ''
  capitalize = True
  for c in string:
    if c == '_':
      capitalize = True
    else:
      result += c.upper() if capitalize else c
      capitalize = False
  return result


def indentation(line):
  """Returns the number of leading whitespace characters."""
  return len(line) - len(line.lstrip())


def correct_comment_indentation(comment, indent):
  """Ensure each line of a given block of text has the correct indentation."""
  result = ''
  for line in comment.splitlines():
    result += ' ' * indent + line.lstrip() + '\n'
  return result


def extract_regex_captures(captures, regex, string):
  """Fills `captures` with the matches of `regex` in `string`.

  This is a convenience function that populates a list with all the matching
  substrings of the given regex in the given string and returns if there were
  any matches. This is useful for when there are cascading if/elif statements
  and you want to find the first regex that matches.

  Args:
    captures: A list to populate with matches, if any.
    regex: The regex to match against.
    string: The string to run the regex over.
  Returns:
    Whether or not there were any matches.
  """
  del captures[:]
  matches = regex.search(string)
  if matches:
    captures.extend(list(matches.groups()))
  return matches is not None


def scan_input_file(comments, input_filename):
  """Scan a file for comments prepended to classes, structs and namespaces.

  Scans each line of the given file, looking for block of Doxygen comments (that
  is, lines that start with '///') which appear immediately before a class,
  struct or namespace declaration. Each match is stored in the given dict using
  the name identifier as the key and the comment as the value.

  Namespaces are handled slightly differently. Because our namespaces in C++
  map to classes in C# (but with different casing rules) we convert the
  namespace name from snake_case to CamelCase.

  Args:
    comments: A dict mapping C# class names to the comments that should be
        prepended to them.
    input_filename: The name of the file to scan for comments.
  """
  with open(input_filename, 'r') as input_file:
    most_recent_comment = []
    most_recent_enum = None
    for line in input_file:
      captures = []
      parsing_comment = False
      if line.strip().startswith('///'):
        most_recent_comment.append(line)
        parsing_comment = True
      elif line.strip().startswith('//'):
        # Filter out non-doxygen comment lines.
        pass
      elif extract_regex_captures(captures, CPP_CLASS_DECLARATION_REGEX, line):
        class_name = captures[0]
        # This handles replacing forward declarations with no docs.
        comments['classes'][class_name] = comments['classes'].get(
            class_name, '') or ''.join(most_recent_comment)
      elif extract_regex_captures(captures, CPP_NAMESPACE_DECLARATION_REGEX,
                                  line):
        # The C++ namespace is converted to a C# class.
        class_name = FLAGS.namespace_prefix + snake_case_to_camel_case(
            captures[0])
        comments['classes'][class_name] = ''.join(most_recent_comment)
      elif extract_regex_captures(captures, CPP_ENUM_DECLARATION_REGEX, line):
        enum_name = captures[0]
        most_recent_enum = {
            'comment': ''.join(most_recent_comment),
            'values': {}
        }
        comments['enums'][enum_name] = most_recent_enum
      elif extract_regex_captures(captures, CPP_GLOBAL_STRING_REGEX, line):
        property_name = captures[0]
        comments['properties'][property_name] = ''.join(most_recent_comment)
      elif most_recent_enum and '};' in line:
        most_recent_enum = None
      elif most_recent_enum and extract_regex_captures(
          captures, CPP_ENUM_VALUE_IDENTIFIER_REGEX, line):
        identifier = captures[0]
        most_recent_enum['values'][identifier] = ''.join(most_recent_comment)
      if not parsing_comment:
        most_recent_comment = []


def process_output_file(output_filename, comments):
  """Annontates classes in the given file with the Doxygen comments.

  Scan a file for classes, and if those class_names appear as keys in the given
  class_comments dict, prepend those comments to the class (accounting for
  indentation)

  Args:
    output_filename: The name of the file to insert comments into.
    comments: A dict mapping C# class names to the comments that should be
        prepended to them.
  """
  with open(output_filename, 'r') as output_file:
    file_content = output_file.readlines()

  try:
    with open(output_filename, 'w') as output_file:
      most_recent_enum = None
      for line in file_content:
        captures = []
        if most_recent_enum:
          if extract_regex_captures(captures,
                                    CSHARP_ENUM_VALUE_IDENTIFIER_REGEX, line):
            enum_identifier = captures[0]
            comment = most_recent_enum['values'].get(enum_identifier)
            if comment:
              output_file.write(
                  correct_comment_indentation(comment, indentation(line)))

          elif '}' in line:
            most_recent_enum = None

        else:
          if extract_regex_captures(captures, CSHARP_CLASS_DECLARATION_REGEX,
                                    line):
            class_name = captures[0]
            # b/36068888: we should support symbol renaming properly
            comment = comments['classes'].get(class_name) or comments[
                'classes'].get('Firebase' + class_name)
            if comment:
              output_file.write(
                  correct_comment_indentation(comment, indentation(line)))

          elif extract_regex_captures(captures, CSHARP_ENUM_DECLARATION_REGEX,
                                      line):
            enum_name = captures[0]
            enum_entry = comments['enums'].get(enum_name)
            if enum_entry:
              most_recent_enum = enum_entry
              output_file.write(
                  correct_comment_indentation(enum_entry['comment'],
                                              indentation(line)))

          elif extract_regex_captures(captures, CSHARP_PROPERTY_REGEX, line):
            property_name = captures[0]
            comment = comments['properties'].get(property_name)
            if comment:
              output_file.write(
                  correct_comment_indentation(comment, indentation(line)))

        output_file.write(line)

  except IOError:
    sys.stderr.write('Could not open %s for writing' % output_filename)


def main(unused_argv):
  """Extract comments from C++ classes and inject comments into C# classes.

  Scans the list of input files for the Doxygen comments that preceed classes,
  structs and namespaces, and then inserts those comments before the appropriate
  C# classes and structs.
  """
  comments = {
      'classes': {},
      'enums': {},
      'properties': {},
  }
  for input_filename in FLAGS.input:
    scan_input_file(comments, input_filename)

  for output_filename in FLAGS.output:
    process_output_file(output_filename, comments)


if __name__ == '__main__':
  flags.mark_flag_as_required('input')
  flags.mark_flag_as_required('output')
  app.run(main)
