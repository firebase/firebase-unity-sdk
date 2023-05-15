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
"""Convert enums from C naming convention to C# naming convention.

We have multiple cases where we need to run post-process fixes to SWIG generated
output, so this provides a single place to base whole file edits in-place, using
Python. Each process can be defined in it's own class so that it can keep
internal state, which might be useful for doing multiple passes.
"""

import os
import re

from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string('csharp_dir', None,
                    'Path with generated csharp files to be scrubbed.')
flags.DEFINE_string('cpp_dir', None,
                    'Path with generated C++ files to fix up.')
flags.DEFINE_string('dll_import', None,
                    'Library name to use in DllImport statements. '
                    'If this is unspecified DllImport statements are not '
                    'modified.')
flags.DEFINE_string('module_name_prefix', None,
                    'Prefix to add to generated C methods.')
flags.DEFINE_string('exception_module_name', None,
                    'Name of the module to check for pending exceptions.')
flags.DEFINE_list('exception_ignore_path_prefixes', [],
                  'List of file path prefixes to ignore when replacing the '
                  'name of the module that checks for pending exceptions.')
flags.DEFINE_boolean('fix_sealed_classes', True,
                     'Remove the virtual modifier from sealed class methods '
                     'and make protected members private.')
flags.DEFINE_boolean('rename_async_methods', True,
                     'Rename all methods that return Task to *Async().')
flags.DEFINE_boolean('snake_to_camel_case_args', True,
                     'Rename method arguents from snake_case to camelCase.')
flags.DEFINE_boolean('internal_visibility', True,
                     'Change visibility to internal for all methods and '
                     'properties that end with "Internal".')


class SWIGPostProcessingInterface(object):
  """An interface for objects that scrub code generated from SWIG."""

  def __call__(
      self, file_str, filename, iteration):  # pylint: disable=unused-argument
    """Modify the contents of a file as a string and return the result.

    This is executed on each file and then repeated until a pass over all the
    files has no effect for this process.

    Args:
      file_str:  This is a string containing the file contents to be processed.
      filename:  This is the full path of the file being processed.
      iteration: Pass number (0-based) for how many times this process has run
                 on this file. The process runs until no changes are made.

    Returns:
      Returns the modified file_str.
    """
    return file_str


class SWIGEnumPostProcessing(SWIGPostProcessingInterface):
  """A class to scrub swig generated code and convert enums from C++ to C#."""

  def __init__(self):
    """Initializes this instance."""
    self.enum_mapping = {}

  def _replace_enum_lines(self, enum_lines, enum_name):
    """Internal. Processes lines inside the enum, and saves the mapping."""
    def _cache_replace(match):
      """Convert enum lines, and save mappings from old to new enum names."""
      self.enum_mapping[match.group('old')] = match.group('new')
      return match.group('space') + match.group('new') + match.group('end')

    # ^\s* avoids anything inside comments by restricting to the first non space
    #   text on the line.
    #   Note: This will still break if using block style comments and the first
    #   word starts with 'k'. In practice all of our  comments are led by ///.
    # The named group "old" matches at least a k, and then optionally the enum
    #   identifier after. For example kInitResultSuccess, will match kInitResult
    #   if InitResult is the enum_name.
    # The named group "new" matches any symbol with letters and numbers, and
    #   underscores.
    # The named group "end" matches anything left after that.
    return re.sub((r'^(?P<space>\s*)(?P<old>k(?:%s)?(?P<new>[a-zA-Z_0-9]*))'
                   r'(?P<end>.*)') %
                  enum_name, _cache_replace, enum_lines, flags=re.MULTILINE)

  def __call__(self, file_str, filename, iteration):
    """Processes each file for enum definitions, and processes them.

    Enums definitions are scraped from the file strings passed in and are
    modified in place, stripping the k<enum_name> prefix. Each renamed enum is
    also be added to a dictionary mapping on the class, which is used for
    doing reference fixups.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """

    # Replacement function which captures the inside of the enum for extraction.
    def repl(m):
      ret = ''.join([m.group('pre'),
                     m.group('enum_name'),
                     m.group('start'),
                     self._replace_enum_lines(m.group('enum_lines'),
                                              m.group('enum_name')),
                     m.group('end')])
      return ret

    file_str = re.sub(
        (r'(?P<pre>\s+enum\s+)(?P<enum_name>[a-zA-Z_][a-zA-Z_0-9]*)'
         r'(?P<start>\s*{\s*)(?P<enum_lines>.*?)(?P<end>\s*})'),
        repl, file_str, flags=re.DOTALL)

    return file_str


