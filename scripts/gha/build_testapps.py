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

r"""Build automation tool for Firebase Unity testapps.

USAGE:

Android applications will be built as an .apk. iOS applications will be built
as an .app or .ipa artifact, placed into a folder called app_ios_build.
All build artifacts can be found in a folder that looks like
testapps<timestamp>/<unity_version>-NET<runtime>/<API>, in a directory
specified by a flag (home directory, by default).

Build these two testapps for the two given Unity versions:
$ build_testapps.py --t auth,storage --u 2019.4.39f1

Build auth for 2017.4, using the latest .NET runtime (4.6). Normally the
default runtime for that version of Unity will be used.
$ build_testapps.py --t auth --u 2019.4.39f1 --force_latest_runtime

Build all APIs for 2019.4.39f1.
$ build_testapps.py --u 2019.4.39f1

IN-EDITOR TESTING:

In addition to building, this tool can also be used for in-editor (playmode)
testing. This runs the testapps in playmode within the Editor itself, and
automatically validates the results, reporting testapps that failed.

To perform in-editor testing, specify the 'Playmode' platform:
$ build_testapps.py --t auth,storage --u 2017.4.37f1 --p Playmode

REQUIREMENTS:

(1) Unity must be installed locally. If using the Unity Hub, no extra
steps need to be taken. Otherwise, the default installation directory
needs to be modified to include the version. The following formats are
expected, without the <> brackets:

Mac: /Applications/Unity<VERSION>/Unity.app/Contents/MacOS/Unity
Linux: ~/Unity<VERSION>/Editor/Unity
Windows: C:\program files\unity<VERSION>\editor\unity.exe

Note that Unity<Version>, Unity_<Version>, and Unity-<Version> are
all acceptable.

<VERSION> is then passed to the --u flag to select this version of Unity,
as illustrated in the above command examples.

(2) Android specific: 
Set ANDROID_HOME, JAVA_HOME
Download install Android SDK tools. Unity 2017.4 tested with tools_r25.2.5.
Unity 2017, 2018 requires the 64-bit version JDK 8 (1.8).

iOS specific: 
Download XCode. Unity 2017.4 tested with XCode 10.

(3) You have plugins available and unzipped somewhere locally. By default
the script looks for them in ~/Downloads/client_unity_plugins, though this
path can be overridden with the --plugin_dir flag. Plugins are unity packages,
e.g. FirebaseAnalytics.unitypackage.

Alternatively, you can use the new style of packages with Unity's package
manager, by supplying the direction containing them to --use_local_packages.

For .unitypackages, the plugins should not be in the directory itself, but
a dotnet3 or dotnet4 subdirectory based on whether they are to be used in
projects configured for .NET 3.5 (legacy runtime) or .NET 4.6 (latest runtime).
For example:
~/Downloads/client_unity_plugins/dotnet3/FirebaseAuth.unitypackage
~/Downloads/client_unity_plugins/dotnet4/FirebaseAuth.unitypackage

If the same plugin is intended for both runtimes, then they can be
copied or symlinked.

(4) Install the Python dependencies in requirements.txt, which should be in
in the same directory as this script.

"""

import datetime
from distutils import dir_util
from genericpath import isdir
import glob
import os
import platform
import stat
import shutil
import subprocess
import time
import requests
import zipfile
import json

from absl import app
from absl import flags
from absl import logging

import attr

from integration_testing import config_reader
from integration_testing import test_validation
from integration_testing import unity_commands
from integration_testing import unity_finder
from integration_testing import unity_version
from integration_testing import xcodebuild
from print_matrix_configuration import UNITY_SETTINGS

# Used in specifying whether xcodebuild should build for device or simulator
_DEVICE_REAL = "real"
_DEVICE_VIRTUAL = "virtual"

_IOS_SDK = {
  _DEVICE_REAL: "device",
  _DEVICE_VIRTUAL: "simulator"
}

_UNITY_PROJECT_NAME = "testapp"

_NET46 = "4.6"

# Possible values for the 'platform' flag. These correspond to Unity build
# targets. Exceptions are "Playmode", which will run tests within the editor,
# and "Desktop", which will get translated to the desktop build target
# corresponding to the operating system being used.
_ANDROID = "Android"
_PLAYMODE = "Playmode"
_IOS = "iOS"
_WINDOWS = "Windows"
_MACOS = "macOS"
_LINUX = "Linux"
_DESKTOP = "Desktop"

_BUILD_TARGET = {
  _ANDROID: "Android",
  _IOS: "iOS",
  _WINDOWS: "Win64",
  _MACOS: "OSXUniversal",
  _LINUX: "Linux64",
  _PLAYMODE: "Playmode"
}

_SUPPORTED_PLATFORMS = (
    _ANDROID, _IOS, _PLAYMODE, _DESKTOP,
    _WINDOWS, _LINUX, _MACOS)

_SUPPORTED_XCODE_CONFIGURATIONS = (
    "ReleaseForRunning", "Release", "Debug", "ReleaseForProfiling")

FLAGS = flags.FLAGS

flags.DEFINE_list(
    "platforms", "Android,iOS",
    "Build targets. Note that 'Playmode' corresponds to performing an in-editor"
    " playmode test, not a build.",
    short_name="p")

flags.DEFINE_list(
    "testapps", "all",
    "Which testapps (Firebase APIs) to build. Should either match the"
    " short names (e.g. analytics,dynamic_links) in the config file"
    " or should be 'all' to build testapps for every API listed in the config.",
    short_name="t")

flags.DEFINE_list(
    "unity_versions", "2019.4.39f1",
    "Unity versions to build against. Must match the folder name in your"
    " applications directory or Unity Hub subdirectory.",
    short_name="u")

flags.DEFINE_string(
    "config_path", None, "Override the path to the config file used.")

