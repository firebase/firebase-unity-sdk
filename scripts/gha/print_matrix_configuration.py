#!/usr/bin/env python

# Copyright 2021 Google
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
""" Fetch and print Github workflow matrix values for a given configuration.

This script holds the configurations (standard, expanded) for all of our
Github worklows and prints a string in the format that can be easily parsed
in Github workflows.

Usage examples:
# Query value for matrix (default) parameter "unity_version".
python scripts/gha/print_matrix_configuration.py -k unity_version

# Override the value for "unity_version" for "integration_tests"
python scripts/gha/print_matrix_configuration.py -w integration_tests
        -o my_custom_unity_version -k unity_version

# Query value for config parameter "apis" for "integration_tests" workflow.
python scripts/gha/print_matrix_configuration.py -c -w integration_tests -k apis

# Override the value for config parameters "apis" for integration_tests
python scripts/gha/print_matrix_configuration.py -c -w integration_tests
        -o my_custom_api -k apis
"""

import argparse
import json
import logging
import platform
import itertools


DEFAULT_WORKFLOW = "desktop"
EXPANDED_KEY = "expanded"
MINIMAL_KEY = "minimal"

_WINDOWS = "Windows"
_MACOS = "macOS"
_LINUX = "Linux"

PARAMETERS = {
  "integration_tests": {
    "matrix": {
      "build_os": [],
      "unity_versions": ["2020"],
      "mobile_devices": ["android_target", "emulator_latest", "ios_target", "simulator_target"],
      "mobile_test_on": ["real"],

      MINIMAL_KEY: {
        "platforms": ["Playmode"],
      },

      EXPANDED_KEY: {
        "build_os": ["macos-latest","windows-latest"],
        "unity_versions": ["2020", "2019", "2018"],
        "mobile_test_on": ["real", "virtual"],
        "mobile_devices": ["android_target", "emulator_latest", "ios_target", "simulator_target"],
      }
    },
    "config": {
      "platforms": "Windows,macOS,Linux,Android,iOS,tvOS,Playmode",
      "apis": "analytics,auth,crashlytics,database,dynamic_links,firestore,functions,installations,messaging,remote_config,storage",
    }
  },
}

