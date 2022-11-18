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

import platform
import shutil
import subprocess

from absl import app
from absl import flags
from absl import logging


_CMD_TIMEOUT = 900
_MAX_ATTEMPTS = 3

_DEFALUT = "Default"
_ANDROID = "Android"
_IOS = "iOS"
_TVOS = "tvOS"
_WINDOWS = "Windows"
_MACOS = "macOS"
_LINUX = "Linux"
_SUPPORTED_PLATFORMS = (_ANDROID, _IOS, _TVOS, _WINDOWS, _MACOS, _LINUX)

# Plese use Unity LTS versions: https://unity3d.com/unity/qa/lts-releases
# The modules below is valid only if Unity Hub is not installed.
UNITY_SETTINGS = {
  "2020": {
    _WINDOWS: {
      "version": "2020.3.34f1",
      "modules": {"Default": ["Unity"], "Android": ["android", "ios"], "iOS": ["ios"], "tvOS": ["appletv"], "Windows": None, "macOS": ["mac-mono"], "Linux": ["linux-mono"], "Playmode": ["ios"]},
    },
    _MACOS: {
      "version": "2020.3.34f1",
      "modules": {"Default": ["Unity"], "Android": ["android"], "iOS": ["ios", "appletv"], "tvOS": ["appletv"], "Windows": ["windows-mono"], "macOS": ["ios"], "Linux": ["linux-mono"], "Playmode": None},
    },
    _LINUX: {
      "version": "2020.3.40f1",
      "modules": {"Default": ["Unity"], "Android": ["android"], "iOS": ["ios"], "tvOS": None, "Windows": ["windows-mono"], "macOS": ["mac-mono"], "Linux": None, "Playmode": None}
    }
  },
  "2019": {
    _WINDOWS: {
      "version": "2019.4.39f1",
      "modules": {"Default": ["Unity"], "Android": ["android"], "iOS": ["ios"], "tvOS": ["appletv"], "Windows": None, "macOS": ["mac-mono"], "Linux": ["linux-mono"], "Playmode": ["ios"]},
    },
    _MACOS: {
      "version": "2019.4.39f1",
      "modules": {"Default": ["Unity"], "Android": ["android"], "iOS": ["ios"], "tvOS": ["appletv"], "Windows": ["windows-mono"], "macOS": ["ios"], "Linux": ["linux-mono"], "Playmode": None},
    },
    _LINUX: {
      "version": "2019.4.40f1",
      "modules": {"Default": ["Unity"], "Android": ["android"], "iOS": ["ios"], "tvOS": ["appletv"], "Windows": ["windows-mono"], "macOS": ["mac-mono"], "Linux": None, "Playmode": None}
    }
  },
}

FLAGS = flags.FLAGS

flags.DEFINE_bool(
    "setting", False,
    "Print out detailed Unity Setting. Supply --version.")

# TODO: @sunmou
# flags.DEFINE_bool(
#     "install", False,
#     "Install Unity and build supports. Supply --version and --platforms.")

flags.DEFINE_bool(
    "install_modules", False,
    "Install Unity Modules and build supports. Supply --version and --platforms.")

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
    "platforms", None,
    "(Optional) Additional modules to install based on platforms. Should be"
    " in the format of Unity build targets, i.e. the same format as taken by"
    " build_testapps.py. Invalid values will be ignored."
    " Valid values: " + ",".join(_SUPPORTED_PLATFORMS))


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  if FLAGS.setting:
    print_setting(FLAGS.version)

  if FLAGS.install_modules:
    install_modules(FLAGS.version, FLAGS.platforms)

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
  os = get_os()
  unity_full_version = UNITY_SETTINGS[unity_version][os]["version"]
  unity_path = get_unity_path(unity_version)
  print("%s,%s" % (unity_full_version, unity_path))


def install_modules(unity_version, platforms):
  os = get_os()
  unity_full_version = UNITY_SETTINGS[unity_version][os]["version"]
  unity_hub_path = get_unity_hub_path()
  if platforms:
    for p in platforms:
      if UNITY_SETTINGS[unity_version][os]["modules"][p]:
        for module in UNITY_SETTINGS[unity_version][os]["modules"][p]:
          run([unity_hub_path, "--", "--headless", 
                "install-modules", 
                "--version", unity_full_version, 
                "--module", module, 
                "--childModules"], 
              check=False)


def activate_license(username, password, serial_ids, logfile, unity_version):
  """Activates an installation of Unity with a license."""
  # Unity can report a license activation failure even if the activation
  # succeeds. This has occurred e.g. in Unity 2019.3.15 on Mac.
  # To handle this case, we check the Unity logs for the message indicating
  # successful activation and ignore the error in that case.
  unity = get_unity_executable(unity_version)
  logging.info("Found %d licenses. Attempting each.", len(serial_ids))
  for i, serial_id in enumerate(serial_ids):
    logging.info("Attempting license %d", i)
    try:
      run([unity, "-quit", "-batchmode",
           "-username", username,
           "-password", password,
           "-serial", serial_id,
           "-logfile", logfile])
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
  unity = get_unity_executable(unity_version)
  run([unity, "-quit", "-batchmode", "-returnlicense", "-logfile", logfile])
  logging.info("Unity license released.")


def get_os():
  """Current Operation System"""
  if platform.system() == 'Windows':
    return _WINDOWS
  elif platform.system() == 'Darwin':
    return _MACOS
  elif platform.system() == 'Linux':
    return _LINUX


def get_unity_executable(version):
  """Returns the path to this version of Unity."""
  full_version = UNITY_SETTINGS[version][get_os()]["version"]
  if platform.system() == "Windows":
    return "C:/Program Files/Unity/Hub/Editor/%s/Editor/Unity.exe" % full_version
  elif platform.system() == "Darwin":
    return "/Applications/Unity/Hub/Editor/%s/Unity.app/Contents/MacOS/Unity" % full_version
  else:
    # Linux is not yet supported.
    raise RuntimeError("Only Windows and MacOS are supported.")


def get_unity_hub_path():
  """Returns the path to Unity Hub."""
  if platform.system() == "Windows":
    return "C:/Program Files/Unity Hub/Unity Hub.exe"
  elif platform.system() == "Darwin":
    return "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub"
  elif platform.system() == 'Linux':
    return "/home/runner/Unity Hub/UnityHub.AppImage"
 

def get_unity_path(version):
  """Returns the path to this version of Unity."""
  full_version = UNITY_SETTINGS[version][get_os()]["version"]
  if platform.system() == "Windows":
    return "C:/Program Files/Unity/Hub/Editor/%s" % full_version
  elif platform.system() == "Darwin":
    return "/Applications/Unity/Hub/Editor/%s" % full_version
  elif platform.system() == 'Linux':
    return "/home/runner/Unity/Hub/Editor/%s" % full_version


def run(args, check=True, timeout=_CMD_TIMEOUT):
  """Runs args in a subprocess, throwing an error on non-zero return code."""
  logging.info("run cmd: %s", " ".join(args))
  result = subprocess.run(args=args, check=check, timeout=timeout, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
  logging.info("cmd result.stdout: %s", result.stdout)
  logging.info("cmd result.stderr: %s", result.stderr)


if __name__ == "__main__":
  app.run(main)
