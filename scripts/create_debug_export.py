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

"""Based on the unity_packer/exports.json file, generate separate debug
export config for each product.

Example usage:
  python scripts/create_debug_export.py
"""

import os
import json

from absl import app
from absl import flags
from absl import logging

API_PACKAGE_MAP = {
    "analytics": "FirebaseAnalytics.unitypackage",
    "app_check": "FirebaseAppCheck.unitypackage",
    "auth": "FirebaseAuth.unitypackage",
    "crashlytics": "FirebaseCrashlytics.unitypackage",
    "database": "FirebaseDatabase.unitypackage",
    "dynamic_links": "FirebaseDynamicLinks.unitypackage",
    "firestore": "FirebaseFirestore.unitypackage",
    "functions": "FirebaseFunctions.unitypackage",
    "installations": "FirebaseInstallations.unitypackage",
    "messaging": "FirebaseMessaging.unitypackage",
    "remote_config": "FirebaseRemoteConfig.unitypackage",
    "storage": "FirebaseStorage.unitypackage",
}

default_package_names = [
    "FirebaseApp.unitypackage", "SampleCommon.unitypackage"]


FLAGS = flags.FLAGS
flags.DEFINE_string('prod_export', "exports.json",
                    'Production export json template to copy from')
flags.DEFINE_string("json_folder", "unity_packer",
                    ("The root folder for json configs."))
flags.DEFINE_string("output_folder", "debug_single_export_json",
                    "Json file with stable guids cache.")


def main(argv):
  if len(argv) > 1:
    raise app.UsageError('Too many command-line arguments.')
  prod_export_json_path = os.path.join(
      os.getcwd(), FLAGS.json_folder, FLAGS.prod_export)

  with open(prod_export_json_path, "r") as fin:
    export_json = json.load(fin)

    packages = export_json["packages"]

    for api_name, package_name in API_PACKAGE_MAP.items():
      output_path = os.path.join(
          os.getcwd(), FLAGS.json_folder, FLAGS.output_folder, api_name + ".json")
      output_dict = {}
      output_package_list = []
      for idx, package_dict in enumerate(packages):
        if package_dict["name"] in default_package_names:
          output_package_list.append(packages[idx])
        elif package_dict["name"] == package_name:
          output_package_list.append(packages[idx])
      output_dict["packages"] = output_package_list

      with open(output_path, 'w', encoding='utf-8') as fout:
        fout.write(json.dumps(output_dict, indent=2))
      logging.info("Write %s", output_path)


if __name__ == '__main__':
  app.run(main)
