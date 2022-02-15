#!/usr/bin/python
#
# Copyright 2021 Google LLC
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

"""Update the version numbers in cmake/firebase_unity_version.cmake.

To use locally, make sure call following installation first.
pip install PyGithub

Example usage (in root folder):
  python scripts/update_versions.py --unity_sdk_version=<version number>
"""
import os

from github import Github
from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string("unity_sdk_version", None,
                    "Required, will check and upgrade cmake/firebase_unity_version.cmake")

def get_latest_repo_tag(repo_url):
  repo = Github().get_repo(repo_url)
  tags = sorted(repo.get_tags(), key=lambda t: t.last_modified)
  return tags[0].name

def get_ios_pod_version_from_cpp():
  pod_path = os.path.join(os.getcwd(), "..", "firebase-cpp-sdk", "ios_pod", "Podfile")
  if not os.path.exists(pod_path):
    return None
  with open(pod_path, "r") as f:
    datafile = f.readlines()
    for line in datafile:
      if 'Firebase/Core' in line:
        result = line.split()
        return result[-1].strip("\'")

def update_unity_version(unity_sdk_version):
  version_cmake_path = os.path.join(os.getcwd(), "cmake", "firebase_unity_version.cmake")
  replacement = ""
  with open(version_cmake_path, "r") as f:
    datafile = f.readlines()
    for line in datafile:
      if "FIREBASE_UNITY_SDK_VERSION" in line:
        newline = "set(FIREBASE_UNITY_SDK_VERSION \"" + unity_sdk_version + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_IOS_POD_VERSION" in line:
        podversion = get_ios_pod_version_from_cpp()
        newline = "set(FIREBASE_IOS_POD_VERSION \"" + podversion + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_UNITY_JAR_RESOLVER_VERSION" in line:
        jar_version = get_latest_repo_tag('googlesamples/unity-jar-resolver')
        jar_version = jar_version.lstrip("v") # jar resolver need to strip "v" from the tag
        newline = "set(FIREBASE_UNITY_JAR_RESOLVER_VERSION \"" + jar_version + "\""
        replacement = replacement + newline + "\n"
      elif "FIREBASE_CPP_SDK_PRESET_VERSION" in line:
        cpp_version = get_latest_repo_tag('firebase/firebase-cpp-sdk')
        newline = "set(FIREBASE_CPP_SDK_PRESET_VERSION \"" + cpp_version + "\""
        replacement = replacement + newline + "\n"
      else:
        replacement = replacement + line
  
  with open(version_cmake_path, "w") as fout:
    fout.write(replacement)

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')

  if FLAGS.unity_sdk_version == None:
    raise app.UsageError('Please set unity_sdk_version.')
    
  update_unity_version(FLAGS.unity_sdk_version)

if __name__ == '__main__':
  app.run(main)
