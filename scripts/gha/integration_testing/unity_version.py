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

"""Helper class for managing unity versions.

This module serves as a source of truth for what constitutes a valid Unity
version, and provides a class for storing a version and accessing its parts.
It also provides a number of helpful methods related to properties of Unity
projects of particular versions, such as which .NET runtimes they support.

Usage:
>>> version = UnityVersion("5.6.3p2")
>>> print(version.major)
int: 5
>>> print(version.minor)
int: 6
>>> print(version.revision)
string: 3p2

>>> version = UnityVersion("5.6")
>>> print(version.major)
int: 5
>>> print(version.minor)
int: 6
>>> print(version.revision)
NoneType: None

>>> version = UnityVersion("5.6")
>>> version == "5.6"
bool: True
>>> version > "5.6.1f1"
bool: False

"""

import functools
import re

_RUNTIME_35 = "3.5"
_RUNTIME_46 = "4.6"

_RUNTIMES = (_RUNTIME_35, _RUNTIME_46)

# Sorting order of possible version types in the version string
# (bigger is most recent).
_VERSION_TYPE_ORDER = (None, "a", "b", "rc", "f", "p")

_RE = re.compile(
    "("
    "(?P<major>[0-9]+)"  # Starts with digits (major version).
    "\\."  # Followed by a period.
    "(?P<minor>[0-9]+)"  # Followed by more digits (minor version).
    "(?:"  # Begin non-capturing group (so we can later mark it optional).
    "\\."  # Start with period.
    "(?P<revision>"  # Begin revision group.
    "(?P<revision_major>[0-9]+)"  # Revision starts with digits.
    "(?P<version_type>p|f|rc|a|b)"  # Followed by one of these version types.
    "(?P<revision_minor>[0-9]+)"  # Revision ends with digits.
    ")"  # End revision group.
    ")"  # End non-capturing group.
    "?"  # Mark previous group as optional.
    "$"  # Must reach the end of string.
    ")")


