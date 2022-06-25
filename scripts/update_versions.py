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
import re
import requests
from xml.etree import ElementTree

from github import Github
from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS
flags.DEFINE_string("unity_sdk_version", None,
                    "Required, will check and upgrade cmake/firebase_unity_version.cmake",
                    short_name="u")

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

########## Android versions update #############################

# Android gMaven repository from where we scan available Android packages
# and their versions
GMAVEN_MASTER_INDEX = "https://dl.google.com/dl/android/maven2/master-index.xml"
GMAVEN_GROUP_INDEX = "https://dl.google.com/dl/android/maven2/{0}/group-index.xml"

# Regex to match versions with just digits (ignoring things like -alpha, -beta)
RE_NON_EXPERIMENTAL_VERSION = re.compile('[0-9.]+$')

def get_latest_maven_versions(ignore_packages=None, allow_experimental=False):
  """Gets latest versions of android packages from google Maven repository.

  Args:
      ignore_packages (list[str], optional): Case insensitive list of substrings
        If any of these substrings are present in the android package name, it
        will not be updated.
        Eg: ['Foo', 'bar'] will ignore all android packages that have 'foo' or
        'bar' in their name. For example, 'my.android.foo.package',
        'myfoo.android.package'
      allow_experimental (bool): Allow experimental versions.
        Eg: 1.2.3-alpha, 1.2.3-beta, 1.2.3-rc

  Returns:
      dict: Dictionary of the form {<str>: list(str)} containing full name of
        android package as the key and its list of versions as value.
  """
  if ignore_packages is None:
    ignore_packages = []

  latest_versions = {}
  response = requests.get(GMAVEN_MASTER_INDEX)
  index_xml = ElementTree.fromstring(response.content)
  for index_child in index_xml:
    group_name = index_child.tag
    group_path = group_name.replace('.', '/')
    response = requests.get(GMAVEN_GROUP_INDEX.format(group_path))
    group_xml = ElementTree.fromstring(response.content)
    for group_child in group_xml:
      package_name = group_child.tag.replace('-', '_')
      package_full_name = group_name + "." + package_name
      if any(ignore_package.lower().replace('-', '_') in package_full_name.lower()
              for ignore_package in ignore_packages):
        continue
      versions = group_child.attrib['versions'].split(',')
      if not allow_experimental:
        versions = [version for version in versions
          if re.match(RE_NON_EXPERIMENTAL_VERSION, version) or '-cppsdk' in version]
      if versions:
        latest_valid_version = versions[-1]
        latest_versions[package_full_name] = latest_valid_version
  return latest_versions

  # Regex to match lines like:
# 'com.google.firebase:firebase-auth:1.2.3'
RE_GENERIC_DEPENDENCY_MODULE = re.compile(
    r"(?P<quote>[\'\"])(?P<pkg>[a-zA-Z0-9._-]+:[a-zA-Z0-9._-]+):([0-9.]+)[\'\"]"
)

def modify_dependency_file(dependency_filepath, version_map):
  """Modify a dependency file to reference the correct module versions.

  Looks for lines like: 'com.google.firebase:firebase-auth:1.2.3'
  for modules matching the ones in the version map, and modifies them in-place.

  Args:
    dependency_filename: Relative path to the dependency file to edit.
    version_map: Dictionary of packages to version numbers, e.g. {
      'com.google.firebase.firebase_auth': '15.0.0' }
  """
  global logfile_lines
  logging.debug('Reading dependency file: {0}'.format(dependency_filepath))
  lines = None
  with open(dependency_filepath, "r") as dependency_file:
    lines = dependency_file.readlines()
  if not lines:
    logging.fatal('Update failed. ' +
          'Could not read contents from file {0}.'.format(dependency_filepath))

  output_lines = []

  # Replacement function, look up the version number of the given pkg.
  def replace_dependency(m):
    if not m.group('pkg'):
      return m.group(0)
    pkg = m.group('pkg').replace('-', '_').replace(':', '.')
    if pkg not in version_map:
      return m.group(0)
    quote_type = m.group('quote')
    if not quote_type:
      quote_type = "'"
    return '%s%s:%s%s' % (quote_type, m.group('pkg'), version_map[pkg],
                          quote_type)

  substituted_pairs = []
  to_update = False
  for line in lines:
    substituted_line = re.sub(RE_GENERIC_DEPENDENCY_MODULE, replace_dependency,
                              line)
    output_lines.append(substituted_line)
    if substituted_line != line:
      substituted_pairs.append((line, substituted_line))
      to_update = True
      log_match = re.search(RE_GENERIC_DEPENDENCY_MODULE, line)
      log_pkg = log_match.group('pkg').replace('-', '_').replace(':', '.')

  if to_update:
    print('Updating contents of {0}'.format(dependency_filepath))
    for original, substituted in substituted_pairs:
      print('(-) ' + original + '(+) ' + substituted)
    with open(dependency_filepath, 'w') as dependency_file:
      dependency_file.writelines(output_lines)
    print()

def update_android_deps():
  deps_path = os.path.join(os.getcwd(), "cmake", "android_dependencies.cmake")
  latest_android_versions_map = get_latest_maven_versions()
  modify_dependency_file(deps_path, latest_android_versions_map)


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

def update_readme(unity_sdk_version):
  readme_path = os.path.join(os.getcwd(), "docs", "readme.md")
  replacement = ""
  with open(readme_path, "r") as f:
    replacement = f.read()
    if "### Upcoming Release" in replacement:
      replacement = replacement.replace("### Upcoming Release", "### "+unity_sdk_version)
    else:
      logging.warning("No upcoming release defined in docs/readme.md")

  with open(readme_path, "w") as fout:
    fout.write(replacement)

def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')

  if FLAGS.unity_sdk_version == None:
    raise app.UsageError('Please set unity_sdk_version.')
    
  update_unity_version(FLAGS.unity_sdk_version)
  update_android_deps()
  update_readme(FLAGS.unity_sdk_version)

if __name__ == '__main__':
  app.run(main)