class PostProcessDllImport(SWIGPostProcessingInterface):
  """Replace DllImport statements in cs files."""

  def __init__(self, shared_lib_name):
    """Initialize the instance.

    Args:
      shared_lib_name: Name of the shared library to load.
    """
    self.shared_lib_name = shared_lib_name
    self.replace_regexp = re.compile(
        r'(\[global::.*\.DllImport\()("[^"]*")(,[^\]]*)')

  def __call__(self, file_str, filename, iteration):
    """Replace DllImport statements to load shared_lib_name.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    return self.replace_regexp.sub(r'\g<1>"%s"\g<3>' % self.shared_lib_name,
                                   file_str)


class NamespaceCMethods(SWIGPostProcessingInterface):
  """Add a module namespace prefix to all generated C methods."""

  def __init__(self, module_name):
    """Initialize the instance.

    Args:
      module_name: Name of the module to use as a prefix for C methods.
    """
    self.module_name = module_name
    self.replace_regexp = re.compile(r'([ "])(CSharp_)([^"(]*)')

  def __call__(self, file_str, filename, iteration):
    """Add a prefix to all generated C methods statements.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    return self.replace_regexp.sub(r'\g<1>%s_\g<2>\g<3>' % self.module_name,
                                   file_str)


class ReplaceExceptionChecks(SWIGPostProcessingInterface):
  """Redirect module local exception checks to a global module."""

  def __init__(self, module_name, ignore_paths):
    """Initialize the instance.

    Args:
      module_name: Name of the module to redirect exceptions to.
      ignore_paths: List of path prefixes to ignore when doing the replacement.
    """
    self.module_name = module_name
    self.ignore_paths = ignore_paths
    self.replace_regexp = re.compile(
        r'[A-Za-z]+(PINVOKE\.SWIGPendingException\.Pending.*throw *)'
        r'[A-Za-z]+(PINVOKE\.SWIGPendingException\.Retrieve)')

  def __call__(self, file_str, filename, iteration):
    """Redirect module local exception checks to a global module.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Only performs the replacement if this file isn't in the PINVOKE
         module.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    if filename.endswith('PINVOKE.cs'):
      return file_str

    for path in self.ignore_paths:
      if path in filename:
        return file_str

    return self.replace_regexp.sub(
        r'{module_name}\g<1>{module_name}\g<2>'.format(
            module_name=self.module_name), file_str)


class FixSealedClasses(SWIGPostProcessingInterface):
  """Fixes sealed class members.

  * Removes the virtual modifier on methods in sealed classes.
  * Changes protected members to private.
  """

  def __init__(self):
    """Initialize the instance."""
    self.virtual_regexp = re.compile(r'( +)virtual +')
    self.protected_regexp = re.compile(r'protected ([^ ]+ [^ ]+ *;)')
    self.sealed_class_regexp = re.compile(r'sealed .*class ([^ ]+).*{')

  def __call__(self, file_str, filename, iteration):
    """Replace the virtual modifier on methods in sealed classes.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    output = []
    class_stack = []
    scope_count = 0
    for line_number, line in enumerate(file_str.splitlines()):
      line_number += 1  # Convert from zero based series to 1 based.
      if class_stack:
        # If we're on a line inside a class rather than a method.
        if class_stack[0][0] == scope_count:
          logging.debug('%d: %s(%d), %s',
                        line_number, class_stack[0][1], scope_count, line)
          line = self.virtual_regexp.sub(r'\g<1>', line)
          line = self.protected_regexp.sub(r'private \g<1>', line)
        elif scope_count < class_stack[0][0]:
          logging.debug('%d: %s(%d), %s',
                        line_number, class_stack[0][1], scope_count,
                        'end of class')
          class_stack.pop()
      # Track the number of braces to determine which scope we're in.
      # obviously this is going to break in cases where strings contain
      # braces but we don't generate any code like that at the moment.
      scope_count += line.count('{') - line.count('}')
      class_match = self.sealed_class_regexp.search(line)
      if class_match:
        class_name = class_match.groups()[0]
        logging.debug('%d: %s(%d), %s', line_number, class_name,
                      scope_count, 'found sealed class')
        # Brace matched on the line so increase the scope count.
        class_stack.append((scope_count, class_name))
      output.append(line)
    return '\n'.join(output)


