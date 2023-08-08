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
"""Build SDK for certain platform into zip file


Example usage:
  python build_zips.py --platform=macos --apis=auth --targets=firestore
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

SUPPORT_PLATFORMS = ("linux", "macos", "windows", "ios", "tvos", "android")
SUPPORT_TARGETS = [
    "analytics", "app_check", "auth", "crashlytics", "database", "dynamic_links",
    "firestore", "functions", "installations", "messaging", "remote_config",
    "storage"
]
TVOS_SUPPORT_TARGETS = [
    "analytics", "app_check", "auth", "crashlytics", "database", "firestore",
    "functions", "installations", "messaging", "remote_config", "storage"
]
SUPPORT_DEVICE = ["device", "simulator"]

IOS_SUPPORT_ARCHITECTURE = ["arm64", "x86_64"]
IOS_DEVICE_ARCHITECTURE = ["arm64"]
IOS_SIMULATOR_ARCHITECTURE = ["arm64", "x86_64"]

IOS_CONFIG_DICT = {
    "device": {
        "architecture": ["arm64"],
        "ios_platform_location": "iPhoneOS.platform",
        "osx_sysroot": "iphoneos",
    },
    "simulator": {
        "architecture": ["arm64", "x86_64"],
        "ios_platform_location": "iPhoneSimulator.platform",
        "osx_sysroot": "iphonesimulator",
    }
}

TVOS_CONFIG_DICT = {
    "device": {
        "architecture": ["arm64"],
        "ios_platform_location": "AppleTvOS.platform",
        "osx_sysroot": "appletvos",
        "toolchain_platform": "TVOS",
    },
    "simulator": {
        "architecture": ["x86_64"],
        "ios_platform_location": "AppleTvSimulator.platform",
        "osx_sysroot": "appletvsimulator",
        "toolchain_platform": "SIMULATOR_TVOS",
    }
}

ANDROID_SUPPORT_ARCHITECTURE = ["armeabi-v7a", "arm64-v8a", "x86", "x86_64"]

MACOS_SUPPORT_ARCHITECTURE = ["x86_64", "arm64"]

g_target_architectures = []
g_cpp_sdk_realpath = ""

FLAGS = flags.FLAGS
flags.DEFINE_string(
    'platform', None,
    'Which platform to build SDK on. Required one entry from ({})'.format(
        ",".join(SUPPORT_PLATFORMS)))
flags.DEFINE_string(
    'unity_root', None,
    "The root dir for Unity Engine. If not set, cmake will try to guess in the default unity installation location."
)
flags.DEFINE_multi_string(
    "targets", None,
    ("Target product to includes in the build. List items pick from"
     "({})".format(",".join(SUPPORT_TARGETS))))
flags.DEFINE_string(
    "apis", None,
    ("One single string that has multiple targets combined with ','. \n"
     "Usage example is --apis=auth,firestore,remote_config. \n"
     "Exclusive with parameter targets. List items pick from"
     "({})".format(",".join(SUPPORT_TARGETS))))
flags.DEFINE_multi_string(
    "device", None,
    "To build on device or simulator. If not set, built on both. Only take affect for ios and android build"
)
flags.DEFINE_multi_string(
    "architecture", None, "Which architectures in build on. Ignored on tvOS.\n"
    "For iOS device ({}).\n"
    "For iOS simulator ({}).\n"
    "For android ({}).\n"
    "For MacOS ({})".format(",".join(IOS_CONFIG_DICT["device"]["architecture"]),
                               ",".join(
        IOS_CONFIG_DICT["simulator"]["architecture"]),
        ",".join(ANDROID_SUPPORT_ARCHITECTURE),
        ",".join(MACOS_SUPPORT_ARCHITECTURE)))
flags.DEFINE_multi_string('cmake_extras', None,
                          "Any extra arguments wants to pass into cmake.")
flags.DEFINE_bool("clean_build", False, "Whether to clean the build folder")
flags.DEFINE_bool("use_boringssl", False, "Build with BoringSSL instead of openSSL.")
flags.DEFINE_bool("verbose", False, "If verbose, cmake build with DCMAKE_VERBOSE_MAKEFILE=1")
flags.DEFINE_string("swig_dir", None, "If pass in swig dir directly rather than find swig by cmake")
flags.DEFINE_bool("gen_documentation_zip", False, "Also generate a zip file containing files to document")
flags.DEFINE_bool("gha", False, "True if the build is triggered by Github Action.")
flags.DEFINE_bool("gen_swig_only", False, "Should it only generate swig, skipping building libraries")

def get_build_path(platform, clean_build=False):
  """Get the folder that cmake configure and build in.

    Args:
      platform: linux, macos, windows, ios, android.
      clean_build: If True, delete the build folder and build from clean.

    Returns:
      The folder path to build sdk inside.
  """
  platform_path = os.path.join(os.getcwd(), platform + "_unity")
  if os.path.exists(platform_path) and clean_build:
    shutil.rmtree(platform_path)
  if not os.path.exists(platform_path):
    os.makedirs(platform_path)
  return platform_path


def get_cpp_folder_args(source_path):
  """Get the cmake args to pass in local Firebase C++ SDK folder.
    If not found, will download from Firebase C++ git repo.

    Args:
      source_path: root source folder cd back.

    Returns:
      cmake args with the folder path of local Firebase C++ SDK. 
      Empty string if not found.
  """
  global g_cpp_sdk_realpath
  cpp_folder = os.path.join(os.getcwd(), "..", "firebase-cpp-sdk")
  if os.path.exists(cpp_folder):
    g_cpp_sdk_realpath = os.path.realpath(cpp_folder)
    return "-DFIREBASE_CPP_SDK_DIR=" + g_cpp_sdk_realpath
  else:
    return ""


def get_unity_engine_folder_args(unity_root):
  """Get the cmake args to pass in Unity engine folder. If not passed in 
     through the parameter, cmake will try to find using logic in 
     cmake/FindUnity.cmake

    Args:
      unity_root: folder path of the Unity Engine.
    Returns:
      camke args with the folder path of Unity Engine. Empty string if not set.
  """
  if unity_root and os.path.exists(unity_root):
    return "-DUNITY_ROOT_DIR=" + unity_root
  else:
    return ""


def get_targets_args(targets):
  """Get the cmake args to pass in built targets of Firebase products.

    Args:
      targets: list of target names defined in SUPPORT_TARGETS.
    Returns:
      camke args included targets.
  """
  result_args = []
  
  support_targets = SUPPORT_TARGETS
  if is_tvos_build():
    support_targets = TVOS_SUPPORT_TARGETS
    if not targets:
      targets = TVOS_SUPPORT_TARGETS
    else:
      if 'dynamic_links' in targets:
        logging.warning("Dynamic Links is not supported on tvOS. " +
          "Removing it from the api build list.")
        targets.remove('dynamic_links')

  if targets:
    # check if all the entries are valid
    for target in targets:
      if target not in support_targets:
        raise app.UsageError(
            'Wrong target "{}", please pick from {}'.format(
                target, ",".join(support_targets)))
    for target in SUPPORT_TARGETS:
      if target in targets:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=ON")
      else:
        result_args.append("-DFIREBASE_INCLUDE_" + target.upper() +
                           "=OFF")
  logging.debug("get target args are:" + ",".join(result_args))
  return result_args


def get_tvos_args(source_path):
  """Get the cmake args for tvOS platforms.

    Args:
      source_path: root source folder to find toolchain file.
    Returns:
      camke args for iOS platform.
  """
  result_args = []
  toolchain_path = os.path.join(source_path, "cmake", "unity_tvos.cmake")
  result_args.append("-DCMAKE_TOOLCHAIN_FILE=" + toolchain_path)
  # check device input
  global g_target_devices
  if FLAGS.device:
    for device in FLAGS.device:
      if device not in SUPPORT_DEVICE:
        raise app.UsageError(
            'Wrong device type {}, please pick from {}'.format(
                device, ",".join(SUPPORT_DEVICE)))
    g_target_devices = FLAGS.device
  else:
    g_target_devices = SUPPORT_DEVICE

  return result_args

def get_ios_args(source_path):
  """Get the cmake args for iOS platform specific.

    Args:
      source_path: root source folder to find toolchain file.
    Returns:
      camke args for iOS platform.
  """
  result_args = []
  toolchain_path = os.path.join(source_path, "cmake", "unity_ios.cmake")
  # toolchain args is required
  result_args.append("-DCMAKE_TOOLCHAIN_FILE=" + toolchain_path)
  # check device input
  if FLAGS.device:
    for device in FLAGS.device:
      if device not in SUPPORT_DEVICE:
        raise app.UsageError(
            'Wrong device type {}, please pick from {}'.format(
                device, ",".join(SUPPORT_DEVICE)))
    devices = FLAGS.device
  else:
    devices = SUPPORT_DEVICE

  global g_target_architectures
  # check architecture input
  if (len(devices) > 1):
    archs_to_check = IOS_SUPPORT_ARCHITECTURE
  else:
    archs_to_check = IOS_CONFIG_DICT[devices[0]]["architecture"]
  if FLAGS.architecture:
    for arch in FLAGS.architecture:
      if arch not in archs_to_check:
        raise app.UsageError(
            'Wrong architecture "{}" for device type {}, please pick from {}'.format(
                arch, ",".join(devices), ",".join(archs_to_check)))
    g_target_architectures = FLAGS.architecture
  else:
    g_target_architectures = archs_to_check

  if len(g_target_architectures) != len(IOS_SUPPORT_ARCHITECTURE):
    # Need to override only if the archs are not default
    result_args.append("-DCMAKE_OSX_ARCHITECTURES=" +
                       ";".join(g_target_architectures))

  if len(devices) != len(SUPPORT_DEVICE):
    # Need to override if only passed in device or simulator
    result_args.append("-DCMAKE_OSX_SYSROOT=" +
                       IOS_CONFIG_DICT[devices[0]]["osx_sysroot"])
    result_args.append("-DCMAKE_XCODE_EFFECTIVE_PLATFORMS=" +
                       "-"+IOS_CONFIG_DICT[devices[0]]["osx_sysroot"])
    result_args.append("-DIOS_PLATFORM_LOCATION=" +
                       IOS_CONFIG_DICT[devices[0]]["ios_platform_location"])
  return result_args


def get_android_args():
  """Get the cmake args for android platform specific.

    Returns:
      camke args for android platform.
  """
  result_args = []
  # get Android NDK path
  system_android_ndk_home = os.getenv('ANDROID_NDK_HOME')
  if system_android_ndk_home:
    toolchain_path = os.path.join(
        system_android_ndk_home, "build", "cmake", "android.toolchain.cmake")
    result_args.append("-DCMAKE_TOOLCHAIN_FILE=" + toolchain_path)
    logging.info("Use ANDROID_NDK_HOME(%s) cmake toolchain(%s)",
                 system_android_ndk_home, toolchain_path)
  else:
    system_android_home = os.getenv('ANDROID_HOME')
    if system_android_home:
      toolchain_files = glob.glob(os.path.join(system_android_home,
                                               "**", "build", "cmake", "android.toolchain.cmake"), recursive=True)
      if toolchain_files:
        result_args.append("-DCMAKE_TOOLCHAIN_FILE=" + toolchain_files[0])
      logging.info("Use ANDROID_HOME(%s) cmake toolchain (%s)",
                   system_android_home, toolchain_files[0])
    else:
      raise app.UsageError(
          'Neither ANDROID_NDK_HOME nor ANDROID_HOME is set.')

  # get architecture setup
  global g_target_architectures
  if FLAGS.architecture:
    for arch in FLAGS.architecture:
      if arch not in ANDROID_SUPPORT_ARCHITECTURE:
        raise app.UsageError(
            'Wrong architecture "{}", please pick from {}'.format(
                arch, ",".join(ANDROID_SUPPORT_ARCHITECTURE)))
    g_target_architectures = FLAGS.architecture
  else:
    g_target_architectures = ANDROID_SUPPORT_ARCHITECTURE

  if len(g_target_architectures) == 1:
    result_args.append("-DANDROID_ABI="+g_target_architectures[0])

  result_args.append("-DFIREBASE_ANDROID_BUILD=true")
  # android default to build release.
  result_args.append("-DCMAKE_BUILD_TYPE=release")
  result_args.append("-DANDROID_STL=c++_shared")
  return result_args

def make_android_multi_arch_build(cmake_args, merge_script):
  """Make android build for different architectures, and then combine them together.

    Args:
      cmake_args: cmake arguments used to build each architecture.
      merge_script: script path to merge the srcaar files.
  """
  global g_target_architectures
  current_folder = os.getcwd()
  # build multiple archictures
  for arch in g_target_architectures:
    if not os.path.exists(arch):
      os.makedirs(arch)
    build_dir = os.path.join(current_folder, arch)
    cmake_args.append("-DANDROID_ABI="+arch)
    subprocess.call(cmake_args, cwd=build_dir)
    subprocess.call("make", cwd=build_dir)

    cmake_pack_args = [
      "cpack",
      ".",
    ]
    subprocess.call(cmake_pack_args, cwd=build_dir)

  # merge them
  zip_base_name = ""
  srcarr_list = []
  base_temp_dir = tempfile.mkdtemp()
  for arch in g_target_architectures:
    # find *Android.zip in subfolder architecture
    arch_zip_path = glob.glob(os.path.join(arch, "*Android.zip"))
    if not arch_zip_path:
      logging.error("No *Android.zip generated for architecture %s", arch)
      return
    if not zip_base_name:
      # first architecture, so extract to the final temp folder. The following
      # srcaar files will merge to the ones in this folder.
      zip_base_name = arch_zip_path[0]
      with zipfile.ZipFile(zip_base_name) as zip_file:
        zip_file.extractall(base_temp_dir)
      srcarr_list.extend(glob.glob(os.path.join(
          base_temp_dir, "**", "*.srcaar"), recursive=True))
    else:
      temporary_dir = tempfile.mkdtemp()
      # from the second *Android.zip, we only need to extract *.srcaar files to operate the merge.
      with zipfile.ZipFile(arch_zip_path[0]) as zip_file:
        for file in zip_file.namelist():
          if file.endswith('.srcaar'):
            zip_file.extract(file, temporary_dir)
            logging.debug("Unpacked file %s from zip file %s to %s",
                          file, arch_zip_path, temporary_dir)

      for srcaar_file in srcarr_list:
        srcaar_name = os.path.basename(srcaar_file)
        matching_files = glob.glob(os.path.join(
            temporary_dir, "**", "*"+srcaar_name), recursive=True)
        if matching_files:
          merge_args = [
              "python",
              merge_script,
              "--inputs=" + srcaar_file,
              "--inputs=" + matching_files[0],
              "--output=" + srcaar_file,
          ]
          subprocess.call(merge_args)
          logging.debug("merging %s to %s", matching_files[0], srcaar_file)

  # achive the temp folder to the final firebase_unity-<version>-Android.zip
  final_zip_path = os.path.join(current_folder, os.path.basename(zip_base_name))
  with zipfile.ZipFile(final_zip_path, "w", allowZip64=True) as zip_file:
    for current_root, _, filenames in os.walk(base_temp_dir):
      for filename in filenames:
        fullpath = os.path.join(current_root, filename)
        zip_file.write(fullpath, os.path.relpath(fullpath, base_temp_dir))
  logging.info("Generated Android multi-arch (%s) zip %s",
               ",".join(g_target_architectures), final_zip_path)

def get_windows_args():
  """Get the cmake args for windows platform specific.

    Returns:
      cmake args for windows platform.
  """
  result_args = []
  result_args.append('-G Visual Studio 16 2019')
  result_args.append('-A x64') # TODO flexibily for x32
  result_args.append("-DFIREBASE_PYTHON_HOST_EXECUTABLE:FILEPATH=%s" % sys.executable)
  # Use a newer version of the Windows SDK, as the default one has build issues with grpc
  if FLAGS.gha:
    result_args.append('-DCMAKE_SYSTEM_VERSION=10.0.20348.0')
  return result_args

def get_macos_args():
  """Get the cmake args for macos platform specific.

    Returns:
      cmake args for macos platform.
  """
  result_args = []
  global g_target_architectures
  # get architecture setup global g_target_architectures
  if FLAGS.architecture:
    for arch in FLAGS.architecture:
      if arch not in MACOS_SUPPORT_ARCHITECTURE:
        raise app.UsageError(
            'Wrong architecture "{}", please pick from {}'.format(
                arch, ",".join(MACOS_SUPPORT_ARCHITECTURE)))
    g_target_architectures = FLAGS.architecture
  else:
    # Default to selecting none, as it will likely only be able to build the local architecture.
    g_target_architectures = []
  if len(g_target_architectures) == 1:
    result_args.append('-DCMAKE_OSX_ARCHITECTURES='+g_target_architectures[0])
  
  return result_args

def make_macos_arch(arch, cmake_args):
  """Make the macos build for the given architecture.
     Assumed to be called from the build directory.

    Args:
      arch: The architecture to build for.
      cmake_args: Additional cmake arguments to use.
  """
  if not os.path.exists(arch):
    os.makedirs(arch)
  build_dir = os.path.join(os.getcwd(), arch)
  cmake_args.append('-DCMAKE_OSX_ARCHITECTURES='+arch)
  subprocess.call(cmake_args, cwd=build_dir)
  subprocess.call('make', cwd=build_dir)
  subprocess.call(['cpack', '.'], cwd=build_dir)

def make_macos_multi_arch_build(cmake_args):
  """Make macos build for different architectures, and then combine them together

    Args:
      cmake_args: cmake arguments used to build each architecture.
  """
  global g_target_architectures
  # build multiple architectures
  current_folder = os.getcwd()
  threads = []
  for arch in g_target_architectures:
    t = threading.Thread(target=make_macos_arch, args=(arch, cmake_args))
    t.start()
    threads.append(t)

  for t in threads:
    t.join()
  
  # Merge the different zip files together, using lipo on the bundle files
  zip_base_name = ""
  bundle_list = []
  base_temp_dir = tempfile.mkdtemp()
  for arch in g_target_architectures:
    # find *Darwin.zip in subfolder architecture
    arch_zip_path = glob.glob(os.path.join(arch, "*Darwin.zip"))
    if not arch_zip_path:
      logging.error("No *Darwin.zip generated for architecture %s", arch)
      return
    if not zip_base_name:
      # first architecture, so extract to the final temp folder. The following
      # bundle files will merge to the ones in this folder.
      zip_base_name = arch_zip_path[0]
      with zipfile.ZipFile(zip_base_name) as zip_file:
        zip_file.extractall(base_temp_dir)
      bundle_list.extend(glob.glob(os.path.join(
          base_temp_dir, "**", "*.bundle"), recursive=True))
    else:
      temporary_dir = tempfile.mkdtemp()
      # from the second *Darwin.zip, we only need to extract *.bundle files to operate the merge.
      with zipfile.ZipFile(arch_zip_path[0]) as zip_file:
        for file in zip_file.namelist():
          if file.endswith('.bundle'):
            zip_file.extract(file, temporary_dir)
            logging.debug("Unpacked file %s from zip file %s to %s",
                          file, arch_zip_path, temporary_dir)

      for bundle_file in bundle_list:
        bundle_name = os.path.basename(bundle_file)
        matching_files = glob.glob(os.path.join(
            temporary_dir, "**", "*"+bundle_name), recursive=True)
        if matching_files:
          merge_args = [
              "lipo",
              bundle_file,
              matching_files[0],
              "-create",
              "-output",
              bundle_file,
          ]
          subprocess.call(merge_args)
          logging.debug("merging %s to %s", matching_files[0], bundle_file)

  # achive the temp folder to the final firebase_unity-<version>-Darwin.zip
  final_zip_path = os.path.join(current_folder, os.path.basename(zip_base_name))
  with zipfile.ZipFile(final_zip_path, "w", allowZip64=True) as zip_file:
    for current_root, _, filenames in os.walk(base_temp_dir):
      for filename in filenames:
        fullpath = os.path.join(current_root, filename)
        zip_file.write(fullpath, os.path.relpath(fullpath, base_temp_dir))
  logging.info("Generated Darwin (MacOS) multi-arch (%s) zip %s",
               ",".join(g_target_architectures), final_zip_path)


def configure_tvos_target(device, arch, cmake_args):
  """Configure the tvos build for the given device and architecture.
     Assumed to be called from the build directory.

    Args:
      device: Building for device or simulator.
      arch: The architecture to build for.
      cmake_args: Additional cmake arguments to use.

    Returns:
      The directory that the project is configured in.
  """
  build_args = cmake_args.copy()
  build_args.append("-DCMAKE_OSX_ARCHITECTURES=" + arch)
  build_args.append("-DCMAKE_OSX_SYSROOT=" +
                      TVOS_CONFIG_DICT[device]["osx_sysroot"])
  build_args.append("-DCMAKE_XCODE_EFFECTIVE_PLATFORMS=" +
                       "-"+TVOS_CONFIG_DICT[device]["osx_sysroot"])
  build_args.append("-DIOS_PLATFORM_LOCATION=" +
                       TVOS_CONFIG_DICT[device]["ios_platform_location"])
  build_args.append("-DPLATFORM=" +
                       TVOS_CONFIG_DICT[device]["toolchain_platform"])

  if not os.path.exists(arch):
    os.makedirs(arch)
  build_dir = os.path.join(os.getcwd(), arch)
  subprocess.call(build_args, cwd=build_dir)
  return build_dir

def make_tvos_target(build_dir):
  """Builds the previously configured cmake project in the given directory.

    Args:
      The full path to the directory to perform the build in.
  """
  subprocess.call('make', cwd=build_dir)
  subprocess.call(['cpack', '.'], cwd=build_dir)

def make_tvos_multi_arch_build(cmake_args):
  """Make tvos build for different architectures, and then combine
    them together into a fat libraries and a single zip file.

    Args:
      cmake_args: cmake arguments used to build each architecture.
  """
  global g_target_devices
  current_folder = os.getcwd()
  target_architectures = []

  # build multiple architectures
  current_folder = os.getcwd()
  threads = []
  for device in g_target_devices:
    for arch in TVOS_CONFIG_DICT[device]["architecture"]:
      target_architectures.append(arch)
      # Run the configure step sequentially, since they can clobber the shared Cocoapod cache
      build_dir = configure_tvos_target(device, arch, cmake_args)
      # Run the builds in parallel, since they can be
      t = threading.Thread(target=make_tvos_target, args=(build_dir,))
      t.start()
      threads.append(t)

  # Wait for the builds to be finished
  for t in threads:
    t.join()

  # Merge the different zip files together, using lipo on the library files
  zip_base_name = ""
  library_list = []
  base_temp_dir = tempfile.mkdtemp()
  for arch in target_architectures:
    # find *.zip in subfolder architecture
    arch_zip_path = glob.glob(os.path.join(arch, "*-tvOS.zip"))
    if not arch_zip_path:
      logging.error("No *-tvOS.zip generated for architecture %s", arch)
      return
    if not zip_base_name:
      # first architecture, so extract to the final temp folder. The following
      # library files will merge to the ones in this folder.
      zip_base_name = arch_zip_path[0]
      with zipfile.ZipFile(zip_base_name) as zip_file:
        zip_file.extractall(base_temp_dir)
      library_list.extend(glob.glob(os.path.join(
          base_temp_dir, "**", "*.a"), recursive=True))
    else:
      temporary_dir = tempfile.mkdtemp()
      # from the second *-tvOS.zip, we only need to extract *.a files to operate the merge.
      with zipfile.ZipFile(arch_zip_path[0]) as zip_file:
        for file in zip_file.namelist():
          if file.endswith('.a'):
            zip_file.extract(file, temporary_dir)

      for library_file in library_list:
        library_name = os.path.basename(library_file)
        matching_files = glob.glob(os.path.join(
            temporary_dir, "Plugins", "tvOS", "Firebase", library_name))
        if matching_files:
          merge_args = [
              "lipo",
              library_file,
              matching_files[0],
              "-create",
              "-output",
              library_file,
          ]
          subprocess.call(merge_args)
          logging.info("merging %s to %s", matching_files[0], library_name)

  # archive the temp folder to the final firebase_unity-<version>-tvOS.zip
  final_zip_path = os.path.join(current_folder, os.path.basename(zip_base_name))
  with zipfile.ZipFile(final_zip_path, "w", allowZip64=True) as zip_file:
    for current_root, _, filenames in os.walk(base_temp_dir):
      for filename in filenames:
        fullpath = os.path.join(current_root, filename)
        zip_file.write(fullpath, os.path.relpath(fullpath, base_temp_dir))
  logging.info("Generated Darwin (tvOS) multi-arch (%s) zip %s",
               ",".join(g_target_architectures), final_zip_path)

def gen_documentation_zip():
  """If the flag was enabled, builds the zip file containing source files to document.
  """
  if not FLAGS.gen_documentation_zip and not FLAGS.gen_swig_only:
    return
  cpack_args = [
    'cpack',
    '-D', 'CPACK_COMPONENTS_ALL=documentation',
    '-D', 'CPACK_COMPONENTS_ALL_IN_ONE_PACKAGE=ON',
    '-D', 'CPACK_ARCHIVE_FILE_NAME=documentation_sources'
  ]
  subprocess.call(cpack_args)

def is_android_build():
  """
    Returns:
      If the build platform is android
  """
  return FLAGS.platform == "android"

def is_ios_build():
  """
    Returns:
      If the build platform is ios
  """
  return FLAGS.platform == "ios"

def is_tvos_build():
  """
    Returns:
      If the build platform is tvos
  """
  return FLAGS.platform == "tvos"

def is_windows_build():
  """
    Returns:
      If the build platform is windows
  """
  return FLAGS.platform == "windows"

def is_macos_build():
  """
    Returns:
      If the build platform is macos
  """
  return FLAGS.platform == "macos"

def is_linux_build():
  """
    Returns:
      If the build platform is linux
  """
  return FLAGS.platform == "linux"


def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  platform = FLAGS.platform
  if platform not in SUPPORT_PLATFORMS:
    raise app.UsageError('Wrong platform "{}", please pick from {}'.format(
        platform, ",".join(SUPPORT_PLATFORMS)))

  source_path = os.getcwd()
  cmake_cpp_folder_args = get_cpp_folder_args(source_path)
  build_path = get_build_path(platform, FLAGS.clean_build)
  if is_android_build() and g_cpp_sdk_realpath:
    # For android build, if we find local cpp folder,
    # We trigger the cpp android build first.
    subprocess.call("./gradlew", cwd=g_cpp_sdk_realpath)

  os.chdir(build_path)
  cmake_setup_args = [
      "cmake",
      source_path
  ]
  
  if FLAGS.verbose:
    cmake_setup_args.append('-DCMAKE_VERBOSE_MAKEFILE=1')

  if is_windows_build():
    # windows args need to happen right after target path
    cmake_setup_args.extend(get_windows_args())

  cmake_setup_args.extend([   
    "-DFIREBASE_INCLUDE_UNITY=ON",
    "-DFIREBASE_UNITY_BUILD_TESTS=ON",
    "-DFIREBASE_CPP_BUILD_STUB_TESTS=ON",
  ])

  if FLAGS.gha:
    cmake_setup_args.append("-DFIREBASE_GITHUB_ACTION_BUILD=ON")

  unity_root_args = get_unity_engine_folder_args(FLAGS.unity_root)
  if unity_root_args:
    cmake_setup_args.append(unity_root_args)
  if cmake_cpp_folder_args:
    cmake_setup_args.append(cmake_cpp_folder_args)
  
  if FLAGS.apis and FLAGS.targets:
    raise app.UsageError('Parameter apis and targets are exclusive, please only pick one.')
  
  api_target_list = []
  if FLAGS.apis:
    api_target_list = FLAGS.apis.split(',')
  elif FLAGS.targets:
    api_target_list = FLAGS.targets
  target_arg_list = get_targets_args(api_target_list)

  if FLAGS.swig_dir:
    cmake_setup_args.append("-DSWIG_DIR="+FLAGS.swig_dir)
  
  if target_arg_list:
    cmake_setup_args.extend(target_arg_list)

  if FLAGS.cmake_extras:
    cmake_setup_args.extend(FLAGS.cmake_extras)

  if FLAGS.use_boringssl:
    cmake_setup_args.append("-DFIREBASE_USE_BORINGSSL=ON")
  else:
    cmake_setup_args.append("-DFIREBASE_USE_BORINGSSL=OFF")

  if is_ios_build():
    cmake_setup_args.extend(get_ios_args(source_path))
  elif is_tvos_build():
    cmake_setup_args.extend(get_tvos_args(source_path))
  elif is_android_build():
    cmake_setup_args.extend(get_android_args())
  elif is_macos_build():
    cmake_setup_args.extend(get_macos_args())

  if FLAGS.gen_swig_only:
    cmake_setup_args.append("-DFIREBASE_GENERATE_SWIG_ONLY=ON")

  global g_target_architectures
  logging.info("cmake_setup_args is: " + " ".join(cmake_setup_args))
  if is_android_build() and len(g_target_architectures) > 1:
    logging.info("Build android with multiple architectures %s",
                 ",".join(g_target_architectures))
    # android multi architecture build is a bit different
    make_android_multi_arch_build(cmake_setup_args, os.path.join(
        source_path, "aar_builder", "merge_aar.py"))
  elif is_macos_build() and len(g_target_architectures) > 1:
    logging.info("Build macos with multiple architectures %s",
                 ",".join(g_target_architectures))
    make_macos_multi_arch_build(cmake_setup_args)
  elif is_tvos_build():
    make_tvos_multi_arch_build(cmake_setup_args)
  else:
    subprocess.call(cmake_setup_args)
    if (not FLAGS.gen_swig_only):
      if is_windows_build():
        # no make command in windows. TODO make config passable
        subprocess.call("cmake --build .  --config Release")
      else:
        subprocess.call("make")

      cmake_pack_args = [
        "cpack",
        ".",
      ]
      subprocess.call(cmake_pack_args)
    else:
      subprocess.call(["cmake", "--build", ".", "--target", "firebase_swig_targets"])

    gen_documentation_zip()

  os.chdir(source_path)


if __name__ == '__main__':
  flags.mark_flag_as_required("platform")
  app.run(main)
