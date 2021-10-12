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
import os
import re
import subprocess
import sys

from integration_testing import config_reader


DEFAULT_WORKFLOW = "desktop"
EXPANDED_KEY = "expanded"
MINIMAL_KEY = "minimal"

PARAMETERS = {
  "integration_tests": {
    "matrix": {
      "unity_version": ["2019.4.30f1"],
      "android_device": ["android_target", "emulator_target"],
      "ios_device": ["ios_target", "simulator_target"],

      MINIMAL_KEY: {
        "platform": ["Linux"],
        "apis": "firestore"
      },

      EXPANDED_KEY: {
        "unity_version": ["2020.3.19f1", "2019.4.30f1", "2018.4.36f1", "2017.4.40f1"],
        "android_device": ["android_target", "android_latest", "emulator_target", "emulator_latest", "emulator_32bit"],
        "ios_device": ["ios_min", "ios_target", "ios_latest", "simulator_min", "simulator_target", "simulator_latest"],
      }
    },
    "config": {
      "platform": "Windows,macOS,Linux,Android,iOS",
      "apis": "analytics,auth,crashlytics,database,dynamic_links,functions,installations,messaging,remote_config,storage",
      "mobile_test_on": "real,virtual"
    }
  },
}

# Plese use Unity LTS versions: https://unity3d.com/unity/qa/lts-releases
# To list avaliable packages, install u3d, and use cmd "u3d available -u $unity_version -p"
# The packages below is valid only if Unity Hub is not installed.
UNITY_PACKAGES = {
  "2020.3.19f1": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "Windows": ["Windows-mono"], "macOS": None, "Linux": ["Linux-mono"]},
  "2019.4.30f1": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "Windows": ["Windows-mono"], "macOS": None, "Linux": ["Linux-mono"]},
  "2018.4.36f1": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "Windows": ["Windows-mono"], "macOS": None, "Linux": ["Linux"]},
  "2017.4.40f1": {"Default": ["Unity"], "Android": ["Android"], "iOS": ["Ios"], "Windows": ["Windows"], "macOS": None, "Linux": ["Linux"]}
}

TEST_DEVICES = {
  "android_min": {"type": "real", "model": "Nexus10", "version": "19"},
  "android_target": {"type": "real", "model": "blueline", "version": "28"},
  "android_latest": {"type": "real", "model": "flame", "version": "29"},
  "emulator_min": {"type": "virtual", "image": "system-images;android-18;google_apis;x86"},
  "emulator_target": {"type": "virtual", "image": "system-images;android-28;google_apis;x86_64"},
  "emulator_latest": {"type": "virtual", "image": "system-images;android-30;google_apis;x86_64"},
  "emulator_32bit": {"type": "virtual", "image": "system-images;android-30;google_apis;x86"},
  "ios_min": {"type": "real", "model": "iphone8", "version": "11.4"},
  "ios_target": {"type": "real", "model": "iphone8plus", "version": "12.0"},
  "ios_latest": {"type": "real", "model": "iphone11", "version": "13.6"},
  "simulator_min": {"type": "virtual", "name": "iPhone 6", "version": "11.4"},
  "simulator_target": {"type": "virtual", "name": "iPhone 8", "version": "12.0"},
  "simulator_latest": {"type": "virtual", "name": "iPhone 11", "version": "14.4"},
}


def get_value(workflow, test_matrix, parm_key, config_parms_only=False):
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
    if test_matrix and test_matrix in workflow_block["matrix"]:
      if parm_key in workflow_block["matrix"][test_matrix]:
        return workflow_block["matrix"][test_matrix][parm_key]
    
    return workflow_block[parm_type_key][parm_key]

  else:
    raise KeyError("Parameter key: '{0}' of type '{1}' not found "\
                   "for workflow '{2}' (test_matrix = {3}) .".format(parm_key,
                                                                parm_type_key,
                                                                workflow,
                                                                test_matrix))


def filter_devices(devices, device_type):
  """ Filter device by device_type
  """
  filtered_value = filter(lambda device: TEST_DEVICES.get(device).get("type") in device_type, devices)
  return list(filtered_value)  


def print_value(value):
  """ Print Json formatted string that can be consumed in Github workflow."""
  # Eg: for lists,
  # print(json.dumps) ->
  # ["2017.4.37f1", "2019.2.8f1", "2021.1.9f1"]
  # print(repr(json.dumps)) ->
  # '["2017.4.37f1", "2019.2.8f1", "2021.1.9f1"]'

  # Eg: for strings
  # print(json.dumps) -> "flame"
  # print(repr(json.dumps)) -> '"flame"'

  print(json.dumps(value))


# TODO(sunmou): add auto_diff feature
def filter_values_on_diff(parm_key, value, auto_diff):
  return value


def main():
  args = parse_cmdline_args()
  if args.override:
    # If it is matrix parm, convert CSV string into a list
    if not args.config:
      args.override = args.override.split(',')

    print_value(args.override)
    return

  if args.device:
    print(TEST_DEVICES.get(args.parm_key).get("type"))
    return 

  if args.expanded:
    test_matrix = EXPANDED_KEY
  elif args.minimal:
    test_matrix = MINIMAL_KEY
  else:
    test_matrix = ""
  value = get_value(args.workflow, test_matrix, args.parm_key, args.config)
  if args.workflow == "integration_tests" and (args.parm_key == "android_device" or args.parm_key == "ios_device"):
    value = filter_devices(value, args.device_type)
  if args.auto_diff:
    value = filter_values_on_diff(args.parm_key, value, args.auto_diff)
  print_value(value)


def parse_cmdline_args():
  parser = argparse.ArgumentParser(description='Query matrix and config parameters used in Github workflows.')
  parser.add_argument('-c', '--config', action='store_true', help='Query parameter used for Github workflow/dispatch configurations.')
  parser.add_argument('-w', '--workflow', default=DEFAULT_WORKFLOW, help='Config key for Github workflow.')
  parser.add_argument('-m', '--minimal', type=bool, default=False, help='Use minimal matrix')
  parser.add_argument('-e', '--expanded', type=bool, default=False, help='Use expanded matrix')
  parser.add_argument('-k', '--parm_key', required=True, help='Print the value of specified key from matrix or config maps.')
  parser.add_argument('-a', '--auto_diff', metavar='BRANCH', help='Compare with specified base branch to automatically set matrix options')
  parser.add_argument('-o', '--override', help='Override existing value with provided value')
  parser.add_argument('-d', '--device', action='store_true', help='Get the device type, used with -k $device')
  parser.add_argument('-t', '--device_type', default=['real', 'virtual'], help='Test on which type of mobile devices')
  args = parser.parse_args()
  return args


if __name__ == '__main__':
  main()
