#!/usr/bin/env python
"""
%s <src_dir> <dest_dir> [dest_dir2, [dest_dir3, ...]] [flags]

Copies the macOS build artifact of the Firebase Unity SDK to the directory of
a Unity application.

src_dir
  The directory into which the build artifacts of the Firebase Unity SDK were
  written. This is the value that was specified to the -B argument of cmake.

dest_dir
  The directories of the Unity applications into which to deploy the build
  artifacts.
"""

from collections.abc import Sequence
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
    "fnmatch module, which recognizes *, ?, and [anychar]",
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
  dest_dirs = tuple(pathlib.Path(x) for x in argv[2:])

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
    logging.info("Copying macOS build artifacts from %s to %s", self.src_dir, self.dest_dir)
    (src_cpp_app_library_os, src_cpp_app_library_file) = self.find_src_cpp_app_library()
    self.cleanup_old_dlls(src_cpp_app_library_os, src_cpp_app_library_file.name)
    self.copy_files(src_cpp_app_library_os, src_cpp_app_library_file)

  def find_src_cpp_app_library(self) -> tuple[OS, pathlib.Path]:
    found_artifacts: list[tuple[OS, pathlib.Path]] = []

    logging.info("Searching for C++ library build artifacts in %s", self.src_dir)
    for candidate_artifact_path in self.src_dir.iterdir():
      for candidate_os in OS:
        if candidate_artifact_path.name.endswith(candidate_os.lib_filename_suffix):
          logging.info("Found C++ library build artifact: %s", candidate_artifact_path)
          found_artifacts.append((candidate_os, candidate_artifact_path))

    if len(found_artifacts) == 0:
      raise FirebaseCppLibraryBuildArtifactNotFound(
        f"No C++ library build artifacts found in {self.src_dir}; "
        f"expected to find exactly one file whose name has one of the "
        f"following suffixes: " + ", ".join(OS)
      )
    elif len(found_artifacts) > 1:
      raise FirebaseCppLibraryBuildArtifactNotFound(
        f"Found {len(found_artifacts)} C++ library build artifacts in "
        f"{self.src_dir}; "
        f"expected to find exactly one file whose name has one of the "
        f"following suffixes: " + ", ".join(OS)
      )

    return found_artifacts[0]

  def cleanup_old_dlls(self, os: OS, src_cpp_app_library_file_name: str) -> None:
    if self.copy_pattern is not None and not self.copy_pattern.fullmatch(src_cpp_app_library_file_name):
      return

    expr = re.compile(r"FirebaseCppApp-\d+_\d+_\d+" + re.escape(os.lib_filename_suffix))
    if not expr.fullmatch(src_cpp_app_library_file_name):
      raise ValueError(f"invalid filename: {src_cpp_app_library_file_name}")

    x86_64_dir = self.dest_dir / "Assets" / "Firebase" / "Plugins" / "x86_64"
    for candidate_dll_file in x86_64_dir.iterdir():
      if expr.fullmatch(candidate_dll_file.name):
        if candidate_dll_file.name == src_cpp_app_library_file_name:
          continue
        logging.info("Deleting %s", candidate_dll_file)
        self.fs.unlink(candidate_dll_file)
        candidate_dll_meta_file = candidate_dll_file.parent / (candidate_dll_file.name + ".meta")
        if candidate_dll_meta_file.exists():
          logging.info("Deleting %s", candidate_dll_meta_file)
          self.fs.unlink(candidate_dll_meta_file)

  def copy_files(self, os: OS, src_cpp_app_library_file: pathlib.Path) -> None:
    dest_file_str_by_src_file_str = {
      "app/obj/x86/Release/Firebase.App.dll": "Assets/Firebase/Plugins/Firebase.App.dll",
      "app/platform/obj/x86/Release/Firebase.Platform.dll": "Assets/Firebase/Plugins/Firebase.Platform.dll",
      "app/task_extension/obj/x86/Release/Firebase.TaskExtension.dll": "Assets/Firebase/Plugins/Firebase.TaskExtension.dll",
      "auth/obj/x86/Release/Firebase.Auth.dll": "Assets/Firebase/Plugins/Firebase.Auth.dll",
      "firestore/obj/x86/Release/Firebase.Firestore.dll": "Assets/Firebase/Plugins/Firebase.Firestore.dll",
      "firestore/FirebaseCppFirestore" + os.lib_filename_suffix: "Assets/Firebase/Plugins/x86_64/FirebaseCppFirestore" + os.lib_filename_suffix,
      "auth/FirebaseCppAuth" + os.lib_filename_suffix: "Assets/Firebase/Plugins/x86_64/FirebaseCppAuth" + os.lib_filename_suffix,
    }

    dest_path_by_src_path = {
      self.src_dir / src_file_str: self.dest_dir / dest_file_str
      for (src_file_str, dest_file_str)
      in dest_file_str_by_src_file_str.items()
    }
    dest_path_by_src_path[src_cpp_app_library_file] = (
      self.dest_dir / "Assets" / "Firebase" / "Plugins"
      / "x86_64" / src_cpp_app_library_file.name
    )

    for (src_file, dest_file) in sorted(dest_path_by_src_path.items()):
      if self.copy_pattern is None or self.copy_pattern.fullmatch(src_file.name):
        logging.info("Copying %s to %s", src_file, dest_file)
        self.fs.copy_file(src_file, dest_file)


class FirebaseCppLibraryBuildArtifactNotFound(Exception):
  pass


if __name__ == "__main__":
  app.run(main)
