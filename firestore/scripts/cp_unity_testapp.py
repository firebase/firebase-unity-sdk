#!/usr/bin/env python

"""
Syntax: %s [options] <dest_dir>

Copies the Unity Firestore testapp from the Firebase Unity SDK GitHub repository
into another directory and tweaks its structure and configuration to suit local
development. For example, it copies the "framework" files into their correct
locations even though they are located in a different directory tree in the Git
repository.
"""

from collections.abc import Sequence
import json
import pathlib
import shutil
import re
import sys
from typing import Optional
from xml.dom import minidom

from absl import app
from absl import flags
from absl import logging


DEFAULT_GIT_REPO_DIR = pathlib.Path(__file__).parent.parent.parent

FLAG_GIT_REPO_DIR = flags.DEFINE_string(
  name="git_repo_dir",
  default=None,
  help="The directory of the Unity Git repository whose Firestore testapp "
    "to copy. If not specified, this directory is inferred from the location "
    f"of this file, which, in this case, is {DEFAULT_GIT_REPO_DIR}",
)

FLAG_GOOGLE_SERVICES_JSON_FILE = flags.DEFINE_string(
  name="google_services_json",
  default=None,
  help="The google-services.json file to use in the Unity application. "
    "This file will be copied into the destination directory and the Android "
    "package name will be read from it. The Unity project will then be edited "
    "to use the parsed Android package name. If this flag is not specified, "
    "or is empty, then these steps will need to be performed manually.",
)

FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE = flags.DEFINE_string(
  name="google_service_info_plist",
  default=None,
  help="The GoogleService-Info.plist file to use in the Unity application. "
    "This file will be copied into the destination directory and the Bundle ID "
    "will be read from it. The Unity project will then be edited to use the "
    "parsed Bundle ID. If this flag is not specified, or is empty, then these "
    "steps will need to be performed manually if targeting iOS.",
)

FLAG_ANDROID_PACKAGE_NAME = flags.DEFINE_string(
  name="android_package_name",
  default=None,
  help="The Android package name to use; must be one of the package names "
    "listed in google-services.json. If this flag is not specified, or is "
    "empty then the first Android package name found in the "
    "google-services.json will be used.",
)

FLAG_APPLE_DEVELOPER_TEAM_ID = flags.DEFINE_string(
  name="apple_developer_team_id",
  default=None,
  help="The Apple developer team ID to use. The Unity project in the "
    "destination directory will be edited to use this value in the generated "
    "Xcode project. If this flag is not specified, or is empty, then the "
    "Developer Team ID will need to be manually set in Xcode.",
)

FLAG_HARDLINK_CS_FILES = flags.DEFINE_boolean(
  name="hardlink",
  default=False,
  help="Instead of copying the .cs source files, hardlink them. This can be "
    "useful when developing the C# code for the testapp itself, as changes "
    "to those files will be instantly reflected both in the destination "
    "Unity project and the GitHub repository. (default: %(default)s)"
)


def main(argv: Sequence[str]) -> None:
  if len(argv) < 2:
    raise app.UsageError(f"<dest_dir> must be specified")
  if len(argv) > 2:
    raise app.UsageError(f"unexpected argument: {argv[2]}")

  dest_dir = pathlib.Path(argv[1])

  git_repo_dir = \
    pathlib.Path(FLAG_GIT_REPO_DIR.value) \
    if FLAG_GIT_REPO_DIR.value else DEFAULT_GIT_REPO_DIR
  google_services_json_file = \
    pathlib.Path(FLAG_GOOGLE_SERVICES_JSON_FILE.value) \
    if FLAG_GOOGLE_SERVICES_JSON_FILE.value else None
  google_service_info_plist_file = \
    pathlib.Path(FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE.value) \
    if FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE.value else None
  android_package_name = \
    FLAG_ANDROID_PACKAGE_NAME.value \
    if FLAG_ANDROID_PACKAGE_NAME.value else None
  apple_developer_team_id = \
    FLAG_APPLE_DEVELOPER_TEAM_ID.value \
    if FLAG_APPLE_DEVELOPER_TEAM_ID.value else None

  copier = UnityTestappCopier(
    git_repo_dir=git_repo_dir,
    dest_dir=dest_dir,
    google_services_json_file=google_services_json_file,
    google_service_info_plist_file=google_service_info_plist_file,
    android_package_name=android_package_name,
    apple_developer_team_id=apple_developer_team_id,
    hardlink_cs_files=FLAG_HARDLINK_CS_FILES.value,
  )

  try:
    copier.run()
  except copier.Error as e:
    print(f"ERROR: {e}", file=sys.stderr)
    sys.exit(1)