class RenameAsyncMethods(SWIGPostProcessingInterface):
  """Renames all methods that return a Task to *Async()."""

  def __init__(self):
    """Initialize the instance."""
    self.replace_regexps = [
        re.compile(r'( +System\.Threading\.Tasks\.Task<[^>]+> +)'
                   r'([^ ]+)( *\([^)]*\) *{)$'),
        re.compile(r'( +System\.Threading\.Tasks\.Task +)'
                   r'(\w+)( *\([^)]*\) *{)$')]

  def __call__(self, file_str, filename, iteration):
    """Renames all methods that return a Task to *Async().

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    output = []
    for line in file_str.splitlines():
      for regexp in self.replace_regexps:
        m = regexp.search(line)
        if m:
          # If the function name already ends with Async or is GetTask
          # (since it's from a Future result class) don't replace it.
          function_name = m.groups()[1]
          if (function_name.endswith('Async') or
              function_name.endswith('Async_DEPRECATED') or
              function_name == 'GetTask'):
            break
          is_deprecated = function_name.endswith('_DEPRECATED')
          line = regexp.sub(r'\g<1>\g<2>Async\g<3>', line)
          if function_name.endswith('_DEPRECATED'):
            # Swap the location of 'Async' and '_DEPRECATED'
            line = line.replace('_DEPRECATEDAsync', 'Async_DEPRECATED')

          break
      output.append(line)
    return '\n'.join(output)


class RenameArgsFromSnakeToCamelCase(SWIGPostProcessingInterface):
  """Renames all public method arguments from snake_case to camelCase."""

  def __init__(self):
    """Initialize the instance."""
    self.function_regexp = re.compile(
        r'(public.*? )([^ (]+ *\()([^)]+)(\).*{)$')

  @staticmethod
  def snake_to_camel_case(identifier):
    """Convert an identifier string from snake_case to camelCase.

    Args:
      identifier: Identifier to convert.

    Returns:
      Modifier identifier string.
    """
    output_words = []
    for index, word in enumerate(identifier.split('_')):
      if index == 0:
        output_words.append(word)
      elif word:
        output_words.append(word[0].upper() + word[1:])
    return ''.join(output_words)

  @staticmethod
  def replace_arguments(replacements, line):
    """Replace arguments in a line.

    Args:
      replacements: List of (regex, replacement) tuples to replace in the line.
      line: Line to modify.

    Returns:
      Modified line.
    """
    for argument_regexp, argument_replacement in replacements:
      line = argument_regexp.sub(argument_replacement, line)
    return line

  def __call__(self, file_str, filename, iteration):
    """Rename all public method arguments from snake_case to camelCase.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    output = []
    function_stack = []
    scope_count = 0
    for line_number, line in enumerate(file_str.splitlines()):
      line_number += 1  # Convert from zero based series to 1 based.
      # Track the number of braces to determine which scope we're in.
      # obviously this is going to break in cases where strings contain
      # braces but we don't generate any code like that at the moment.
      # This also breaks with single line functions e.g
      # public void SomeFunc(int bish_bash) { state += bish_bash; }
      scope_count += line.count('{') - line.count('}')

      # Search for a function on the line.
      function_match = self.function_regexp.search(line)
      if function_match:
        function_name = function_match.groups()[1]
        argument_substitutions = []
        # Build a set of identifier replacements to apply.
        for type_argument in function_match.groups()[2].split(','):
          type_argument = type_argument.strip()
          logging.debug('%d: %s types and args %s', line_number, function_name,
                        str(type_argument))
          # Split type and argument, handling generic types.
          end_of_type = type_argument.rfind('>')
          end_of_type = (end_of_type if end_of_type > 0 else
                         type_argument.rfind(' '))
          argument = type_argument[end_of_type:].strip()
          camel_case_arg = RenameArgsFromSnakeToCamelCase.snake_to_camel_case(
              argument)
          if argument != camel_case_arg:
            logging.debug('%d: %s arg %s --> %s', line_number, function_name,
                          argument, camel_case_arg)
            regex = r'(\b)' + argument + r'(\b)'
            argument_substitutions.append((
                re.compile(regex), r'\g<1>' + camel_case_arg + r'\g<2>'))
        logging.debug('%d: %s(%d), %s args=%s', line_number, function_name,
                      scope_count, 'found function',
                      str([a[1] for a in argument_substitutions]))
        function_stack.append((scope_count, function_name,
                               argument_substitutions))
        # Update the doc string if there is one.
        if output and argument_substitutions:
          line_index = len(output) - 1
          while (line_index >= 0 and
                 output[line_index].lstrip().startswith('///')):
            output[line_index] = (
                RenameArgsFromSnakeToCamelCase.replace_arguments(
                    argument_substitutions, output[line_index]))
            line_index -= 1

      if function_stack:
        if function_stack[0][0] == scope_count:
          line = RenameArgsFromSnakeToCamelCase.replace_arguments(
              function_stack[0][2], line)
          logging.debug('%d: %s(%d), %s', line_number, function_stack[0][1],
                        scope_count, line)
        elif scope_count < function_stack[0][0]:
          logging.debug('%d: %s(%d), %s', line_number, function_stack[0][1],
                        scope_count, 'end of function')
          function_stack.pop()

      output.append(line)
    return '\n'.join(output)


