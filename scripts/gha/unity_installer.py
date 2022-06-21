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

Uses u3d for downloading and installing Unity, which must first be installed.
Can be installed as a gem:

gem install u3d

See https://github.com/DragonBox/u3d for details on u3d.

USAGE
Using Unity on CI requires the following flow:
(1) Install Unity
(2) Activate Unity License
(*) Use Unity
(3) Release Unity License

This tool supports (1), (2) and (3).


(1) Installation:
  unity_installer.py --install --version 2017.3.1f1 --platforms Android,iOS

'platforms' specifies additional build supports to install. Always installs
Unity itself.

u3d will install Unity to the following path. The full path to the binary is
listed, as that's needed to use Unity in batchmode.

Windows: C:/Program Files/Unity_{version}/editor/unity.exe
MacOS: /Applications/Unity_{version}/Unity.app/Contents/MacOS/Unity


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
  unity_installer.py --release_license --version 2017.3.1f1 --logfile return.log

"""

import platform
import shutil
import subprocess

from absl import app
from absl import flags
from absl import logging

from print_matrix_configuration import UNITY_SETTINGS


FLAGS = flags.FLAGS

_DEFALUT = "Default"
_ANDROID = "Android"
_IOS = "iOS"
_WINDOWS = "Windows"
_MACOS = "macOS"
_LINUX = "Linux"
_SUPPORTED_PLATFORMS = (_ANDROID, _IOS, _WINDOWS, _MACOS, _LINUX)

# These are the three actions supported by the tool.
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

flags.DEFINE_string("version", None, "Version string, e.g. 2017.3.1f1")
flags.DEFINE_string("license_file", None, "Path to the license file.")
flags.DEFINE_string("username", None, "username for a Unity account.")
flags.DEFINE_string("password", None, "password for that Unity account.")
flags.DEFINE_list("serial_ids", [], "Unity serial ID.")
flags.DEFINE_string("logfile", None, "Where to store Unity logs.")

# Implementation note: it would have been simpler to pass a
# string directly to u3d. The issue is that there's a relationship between
# the value of this flag, and the --platforms flag in build_testapps.py.
# e.g. if we want to pass "Android,Desktop,iOS" to build_testapps.py, then we
# need to pass "Unity,Android,Ios" to U3D.
# In a CI context, where platforms may be parameterized, some logic would be
# needed to pass the appropriately formatted values to both tools.
# Instead, this tool is written to expect the same format as build_testapps.py.
# This keeps the CI workflow logic simple, but requires some parsing of the
# platforms here, before passing them to U3D.
flags.DEFINE_list(
    "platforms", None,
    "(Optional) Additional platforms to install. Should be in the format of"
    " Unity build targets, i.e. the same format as taken by build_testapps.py."
    " Invalid values will be ignored."
    " Valid values: " + ",".join(_SUPPORTED_PLATFORMS))


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  if FLAGS.install:
    install_unity(FLAGS.version, FLAGS.platforms)

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


def install_unity(unity_version, platforms):
  """Installs Unity and build supports (packages)."""
  # This always installs Unity, and installs build supports for Android
  # and iOS. Other supports can be added here, e.g. desktop platforms
  # for platforms other than the running OS, or embedded Android SDK/NDK.
  os = get_os()
  unity_full_version = UNITY_SETTINGS[unity_version][os]["version"]
  package_list = UNITY_SETTINGS[unity_version][os]["packages"][_DEFALUT]
  if platforms:
    for p in platforms:
      if UNITY_SETTINGS[unity_version][os]["packages"][p]:
        package_list.extend(UNITY_SETTINGS[unity_version][os]["packages"][p])
  package_csv = ",".join(filter(None.__ne__, package_list))

  u3d = find_u3d()
  run([u3d, "available", "-u", unity_version], check=False)
  run([u3d, "available", "-u", unity_full_version, "-p"], check=False)
  run([u3d, "install", "--trace",
       "--verbose", unity_full_version,
       "-p", package_csv])
  # This will list what u3d has installed. For debugging purposes.
  run([u3d, "list"], check=False)
  logging.info("Finished installing Unity.")


def activate_license(username, password, serial_ids, logfile, unity_version):
  """Activates an installation of Unity with a license."""
  # Unity can report a license activation failure even if the activation
  # succeeds. This has occurred e.g. in Unity 2019.3.15 on Mac.
  # To handle this case, we check the Unity logs for the message indicating
  # successful activation and ignore the error in that case.
  unity = get_unity_path(unity_version)
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
  unity = get_unity_path(unity_version)
  run([unity, "-quit", "-batchmode", "-returnlicense", "-logfile", logfile])
  logging.info("Unity license released.")


def find_u3d():
  """Returns the full path to the u3d tool."""
  # On Windows, running in a subprocess will not search the system path,
  # so calling "u3d" won't work. Find full path instead.
  u3d = shutil.which("u3d")
  if not u3d:
    raise RuntimeError("Could not find u3d. Ensure it's installed and on path.")
  return u3d


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
  full_version = UNITY_SETTINGS[version][get_os()]["version"]
  if platform.system() == "Windows":
    return "C:/Program Files/Unity_%s/editor/unity.exe" % full_version
  elif platform.system() == "Darwin":
    return "/Applications/Unity_%s/Unity.app/Contents/MacOS/Unity" % full_version
  else:
    # /opt/unity-editor-%s/Editor/Unity is the path for Linux expected by U3D,
    # but Linux is not yet supported.
    raise RuntimeError("Only Windows and MacOS are supported.")


def run(args, check=True):
  """Runs args in a subprocess, throwing an error on non-zero return code."""
  subprocess.run(args=args, check=check)


if __name__ == "__main__":
  app.run(main)
