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
"""Fixes swig code generation c++ and csharp.

As part of the CMake project, swig is used to generate C++ and CSharp code.
However, there are a few problems with the code generation that need to be fixed
to allow Firebase to play nicely with the Unity game engine.

This script reads the generated code file, applies the required transformations
and saves it back as a new file to allow CMake dependency rules to work.
"""

import os
import re
import sys

from absl import app
from absl import flags
from absl import logging

parent_dir = os.path.abspath(os.path.dirname(os.path.abspath(__file__)) + '/..')

sys.path.append(parent_dir)
import swig_post_process  # pylint: disable=g-import-not-at-top

FLAGS = flags.FLAGS
flags.DEFINE_enum('language', None, ['cpp', 'csharp'],
                  'Language of in file to process')
flags.DEFINE_string('in_file', None,
                    'Path to input file to fix')
flags.DEFINE_string('out_file', None,
                    'Path where output file is written to')
flags.DEFINE_string('namespace', None,
                    'Namespace for pinvoke functions to apply')
flags.DEFINE_boolean('clear_in_file', False,
                     'Clear out in file once out file is written')
flags.DEFINE_string('import_module', None,
                    'Change DllImport module to this value')
flags.DEFINE_boolean('static_k_removal', False,
                    'Whether to remove public static attributes k initial')


def get_dll_import_transformation():
  """Gets the transforms required for CSharp dll import fix up.

  Returns:
    list of dll import fix up transformations.
  """
  if not FLAGS.import_module:
    return []

  return [
      swig_post_process.PostProcessDllImport(FLAGS.import_module)
  ]


def get_static_k_removal_transformation():
  """Gets the transforms to remove k initial for public static attribute
  
  Returns:
    list of static k removal transformations.
  """
  if not FLAGS.static_k_removal:
    return []

  return [
      swig_post_process.StaticFunctionKInitRemoval()
  ]


class NamespaceCMethodsCMake(swig_post_process.SWIGPostProcessingInterface):
  """Add a module namespace prefix to all generated C methods."""

  def __init__(self, module_name):
    """Initialize the instance.

    Args:
      module_name: Name of the module to use as a prefix for C methods.
    """
    self.module_name = module_name
    # Newer versions of swig adds CSharp namespace into function names
    self.replace_regexp1 = re.compile(
        r'([ "])CSharp_Firebase(?:(?:f[A-Za-z]+_)|_)([^"(]*)')
    # Older version of swig doesnt inject the namespace into export names
    self.replace_regexp2 = re.compile(
        r'([ "])CSharp_([^"(]*)')

  def __call__(self, file_str, filename, iteration):
    """Add a prefix to all generated C methods statements.

    Args:
      file_str: This is a string containing the file contents to be processed.
      filename: Unused.
      iteration: Unused.

    Returns:
      Returns the modified file_str.

    Note:
      If swig tries to generate an export with characters that are not legal in
      c++ it will transcode them using a letter and append 3 underscores to
      uniquify it. Since we pass the CSharp namespace to swig for CMake it
      converts the '.' to f and appends '___' and the regex below converts it to
      a more readable name.

    Sample inputs:
      [DllImport("FirebaseCppApp", EntryPoint="CSharp_FirebasefAuth_UserInfoInterfaceList_Clear___")]
      [DllImport("FirebaseCppApp", EntryPoint="CSharp_Firebase_UserInfoInterfaceList_Clear")]
      [DllImport("FirebaseCppApp", EntryPoint="CSharp_new_AppOptionsInternal")]
      SWIGEXPORT void SWIGSTDCALL CSharp_FirebasefAuth_UserInfoInterfaceList_Clear___(void * jarg1) {
      SWIGEXPORT void SWIGSTDCALL CSharp_Firebase_UserInfoInterfaceList_Clear(void * jarg1) {

    Expected outputs:
      [DllImport("FirebaseCppApp", EntryPoint="Firebase_Auth_CSharp_UserInfoInterfaceList_Clear")]
      [DllImport("FirebaseCppApp", EntryPoint="Firebase_Auth_CSharp_UserInfoInterfaceList_Clear")]
      [DllImport("FirebaseCppApp", EntryPoint="Firebase_App_CSharp_new_AppOptionsInternal")]
      SWIGEXPORT void SWIGSTDCALL Firebase_Auth_CSharp_UserInfoInterfaceList_Clear(void * jarg1) {
      SWIGEXPORT void SWIGSTDCALL Firebase_Auth_CSharp_UserInfoInterfaceList_Clear(void * jarg1) {
    """

    file_str = file_str.replace('___(', '(').replace('___"', '"')
    file_str = self.replace_regexp1.sub(
        r'\g<1>%s_CSharp_\g<2>' % self.module_name, file_str)
    file_str = self.replace_regexp2.sub(
        r'\g<1>%s_CSharp_\g<2>' % self.module_name, file_str)

    return file_str


def get_transformations(namespace):
  """Gets a dict of transformations for each input language.

  Args:
    namespace: CSharp namespace of modules that input files belong too

  Returns:
    dict with key being language and value being list of transformations
  """
  return {
      'cpp': [
          NamespaceCMethodsCMake(namespace),
          swig_post_process.DynamicToReinterpretCast(),
      ],
      'csharp': [
          swig_post_process.SWIGEnumPostProcessing(),
          NamespaceCMethodsCMake(namespace),
          swig_post_process.ReplaceExceptionChecks(
              'AppUtil', ['Firebase.Firestore.cs']),
          swig_post_process.FixSealedClasses(),
          swig_post_process.InternalMethodsToInternalVisibility(),
          swig_post_process.RenameAsyncMethods(),
          swig_post_process.RenameArgsFromSnakeToCamelCase(),
          swig_post_process.AddPInvokeAttribute(),
      ] + get_dll_import_transformation()
        + get_static_k_removal_transformation(),
  }


def main(argv):  # pylint: disable=unused-argument

  # Change C# namespace into suitable C++ function prefix
  namespace = FLAGS.namespace.replace('.', '_')
  processes = get_transformations(namespace)[FLAGS.language]

  contents = ''

  with open(FLAGS.in_file, 'r') as in_file:
    contents = in_file.read()

  # TODO(markchandler) Fix swig generation to not need this hack
  if not contents:
    logging.warning('Empty input file "%s"', FLAGS.in_file)
    return 0

  new_contents = swig_post_process.apply_post_process(contents, FLAGS.in_file, processes)

  with open(FLAGS.out_file, 'w') as out_file:
    out_file.write(new_contents)

  if FLAGS.clear_in_file:
    with open(FLAGS.in_file, 'w') as out_file:
      pass

  return 0

if __name__ == '__main__':
  app.run(main)