flags.DEFINE_string(
    "root_dir", os.getcwd(),
    "Directory with which to join the relative paths in the config.")

flags.DEFINE_string(
    "plugin_dir", "~/Downloads/client_unity_plugins",
    "Directory of unzipped plugins (.unitypackage files).")

flags.DEFINE_string(
    "use_local_packages", None,
    "Path to a directory containing tarred unity packages. If supplied, will"
    " use the Unity package manager to install local packages (as .tgz"
    " archives) instead of importing .unitypackage plugins normally.")

flags.DEFINE_string(
    "output_directory", "~", "Build output will be placed in this directory.")

flags.DEFINE_string(
    "unity_folder_override", "",
    "Override the default behaviour of looking for Unity in the default"
    " installation directory. Will instead look in this provided folder.")

flags.DEFINE_list(
    "ios_sdk", _DEVICE_REAL,
    "Build for device or simulator (only affects iOS).")

flags.DEFINE_enum(
    "xcode_configuration", "ReleaseForRunning", _SUPPORTED_XCODE_CONFIGURATIONS,
    "xcode configuration that will be used by xcodebuild.")

flags.DEFINE_bool(
    "timestamp", True,
    "Add a timestamp to the outermost output directory for disambiguation."
    " Recommended when running locally, so each execution gets its own"
    " directory.")

flags.DEFINE_bool(
    "force_latest_runtime", False,
    "Force Unity to use the latest runtime. Only useful when using version"
    " where the latest runtime is not the default, i.e. versions before 2018.3"
    " although this may change in the future. Must use only compatible"
    " versions of Unity (2017+) if setting this flag to true. Otherwise,"
    " program will abort with an error.")

flags.DEFINE_bool(
    "use_unity_ios_symlinks", True,
    "Tell Unity to symlink iOS libraries into the xcode project"
    " instead of copying them (~700mb on at least some versions)."
    " Enabled by default to significantly reduce disk space utilization.")

flags.DEFINE_bool(
    "force_xcode_project", False,
    "Force the iOS resolver to use a .xcodeproj instead of .xcworkspace."
    " This is only intended to be used to ensure this resolution type works.")

flags.DEFINE_bool(
    "enable_firebase", True,
    "By default, a Firebase Unity package is always expected to be included in"
    " the Test Apps. By setting this flag to false, it will allow Test Apps to"
    " be built without any dependency on Firebase.")

flags.DEFINE_bool(
    "enable_edm4u", True,
    "By default, the External Dependency Manager for Unity (EDM4U) is enabled."
    " By setting this flag to false, it will prevent it from running.")

flags.DEFINE_string(
    "artifact_name", "local-build",
    "artifacts will be created and placed in output_directory."
    " testapps artifact is testapps-$artifact_name;"
    " build log artifact is build-results-$artifact_name.log.")   

flags.DEFINE_bool(
    "ci", False, "If running the script on CI")

flags.register_validator(
    "platforms", lambda x: set(x) <= set(_SUPPORTED_PLATFORMS))

flags.register_validator(
    "unity_versions",
    lambda x: all(unity_version.validate(version) for version in x),
    "Unity versions must be of form a.b or a.b.c, where a and b are digits, and"
    " c can be a combination of digits and letters.")


@attr.s(frozen=False, eq=False)
class Test(object):
  """Holds data related to the testing of one testapp."""
  testapp_path = attr.ib()
  logs = attr.ib()


