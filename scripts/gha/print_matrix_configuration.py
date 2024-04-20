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
import platform
import itertools


DEFAULT_WORKFLOW = "desktop"
EXPANDED_KEY = "expanded"
MINIMAL_KEY = "minimal"

# platform
WINDOWS = "Windows"
MACOS = "macOS"
LINUX = "Linux"
ANDROID = "Android"
IOS = "iOS"
TVOS = "tvOS"
PLAYMODE = "Playmode"

# GitHub Runner
WINDOWS_RUNNER = "windows-latest"
MACOS_RUNNER = "macos-latest"
LINUX_RUNNER = "ubuntu-latest"

PARAMETERS = {
  "integration_tests": {
    "matrix": {
      "unity_versions": ["2020"],
      "build_os": [""],
      "platforms": [WINDOWS, MACOS, LINUX, ANDROID, IOS, TVOS, PLAYMODE],
      "mobile_devices": ["android_target", "ios_target", "simulator_target", "tvos_simulator"],
      "mobile_test_on": ["real"],

      MINIMAL_KEY: {
        "platforms": [PLAYMODE],
      },

      EXPANDED_KEY: {
        "build_os": [MACOS_RUNNER,WINDOWS_RUNNER],
        "unity_versions": ["2020"],
        "mobile_test_on": ["real", "virtual"],
      }
    },
    "config": {
      "apis": "analytics,auth,crashlytics,database,dynamic_links,firestore,functions,installations,messaging,remote_config,storage",
    }
  },
}

BUILD_CONFIGS = ["Unity Version(s)", "Build OS(s)", "Platform(s)", "Test Device(s)"]

TEST_DEVICES = {
  "android_min": {"platform": ANDROID, "type": "real",
                  "device": [
                    "model=1610,version=23",  # vivo 1610
                    "model=hammerhead,version=23", # Nexus 5
                    "model=harpia,version=23",  # Moto G Play
                  ]},
  "android_target": {"platform": ANDROID, "type": "real",
                     "device": [
                       "model=blueline,version=28", # Pixel 3
                       "model=gts3lltevzw,version=28",  # Galaxy Tab S3
                       "model=SH-01L,version=28",  # AQUOS sense2 SH-01L
                     ]},
  "android_latest": {"platform": ANDROID, "type": "real",
                     "device": [
                       "model=oriole,version=33",  # Pixel 6
                       "model=panther,version=33",  # Pixel 7
                       "model=lynx,version=33",  # Pixel 7a
                       "model=cheetah,version=33",  # Pixel 7 Pro
                       "model=felix,version=33",  # Pixel Fold
                       "model=tangorpro,version=33",  # Pixel Tablet
                       "model=gts8uwifi,version=33",  # Galaxy Tab S8 Ultra
                       "model=b0q,version=33",  # Galaxy S22 Ultra
                       "model=b4q,version=33",  # Galaxy Z Flip4
                     ]},
  "emulator_ftl_target": {"platform": ANDROID, "type": "real",
                          "device": [
                            "model=Pixel2,version=28",
                            "model=Pixel2.arm,version=28",
                            "model=MediumPhone.arm,version=28",
                            "model=MediumTablet.arm,version=28",
                            "model=SmallPhone.arm,version=28",
                          ]},
  "emulator_target": {"platform": ANDROID, "type": "virtual", "image": "system-images;android-28;google_apis;x86_64"},
  "emulator_latest": {"platform": ANDROID, "type": "virtual", "image": "system-images;android-30;google_apis;x86_64"},
  "emulator_32bit": {"platform": ANDROID, "type": "virtual", "image": "system-images;android-30;google_apis;x86"},
  "ios_min": {"platform": IOS, "type": "real",
              "device": [
                # Slightly different OS versions because of limited FTL selection.
                "model=iphone8,version=14.7",
                "model=iphone11pro,version=14.7",
                "model=iphone12pro,version=14.8",
              ]},
  "ios_target": {"platform": IOS, "type": "real",
                 "device": [
                   # Slightly different OS versions because of limited FTL selection.
                   "model=iphone13pro,version=15.7",
                   "model=iphone8,version=15.7",
                 ]},
  "ios_latest": {"platform": IOS, "type": "real",
                 "device": [
                   "model=iphone14pro,version=16.6",
                   "model=iphone11pro,version=16.6",
                   "model=iphone8,version=16.6",
                   "model=ipad10,version=16.6",
                 ]},
  "simulator_min": {"platform": IOS, "type": "virtual", "name": "iPhone 8", "version": "15.2"},
  "simulator_target": {"platform": IOS, "type": "virtual", "name": "iPhone 12", "version": "16.1"},
  "simulator_latest": {"platform": IOS, "type": "virtual", "name": "iPhone 12", "version": "16.2"},
  "tvos_simulator": {"platform": TVOS, "type": "virtual", "name": "Apple TV", "version": "16.1"},
}


