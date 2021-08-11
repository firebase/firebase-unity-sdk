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

r"""Module for finding Unity executables on a system.

This module searches a Unity installation directory for Unity executables.
By default, it searches the default Unity installation directories, but this
behaviour can be overridden.

The default installation formats are as follows:

    OSX:     /Applications/Unity{}/Unity.app/Contents/MacOS/Unity
    Windows: C:\program files\unity{}\editor\unity.exe
    Linux:  ~/Unity{}/Editor/Unity

The {} are substituted with the version to form a path. Note that in each case,
the following can be substituted for each other: Unity{}, Unity_{}, Unity-{},
and their lower-case versions as well. i.e. the finder will find any of those 6
formats.

This will also automatically search the Unity Hub, with the following formats:

    OSX:     /Applications/Unity/Hub/Editor/{}.app/Contents/MacOS/Unity
    Windows: C:\program files\unity\hub\editor\{}\editor\unity.exe
    Linux:  ~/Unity/Hub/Editor/{}/Editor/Unity

"""

import os
import platform

import attr

# These are the values from platform.system() supported by the module,
# corresponding to the three desktop platforms.
_LINUX = "Linux"
_WINDOWS = "Windows"
_OSX = "Darwin"


def get_path(version, folder_override=None):
  r"""Returns the path to this version of Unity on this system.

  If multiple installations exist, will return the first one found. The Unity
  hub will be searched before independent installations, but beyond that,
  the order will be arbitrary.

  Args:
    version: UnityVersion object, or a valid Unity version string as defined
        by the unity_version module.
    folder_override: (Default: None) The folder in which we should search for
        this version of Unity instead of the default location. The default
        behaviour corresponds to setting this equal to /Applications,
        C:\program files, or the user directory, on MacOS, Windows and Linux
        respectively.

  Returns:
    Valid path to the Unity executable corresponding to this version, or None
    if an executable matching this version is not found.

  """
  unity_paths = _UnityPaths.create(folder_override)
  for path in unity_paths.get_potential_unity_paths(version):
    if os.path.exists(path):
      return path
  return None


@attr.s(frozen=True, eq=False)
class _UnityPaths(object):
  """Manages paths to Unity installations."""
  hub_path_format = attr.ib()
  non_hub_path_format = attr.ib()

  @classmethod
  def create(cls, folder_override=None):
    """Builds an os-specific _UnityPaths object. Factory method."""
    operating_system = platform.system()
    if operating_system == _OSX:
      default_dir = r"/Applications"
      local_path_format = r"%s/Unity.app/Contents/MacOS/Unity"
    elif operating_system == _WINDOWS:
      default_dir = r"C:\program files"
      local_path_format = r"%s\editor\unity.exe"
    elif operating_system == _LINUX:
      default_dir = os.path.expanduser("~")
      local_path_format = r"%s/Editor/Unity"
    else:
      raise ValueError("OS not supported: %s" % operating_system)

    base_dir = folder_override or default_dir
    hub_dir = os.path.join(base_dir, "Unity", "Hub", "Editor")
    non_hub_path_format = os.path.join(base_dir, local_path_format)
    hub_path_format = os.path.join(hub_dir, local_path_format)
    return cls(hub_path_format, non_hub_path_format)

  def get_potential_unity_paths(self, version):
    """Builds a sequence of possible Unity paths for this version."""
    # In order to have multiple parallel Unity installations, it is necessary
    # to rename a folder from "Unity" to add the Unity version. To be lenient
    # about the exact convention needed, we check the system for several
    # different conventions.
    version_formats = ["Unity%s", "Unity_%s", "Unity-%s", "Unity %s"]
    version_formats += [s.lower() for s in version_formats]
    path_formats = [self.non_hub_path_format % version_format
                    for version_format in version_formats]
    paths = [path_format % version for path_format in path_formats]
    # The Unity hub is smarter about parallel installations, using the
    # version name instead of "Unity", so we don't check multiple formats.
    # Insert at front so that the hub is checked first.
    paths.insert(0, self.hub_path_format % version)
    return paths