def main(argv):
  del argv  # Unused.

  root_dir = _fix_path(FLAGS.root_dir)
  plugins_dir = _fix_path(FLAGS.plugin_dir)
  root_output_dir = _fix_path(FLAGS.output_directory)
  unity_folder_override = _fix_path(FLAGS.unity_folder_override)
  use_local_packages = _fix_path(FLAGS.use_local_packages)

  # The config object contains all the configuration from the json file.
  config = config_reader.read_config(_fix_path(FLAGS.config_path))

  # Dict of unity version -> full path to Unity binary.
  version_path_map = get_version_path_map(
      FLAGS.unity_versions, unity_folder_override)
  unity_versions = update_unity_versions(version_path_map)
  timestamp = get_timestamp() if FLAGS.timestamp else ""

  testapps = validate_testapps(FLAGS.testapps, config.apis)
  platforms = validate_platforms(FLAGS.platforms)

  output_root = os.path.join(root_output_dir, "testapps")
  playmode_tests = []
  failures = []
  for version in unity_versions:
    runtime = get_runtime(version, FLAGS.force_latest_runtime)
    output_dir = get_output_dir(output_root, str(version), runtime, timestamp)
    logging.info("Output directory: %s", output_dir)
    for testapp in testapps:
      api_config = config.get_api(testapp)
      setup_options = _SetupOptions(
          switch_to_latest=FLAGS.force_latest_runtime,
          testapp_file_filters=config.skipped_testapp_files,
          enable_firebase=FLAGS.enable_firebase,
          enable_edm4u=FLAGS.enable_edm4u)
      dir_helper = _DirectoryHelper.from_config(
          root_dir=root_dir,
          api_config=api_config,
          unity_path=version_path_map[version],
          output_dir=output_dir,
          builder_dir=os.path.join(root_dir, config.builder_directory),
          unity_plugins=_resolve_plugins(plugins_dir, api_config, runtime),
          upm_packages=_resolve_upm_packages(use_local_packages, api_config),
          xcode_name=get_xcode_name(version, FLAGS.force_xcode_project))
      ios_config = _IosConfig(
          bundle_id=api_config.bundle_id,
          ios_sdk=FLAGS.ios_sdk,
          configuration=FLAGS.xcode_configuration,
          scheme="Unity-iPhone",
          use_unity_symlinks=FLAGS.use_unity_ios_symlinks)
      build_desc = "{0}, .NET{1}, Unity{2}".format(
          testapp, runtime, str(version))
      logging.info("BEGIN %s", build_desc)
      if _ANDROID in platforms:
        patch_android_env(version)
      try:
        setup_unity_project(dir_helper, setup_options)
      except (subprocess.SubprocessError, RuntimeError) as e:
        failures.append(Failure(testapp=testapp, description=build_desc, error_message=str(e)))
        logging.info(str(e))
        continue  # If setup failed, don't try to build. Move to next testapp.
      for p in platforms:
        try:
          if p == _DESKTOP:  # e.g. 'Desktop' -> 'OSXUniversal'
            p = get_desktop_platform()
          if p == _PLAYMODE:
            logs = perform_in_editor_tests(dir_helper)
            playmode_tests.append(Test(testapp_path=dir_helper.unity_project_dir, logs=logs))
          else:
            build_testapp(
                dir_helper=dir_helper,
                api_config=api_config,
                ios_config=ios_config,
                target=_BUILD_TARGET[p])
        except (subprocess.SubprocessError, RuntimeError) as e:
          if p == _PLAYMODE:
            playmode_tests.append(Test(testapp_path=dir_helper.unity_project_dir, logs=str(e)))
          failures.append(
              Failure(
                  testapp=testapp, 
                  description=build_desc + " " + p,
                  error_message=str(e)))
          logging.info(str(e))
          # If there is an error, print out the log file.
          log_file = dir_helper.make_log_path("build_" + _BUILD_TARGET[p])
          logging.info(log_file)
          with open(log_file, 'r') as f:
            logging.info(f.read())
      # Free up space by removing unneeded Unity directory.
      if FLAGS.ci:
        _rm_dir_safe(dir_helper.unity_project_dir)
      else:
        _rm_dir_safe(os.path.join(dir_helper.unity_project_dir, "Library"))
      logging.info("END %s", build_desc)

  playmode_passes = True
  build_passes = True
  if _PLAYMODE in platforms:
    platforms.remove(_PLAYMODE)
    playmode_passes = test_validation.summarize_test_results(
      playmode_tests, 
      test_validation.UNITY, 
      root_output_dir, 
      file_name="test-results-" + FLAGS.artifact_name + ".log")
    
  if platforms:
    _collect_integration_tests(config, testapps, root_output_dir, output_dir, FLAGS.artifact_name)
    build_passes = _summarize_build_results(
        testapps=testapps,
        platforms=platforms,
        versions=unity_versions,
        failures=failures,
        output_dir=root_output_dir, 
        artifact_name=FLAGS.artifact_name)
  
  return (playmode_passes and build_passes)


def setup_unity_project(dir_helper, setup_options):
  """Creates a confgures a Unity project to build testapps.

  This will copy a Unity project from the source repo, and perform
  setup tasks such as enabling assemblies, installing plugins,
  and copying over the editor script responsible for performing
  automating builds.

  Args:
    dir_helper: _DirectoryHelper object for this testapp.
    setup_options: _SetupOptions object for this testapp.

  """
  logging.info("Setting up Unity project: %s", dir_helper.unity_project_dir)
  arg_builder = unity_commands.UnityArgBuilder(
      dir_helper.unity_path, dir_helper.unity_project_dir)

  # We create the Unity project upfront by copying over ProjectSettings, but
  # not Assets: Assets include scripts that depend on plugins that first have
  # to be imported and enabled. Otherwise, compiler errors block these steps.
  _create_unity_project(dir_helper)

  if setup_options.switch_to_latest:
    _switch_to_latest_runtime(dir_helper, arg_builder)

  if dir_helper.upm_packages:
    _import_upm_packages(dir_helper, arg_builder)
  else:
    _import_unity_plugins(dir_helper, arg_builder)

  if setup_options.enable_firebase:
    _enable_firebase_assemblies(dir_helper, arg_builder)
  if setup_options.enable_edm4u:
    _edm4u_update(dir_helper, arg_builder)

  _copy_unity_assets(dir_helper, setup_options.testapp_file_filters)
  _add_menu_scene(dir_helper)
  _add_automated_test_runner(dir_helper)
  # This is the editor script that performs builds.
  app_builder = dir_helper.copy_editor_script("AppBuilderHelper.cs")
  if not setup_options.enable_firebase:
    remove_define_from_file(app_builder, "FIREBASE_IS_ENABLED")
  if not setup_options.enable_edm4u:
    remove_define_from_file(app_builder, "EDM4U_IS_ENABLED")
  logging.info("Finished setting up Unity project.")


def build_testapp(dir_helper, api_config, ios_config, target):
  """Builds a testapp with Unity.

  Assumes that a Unity project already exists in the path specified by
  the directory helper, and is configured with the appropriate testapp
  files, plugin, and the testapp builder itself.

  For iOS, this will generate an xcode project. For other platforms, this will
  generate the final binary (apk or standalone binary).

  Args:
    dir_helper: _DirectoryHelper object for this testapp.
    api_config: _APIConfig object for this testapp.
    ios_config: _IosConfig object for this testapp.
    target: Unity build target. Must be recognized by the built-in Unity
        -buildTarget flag.

  """
  logging.info("Building target %s...", target)
  arg_builder = unity_commands.UnityArgBuilder(
      dir_helper.unity_path, dir_helper.unity_project_dir)
  arg_builder.set_log_file(dir_helper.make_log_path("build_" + target))

  # Flags for the C# build script.
  build_flags = [
      "-AppBuilderHelper.outputDir", dir_helper.output_dir,
      "-buildTarget", target
  ]
  if target == _IOS:
    for device_type in ios_config.ios_sdk:
      build_flags += ["-AppBuilderHelper.targetIosSdk", _IOS_SDK[device_type]]
      if not ios_config.use_unity_symlinks:
        build_flags.append("-AppBuilderHelper.noSymlinkLibraries")
      if dir_helper.xcode_path.endswith(".xcodeproj"):
        build_flags.append("-AppBuilderHelper.forceXcodeProject")
      # This script will automatically configure the generated xcode project
      dir_helper.copy_editor_script("XcodeCapabilities.cs")
      # Some testapps have xcode entitlements
      if api_config.entitlements:
        shutil.copy(
            os.path.join(dir_helper.root_dir, api_config.entitlements),
            os.path.join(dir_helper.unity_project_editor_dir, "dev.entitlements"))
      try:
        # This is a patch. Unity 2018 retrun non 0 value, but it build successfully
        _run(arg_builder.get_args_to_open_project(build_flags))
        logging.info("Finished building target %s xcode project", target)
      except(subprocess.SubprocessError, RuntimeError) as e:
        logging.info(str(e))
      finally:
        run_xcodebuild(dir_helper=dir_helper, ios_config=ios_config, device_type = device_type)
  else:
    if api_config.minify:
      build_flags += ["-AppBuilderHelper.minify", api_config.minify]
    _run(arg_builder.get_args_to_open_project(build_flags))
    logging.info("Finished building target %s", target)