class InternalMethodsToInternalVisibility(SWIGPostProcessingInterface):
  """Changes visibility to internal for "Internal" methods and properties.

  Any methods or properties with an identifier that ends with "Internal" are
  changed to internal visibility.
  """

  def __init__(self):
    """Initialize the instance."""
    self.function_property_regexp = re.compile(
        r'(public.*? )([^ )]+Internal(Async)?(_DEPRECATED)?)($| +|[({])')

  def __call__(self, file_str, filename, iteration):
    """Change "Internal" methods and properties to internal visibility.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    output = []
    for line in file_str.splitlines():
      match = self.function_property_regexp.search(line)
      if match:
        visibility = match.groups()[0]
        line = ''.join([line[:match.start(1)],
                        visibility.replace('public', 'internal'),
                        line[match.end(1):]])
      output.append(line)
    return '\n'.join(output)

class StaticFunctionKInitRemoval(SWIGPostProcessingInterface):
  """Change static function or property that has k in the name initial.

  Any function or property after "public static" and named as kXYZ will
  be renamed to XYZ.
  """

  def __init__(self):
    """Initialize the instance."""
    self.static_function_regex = re.compile(
        r'public static ([^ =]*) (k[^ =]*)')

  def __call__(self, file_str, filename, iteration):
    """Change static function or property that has k in the name initial.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """
    output = []
    for line in file_str.splitlines():
      match = self.static_function_regex.search(line)
      if match:
        function_name = match.groups()[1]
        line = ''.join([line[:match.end(1)+1],
                        function_name.replace('k', '', 1),
                        line[match.end(2):]])
      output.append(line)
    return '\n'.join(output)

class DynamicToReinterpretCast(SWIGPostProcessingInterface):
  """Changes the use of dynamic_cast in SWIG generated code to reinterpret_cast.

  SWIG uses dynamic_cast even though the type is guaranteed to be
  derived from the base class. This only happens when you use the
  "directors" feature, and it's safe to replace with reinterpret cast to
  avoid needing RTTI.
  """

  def __call__(self, file_str, filename, iteration):
    """Change dynamic_cast to reinterpret_cast.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """

    if iteration != 0:
      return file_str

    return re.sub(r'dynamic_cast<', 'reinterpret_cast<', file_str)


class AddPInvokeAttribute(SWIGPostProcessingInterface):
  """Adds missing PInvoke attributes.

  Unity requires certain CSharp to C++ functions be tagged with this
  attribute to allow proper C++ code gen from IL.
  """

  def __init__(self):
    """Initialize the instance."""
    self.search_strings = [
        ('ExceptionArgumentDelegate',
         re.compile('static void SetPendingArgument.*string paramName\\)')),
        ('FirestoreExceptionDelegate',
         re.compile('static void SetPendingFirestoreException.*string message')),
        ('ExceptionDelegate',
         re.compile('static void SetPending.*string message')),
        ('SWIGStringDelegate',
         re.compile('static string CreateString')),
    ]

  def __call__(self, file_str, filename, iteration):
    """Adds missing PInvoke attributes.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.
    """

    if iteration != 0:
      return file_str

    def get_line_delegate(line):
      """Checks to see if a line matches search regex and gets the delegate name.

      Args:
        line: Input line

      Returns:
        Delegate name if line matches or None
      """
      for delegate, regex in self.search_strings:
        if regex.search(line):
          return delegate
      return None

    output = []
    for line in file_str.splitlines():
      delegate = get_line_delegate(line)

      if delegate:
        leading_whitespace = re.search(r'^(\s+)', line).group(0)
        output.append('%s[MonoPInvokeCallback(typeof(%s))]' %
                      (leading_whitespace, delegate))

      output.append(line)
    return '\n'.join(output)


def apply_post_process(file_buffer, file_name, processes):
  """Applys a set of transformation processes to a file.

  This takes objects of the SWIGPostProcessingInterface "processes", and
  continuously triggers them on a file until no more changes are applied.

  Args:
    file_buffer: Contents of the file
    file_name: Path of the file
    processes: A list of objects implementing the SWIGPostProcessingInterface.

  Returns:
    Contents of the file with transformations applied.
  """
  iteration = 0

  while processes:
    # keep track of any process that caused a change, to make sure we do another
    # pass. Each process is run over all files until no changes are made.
    processors_still_in_effect = set()

    for proc in processes:
      file_buffer_after = proc(file_buffer, file_name, iteration)

      if file_buffer_after != file_buffer:
        processors_still_in_effect.add(proc)
        file_buffer = file_buffer_after

    iteration += 1
    # preserve list order and remove processors which had no effect.
    processes = [x for x in processes if x in processors_still_in_effect]

  return file_buffer


def post_process(directory, file_extensions, processes):
  """Scrubs all the code generated by SWIG with post process scripts.

  This takes objects of the SWIGPostProcessingInterface "processes", and
  triggers them on each file in the "directory".

  Args:
    directory: Directory containing all source files to be processed.
    file_extensions: List of extensions of files to process.
    processes: A list of objects implementing the SWIGPostProcessingInterface.
  """
  # Get all of the paths to files in the proxy dir.
  paths = []
  for path in [os.path.join(directory, f) for f in os.listdir(directory)]:
    if ((not os.path.isdir(path)) and
        os.path.splitext(path)[1] in file_extensions):
      paths.append(path)

  # Edit each file in-place using the set of post processing objects in the
  # processes list.
  for path in paths:
    with open(path, 'r+') as f:
      file_buffer = f.read()
      f.seek(0)

      file_buffer_after = apply_post_process(file_buffer, path, processes)

      if file_buffer != file_buffer_after:
        f.write(file_buffer_after)
        f.truncate()


def main(unused_argv):
  """Registers a list of post processes and triggers them on the csharp_dir.

  Args:
    unused_argv: Extra arguments not consumed by the config flags.

  Returns:
    The exit code status; 0 for success, non-zero for an error.
  """
  # Create the post processing objects
  post_processes = [SWIGEnumPostProcessing(), AddPInvokeAttribute()]
  if FLAGS.dll_import:
    post_processes += [PostProcessDllImport(FLAGS.dll_import)]
  if FLAGS.module_name_prefix:
    post_processes += [NamespaceCMethods(FLAGS.module_name_prefix)]
  if FLAGS.exception_module_name:
    post_processes += [ReplaceExceptionChecks(FLAGS.exception_module_name,
                                              FLAGS.exception_ignore_path_prefixes)]
  if FLAGS.fix_sealed_classes:
    post_processes += [FixSealedClasses()]
  if FLAGS.internal_visibility:
    post_processes += [InternalMethodsToInternalVisibility()]
  if FLAGS.rename_async_methods:
    post_processes += [RenameAsyncMethods()]
  if FLAGS.snake_to_camel_case_args:
    post_processes += [RenameArgsFromSnakeToCamelCase()]
  post_process(FLAGS.csharp_dir, ['.cs'], post_processes)

  if FLAGS.cpp_dir:
    post_processes = [DynamicToReinterpretCast()]
    if FLAGS.module_name_prefix:
      post_processes += [NamespaceCMethods(FLAGS.module_name_prefix)]
    post_process(FLAGS.cpp_dir, ['.h', '.cc', '.cpp'], post_processes)

  return 0

if __name__ == '__main__':
  flags.mark_flag_as_required('csharp_dir')
  app.run(main)
