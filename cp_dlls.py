#!/usr/bin/env python
"""
%s <src_dir> <dest_dir> [dest_dir2, [dest_dir3, ...]] [flags]

Copies the macOS or Linux build artifacts of the firebase-unity-sdk to the
directory of the Firestore Unity testapp.

This script is designed for use during iterative development. The idea is that
you make changes to a source file, rebuild with cmake --build, then use this
script to copy the build artifacts into a Unity project to test them out.

src_dir
  The directory into which the build artifacts of firebase-unity-sdk were
  written. This is the directory that was specified to cmake as the -B argument.

dest_dir
  The directories of the Unity projects into which to copy the build artifacts.
  The same "copy" steps will be repeated for each of these directories. Each of
  these Unity projects must have imported FirebaseFirestore.unitypackage at
  some point in the past, since this script only publishes a minimal subset of
  build artifacts and not everything needed for the Firebase Unity SDK.
"""

# NOTE: This script requires Python 3.9 or newer.

from collections.abc import Iterator, Sequence
import abc
import enum
import fnmatch
import pathlib
import re
import shutil
from typing import Optional

from absl import app
from absl import flags
from absl import logging


FLAG_FILTER = flags.DEFINE_string(
  name="filter",
  default=None,
  help="The names of the files to include when copying; only the files whose "
    "names match this filter will be copied. The pattern uses Python's "
    "fnmatch module, which recognizes *, ?, and [anychar]. One use case would "
    "be to specify *dll so that only the .dll files are copied, since copying "
    "the .so files can crash the Unity Editor, and is not necessary if only C# "
    "files have been modified.",
)

FLAG_DRY_RUN = flags.DEFINE_boolean(
  name="dry_run",
  default=False,
  help="Log what file operations would be performed, without actually "
    "performing them.",
)


def main(argv: Sequence[str]) -> None:
  if len(argv) < 2:
    raise app.UsageError("src_dir must be specified")
  elif len(argv) < 3:
    raise app.UsageError("at least one dest_dir must be specified")

  src_dir = pathlib.Path(argv[1])
  dest_dirs = tuple(pathlib.Path(dest_dir_str) for dest_dir_str in argv[2:])

  if FLAG_FILTER.value:
    copy_pattern = re.compile(fnmatch.translate(FLAG_FILTER.value))
  else:
    copy_pattern = None

  if FLAG_DRY_RUN.value:
    fs = NoOpFileSystem()
  else:
    fs = RealFileSystem()

  for dest_dir in dest_dirs:
    copier = BuildArtifactCopier(src_dir, dest_dir, fs, copy_pattern)
    copier.run()


@enum.unique
class OS(enum.Enum):
  MAC = (".bundle")
  LINUX = (".so")

  def __init__(self, lib_filename_suffix: str) -> None:
    self.lib_filename_suffix = lib_filename_suffix


class FileSystem(abc.ABC):

  @abc.abstractmethod
  def unlink(self, path: pathlib.Path) -> None:
    raise NotImplementedError()

  @abc.abstractmethod
  def copy_file(self, src: pathlib.Path, dest: pathlib.Path) -> None:
    raise NotImplementedError()


class RealFileSystem(FileSystem):

  def unlink(self, path: pathlib.Path) -> None:
    path.unlink()

  def copy_file(self, src: pathlib.Path, dest: pathlib.Path) -> None:
    shutil.copy2(src, dest)


class NoOpFileSystem(FileSystem):

  def unlink(self, path: pathlib.Path) -> None:
    pass

  def copy_file(self, src: pathlib.Path, dest: pathlib.Path) -> None:
    pass