def get_os():
  """Current Operation System"""
  if platform.system() == 'Windows':
    return WINDOWS
  elif platform.system() == 'Darwin':
    return MACOS
  elif platform.system() == 'Linux':
    return LINUX


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


def filter_non_desktop_platform(platform):
  mobile_platform = [ANDROID, IOS, TVOS]
  filtered_value = filter(lambda p: p in platform, mobile_platform)
  return list(filtered_value)


def filter_build_platforms(platforms):
  build_platforms = []
  build_platforms.extend(filter_non_desktop_platform(platforms))
  # testapps from different desktop platforms are built in one job.
  desktop_platforms = ','.join(list(filter(lambda p: p in platforms, [WINDOWS, MACOS, LINUX])))
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
  # matrix structure:
  # {
  #   "unity_version":"2020",
  #   "platform":"iOS",
  #   "os":"macos-latest",
  #   "ios_sdk":"real"
  # }

  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: platforms = filter_build_platforms(get_value("integration_tests", matrix_type, "platforms"))
  else: platforms = filter_build_platforms(platforms)
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")
  if matrix_type: ios_sdk = get_value("integration_tests", matrix_type, "mobile_test_on")

  # generate base matrix: combinations of (unity_versions, platforms, build_os)
  l = list(itertools.product(unity_versions, platforms, build_os))
  if not l: return ""

  matrix = {"include": []}
  for li in l:
    unity_version = li[0]
    platform = li[1]
    os = li[2] if li[2] else (MACOS_RUNNER if (platform in [IOS, TVOS]) else WINDOWS_RUNNER)

    # TODO: Remove this when we can get it working on GHA again
    # Skip the MacOS + Android combo, because it has been having configuration issues on the GHA machines
    if platform==ANDROID and os==MACOS_RUNNER:
      continue

    if platform in [IOS, TVOS]:
      # for iOS, tvOS platforms, exclude non macOS build_os
      if os==MACOS_RUNNER:
        for s in ios_sdk:
          # skip tvOS build for real devices
          if platform==TVOS and s=="real":
            continue
          matrix["include"].append({"unity_version": unity_version, "platform": platform, "os": os, "ios_sdk": s})
    else:
      # for Desktop, Android platforms, set value "NA" for ios_sdk setting
      matrix["include"].append({"unity_version": unity_version, "platform": platform, "os": os, "ios_sdk": "NA"})
  return matrix


def get_testapp_playmode_matrix(matrix_type, unity_versions, platforms, build_os):
  # matrix structure:
  # {
  #   "unity_version":"2020",
  #   "os":"windows-latest",
  # }

  if PLAYMODE not in platforms: return ""
  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")

  l = list(itertools.product(unity_versions, build_os))
  matrix = {"include": []}
  for li in l:
    unity_version = li[0]
    os = li[1] if li[1] else WINDOWS_RUNNER
    matrix["include"].append({"unity_version": unity_version, "os": os})
  return matrix