# Plese use Unity LTS versions: https://unity3d.com/unity/qa/lts-releases
# To list avaliable packages, install u3d, and use cmd "u3d available -u $unity_version -p"
# The packages below is valid only if Unity Hub is not installed.
# TODO(@sunmou): Add Android Setting. e.g. NDK_VERSION
UNITY_SETTINGS = {
  "2020": {
    _WINDOWS: {
      "version": "2020.3.34f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": None, "macOS": ["Mac-mono"], "Linux": ["Linux-mono"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r19-windows-x86_64.zip"
    },
    _MACOS: {
      "version": "2020.3.34f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": None, "Linux": ["Linux-mono"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r19-darwin-x86_64.zip"
    },
    _LINUX: {
      "version": "2020.3.29f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": ["Mac-mono"], "Linux": None}
    }
  },
  "2019": {
    _WINDOWS: {
      "version": "2019.4.39f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": None, "macOS": ["Mac-mono"], "Linux": ["Linux-mono"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r19-windows-x86_64.zip"
    },
    _MACOS: {
      "version": "2019.4.39f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": None, "Linux": ["Linux-mono"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r19-darwin-x86_64.zip"
    },
    _LINUX: {
      "version": "2019.4.40f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": ["Mac-mono"], "Linux": None}
    }
  },
  "2018": {
    _WINDOWS: {
      "version": "2018.4.36f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-il2cpp"], "macOS": ["Mac-mono"], "Linux": ["Linux"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r16b-windows-x86_64.zip"
    },
    _MACOS: {
      "version": "2018.4.36f1",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": ["Mac-il2cpp"], "Linux": ["Linux"]},
      "ndk": "https://dl.google.com/android/repository/android-ndk-r16b-darwin-x86_64.zip"
    },
    _LINUX: {
      "version": "2018.3.0f2",
      "packages": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "tvOS": ["appletv"], "Windows": ["Windows-mono"], "macOS": ["Mac-mono"], "Linux": None}
    }
  },
}

BUILD_CONFIGS = ["Unity Version(s)", "Build OS(s)", "Platform(s)", "Test Device(s)"]

TEST_DEVICES = {
  "android_min": {"platform": "Android", "type": "real", "device": "model=Nexus10,version=19"},
  "android_target": {"platform": "Android", "type": "real", "device": "model=blueline,version=28"},
  "android_latest": {"platform": "Android", "type": "real", "device": "model=oriole,version=33"},
  "emulator_min": {"platform": "Android", "type": "virtual", "image": "system-images;android-18;google_apis;x86"},
  "emulator_target": {"platform": "Android", "type": "virtual", "image": "system-images;android-28;google_apis;x86_64"},
  "emulator_latest": {"platform": "Android", "type": "virtual", "image": "system-images;android-30;google_apis;x86_64"},
  "emulator_32bit": {"platform": "Android", "type": "virtual", "image": "system-images;android-30;google_apis;x86"},
  "ios_min": {"platform": "iOS", "type": "real", "device": "model=iphone8,version=11.4"},
  "ios_target": {"platform": "iOS", "type": "real", "device": "model=iphone11,version=13.6"},
  "ios_latest": {"platform": "iOS", "type": "real", "device": "model=iphone8,version=14.7"},
  "simulator_min": {"platform": "iOS", "type": "virtual", "name": "iPhone 6", "version": "11.4"},
  "simulator_target": {"platform": "iOS", "type": "virtual", "name": "iPhone 8", "version": "14.5"},
  "simulator_latest": {"platform": "iOS", "type": "virtual", "name": "iPhone 11", "version": "14.4"},
}


def get_os():
  """Current Operation System"""
  if platform.system() == 'Windows':
    return _WINDOWS
  elif platform.system() == 'Darwin':
    return _MACOS
  elif platform.system() == 'Linux':
    return _LINUX


def get_unity_path(version):
  """Returns the path to this version of Unity, as generated by U3D."""
  # These are the path formats assumed by U3D, as documented here:
  # https://github.com/DragonBox/u3d
  unity_full_version = UNITY_SETTINGS[version][get_os()]["version"]
  if platform.system() == "Windows":
    return "/c/Program Files/Unity_%s" % unity_full_version
  elif platform.system() == "Darwin":
    return "/Applications/Unity_%s" % unity_full_version
  elif platform.system() == 'Linux':
    return "/opt/unity-editor-%s" % unity_full_version


def get_value(workflow, matrix_type, parm_key, config_parms_only=False):
  """ Fetch value from configuration

  Args:
      workflow (str): Key corresponding to the github workflow.
      test_matrix (str): Use EXPANDED_KEY or MINIMAL_KEY configuration for the workflow?
      parm_key (str): Exact key name to fetch from configuration.
      config_parms_only (bool): Search in config blocks if True, else matrix
                                blocks.

  Raises:
      KeyError: Raised if given key is not found in configuration.

  Returns:
      (str|list): Matched value for the given key.
  """
  # Search for a given key happens in the following sequential order
  # Minimal/Expanded block (if test_matrix) -> Standard block

  parm_type_key = "config" if config_parms_only else "matrix"
  workflow_block = PARAMETERS.get(workflow)
  if workflow_block:
    if matrix_type and matrix_type in workflow_block["matrix"]:
      if parm_key in workflow_block["matrix"][matrix_type]:
        return workflow_block["matrix"][matrix_type][parm_key]
    return workflow_block[parm_type_key][parm_key]

  else:
    raise KeyError("Parameter key: '{0}' of type '{1}' not found "\
                   "for workflow '{2}' (test_matrix = {3}) .".format(parm_key,
                                                                parm_type_key,
                                                                workflow,
                                                                matrix_type))


def filter_devices(devices, device_type, device_platform):
  """ Filter device by device_type
  """
  filtered_value = filter(lambda device: 
                          TEST_DEVICES.get(device).get("type") in device_type 
                          and TEST_DEVICES.get(device).get("platform") in device_platform, devices)
  return list(filtered_value)  


# TODO(sunmou): add auto_diff feature
def filter_values_on_diff(parm_key, value, auto_diff):
  return value


def filterdesktop_os(platform):
  os_platform_dict = {"windows-latest":"Windows", "macos-latest":"macOS", "ubuntu-latest":"Linux"}
  filtered_value = filter(lambda os: os_platform_dict.get(os) in platform, os_platform_dict.keys())
  return list(filtered_value)  


def filter_mobile_platform(platform):
  # tvOS isn't mobile, but it behaves like iOS.
  mobile_platform = ["Android", "iOS", "tvOS"]
  filtered_value = filter(lambda p: p in platform, mobile_platform)
  return list(filtered_value)  


def filter_build_platforms(platforms):
  platforms = platforms.split(",")
  build_platforms = []
  build_platforms.extend(filter_mobile_platform(platforms))
  # testapps from different desktop platforms are built in one job.
  desktop_platforms = ','.join(list(filter(lambda p: p in platforms, ["Windows", "macOS", "Linux"])))
  if desktop_platforms:
    build_platforms.append(desktop_platforms)
  return build_platforms


def print_value(value, config_parms_only=False):
  """ Print Json formatted string that can be consumed in Github workflow."""
  # Eg: for lists,
  # print(json.dumps) ->
  # ["2017.4.37f1", "2019.2.8f1", "2021.1.9f1"]
  # print(repr(json.dumps)) ->
  # '["2017.4.37f1", "2019.2.8f1", "2021.1.9f1"]'

  # Eg: for strings
  # print(json.dumps) -> "flame"
  # print(repr(json.dumps)) -> '"flame"'
  if config_parms_only:
    print(value)
  else:
    print(json.dumps(value))


def get_testapp_build_matrix(matrix_type, unity_versions, platforms, build_os, ios_sdk):
  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: platforms = filter_build_platforms(get_value("integration_tests", matrix_type, "platforms", True))
  else: platforms = filter_build_platforms(platforms)
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")
  if matrix_type: ios_sdk = get_value("integration_tests", matrix_type, "mobile_test_on")

  li = list(itertools.product(unity_versions, platforms, build_os))
  matrix = {"include": []}
  for l in li:
    unity_version = l[0]
    platform = l[1]
    os = l[2] if l[2] else ("macos-latest" if (platform=="iOS" or platform=="tvOS") else "windows-latest")
    
    if (platform=="iOS" or platform=="tvOS"):
      if os=="macos-latest":
        for s in ios_sdk:
          matrix["include"].append({"unity_version": unity_version, "platform": platform, "os": os, "ios_sdk": s})
    else:
      matrix["include"].append({"unity_version": unity_version, "platform": platform, "os": os, "ios_sdk": ""})
  return matrix


def get_testapp_playmode_matrix(matrix_type, unity_versions, platforms, build_os):
  if "Playmode" not in platforms: return ""
  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")

  li = list(itertools.product(unity_versions, build_os))
  matrix = {"include": []}
  for l in li:
    unity_version = l[0]
    os = l[1] if l[1] else "windows-latest"
    matrix["include"].append({"unity_version": unity_version, "os": os})
  return matrix


def get_testapp_test_matrix(matrix_type, unity_versions, platforms, build_os, mobile_device_types):
  print(matrix_type)
  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: platforms = get_value("integration_tests", matrix_type, "platforms")
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")
  if matrix_type: mobile_device_types = get_value("integration_tests", matrix_type, "mobile_test_on")

  li = list(itertools.product(unity_versions, platforms, build_os))
  matrix = {"include": []}
  for l in li:
    unity_version = l[0]
    platform = l[1]
    build_os = l[2] if l[2] else ("macos-latest" if (platform=="iOS" or platform=="tvOS") else "windows-latest")

    if platform in ["Windows", "macOS", "Linux"]:
      test_os = _get_test_os(platform)
      matrix["include"].append({"unity_version": unity_version, "platform": platform, "build_os": build_os, "test_os": test_os, "test_device": "", "device_type": ""})
    else:
      mobile_devices = get_value("integration_tests", matrix_type, "mobile_devices")
      for mobile_device in mobile_devices:
        device_type = TEST_DEVICES.get(mobile_device).get("type")
        if device_type in mobile_device_types:
          test_os = _get_test_os(platform, device_type)
          matrix["include"].append({"unity_version": unity_version, "platform": platform, "build_os": build_os, "test_os": test_os, "test_device": mobile_device, "device_type": device_type})

  return matrix


def _get_test_os(platform, mobile_device_type=""):
  """Current Operation System"""
  if platform == 'Windows':
    return "windows-latest"
  elif platform == 'Linux' or ((platform=="Android" or platform=="iOS") and mobile_device_type == 'real'):
    return "ubuntu-latest"
  else: 
    return "macos-latest"


def main():
  args = parse_cmdline_args()
  if args.unity_version:
    if args.parm_key == "unity_path":
      print(get_unity_path(args.unity_version))
    else:
      print(UNITY_SETTINGS[args.unity_version][get_os()].get(args.parm_key))
    return 

  if args.override:
    # If it is matrix parm, convert CSV string into a list
    if not args.config:
      args.override = args.override.split(',')

    print_value(args.override, args.config)
    return

  if args.build_matrix:
    print(get_testapp_build_matrix(args.matrix_type, args.unity_versions.split(','), args.platforms, args.os.split(','), args.mobile_test_on.split(',')))
    return
  if args.playmode_matrix:
    print(get_testapp_playmode_matrix(args.matrix_type, args.unity_versions.split(','), args.platforms, args.os.split(',')))
    return
  if args.test_matrix:
    print(get_testapp_test_matrix(args.matrix_type, args.unity_versions.split(','), args.platforms.split(','), args.os.split(','), args.mobile_test_on.split(',')))
    return

  value = get_value(args.workflow, args.matrix_type, args.parm_key, args.config)
  if args.workflow == "integration_tests" and args.parm_key == "mobile_device":
    value = filter_devices(devices=value, device_type=args.device_type, device_platform=args.device_platform)
  if args.auto_diff:
    value = filter_values_on_diff(args.parm_key, value, args.auto_diff)
  print_value(value, args.config)


def parse_cmdline_args():
  parser = argparse.ArgumentParser(description='Query matrix and config parameters used in Github workflows.')
  parser.add_argument('-c', '--config', action='store_true', help='Query parameter used for Github workflow/dispatch configurations.')
  parser.add_argument('-w', '--workflow', default=DEFAULT_WORKFLOW, help='Config key for Github workflow.')
  parser.add_argument('-m', '--matrix_type', default="", help='Use minimal/expanded/default matrix')
  parser.add_argument('-k', '--parm_key', help='Print the value of specified key from matrix or config maps.')
  parser.add_argument('-a', '--auto_diff', metavar='BRANCH', help='Compare with specified base branch to automatically set matrix options')
  parser.add_argument('-o', '--override', help='Override existing value with provided value')
  parser.add_argument('-t', '--device_type', default=['real', 'virtual'], help='Test on which type of mobile devices. Used with "-k $device_type -t $mobile_test_on"')
  parser.add_argument('-p', '--device_platform', default=['Android', 'iOS'], help='Test on which type of mobile devices. Used with "-k $device_type -p $platform"')
  parser.add_argument('-u', '--unity_version', help='Get unity setting based on unity major version. Used with "-k $unity_setting -u $unity_major_version"')
  parser.add_argument('-build_matrix', action='store_true', help='Get the build matrix')
  parser.add_argument('-playmode_matrix', action='store_true', help='Get the playmode matrix')
  parser.add_argument('-test_matrix', action='store_true', help='Get the test matrix')
  parser.add_argument('-unity_versions')
  parser.add_argument('-platforms')
  parser.add_argument('-os')
  parser.add_argument('-mobile_test_on')
  # parser.add_argument('-get_device_platform', action='store_true', help='Get the device platform, used with -k $device')
  # parser.add_argument('-get_ftl_device', action='store_true', help='Get the ftl test device, used with -k $device')
  # parser.add_argument('-desktop_os', type=bool, default=False, help='Get desktop test OS. Use with "-k $build_platform -desktop_os=1"')
  # parser.add_argument('-mobile_platform', type=bool, default=False, help='Get mobile test platform. Use with "-k $build_platform -mobile_platform=1"')
  args = parser.parse_args()
  return args


if __name__ == '__main__':
  main()
