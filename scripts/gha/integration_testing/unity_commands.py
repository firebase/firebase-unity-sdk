# Copyright 2021 Google LLC
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

"""A helper class for building argument lists for unity batch mode execution.

Supplies arguments suitable for use in a subprocess call. Compatible with
SubprocessCall from the command module. The main benefit of this class
is to reduce the amount of book-keeping needed to create
a pipeline of Unity command line actions. Example usage:

  unity_path = "/Applications/Unity/Unity.app/Contents/MacOS/Unity"
  project_path = "~/UnityBuild"
  log_path = "~/UnityBuild/batch.log"
  package_path = "~/Downloads/plugins/FirebaseAnalytics.unitypackage"

  arg_builder = UnityArgBuilder(unity_path, project_path)
  arg_builder.set_log_file(log_path)

  create_project_args = arg_builder.get_args_for_create_project()
  package_args = arg_builder.get_args_for_import(package_path)

  enable_plugin_args = arg_builder.get_args_for_method("PluginHelper.Enable46")

  build_args = arg_builder.get_args_for_method(
      "AppBuilder.Build", method_args=["-ios_sdk", "device", "-buildAutomated"])

The four arg lists above (create_project_args, package_args, enable_plugin_args,
build_args) can now be passed to subprocess.call to perform
the relevant actions.

"""

_EXECUTE_METHOD = "-executeMethod"
_ACCEPT_API_UPDATE = "-accept-apiupdate"
_IMPORT_PACKAGE = "-importPackage"
_PROJECT_PATH = "-projectPath"
_CREATE_PROJECT = "-createProject"
_NO_GRAPHICS = "-nographics"
_BATCHMODE = "-batchmode"
_LOG_FILE = "-logFile"
_QUIT = "-quit"

_FORBIDDEN_SHARED_FLAGS = (
    _LOG_FILE, _CREATE_PROJECT, _PROJECT_PATH, _EXECUTE_METHOD, _IMPORT_PACKAGE)

# -quit means Unity will automatically close after performing an operation.
# -batchmode suppresses UI prompts, necessary for command line automation.
# -accept-apiupdate means that obsolete Unity APIs will trigger Unity's
#   api updater to run, rather than failing. Doesn't work for all obsolete APIs.
_DEFAULT_SHARED_FLAGS = (_QUIT, _BATCHMODE, _ACCEPT_API_UPDATE)