def patch_android_env(unity_version):
  major_version = int(unity_version.split(".")[0])
  # Set ndk env
  if UNITY_SETTINGS[str(major_version)][get_desktop_platform()]["ndk"]:
    url = UNITY_SETTINGS[str(major_version)][get_desktop_platform()]["ndk"]
    logging.info("install ndk: %s", url)
    ndk_zip_path = "ndk_zip"
    ndk_path = "ndk"
    r = requests.get(url, stream=True)
    with open(ndk_zip_path, 'wb') as fd:
      for chunk in r.iter_content(chunk_size=128):
        fd.write(chunk)
    with zipfile.ZipFile(ndk_zip_path, 'r') as zip_ref:
        zip_ref.extractall(ndk_path)
    ndk_direct_folder = ""
    for subfolder in os.listdir(ndk_path):
      if subfolder.startswith("android-ndk-"):
        ndk_direct_folder = subfolder
        break
    if ndk_direct_folder:
      os.environ["ANDROID_NDK_HOME"] = os.path.abspath(os.path.join(ndk_path, ndk_direct_folder))
      logging.info("set ANDROID_NDK_HOME: %s", os.environ["ANDROID_NDK_HOME"])
    else:
      logging.warning("No valid android folder unzipped from url %s, ANDROID_NDK_HOME not overwritten", url) 
  if major_version >= 2020:
    try:
      # This is a bug from Unity: 
      # https://issuetracker.unity3d.com/issues/android-android-build-fails-when-targeting-sdk-31-and-using-build-tools-31-dot-0-0
      _run([os.environ["ANDROID_HOME"]+"/tools/bin/sdkmanager", "--uninstall", "build-tools;31.0.0"], check=False)
      logging.info("Uninstall Android build tool 31.0.0")
    except Exception as e:
      logging.info(str(e))
    
  os.environ["UNITY_ANDROID_SDK"]=os.environ["ANDROID_HOME"]
  os.environ["UNITY_ANDROID_NDK"]=os.environ["ANDROID_NDK_HOME"]
  os.environ["UNITY_ANDROID_JDK"]=os.environ["JAVA_HOME"]


def perform_in_editor_tests(dir_helper, retry_on_license_check=True):
  """Executes the testapp within the Unity Editor's play mode.

  Unity has a feature to run a project within the editor itself,
  without the user first building it into an application. This method
  will uses this feature to run the testapp within the editor.

  InEditorRunner.cs will be copied into the project. This script will
  open a specific scene, and then enter playmode.

  Args:
    dir_helper: _DirectoryHelper object for this testapp.
    retry_on_license_check (bool): If a successful license check is detected in
        the logs, restart the tests. This is because a license check will cause
        a reload of assemblies, which can mess with the tests. The second run
        will not retry if this happens again (which doesn't normally happen).

  Raises:
    RuntimeError: This error will be thrown if any of the following conditions
        are violated: (1) the tests ran to completion, logging a results
        summary; (2) the number of passing test cases is at least 1;
        (3) the number of failing test cases is 0.

  """
  logging.info("Running playmode (in-editor) tests...")
  # We override the default args, so they don't include "-quit". This
  # is needed to keep Unity open while the playmode tests run.
  arg_builder = unity_commands.UnityArgBuilder(
      dir_helper.unity_path,
      dir_helper.unity_project_dir,
      shared_args=["-batchmode", "-nographics", "-accept-apiupdate"])
  log = dir_helper.make_log_path("build_Playmode")
  arg_builder.set_log_file(log)
  run_args = arg_builder.get_args_for_method("InEditorRunner.EditorRun")
  dir_helper.copy_editor_script("InEditorRunner.cs")
  logging.info("Running in subprocess: %s", " ".join(run_args))
  open_process = subprocess.Popen(args=run_args)
  test_finished = False
  time_until_timeout = 120
  while not test_finished and time_until_timeout > 0:
    time.sleep(5)
    time_until_timeout -= 5
    if os.path.exists(log):
      with open(log) as f:
        text = f.read()
      test_finished = "All tests finished" in text
      if retry_on_license_check and "License updated successfully" in text:
        logging.info("License check caused assembly reload. Retrying tests.")
        open_process.kill()
        perform_in_editor_tests(dir_helper, retry_on_license_check=False)
        return
  open_process.kill()
  logging.info("Finished running playmode tests")

  results = test_validation.validate_results(text, test_validation.UNITY)
  if results.complete:
    if results.passes and not results.fails:  # Success
      logging.info(results.summary)
    else:  # Failed
      raise RuntimeError(results.summary)
  else:  # Generally caused by timeout or crash
    raise RuntimeError(
        "Tests did not finish running. Log tail:\n" + results.summary)

  return text