class UnityTestappCopier:

  def __init__(
    self,
    *,
    git_repo_dir: pathlib.Path,
    dest_dir: pathlib.Path,
    google_services_json_file: Optional[pathlib.Path],
    google_service_info_plist_file: Optional[pathlib.Path],
    android_package_name: Optional[str],
    apple_developer_team_id: Optional[str],
    hardlink_cs_files: bool,
  ) -> None:
    self.git_repo_dir = git_repo_dir
    self.dest_dir = dest_dir
    self.google_services_json_file = google_services_json_file
    self.google_service_info_plist_file = google_service_info_plist_file
    self.android_package_name = android_package_name
    self.apple_developer_team_id = apple_developer_team_id
    self.hardlink_cs_files = hardlink_cs_files

  def run(self) -> None:
    if self.dest_dir.exists():
      self._rmtree(self.dest_dir)

    testapp_dir = self.git_repo_dir / "firestore" / "testapp"
    self._copy_tree(testapp_dir, self.dest_dir)

    # Delete the nunit tests, since they are not maintained.
    self._rmtree(self.dest_dir / "Assets" / "Tests")

    # Copy AutomatedTestRunner.cs
    automated_test_runner_cs_src = self.git_repo_dir / "scripts" / "gha" / \
      "integration_testing" / "automated_testapp" / "AutomatedTestRunner.cs"
    automated_test_runner_cs_dest = self.dest_dir / "Assets" / "Firebase" / \
      "Sample" / "AutomatedTestRunner.cs"
    self._copy_file(automated_test_runner_cs_src, automated_test_runner_cs_dest)

    # Copy ftl_testapp_files directory.
    ftl_testapp_files_src = self.git_repo_dir / "scripts" / "gha" / \
      "integration_testing" / "automated_testapp" / "ftl_testapp_files"
    ftl_testapp_files_dest = self.dest_dir / "Assets" / "Firebase" / \
      "Sample" / "FirebaseTestLab"
    self._copy_tree(ftl_testapp_files_src, ftl_testapp_files_dest)

    if self.google_services_json_file is None:
      android_package_name = None
    else:
      google_services_json_dest_file = self.dest_dir / "Assets" / "Firebase" / \
        "Sample" / "Firestore" / "google-services.json"
      self._copy_file(self.google_services_json_file, google_services_json_dest_file)
      android_package_name = self._load_android_package_name(google_services_json_dest_file)

    if self.google_service_info_plist_file is None:
      bundle_id = None
    else:
      google_service_info_plist_dest_file = self.dest_dir / "Assets" / \
        "Firebase" / "Sample" / "Firestore" / "GoogleService-Info.plist"
      self._copy_file(self.google_service_info_plist_file, google_service_info_plist_dest_file)
      bundle_id = self._load_bundle_id(google_service_info_plist_dest_file)

    if android_package_name is not None or bundle_id is not None:
      project_settings_file = self.dest_dir / "ProjectSettings" / "ProjectSettings.asset"
      self._update_unity_app_info(project_settings_file, android_package_name, bundle_id)

  # A drop-in replacement for `shutil.copy()` that creates hard links for some files
  # if hardlink_cs_files=True was specified to __init__().
  def _copy(self, src, dst, *, follow_symlinks=True):
    if self.hardlink_cs_files and str(src).endswith(".cs"):
      src_file = pathlib.Path(src)
      dst_file = pathlib.Path(dst)
      src_file.link_to(dst_file)
    else:
      shutil.copy(src, dst, follow_symlinks=follow_symlinks)

  def _copy_file(self, src_file: pathlib.Path, dest_file: pathlib.Path) -> None:
    logging.info("Copying %s to %s", src_file, dest_file)
    self._copy(src_file, dest_file)

  def _copy_tree(self, src_dir: pathlib.Path, dest_dir: pathlib.Path) -> None:
    logging.info("Copying %s to %s", src_dir, dest_dir)
    shutil.copytree(src_dir, dest_dir, copy_function=self._copy)

  @classmethod
  def _rmtree(cls, dir_path: pathlib.Path) -> None:
    logging.info("Deleting %s", dir_path)
    shutil.rmtree(dir_path)

  def _load_android_package_name(self, file_path: pathlib.Path) -> str:
    logging.info("Loading Android package name from %s", file_path)
    with file_path.open("rb") as f:
      data = json.load(f)

    package_names = []
    clients = data.get("client", [])
    for client in clients:
      client_info = client.get("client_info")
      if client_info is None:
        continue

      android_client_info = client_info.get("android_client_info")
      if android_client_info is None:
        continue

      package_name = android_client_info.get("package_name")
      if package_name is not None:
        logging.debug(f"Found package name in {file_path}: {package_name}")
        package_names.append(package_name)

    if len(package_names) == 0:
      raise self.Error(f"No Android package names found in {file_path}")

    if self.android_package_name is None:
      package_name = package_names[0]
    elif self.android_package_name not in package_names:
      raise self.Error(
        f"Android package name {self.android_package_name} not found in {file_path}; "
        f"consider instead using one of the following {len(package_names)} package "
        f"names that were found: "
        + ", ".join(sorted(package_names)))
    else:
      package_name = self.android_package_name

    logging.info("Loaded Android package name from %s: %s", file_path, package_name)
    return package_name

  @classmethod
  def _load_bundle_id(self, file_path: pathlib.Path) -> str:
    logging.info("Loading bundle ID from %s", file_path)
    with file_path.open("rb") as f:
      root = minidom.parse(f)

    next_element_is_bundle_id = False
    for dict_node in root.documentElement.getElementsByTagName("dict"):
      for child_node in dict_node.childNodes:
        if child_node.nodeType != child_node.ELEMENT_NODE:
          continue
        elif next_element_is_bundle_id:
          bundle_id = child_node.firstChild.nodeValue
          logging.info("Loaded bundle ID from %s: %s", file_path, bundle_id)
          return bundle_id
        elif child_node.tagName == "key":
          if child_node.firstChild.nodeValue == "BUNDLE_ID":
            next_element_is_bundle_id = True

    raise self.Error("No bundle ID found in {file_path}")

  def _update_unity_app_info(
    self,
    file_path: pathlib.Path,
    android_package_name: Optional[str],
    bundle_id: Optional[str],
  ) -> None:
    if android_package_name is not None:
      logging.info("Setting Android package name to %s in %s",
        android_package_name, file_path)
    if bundle_id is not None:
      logging.info("Setting Bundle ID to %s in %s", bundle_id, file_path)

    with file_path.open("rt", encoding="utf8") as f:
      lines = list(f)

    app_id_expr = re.compile(r"\s+(\w+):\s*([a-zA-Z][a-zA-Z0-9.]+)\s*$")
    for i in range(len(lines)):
      line = lines[i]

      apple_developer_team_id_token = "appleDeveloperTeamID:"
      if line.strip() == apple_developer_team_id_token:
        if self.apple_developer_team_id is not None:
          token_index = line.index(apple_developer_team_id_token)
          eol_index = len(line.rstrip())
          lines[i] = line[:token_index] + apple_developer_team_id_token \
            + " " + self.apple_developer_team_id + line[eol_index:]
        continue

      match = app_id_expr.match(line)
      if not match:
        continue

      key = match.group(1)
      value = match.group(2)
      if key in ("Android", "Standalone"):
        new_value = android_package_name
      elif key in ("iOS", "iPhone", "tvOS"):
        new_value = bundle_id
      else:
        new_value = None

      if new_value is not None:
        lines[i] = line[:match.start(2)] + new_value + line[match.end(2):]

    with file_path.open("wt", encoding="utf8") as f:
      f.writelines(lines)


  class Error(Exception):
    pass


if __name__ == "__main__":
  app.run(main)
