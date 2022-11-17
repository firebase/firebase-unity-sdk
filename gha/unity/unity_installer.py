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

r"""Utility for downloading, installing and licensing Unity in CI.

Downloading and installing Unity, which must first be installed.

USAGE
Using Unity on CI requires the following flow:
(1) Install Unity & Modules
(2) Activate Unity License
(*) Use Unity
(3) Release Unity License

This tool supports (1), (2) and (3).


TODO: (1) Installation:
  unity_installer.py --install --version 2017.3.1f1 --platforms Android,iOS

'platforms' specifies additional build supports to install. Always installs
Unity itself.


(2) License activation:

  unity_installer.py --activate_license --version 2017.3.1f1 \
    --license_file ~/license.txt --logfile activate.log

  or:
  unity_installer.py --activate_license --version 2017.3.1f1 \
    --username username --password password --serial_ids serial_ids \
    --logfile activate.log

This will invoke the given version of Unity, passing in the license information
from --license_file to activate Unity. The license file should be structured
as follows:
The first line is the username for a Unity account.
The second line is the password for that Unity account.
Each subsequent line is a serial ID.

Example:
username@google.com
dhuew89IDhdeuwjd98dA
X4-XXXX-XXXX-XXXX-XXXX


(3) License release:
  unity_installer.py --release_license --version 2019 --logfile return.log

"""

import requests
import platform
import subprocess
import glob
import os

from absl import app
from absl import flags
from absl import logging
from os import path


_CMD_TIMEOUT = 900
_MAX_ATTEMPTS = 3

ANDROID = "Android"
IOS = "iOS"
TVOS = "tvOS"
WINDOWS = "Windows"
MACOS = "macOS"
LINUX = "Linux"
PLAYMODE = "Playmode"
BUILD_OS = (WINDOWS, MACOS, LINUX)
SUPPORTED_PLATFORMS = (ANDROID, IOS, TVOS, WINDOWS, MACOS, LINUX, PLAYMODE)
UNITY_VERSION_PLACEHOLDER = "unity_version_placeholder"