def run_xcodebuild(dir_helper, ios_config, device_type):
  """Uses xcode project generated by Unity to build an iOS binary."""
  build_output_dir = os.path.join(dir_helper.output_dir, "ios_output_"+device_type)
  _run(
      xcodebuild.get_args_for_build(
          path=dir_helper.xcode_path,
          scheme=ios_config.scheme,
          output_dir=build_output_dir,
          ios_sdk=_IOS_SDK[device_type],
          configuration=ios_config.configuration))
  if device_type == _DEVICE_REAL:
    xcodebuild.generate_unsigned_ipa(
        output_dir=build_output_dir,
        configuration=ios_config.configuration)


def _collect_integration_tests(config, testapps, root_output_dir, output_dir, artifact_name):
  """Collect testapps to dir /${output_dir}/testapps-${artifact_name}/${platform}/${api}.
  In CI, testapps will be upload as Artifacts.
  """
  testapps_artifact_dir = "testapps-" + artifact_name
  android_testapp_extension = ".apk"
  ios_testapp_dir = "ios_output_" + _DEVICE_REAL
  ios_simualtor_testapp_dir = "ios_output_" + _DEVICE_VIRTUAL
  ios_testapp_extension = ".ipa"
  ios_simualtor_testapp_extension = ".app"
  windows_testapp_dir = "WindowsTestapp" 
  macos_testapp_dir = "MacOSTestapp"
  linux_testapp_dir = "LinuxTestapp"

  android_testapp_paths = []
  ios_testapp_paths = []
  windows_testapp_paths = []
  macos_testapp_paths = []
  linux_testapp_paths = []
  for file_dir, directories, file_names in os.walk(output_dir):
    for directory in directories:
      if directory.endswith(windows_testapp_dir):
        windows_testapp_paths.append(os.path.join(file_dir, directory))
      elif directory.endswith(macos_testapp_dir):
        macos_testapp_paths.append(os.path.join(file_dir, directory))
      elif directory.endswith(linux_testapp_dir):
        linux_testapp_paths.append(os.path.join(file_dir, directory))
      elif ios_simualtor_testapp_dir in file_dir and directory.endswith(ios_simualtor_testapp_extension):
        ios_testapp_paths.append(os.path.join(file_dir, directory))
    for file_name in file_names:
      if file_name.endswith(android_testapp_extension):
        android_testapp_paths.append(os.path.join(file_dir, file_name))
      elif ios_testapp_dir in file_dir and file_name.endswith(ios_testapp_extension):
        ios_testapp_paths.append(os.path.join(file_dir, file_name))

  artifact_path = os.path.join(root_output_dir, testapps_artifact_dir)
  logging.info("Collecting artifacts to: %s", artifact_path)
  try:
    _rm_dir_safe(artifact_path)
  except OSError as e:
    logging.warning("Failed to remove directory:\n%s", e.strerror)

  _collect_integration_tests_platform(config, testapps, artifact_path, android_testapp_paths, _ANDROID)
  _collect_integration_tests_platform(config, testapps, artifact_path, ios_testapp_paths, _IOS)
  _collect_integration_tests_platform(config, testapps, artifact_path, windows_testapp_paths, _WINDOWS)
  _collect_integration_tests_platform(config, testapps, artifact_path, macos_testapp_paths, _MACOS)
  _collect_integration_tests_platform(config, testapps, artifact_path, linux_testapp_paths, _LINUX)


def _collect_integration_tests_platform(config, testapps, artifact_path, testapp_paths, platform):
  logging.info("Collecting %s artifacts from: %s", platform, testapp_paths)
  if not testapp_paths:
    return

  for testapp in testapps:
    os.makedirs(os.path.join(artifact_path, platform ,testapp))
  for path in testapp_paths:
    for testapp in testapps:
      if config.get_api(testapp).full_name in path:
        if os.path.isfile(path):
          shutil.move(path, os.path.join(artifact_path, platform ,testapp))
        else:
          shutil.move(path, os.path.join(artifact_path, platform ,testapp, os.path.basename(path)), copy_function = shutil.copytree)
        break


def _summarize_build_results(testapps, platforms, versions, failures, output_dir, artifact_name):
  """Logs a readable summary of the results of the build."""
  file_name = "build-results-" + artifact_name + ".log"
  summary = []
  summary.append("BUILD SUMMARY:")
  summary.append("TRIED TO BUILD: " + ",".join(testapps))
  summary.append("ON PLATFORMS: " + ",".join(platforms))
  summary.append("FOR VERSIONS: " + ",".join(versions))

  if not failures:
    summary.append("ALL BUILDS SUCCEEDED")
  else:
    summary.append("SOME FAILURES OCCURRED:")
    for i, failure in enumerate(failures, start=1):
      summary.append("%d: %s" % (i, failure.describe()))
  summary = "\n".join(summary)

  logging.info(summary)
  test_validation.write_summary(output_dir, summary, file_name)

  summary_json = {}
  summary_json["type"] = "build"
  summary_json["testapps"] = testapps
  summary_json["errors"] = {failure.testapp:failure.error_message for failure in failures}
  with open(os.path.join(output_dir, file_name+".json"), "a") as f:
    f.write(json.dumps(summary_json, indent=2))
  return 1 if failures else 0


def _switch_to_latest_runtime(dir_helper, arg_builder):
  """Switches .NET runtime used by project to latest."""
  dir_helper.copy_editor_script("RuntimeSwitcher.cs")
  arg_builder.set_log_file(dir_helper.make_log_path("switch_runtime"))
  _run(arg_builder.get_args_for_method("RuntimeSwitcher.SwitchToLatest"))


def _create_unity_project(dir_helper):
  """Creates the Unity project, using project settings from repo."""
  # Copy project settings from source, creating the Unity project.
  # We can't copy the source code yet, because it depends on dependencies
  # that have not yet been imported (in the Unity plugins).
  os.makedirs(dir_helper.unity_project_editor_dir)

  src = dir_helper.testapp_settings_dir
  dest = dir_helper.unity_project_settings_dir
  logging.info("Copying Unity project settings from %s to %s", src, dest)
  dir_util.copy_tree(src, dest)