class UnityArgBuilder(object):
  """Utility class for running Unity commands.

  Provides an API for building arguments to run Unity commands in a subprocess
  call. Intended specifically for creating build pipelines, where a new Unity
  project will be created, one or more plugins will be imported, and then
  build methods on custom scripts will be called through Unity's command line
  interface.

  Methods return a sequence of arguments in the format expected
  by the subprocess module. One argbuilder should be used for each Unity
  project / build pipeline.
  """

  def __init__(
      self, unity_path, project_path, shared_args=_DEFAULT_SHARED_FLAGS):
    """Creates a new arg builder.

    Args:
      unity_path: Path to a Unity executable.
      project_path: Path to an empty directory. Unity project will be created
          here, and all other commands will expect a valid Unity project to
          exist here when executed.
      shared_args: These args will be present in all commands, and should only
          consist of option flags that affect how Unity will run, as opposed
          to command flags such as -executeMethod. By default, this consists of
          -quit, -batchmode, -accept-apiupdate. The Unity path and
          project path will be handled internally and should not be included
          here. If you wish to supply a log path, use the set_log_file method.
          Flags for specific commands should similarly not be included, such as
          -createMethod, -importPackage, etc.

    Raises:
      ValueError: An argument other than shared_args is null or empty,
          or shared args have invalid flags as described in the shared_args
          documentation.
      TypeError: shared_args is not iterable.

    """
    if not unity_path:
      raise ValueError("Unity path not supplied.")
    if not project_path:
      raise ValueError("Project path not supplied.")
    if not shared_args:
      shared_args = []
    for flag in _FORBIDDEN_SHARED_FLAGS:
      if flag in shared_args:
        raise ValueError("Do not include %s in shared_args." % flag)

    self._default_args = [unity_path] + list(shared_args)
    self._project_path = project_path

  def set_log_file(self, log_path):
    """Sets the path Unity will use for log output.

    Args:
      log_path: Unity will output detailed logs to this file. File will be
          created if it doesn't exist, and will be overwritten otherwise.

    Raises:
      ValueError: log_path is None or empty.

    """
    if not log_path:
      raise ValueError("No path supplied.")
    if _LOG_FILE in self._default_args:
      index_of_log = self._default_args.index(_LOG_FILE) + 1
      self._default_args[index_of_log] = log_path
    else:
      self._default_args.extend([_LOG_FILE, log_path])

  def get_args_for_create_project(self):
    """Returns a sequence of arguments to create a new project.

    Executing the returned args more than once without wiping the project
    directory between executions will result in multiple Unity projects being
    installed in the same folder with unpredictable results. The path for the
    project is the one supplied on object creation.

    """
    args = self._clone_args()
    args.extend([_CREATE_PROJECT, self._project_path])
    return args

  def get_args_to_open_project(self, extra_flags=None):
    """Returns a sequence of arguments to open this project in Unity."""
    args = self._clone_args()
    self._add_project_path(args)
    if _QUIT in args:
      args.remove(_QUIT)
    if extra_flags:
      args.extend(extra_flags)
    return args

  def get_args_for_method(
      self, method, method_args=None, suppress_quit_flag=False):
    """Returns a sequence of arguments to execute a custom method.

    Unity provides a way to execute arbitrary methods on C# scripts. This
    method provides access to this feature. See detailed instructions on
    the method arg for important usage information.

    Note that this makes use of -executeMethod by default, which may have some
    odd behaviour with respect to Unity's event lifecycle. As an alternative,
    get_args_to_open_project can be used to avoid -executeMethod, in combination
    with e.g. a method decorated with [InitializeOnLoadMethod] to serve as an
    entry point.

    Args:
      method: Must be of the format 'Classname.Methodname', without brackets.
          Method must be public and static, and take no arguments. The class
          file must be in an editor folder within the Unity project.
      method_args: Command line arguments that will be passed through to the
          C# script being executed. Since the method cannot take arguments,
          this allows options to be passed to the method, which can then be
          consumed by reading the command line arguments with the Environment
          class.
      suppress_quit_flag: The -quit flag is normally useful, but has some
          unusual behaviours that can make it problematic when executing a
          custom method, especially one involving a complex build process with
          non-fatal errors. It can cause the application to exit prematurely.
          It can also cause errors to be suppressed.
          Note that if the arg builder was constructed without -quit as a shared
          arg, this arg does nothing either way.

    Raises:
      ValueError: No method supplied.

    """
    if not method:
      raise ValueError("No method supplied.")
    args = self._clone_args()
    args.extend([_EXECUTE_METHOD, method])
    self._add_project_path(args)
    if suppress_quit_flag and _QUIT in args:
      args.remove(_QUIT)
    if method_args:
      args.extend(method_args)
    return args

  def get_args_for_import(self, package_path):
    """Returns a sequence of arguments to import a Unity package.

    Args:
      package_path: Path to an existing Unity package.

    Raises:
      ValueError: No path supplied.

    """
    if not package_path:
      raise ValueError("No package path supplied.")
    args = self._clone_args()
    args.extend([_IMPORT_PACKAGE, package_path])
    self._add_project_path(args)
    return args

  def _clone_args(self):
    """Clones the instance's current arg list."""
    return list(self._default_args)

  def _add_project_path(self, args):
    """Adds the project path to the given args. Needed for most commands."""
    args.append(_PROJECT_PATH)
    args.append(self._project_path)