# Plese use Unity LTS versions: https://unity3d.com/unity/qa/lts-releases
# The modules below is valid only if Unity Hub & Unity are installed.
UNITY_SETTINGS = {
  "unity_hub_url": {
    WINDOWS: "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe",
    MACOS: "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.dmg",
    LINUX: "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHub.AppImage",
  },
  "unity_hub_path": {
    WINDOWS: '"C:/Program Files/Unity Hub/Unity Hub.exe"',
    MACOS: '"/Applications/Unity Hub.app"',
    LINUX: '"/home/runner/Unity Hub/UnityHub.AppImage"',
  },
  "unity_hub_executable": {
    WINDOWS: '"C:/Program Files/Unity Hub/Unity Hub.exe"',
    MACOS: '"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"',
    LINUX: 'xvfb-run --auto-servernum "/home/runner/Unity Hub/UnityHub.AppImage"',
  },
  "unity_path": {
    WINDOWS: f'"C:/Program Files/Unity/Hub/Editor/{UNITY_VERSION_PLACEHOLDER}"',
    MACOS: f'"/Applications/Unity/Hub/Editor/{UNITY_VERSION_PLACEHOLDER}"',
    LINUX: f'"/home/runner/Unity/Hub/Editor/{UNITY_VERSION_PLACEHOLDER}"',
  },
  "unity_executable": {
    WINDOWS: f'"C:/Program Files/Unity/Hub/Editor/{UNITY_VERSION_PLACEHOLDER}/Editor/Unity.exe"',
    MACOS: f'"/Applications/Unity/Hub/Editor/{UNITY_VERSION_PLACEHOLDER}/Unity.app/Contents/MacOS/Unity"',
    LINUX: None # Linux is not yet supported.
  },
  "2020": {
    WINDOWS: {
      "version": "2020.3.34f1",
      "changeset": "9a4c9c70452b",
      "modules": {ANDROID: ["android", "ios"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: [], MACOS: ["mac-mono"], LINUX: ["linux-mono"], PLAYMODE: ["ios"]},
    },
    MACOS: {
      "version": "2020.3.34f1",
      "changeset": "9a4c9c70452b",
      "modules": {ANDROID: ["android"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: ["windows-mono"], MACOS: ["ios"], LINUX: ["linux-mono"], PLAYMODE: []},
    },
    LINUX: {
      "version": "2020.3.40f1",
      "changeset": "ba48d4efcef1",
      "modules": {ANDROID: ["android"], IOS: ["ios"], TVOS: [], WINDOWS: ["windows-mono"], MACOS: ["mac-mono"], LINUX: [], PLAYMODE: []}
    }
  },
  "2019": {
    WINDOWS: {
      "version": "2019.4.39f1",
      "modules": {ANDROID: ["android"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: [], MACOS: ["mac-mono"], LINUX: ["linux-mono"], PLAYMODE: ["ios"]},
    },
    MACOS: {
      "version": "2019.4.39f1",
      "modules": {ANDROID: ["android"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: ["windows-mono"], MACOS: ["ios"], LINUX: ["linux-mono"], PLAYMODE: []},
    },
    LINUX: {
      "version": "2019.4.40f1",
      "modules": {ANDROID: ["android"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: ["windows-mono"], MACOS: ["mac-mono"], LINUX: [], PLAYMODE: []}
    }
  },
}


FLAGS = flags.FLAGS


flags.DEFINE_bool(
    "install", False,
    "Install Unity and build supports. Supply --version and --platforms.")

flags.DEFINE_bool(
    "activate_license", False,
    "Activate a Unity installation after running in --install mode."
    " Supply --version, --license_file and --logfile.")

flags.DEFINE_bool(
    "release_license", False,
    "Release an activated Unity license. Supply --version and --logfile.")

flags.DEFINE_string("version", None, "Major version string, e.g. 2020")
flags.DEFINE_string("license_file", None, "Path to the license file.")
flags.DEFINE_string("username", None, "username for a Unity account.")
flags.DEFINE_string("password", None, "password for that Unity account.")
flags.DEFINE_list("serial_ids", [], "Unity serial ID.")
flags.DEFINE_string("logfile", None, "Where to store Unity logs.")

# In a CI context, where platforms may be parameterized, some logic would be
# needed to pass the appropriately formatted values to both tools.
# Instead, this tool is written to expect the same format as build_testapps.py.
# This keeps the CI workflow logic simple.
flags.DEFINE_list(
    "platforms", [],
    "(Optional) Additional modules to install based on platforms. Should be"
    " in the format of Unity build targets, i.e. the same format as taken by"
    " build_testapps.py. Invalid values will be ignored."
    " Valid values: " + ",".join(SUPPORTED_PLATFORMS))


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  if FLAGS.install:
    install(FLAGS.version, FLAGS.platforms)
    print_setting(FLAGS.version)

  if FLAGS.activate_license:
    if FLAGS.license_file:
      with open(FLAGS.license_file, "r") as f:
        license_components = f.read().splitlines()
        username = license_components[0]
        password = license_components[1]
        serial_ids = license_components[2:]
    else:
      username = FLAGS.username
      password = FLAGS.password
      serial_ids = FLAGS.serial_ids
    return activate_license(username, password, serial_ids, FLAGS.logfile, FLAGS.version)

  if FLAGS.release_license:
    release_license(FLAGS.logfile, FLAGS.version)


def print_setting(unity_version):
  runner_os = get_os()
  unity_full_version = UNITY_SETTINGS[unity_version][runner_os]["version"]
  unity_path = UNITY_SETTINGS["unity_path"][runner_os].replace(UNITY_VERSION_PLACEHOLDER, unity_full_version)
  print("%s,%s" % (unity_full_version, unity_path))


def install(unity_version, platforms):
  install_unity_hub()
  runner_os = get_os()
  unity_full_version = UNITY_SETTINGS[unity_version][runner_os]["version"]
  changeset = UNITY_SETTINGS[unity_version][runner_os]["changeset"]
  install_unity(unity_full_version, changeset)
  for p in platforms:
    for module in UNITY_SETTINGS[unity_version][runner_os]["modules"][p]:
      install_module(unity_full_version, module)


def install_unity_hub():
  runner_os = get_os()
  unity_hub_url = UNITY_SETTINGS["unity_hub_url"][runner_os]
  unity_hub_installer = path.basename(unity_hub_url)
  download_unity_hub(unity_hub_url, unity_hub_installer, max_attemps=3)
  if runner_os == MACOS:
    run(f'sudo hdiutil attach {unity_hub_installer}', max_attemps=3)
    mounted_to = glob.glob("/Volumes/Unity Hub*/Unity Hub.app")
    if mounted_to:
      run(f'sudo cp -R {mounted_to[0]} /Applications', max_attemps=3)
    run('sudo mkdir -p "/Library/Application Support/Unity"')
    run(f'sudo chown -R {os.environ["USER"]} "/Library/Application Support/Unity"')
  elif runner_os == WINDOWS:
    run(f'{unity_hub_installer} /S', max_attemps=3)
  elif runner_os == LINUX:
    home_dir = os.environ["HOME"]
    unity_hub_path = UNITY_SETTINGS["unity_hub_path"][runner_os]
    run(f'mkdir -p "{home_dir}/Unity Hub" "{home_dir}/.config/Unity Hub"')
    run(f'mv {unity_hub_installer} {unity_hub_path}')
    run(f'chmod +x {unity_hub_path}')
    run(f'touch "{home_dir}/.config/Unity Hub/eulaAccepted"', max_attemps=3)


def download_unity_hub(unity_hub_url, unity_hub_installer, max_attemps=1):
  attempt_num = 1
  while attempt_num <= max_attemps:
    try:
      response = requests.get(unity_hub_url)
      open(unity_hub_installer, "wb").write(response.content)
    except Exception as e:
      logging.info("download unity hub failed. URL: %s (attempt %s of %s). Exception: %s", Exception, e)
      if attempt_num >= max_attemps:
        raise
    else:
      break
    attempt_num += 1


def install_unity(unity_full_version, changeset):
  unity_hub_executable = UNITY_SETTINGS["unity_hub_executable"][get_os()]
  run(f'{unity_hub_executable} -- --headless install --version {unity_full_version} --changeset {changeset}', max_attemps=3)
  run(f'{unity_hub_executable} -- --headless editors --installed')


def install_module(unity_full_version, module):
  unity_hub_executable = UNITY_SETTINGS["unity_hub_executable"][get_os()]
  run(f'{unity_hub_executable} -- --headless install-modules --version {unity_full_version} --module {module} --childModules', max_attemps=3)
          

def activate_license(username, password, serial_ids, logfile, unity_version):
  """Activates an installation of Unity with a license."""
  # Unity can report a license activation failure even if the activation
  # succeeds. This has occurred e.g. in Unity 2019.3.15 on Mac.
  # To handle this case, we check the Unity logs for the message indicating
  # successful activation and ignore the error in that case.
  unity_full_version = UNITY_SETTINGS[unity_version][get_os()]["version"]
  unity_executable = UNITY_SETTINGS["unity_executable"][get_os()].replace(UNITY_VERSION_PLACEHOLDER, unity_full_version)
  logging.info("Found %d licenses. Attempting each.", len(serial_ids))
  for i, serial_id in enumerate(serial_ids):
    logging.info("Attempting license %d", i)
    try:
      run(f'{unity_executable} -quit -batchmode -username {username} -password {password} -serial {serial_id} -logfile {logfile}')
      logging.info("Activated Unity license.")
      return
    except subprocess.CalledProcessError as e:
      # Log file may not have been created: if so, rethrow original error.
      try:
        with open(logfile, "r") as f:
          text = f.read()
      except OSError:
        raise e
      # Presence of this line indicates license activation was successful,
      # despite the error. If not present, rethrow original error.
      if "License activated successfully with user" in text:
        logging.info("Activated Unity license.")
        return
      else:
        logging.info("Failed to activate license %d", i)
        return 1


def release_license(logfile, unity_version):
  """Releases the Unity license. Requires finding an installation of Unity."""
  unity_full_version = UNITY_SETTINGS[unity_version][get_os()]["version"]
  unity_executable = UNITY_SETTINGS["unity_executable"][get_os()].replace(UNITY_VERSION_PLACEHOLDER, unity_full_version)
  run(f'{unity_executable} -quit -batchmode -returnlicense -logfile {logfile}')
  logging.info("Unity license released.")


def get_os():
  """Current Operation System"""
  if platform.system() == 'Windows':
    return WINDOWS
  elif platform.system() == 'Darwin':
    return MACOS
  elif platform.system() == 'Linux':
    return LINUX


def run(command, check=True, max_attemps=1):
  """Runs args in a subprocess, throwing an error on non-zero return code."""
  attempt_num = 1
  while attempt_num <= max_attemps:
    try:
      logging.info("run_with_retry: %s (attempt %s of %s)", command, attempt_num, max_attemps)
      result = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE, universal_newlines=True, shell=True)
      if result.stdout:
        logging.info("cmd stdout: %s", result.stdout.read().strip())
      if result.stderr:
        logging.info("cmd stderr: %s", result.stderr.read().strip())
    except subprocess.SubprocessError as e:
      logging.exception("run_with_retry: %s (attempt %s of %s) FAILED: %s", command, attempt_num, max_attemps, e)
      if check and (attempt_num >= max_attemps):
        raise
    else:
      break
    attempt_num += 1


if __name__ == "__main__":
  app.run(main)