# .unitypackages are the older style of packages in Unity,
# which work by importing files directly into the project.
def _import_unity_plugins(dir_helper, arg_builder):
  """Imports .unitypackage plugins into the Unity project."""
  logging.info("Importing Unity plugins (.unitypackages)...")
  for plugin_path in dir_helper.plugin_paths:
    name = os.path.splitext(os.path.basename(plugin_path))[0]
    arg_builder.set_log_file(dir_helper.make_log_path("import_" + name))
    _run(arg_builder.get_args_for_import(plugin_path))


# Unity Package Manager is Unity's newer style of packaging.
def _import_upm_packages(dir_helper, arg_builder):
  """Imports tarball packages with Unity Package Manager (UPM)."""
  dir_helper.copy_editor_script("PackageImporter.cs")
  # Note: order matters. The packages need to be installed in the same order
  # they appear in the config.
  logging.info("Importing Unity packages with UPM...")
  for package in dir_helper.upm_packages:
    name = os.path.basename(package).replace(".", "_")
    arg_builder.set_log_file(dir_helper.make_log_path("install_" + name))
    _run(
        arg_builder.get_args_for_method(
            method="PackageImporter.Import",
            method_args=["-PackageImporter.package", package]))


# In an automated context with batchmode, it was found that these
# specific assemblies were not being activated, and thus not
# available to code requiring them. Does not occur manually.
def _enable_firebase_assemblies(dir_helper, arg_builder):
  """Enables assemblies needed by Firebase."""
  dir_helper.copy_editor_script("PluginToggler.cs")
  arg_builder.set_log_file(dir_helper.make_log_path("enable_editor_plugins"))
  logging.info("Enabling Firebase assemblies...")
  _run(
      arg_builder.get_args_for_method(
          method="PluginToggler.Enable",
          method_args=[
              "-PluginToggler.plugins",
              "Firebase.Editor.dll,Google.VersionHandlerImpl"]))


def _edm4u_update(dir_helper, arg_builder):
  """Tells EDM4U (External Dependency Manager For Unity) to update."""
  arg_builder.set_log_file(dir_helper.make_log_path("versionhandler_update"))
  logging.info("Running VersionHandler.UpdateNow...")
  _run(arg_builder.get_args_for_method("Google.VersionHandler.UpdateNow"))


def _copy_unity_assets(dir_helper, files_to_ignore):
  """Copies Unity project assets from source into the project."""
  src = dir_helper.testapp_assets_dir
  dest = dir_helper.unity_project_assets_dir
  logging.info("Copying Unity project assets from %s to %s", src, dest)
  copied_files = dir_util.copy_tree(src, dest)
  for copied_file in copied_files:
    if any(name in copied_file for name in files_to_ignore):
      logging.info("Removing %s", copied_file)
      os.remove(copied_file)
  if "firestore" in dest.lower():
    logging.info("Removing firestore a) Tests b) Firebase/Editor/Builder.cs")
    dir_util.remove_tree(os.path.join(dir_helper.unity_project_assets_dir, "Tests"))
    os.remove(os.path.join(dir_helper.unity_project_assets_dir, "Firebase", "Editor", "Builder.cs"))

# The menu scene will timeout to the automated version of the app,
# while leaving an option in manual testing to select the manual version.
def _add_menu_scene(dir_helper):
  """Copies a scene to switch between manual/automated versions of the app."""
  logging.info("Adding menu scene to switch between manual/automated scenes...")
  dir_util.copy_tree(
      os.path.join(dir_helper.builder_dir, "MenuScene"),
      dir_helper.unity_project_assets_dir)


def _add_automated_test_runner(dir_helper):
  """Copies automated_testapp."""
  logging.info("Adding automated_testapp to sample...")
  shutil.copy(
      os.path.join(dir_helper.builder_dir, "automated_testapp", "AutomatedTestRunner.cs"),
      os.path.join(dir_helper.unity_project_sample_dir, "AutomatedTestRunner.cs"))
  dir_util.copy_tree(
    os.path.join(dir_helper.builder_dir, "automated_testapp", "ftl_testapp_files"), 
    os.path.join(dir_helper.unity_project_sample_code_dir, "FirebaseTestLab"))


def remove_define_from_file(path, define):
  """Removes define directive from file at path."""
  logging.info("Removing define directive %s from %s", define, path)
  _replace_in_file(path, "#define " + define + "\n", "")


def _replace_in_file(path, substring, replacement):
  """Replaces instances of substring with replacement for file at path."""
  with open(path, "r", encoding="utf-8") as f:
    text = f.read()
  text = text.replace(substring, replacement)
  with open(path, "w", encoding="utf-8") as f:
    f.write(text)


def _resolve_plugins(plugins_dir, api_config, runtime):
  """Finds paths to .unitypackages based on .NET runtime."""
  # Unity SDK contains subdirectories 'dotnet3' and 'dotnet4'
  # for Unity projects targeting .NET 3.5 or 4.6
  plugin_paths = []
  for plugin in api_config.plugins:
    runtime_subdir = "dotnet4" if runtime == _NET46 else "dotnet3"
    plugin_paths.append(os.path.join(plugins_dir, runtime_subdir, plugin))
  return plugin_paths