def get_testapp_test_matrix(matrix_type, unity_versions, platforms, build_os, mobile_device_types):
  # matrix structure:
  # {
  #   "unity_version":"2020",
  #   "platform":"Android",
  #   "build_os":"windows-latest",
  #   "test_os":"ubuntu-latest",
  #   "test_device":"android_target",
  #   "device_detail":"model=blueline,version=28", # secondary info
  #   "device_type":"real",   # secondary info
  #   "ios_sdk": "NA"
  # }

  if matrix_type: unity_versions = get_value("integration_tests", matrix_type, "unity_versions")
  if matrix_type: platforms = get_value("integration_tests", matrix_type, "platforms")
  if PLAYMODE in platforms: platforms.remove(PLAYMODE)
  if matrix_type: build_os = get_value("integration_tests", matrix_type, "build_os")
  if matrix_type: mobile_device_types = get_value("integration_tests", matrix_type, "mobile_test_on")

  # generate base matrix: combinations of (unity_versions, platforms, build_os)
  l = list(itertools.product(unity_versions, platforms, build_os))
  if not l: return ""

  matrix = {"include": []}
  for li in l:
    unity_version = li[0]
    platform = li[1]
    build_os = li[2] if li[2] else (MACOS_RUNNER if (platform in [IOS, TVOS]) else WINDOWS_RUNNER)

    if platform in [WINDOWS, MACOS, LINUX]:
      test_os = _get_test_os(platform)
      matrix["include"].append({"unity_version": unity_version, "platform": platform, "build_os": build_os, "test_os": test_os, "test_device": "github_runner", "device_detail": "NA", "device_type": "NA", "ios_sdk": "NA"})
    else:
      # for iOS, tvOS platforms, exclude non macOS build_os
      if platform in [IOS, TVOS] and build_os!=MACOS_RUNNER:
        continue
      mobile_devices = get_value("integration_tests", matrix_type, "mobile_devices")
      for mobile_device in mobile_devices:
        device_detail = TEST_DEVICES.get(mobile_device).get("device")
        if device_detail:
          device_detail = ';'.join(device_detail)
        else:
          device_detail = "NA"
        device_type = TEST_DEVICES.get(mobile_device).get("type")
        device_platform = TEST_DEVICES.get(mobile_device).get("platform")
        # testapp & test device must match. e.g. iOS app only runs on iOS device, and cannot run on Android or tvOS devices
        if device_platform == platform and device_type in mobile_device_types:
          test_os = _get_test_os(platform, device_type)
          ios_sdk = device_type if device_platform in [IOS, TVOS] else "NA"
          matrix["include"].append({"unity_version": unity_version, "platform": platform, "build_os": build_os, "test_os": test_os, "test_device": mobile_device, "device_detail": device_detail, "device_type": device_type, "ios_sdk": ios_sdk})

  return matrix


def _get_test_os(platform, mobile_device_type=""):
  # Desktop platform test on their OS respectivly.
  # Mobile platform test on Linux machine if we run tests on FTL, else Mac machine if we run tests on simulators
  if platform == 'Windows':
    return WINDOWS_RUNNER
  elif platform == 'Linux' or (platform in [IOS, ANDROID] and mobile_device_type == 'real'):
    return LINUX_RUNNER
  else:
    return MACOS_RUNNER


def main():
  args = parse_cmdline_args()

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
  parser.add_argument('-playmode_matrix', action='store_true', help='Generate the playmode matrix for integration_test workflow')
  parser.add_argument('-build_matrix', action='store_true', help='Generate the build matrix for integration_test workflow')
  parser.add_argument('-test_matrix', action='store_true', help='Generate the test matrix for integration_test workflow')
  parser.add_argument('-unity_versions', help='Use with -build_matrix/-test_matrix/-playmode_matrix')
  parser.add_argument('-platforms', help='Use with -build_matrix/-test_matrix/-playmode_matrix')
  parser.add_argument('-os', help='Use with -build_matrix/-test_matrix/-playmode_matrix')
  parser.add_argument('-mobile_test_on', help='Use with -build_matrix/-test_matrix/-playmode_matrix')
  args = parser.parse_args()
  return args


if __name__ == '__main__':
  main()