class BuildArtifactCopier:

  def __init__(
      self,
      src_dir: pathlib.Path,
      dest_dir: pathlib.Path,
      fs: FileSystem,
      copy_pattern: Optional[re.Pattern],
    ) -> None:
    self.src_dir = src_dir
    self.dest_dir = dest_dir
    self.fs = fs
    self.copy_pattern = copy_pattern

  def run(self) -> None:
    logging.info("Copying build artifacts from %s to %s", self.src_dir, self.dest_dir)
    (src_cpp_app_library_os, src_cpp_app_library_file) = self.find_src_cpp_app_library()
    self.delete_old_cpp_app_library_files(src_cpp_app_library_os, src_cpp_app_library_file.name)
    self.copy_files(src_cpp_app_library_os, src_cpp_app_library_file)

  def find_src_cpp_app_library(self) -> tuple[OS, pathlib.Path]:
    found_artifacts: list[tuple[OS, pathlib.Path]] = []

    logging.info("Searching for C++ library build artifact files in directory: %s", self.src_dir)
    for candidate_artifact_path in self.src_dir.iterdir():
      for candidate_os in OS:
        if candidate_artifact_path.name.endswith(candidate_os.lib_filename_suffix):
          logging.info("Found C++ library build artifact file: %s", candidate_artifact_path)
          found_artifacts.append((candidate_os, candidate_artifact_path))

    if len(found_artifacts) == 0:
      raise FirebaseCppLibraryBuildArtifactNotFound(
        f"No C++ library build artifact files found in {self.src_dir}, but "
        f"expected to find exactly one file whose name ends with one of the "
        f"following suffixes: " + ", ".join(os.lib_filename_suffix for os in OS)
      )
    elif len(found_artifacts) > 1:
      raise FirebaseCppLibraryBuildArtifactNotFound(
        f"Found {len(found_artifacts)} C++ library build artifacts in "
        f"{self.src_dir}, but expected to find exactly one: "
        + ", ".join(found_artifact[1].name for found_artifact in found_artifacts)
      )

    return found_artifacts[0]

  def delete_old_cpp_app_library_files(
      self,
      os: OS,
      src_cpp_app_library_file_name: str
  ) -> None:
    expr = re.compile(r"FirebaseCppApp-\d+_\d+_\d+" + re.escape(os.lib_filename_suffix))
    if not expr.fullmatch(src_cpp_app_library_file_name):
      raise ValueError(f"invalid filename: {src_cpp_app_library_file_name}")

    # Do not delete old CPP app library files if we aren't even copying them
    # due to the copy_pattern not matching the file name.
    if self.copy_pattern is not None \
        and not self.copy_pattern.fullmatch(src_cpp_app_library_file_name):
      return

    # TODO(dconeybe) Update this logic to also support Mac M1, which probably
    # uses a different directory than "x86_64".
    x86_64_dir = self.dest_dir / "Assets" / "Firebase" / "Plugins" / "x86_64"
    for candidate_dll_file in x86_64_dir.iterdir():
      if expr.fullmatch(candidate_dll_file.name):
        # Don't delete the file that is going to be overwritten anyways; because
        # deleting it is unnecessary and adds noise to the log output.
        if candidate_dll_file.name == src_cpp_app_library_file_name:
          continue
        logging.info("Deleting %s", candidate_dll_file)
        self.fs.unlink(candidate_dll_file)
        candidate_dll_meta_file = candidate_dll_file.parent \
            / (candidate_dll_file.name + ".meta")
        if candidate_dll_meta_file.exists():
          logging.info("Deleting %s", candidate_dll_meta_file)
          self.fs.unlink(candidate_dll_meta_file)

  def copy_files(self, os: OS, src_cpp_app_library_file: pathlib.Path) -> None:
    files_to_copy = sorted(self.files_to_copy(os, src_cpp_app_library_file))
    for (src_file, dest_file) in files_to_copy:
      if self.copy_pattern is None or self.copy_pattern.fullmatch(src_file.name):
        logging.info("Copying %s to %s", src_file, dest_file)
        self.fs.copy_file(src_file, dest_file)

  def files_to_copy(
      self,
      os: OS,
      src_cpp_app_library_file: pathlib.Path
  ) -> Iterator[tuple[pathlib.Path, pathlib.Path]]:
    # As a special case, yield the src_cpp_app_library_file.
    # Since its name encodes the version number, which changes over time, we
    # cannot simply hardcode it like the other files in the dict below.
    dest_cpp_app_library_file = self.dest_dir / "Assets" / "Firebase" \
        / "Plugins" / "x86_64" / src_cpp_app_library_file.name
    yield (src_cpp_app_library_file, dest_cpp_app_library_file )

    src_file_strs_by_dest_dir_str: dict[str, tuple[str, ...]] = {
      "Assets/Firebase/Plugins": (
        "app/obj/x86/Release/Firebase.App.dll",
        "app/platform/obj/x86/Release/Firebase.Platform.dll",
        "app/task_extension/obj/x86/Release/Firebase.TaskExtension.dll",
        "auth/obj/x86/Release/Firebase.Auth.dll",
        "firestore/obj/x86/Release/Firebase.Firestore.dll",
      ),
      "Assets/Firebase/Plugins/x86_64": (
        "firestore/FirebaseCppFirestore" + os.lib_filename_suffix,
        "auth/FirebaseCppAuth" + os.lib_filename_suffix,
      ),
    }

    # Yield (src_file, dest_file) pairs from the dict above.
    for (dest_dir_str, src_file_strs) in src_file_strs_by_dest_dir_str.items():
      for src_file_str in src_file_strs:
        src_file = self.src_dir / src_file_str
        dest_file = self.dest_dir / dest_dir_str / src_file.name
        yield (src_file, dest_file)


class FirebaseCppLibraryBuildArtifactNotFound(Exception):
  pass


if __name__ == "__main__":
  app.run(main)
