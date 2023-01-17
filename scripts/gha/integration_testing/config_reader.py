# Copyright 2019 Google LLC
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

"""A utility for working with testapp builder JSON files.

This module handles loading the central configuration file for a testapp
builder, returning a 'Config' object that exposes all the data.

The motivation for loading the config into a class as opposed to returning
the loaded JSON directly is to validate the data upfront, to fail fast if
anything is missing or formatted incorrectly.

Example of such a configuration file:

{
  "apis": [
    {
      "name": "analytics",
      "full_name": "FirebaseAnalytics",
      "bundle_id": "com.google.firebase.unity.analytics.testapp",
      "testapp_path": "analytics/testapp",
      "plugins": [
        "FirebaseAnalytics.unitypackage"
      ],
      "provision": "Firebase_Dev_Wildcard.mobileprovision",
      "upm_packages": [
        "com.google.external-dependency-manager-*.tgz",
        "com.google.firebase.app-*.tgz",
        "com.google.firebase.analytics-*.tgz"
      ]
    },
    {
      "name": "auth",
      "full_name": "FirebaseAuth",
      "bundle_id": "com.google.FirebaseUnityAuthTestApp.dev",
      "testapp_path": "auth/testapp",
      "plugins": [
        "FirebaseAuth.unitypackage"
      ],
      "provision": "Firebase_Unity_Auth_Test_App_Dev.mobileprovision",
      "entitlements": "scripts/gha/integration_testing/auth.entitlements",
      "upm_packages": [
        "com.google.external-dependency-manager-*.tgz",
        "com.google.firebase.app-*.tgz",
        "com.google.firebase.auth-*.tgz"
      ]
    }
  ],
  "skipped_testapp_files": [
    "UIHandlerWithFacebook"
  ],
  "builder_directory": "scripts/gha/integration_testing",
}

"""

import json
import os
import pathlib

import attr

_DEFAULT_CONFIG_NAME = "build_testapps.json"


def read_config(path=None):
  """Creates an in-memory 'Config' object out of a testapp config file.

  Args:
    path (str): Path to a testapp builder config file. If not specified, will
        look for 'build_testapps.json' in the same directory as this file.

  Returns:
    Config: All of the testapp builder's configuration.

  """
  if not path:
    directory = pathlib.Path(__file__).parent.absolute()
    path = os.path.join(directory, _DEFAULT_CONFIG_NAME)
  with open(path, "r") as config:
    config = json.load(config)
  api_configs = dict()
  try:
    for api in config["apis"]:
      api_name = api["name"]
      api_configs[api_name] = APIConfig(
          name=api_name,
          full_name=api["full_name"],
          captial_name=api["captial_name"],
          testapp_path=api["testapp_path"],
          plugins=api["plugins"],
          platforms=api["platforms"],
          bundle_id=api["bundle_id"],
          upm_packages=api.get("upm_packages", None),
          entitlements=api.get("entitlements", None),
          minify=api.get("minify", None))
    return Config(
        apis=api_configs,
        skipped_testapp_files=config["skipped_testapp_files"],
        builder_directory=config["builder_directory"])
  except (KeyError, TypeError, IndexError):
    # The error will be cryptic on its own, so we dump the JSON to
    # offer context, then reraise the error.
    print(
        "Error occurred while parsing config. Full config dump:\n"
        + json.dumps(config, sort_keys=True, indent=4, separators=(",", ":")))
    raise


@attr.s(frozen=True, eq=False)
class Config(object):
  apis = attr.ib()  # Mapping of str: APIConfig
  skipped_testapp_files = attr.ib()  # Unity project files to ignore.
  builder_directory = attr.ib()  # Relative dir containing helper scripts.

  def get_api(self, api):
    """Returns the APIConfig object for the given api, e.g. 'analytics'."""
    return self.apis[api]


@attr.s(frozen=True, eq=False)
class APIConfig(object):
  """Data container for configuration from the JSON for a single testapp."""
  name = attr.ib()
  full_name = attr.ib()
  captial_name = attr.ib()
  testapp_path = attr.ib()  # Relative path to this testapp's directory
  plugins = attr.ib()  # .unitypackages
  platforms = attr.ib() # supported platforms
  bundle_id = attr.ib()
  upm_packages = attr.ib()  # Optional package tars for use_local_packages
  entitlements = attr.ib()  # Optional relative path to an entitlements file
  minify = attr.ib()  # Optional enables minification of android builds