def _resolve_upm_packages(packages_dir, api_config):
  """Finds paths to upm packages, resolving globs."""
  if not packages_dir:
    return []
  package_globs = api_config.upm_packages
  if not package_globs:
    raise RuntimeError(
        "Specified use_local_packages flag, but no packages specified in"
        " main config for testapp %s." % api_config.name)
  resolved_paths = []
  for package in package_globs:
    # Use glob to resolve the version wildcard in the packages.
    full_glob = os.path.join(packages_dir, package)
    glob_matches = glob.glob(full_glob)
    if not glob_matches:
      raise RuntimeError("No match for package glob %s" % full_glob)
    # Multiple matches means we probably have multiple different versions
    # present, so it's unsafe to pick an arbirtary one.
    if len(glob_matches) > 1:
      raise RuntimeError("Multiple matches for package glob %s" % full_glob)
    resolved_paths.append(glob_matches[0])
  return resolved_paths


def get_output_dir(base_dir, unity_version_string, runtime, timestamp):
  """Assembles the output directory for this build configuration."""
  config = "Unity" + unity_version_string + "-NET" + runtime
  # Note: empty timestamp is okay.
  return os.path.join(base_dir + timestamp, config)


def get_timestamp():
  """Gets the current time as a path-friendly string.

  Returns:
      A string representing the current time, consisting only of
      letters, numbers, underscores, and dashes, making it safe to
      use in a path.

  """
  return datetime.datetime.now().strftime("%Y_%m_%d-%H_%M_%S")


def validate_testapps(apis, api_configs):
  """Ensures the chosen apis are valid, based on the config."""
  if "all" in apis:
    return [key for key in api_configs]

  for api in apis:
    if api not in api_configs:
      raise RuntimeError("Testapp given as flag not found in config: %s" % api)
  return apis


def validate_platforms(platforms):
  """Ensures platforms are valid."""
  if _IOS in platforms and platform.system() != "Darwin":
    logging.warning("iOS requested on non-Mac OS, which is not yet supported.")
    platforms.remove(_IOS)
  return platforms


def get_desktop_platform():
  """This will get the Unity build target corresponding to the running OS."""
  # The motivation for this was to allow a CI job to specify "--p Desktop"
  # for all three operating systems, and build the correct target for each.
  operating_system = platform.system()
  if operating_system == "Windows":
    return _WINDOWS
  elif operating_system == "Linux":
    return _LINUX
  elif operating_system == "Darwin":
    return _MACOS
  else:
    raise RuntimeError("Unexpected OS: %s" % operating_system)


def get_runtime(version, force_latest_runtime):
  """Determines the runtime for this Unity version.

  Will return the default runtime for this version, unless the latest runtime
  is forced. Will always return a valid runtime for this version.

  Args:
    version: The version of Unity being used. Determines the default runtime,
        and compatibility with the latest runtime if forced. Versions prior
        to 2017 are incompatible with the latest runtime.
    force_latest_runtime: Forces the latest .NET runtime to be used.

  Returns:
    The runtime to use with this version of Unity, as a string.

  Raises:
    ValueError: If forcing latest runtime with an incompatible version.

  """
  version = unity_version.UnityVersion(version)
  runtime = _NET46 if force_latest_runtime else version.default_runtime
  if not version.supports_runtime(runtime):
    raise ValueError(
        "Version %s does not support runtime %s" % (version, runtime))
  return runtime


def get_xcode_name(version, force_xcode_project):
  """Determines the name of the xcode project/workspace.

  The main thing to determine is the extension, which will be either
  .xcworkspace or .xcodeproj depending on whether we're using a project
  or a workspace. The workspace is generally preferred, so xcodeproj will only
  be used if the version of Unity doesn't support it (before 5.6) or if we're
  forcing it.

  Args:
    version: The version of Unity being used. Versions prior to 5.6 do not
        produce workspaces, so an xcodeproj is used.
    force_xcode_project: If this flag is set, an xcodeproj will be used even
        if the version of Unity supports it.

  Returns:
    The name (not path), with extension, of the xcode project or
    workspace used for this build.

  """
  version = unity_version.UnityVersion(version)
  if version.generates_workspace and not force_xcode_project:
    return "Unity-iPhone.xcworkspace"
  else:
    return "Unity-iPhone.xcodeproj"


def get_version_path_map(versions, unity_folder_override):
  """Constructs a map of version: path_to_corresponding_executable."""
  return dict([(version, unity_finder.get_path(version, unity_folder_override))
               for version in versions])


def update_unity_versions(version_path_map, log=logging.error):
  """Returns versions with valid paths. Raises RuntimeError if all invalid."""
  valid_versions = [version for version in version_path_map
                    if version_path_map[version]]
  for version in set(version_path_map.keys()) - set(valid_versions):
    log("Unity installation not found for version %s" % version)
  if not valid_versions:
    raise RuntimeError("None of the specified versions of Unity were found.")
  return valid_versions


def _handle_readonly_file(func, path, excinfo):
  """Function passed into shutil.rmtree to handle Access Denied error"""
  os.chmod(path, stat.S_IWRITE)
  func(path)  # will re-throw if a different error occurrs


def _rm_dir_safe(directory_path):
  """Removes directory at given path. No error if dir doesn't exist."""
  logging.info("Deleting %s...", directory_path)
  try:
    shutil.rmtree(directory_path, onerror=_handle_readonly_file)
  except OSError as e:
    # There are two known cases where this can happen:
    # The directory doesn't exist (FileNotFoundError)
    # A file in the directory is open in another process (PermissionError)
    logging.warning("Failed to remove directory:\n%s", e.strerror)


def _fix_path(path):
  """Expands ~, normalizes slashes, and converts relative paths to absolute."""
  if not path:
    return path
  return os.path.abspath(os.path.expanduser(path))


def _run(args, timeout=3000, capture_output=False, text=None, check=True):
  """Executes a command in a subprocess."""
  logging.info("Running in subprocess: %s", " ".join(args))
  return subprocess.run(
      args=args,
      timeout=timeout,
      capture_output=capture_output,
      text=text,
      check=check)