@functools.total_ordering
class UnityVersion(object):
  """Represents a version of the Unity game engine.

  See the constructor documentation for the version string for the required
  format, which is strict. Once constructed, the major, minor and revision
  versions can be accessed as properties. Note that the major and minor versions
  are integers, while the revision is a string (or None, if the version only
  contained a major and minor version).

  To check validity of a version string without passing it to the constructor,
  use the module-level validate function.
  """

  def __init__(self, version):
    """Construct a unity version object.

    Args:
      version: Must take the format a.b or a.b.c. a and b must consist
          of digits, while c can consist of digits and lower case letters.
          a is the major version, b is the minor version, and c is the revision.
          Can also be a UnityVersion object.

    Raises:
      ValueError: Format for version string is not correct.
    """
    # This allows version to be a UnityVersion object, which makes
    # implementing string/UnityVersion comparisons much easier.
    version_string = str(version)
    match = _RE.match(version_string)
    if not match:
      raise ValueError("Invalid version string: %s" % version_string)
    match_dict = match.groupdict()
    self._major = int(match_dict["major"])
    self._minor = int(match_dict["minor"])
    # If no revision was supplied, this will be None.
    self._revision = match_dict["revision"]
    # The following are needed for accurate version comparison.
    if self._revision:
      self._revision_major = int(match_dict["revision_major"])
      self._version_type = match_dict["version_type"]
      self._revision_minor = int(match_dict["revision_minor"])
    else:
      self._revision_major = None
      self._version_type = None
      self._revision_minor = None

  def __repr__(self):
    # Note: it's important that for any Version v, we have the following
    # identity: v == UnityVersion(str(v))
    components = [str(self._major), str(self._minor)]
    if self._revision:
      components.append(self._revision)
    return ".".join(components)

  def __gt__(self, other):
    return self.is_more_recent_than(UnityVersion(other))

  def __eq__(self, other):
    try:
      other = UnityVersion(other)
    except ValueError:
      return NotImplemented
    a = (self.major, self.minor, self.revision)
    b = (other.major, other.minor, other.revision)
    return a == b

  def __ne__(self, other):
    return not self == other

  def __hash__(self):
    # Since we treat a version object as equal to its version string,
    # we also need their hashes to agree.
    return hash(self.__repr__())

  @property
  def major(self):
    """The major version, as an integer."""
    return self._major

  @property
  def minor(self):
    """The minor version, as an integer."""
    return self._minor

  @property
  def revision(self):
    """The revision, as a string. Can be None."""
    return self._revision

  @property
  def revision_major(self):
    """The first number in the revision. None if revision is None."""
    return self._revision_major

  @property
  def version_type(self):
    """The letters in the revision (f, p, b, etc.). None if revision is None."""
    return self._version_type

  @property
  def revision_minor(self):
    """The final number in the revision. None if revision is None."""
    return self._revision_minor

  @property
  def generates_workspace(self):
    """Does this unity version generate an xcode workspace?

    Starting with Unity 5.6, Unity will generate a workspace for xcode when
    performing an iOS build. Prior to that, it only generated a project.
    xcodebuild needs to be used differently through the command line based
    on whether a workspace or project is being used.

    Returns:
        Boolean indicating whether this version will produce a workspace.

    """
    return self >= "5.6"

  # This is redundant with the comparison operators, but seeing as this
  # is a useful operation, it can be useful to be explicit about what
  # the comparison represents.
  def is_more_recent_than(self, other):
    """Is this version of Unity more recent than other?

    Recent means release date. 2017.3.1f1 is more recent than 5.6.3p2, for
    example, and 5.6.3p1 is more recent than 5.6.3f1.

    Note that a.b will be treated as being older than a.b.c for all c.

    Args:
      other: The other version being compared. Can be a string or
          UnityVersion object.

    Returns:
      boolean corresponding to whether this version is more recent than other.

    Raises:
      ValueError: If other is not a UnityVersion object or valid
          Unity version string.

    """
    # This breaks the version down into a tuple of components strictly for
    # lexicographical ordering purposes.
    def componentize(version):
      version = UnityVersion(version)
      return (
          version.major,
          version.minor,
          version.revision_major or 0,
          _VERSION_TYPE_ORDER.index(version.version_type),
          version.revision_minor or 0)

    return componentize(self) > componentize(other)

  def supports_runtime(self, runtime):
    """Does this version of Unity support this .NET runtime?

    Unity began supporting .NET 4.6 starting with 2017, and deprecated 3.5
    with 2018.3. This method will indicate whether the given runtime is
    supported by this version of Unity.

    Args:
      runtime: (string) .NET runtime version. Either '3.5' or '4.6'.

    Returns:
      (boolean) Whether the given runtime is supported by this version.

    Raises:
      ValueError: Unrecognized runtime.

    """
    if runtime == _RUNTIME_35:
      return self < "2018.3"
    if runtime == _RUNTIME_46:
      return self >= "2017.0"
    raise ValueError(
        "Runtime {0} not recognized. Must be one of {1}.".format(
            runtime, str(_RUNTIMES)))

  @property
  def default_runtime(self):
    """Returns the default .NET runtime for this version."""
    return "3.5" if self < "2018.3" else "4.6"


def validate(version_string):
  """Is this a valid Unity version?

  It is recommended to use this before attempting to construct a UnityVersion
  object.

  Args:
      version_string: Must take the format a.b or a.b.cde, where a, b, c and e
          must consist of digits. d can be any version type, i.e. 'a', 'b',
          'rc', 'f', or 'p', corresponding to alpha, beta, release candidate,
          full and patch respectively.
          a is the major version, b is the minor version, c is the
          revision_major, d is the version_type, and e is the revision_minor.

  Returns:
      boolean, corresponding to whether the argument corresponds to a valid
      Unity version.
  """
  return bool(re.match(_RE, version_string))