@attr.s(frozen=True, eq=False)
class Failure(object):
  """Holds context for the failure of a testapp to build/run."""
  testapp = attr.ib()
  description = attr.ib()
  error_message = attr.ib()

  def describe(self):
    return "%s, %s: %s" % (self.testapp, self.description, self.error_message)


@attr.s(frozen=True, eq=False)
class _SetupOptions(object):
  """Options related to the setup of the Unity project."""
  switch_to_latest = attr.ib()  # Switch to .NET 4.6, instead of legacy .NET 3.5
  testapp_file_filters = attr.ib()  # Matching testapp files will not be copied
  enable_firebase = attr.ib()
  enable_edm4u = attr.ib()


@attr.s(frozen=True, eq=False)
class _IosConfig(object):
  """Plain data container for iOS-specific configuration."""
  ios_sdk = attr.ib()
  bundle_id = attr.ib()
  use_unity_symlinks = attr.ib()  # Unity will symlink libraries into xc project
  configuration = attr.ib()
  scheme = attr.ib()


# This class is intended mainly to cut down on boilerplate involving paths,
# particularly those that are used in multiple places.
@attr.s(frozen=True, eq=False)
class _DirectoryHelper(object):
  """Contains various paths for a single testapp."""

  # Local paths to resources on the runner's machine
  unity_path = attr.ib()  # Path to Unity executable
  plugin_paths = attr.ib()  # List of .unitypackages (full paths)
  upm_packages = attr.ib()  # (Optional) Unity packages for UPM (full paths)

  # Paths to resources in a repo (git or google3)
  root_dir = attr.ib()  # Root directory of the git or google3 repo
  builder_dir = attr.ib()  # Path to the directory containing editor scripts
  testapp_dir = attr.ib()  # Path to the testapp's root directory
  testapp_assets_dir = attr.ib()  # Testapp Unity assets dir
  testapp_settings_dir = attr.ib()  # Testapp Unity settings dir

  # Output paths on the runner's machine generated by this tool
  output_dir = attr.ib()
  xcode_path = attr.ib()  # Path to the xcode project generated by Unity
  unity_project_dir = attr.ib()  # Outer Unity project directory
  unity_project_assets_dir = attr.ib()  # Unity assets subdirectory
  unity_project_editor_dir = attr.ib()  # Path to an Assets/Editor subdirectory
  unity_project_settings_dir = attr.ib()  # Unity settings subdirectory
  unity_project_sample_dir = attr.ib()  # Unity sample subdirectory
  unity_project_sample_code_dir = attr.ib()  # Unity sample subdirectory

  @classmethod
  def from_config(
      cls, root_dir, api_config, unity_path, output_dir, builder_dir,
      unity_plugins, upm_packages, xcode_name):
    """Create a directory helper for a particular configuration.

    One of unity_plugins or upm_packages should be specified.
    If both are specified, upm_packages will be used.

    Args:
      root_dir: Full path to the root of the repo.
      api_config: APIConfig for this API.
      unity_path: Path to the Unity executable.
      output_dir: All build artifacts and intermediate files will be
          placed/built in this directory.
      builder_dir: Absolute path to directory containing editor scripts.
      unity_plugins: Sequence of paths to .unitypackages.
      upm_packages: Sequence of paths to UPM package tars.
      xcode_name: xcode_name: Name of the xcode project or workspace. Should end
          with either .xcproj or .xcworkspace.

    Returns:
      Fully configured directory helper.

    """
    testapp_dir = os.path.join(root_dir, api_config.testapp_path)

    output_dir = os.path.join(output_dir, api_config.full_name)
    unity_dir = os.path.join(output_dir, _UNITY_PROJECT_NAME)

    return cls(
        root_dir=root_dir,
        unity_path=unity_path,
        plugin_paths=unity_plugins,
        upm_packages=upm_packages,

        builder_dir=builder_dir,
        testapp_dir=testapp_dir,
        testapp_assets_dir=os.path.join(testapp_dir, "Assets"),
        testapp_settings_dir=os.path.join(testapp_dir, "ProjectSettings"),

        output_dir=output_dir,
        xcode_path=os.path.join(output_dir, "testapp_xcode", xcode_name),
        unity_project_dir=unity_dir,
        unity_project_assets_dir=os.path.join(unity_dir, "Assets"),
        unity_project_editor_dir=os.path.join(unity_dir, "Assets", "Editor"),
        unity_project_sample_dir=os.path.join(unity_dir,  "Assets", "Firebase", "Sample"),
        unity_project_sample_code_dir=os.path.join(unity_dir,  "Assets", "Firebase", "Sample", api_config.captial_name),
        unity_project_settings_dir=os.path.join(unity_dir, "ProjectSettings"))

  # This cuts down a fair bit of boilerplate, since we copy several C#
  # editor scripts. We could merge these C# scripts together, but many of
  # the scripts have specific requirements (e.g. Unity version, or presence
  # of specific libraries).
  def copy_editor_script(self, name):
    """Copies a C# editor script to the Unity project.

    This will copy a file of the given name from the builder_directory
    defined in the configuration file, into an appropriate subdirectory
    of the Unity project for editor scripts. Editor scripts in Unity
    must be in an 'Editor' subdirectory somewhere within Assets.

    Args:
      name (str): File name, e.g. "InEditorRunner.cs".

    Returns:
      (str) Absolute path to the copied script.
    """
    shutil.copy(
        os.path.join(self.builder_dir, name), self.unity_project_editor_dir)
    return os.path.join(self.unity_project_editor_dir, name)

  # Cuts down on boilerplate, and acts as a source
  # of truth for the location of generated logs in a given project.
  def make_log_path(self, name):
    """Generates a path for a log with this extensionless name.

    Args:
      name (str): File name for the log, e.g. "build_android".

    Returns:
      (str) Absolute path.

    """
    return os.path.join(self.output_dir, name + ".log")


if __name__ == "__main__":
  app.run(main)
