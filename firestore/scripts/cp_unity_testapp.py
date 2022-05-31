#!/usr/bin/env python

"""
Copies the Unity Firestore testapp from the Firebase Unity SDK GitHub repository
into another directory and tweaks its structure and configuration to suit local
development. For example, it copies the "framework" files into their correct
locations even though they are located in a different directory tree in the Git
repository.
"""

from collections.abc import Sequence
import dataclasses
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


DEFAULTS_FILE = pathlib.Path.home() / ".cp_unity_testapp.flags.txt"
DEFAULT_GIT_REPO_DIR = pathlib.Path(__file__).parent.parent.parent

FLAG_DEFAULTS_FILE = flags.DEFINE_string(
  name="defaults_file",
  default=None,
  help="The file from which to load the default values for flags that are "
    "not explicitly specified. This is a text file where each line is stripped "
    "of leading and trailing whitespace and each line is then treated as a "
    f"single command-line flag. (default: {DEFAULTS_FILE})",
)

FLAG_GIT_REPO_DIR = flags.DEFINE_string(
  name="git_repo_dir",
  default=None,
  help="The directory of the Unity Git repository whose Firestore testapp "
    "to copy. If not specified, this directory is inferred from the location "
    f"of this file, which, in this case, is {DEFAULT_GIT_REPO_DIR}",
)

FLAG_DEST_DIR_2017 = flags.DEFINE_string(
  name="dest_dir_2017",
  default=None,
  help="The directory to which to assemble the Unity application, modified for "
    "support in Unity 2017. This directory will be deleted if it exists.",
)

FLAG_DEST_DIR_2020 = flags.DEFINE_string(
  name="dest_dir_2020",
  default=None,
  help="The directory to which to assemble the Unity application, modified for "
    "support in Unity 2020. This directory will be deleted if it exists.",
)

FLAG_GOOGLE_SERVICES_JSON_FILE = flags.DEFINE_string(
  name="google_services_json",
  default=None,
  help="The google-services.json file to use in the Unity application. "
    "This file will be copied into the destination directory and the Android "
    "package name will be read from it. The Unity project will then be edited "
    "to use the parsed Android package name. If this flag is not specified "
    "then these steps will need to be performed manually.",
)

FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE = flags.DEFINE_string(
  name="google_service_info_plist",
  default=None,
  help="The GoogleService-Info.plist file to use in the Unity application. "
    "This file will be copied into the destination directory and the Bundle ID "
    "will be read from it. The Unity project will then be edited to use the "
    "parsed Bundle ID. If this flag is not specified then these steps will "
    "need to be performed manually if targeting iOS.",
)

FLAG_ANDROID_PACKAGE_NAME = flags.DEFINE_string(
  name="android_package_name",
  default=None,
  help="The Android package name to use; must be one of the package names "
    "listed in google-services.json. If this flag is not specified then the "
    "first Android package name found in the google-services.json will be used.",
)

FLAG_APPLE_DEVELOPER_TEAM_ID = flags.DEFINE_string(
  name="apple_developer_team_id",
  default=None,
  help="The Apple developer team ID to use. The Unity project in the "
    "destination directory will be edited to use this value in the generated "
    "Xcode project. If this flag is not specified then the Developer Team ID "
    "will need to be manually set in Xcode.",
)


def main(argv: Sequence[str]) -> None:
  if len(argv) > 1:
    raise app.UsageError(f"unexpected argument: {argv[1]}")

  flags_parser = FlagsParser()
  try:
    flags = flags_parser.parse()
  except flags_parser.DefaultsFileParseError as e:
    print("ERROR: loading flag default values from "
      f"{flags_parser.defaults_file} failed: {e}", file=sys.stderr)
    sys.exit(1)
  except flags_parser.Error as e:
    print(f"ERROR: {e}", file=sys.stderr)
    sys.exit(2)

  copier = UnityTestappCopier(
    git_repo_dir=flags.git_repo_dir,
    dest_dir_2017=flags.dest_dir_2017,
    dest_dir_2020=flags.dest_dir_2020,
    google_services_json_file=flags.google_services_json_file,
    google_service_info_plist_file=flags.google_service_info_plist_file,
    android_package_name=flags.android_package_name,
    apple_developer_team_id=flags.apple_developer_team_id,
  )

  try:
    copier.run()
  except copier.Error as e:
    print(f"ERROR: {e}", file=sys.stderr)
    sys.exit(1)


class FlagsParser:

  def __init__(self) -> None:
    if FLAG_DEFAULTS_FILE.value is not None:
      self.defaults_file = pathlib.Path(FLAG_DEFAULTS_FILE.value)
    else:
      self.defaults_file = DEFAULTS_FILE

    self.git_repo_dir = DEFAULT_GIT_REPO_DIR
    self.dest_dir_2017: Optional[pathlib.Path] = None
    self.dest_dir_2020: Optional[pathlib.Path] = None
    self.google_services_json_file: Optional[pathlib.Path] = None
    self.google_service_info_plist_file: Optional[pathlib.Path] = None
    self.android_package_name: Optional[str] = None
    self.apple_developer_team_id: Optional[str] = None

  @dataclasses.dataclass(frozen=True)
  class ParsedFlags:
    git_repo_dir: pathlib.Path
    dest_dir_2017: Optional[pathlib.Path]
    dest_dir_2020: Optional[pathlib.Path]
    google_services_json_file: Optional[pathlib.Path]
    google_service_info_plist_file: Optional[pathlib.Path]
    android_package_name: Optional[str]
    apple_developer_team_id: Optional[str]

  def parse(self) -> ParsedFlags:
    self._load_defaults_file()
    self._load_flag_values()
    return self._to_parsed_flags()

  def _to_parsed_flags(self) -> ParsedFlags:
    return self.ParsedFlags(
      git_repo_dir = self.git_repo_dir,
      dest_dir_2017 = self.dest_dir_2017,
      dest_dir_2020 = self.dest_dir_2020,
      google_services_json_file = self.google_services_json_file,
      google_service_info_plist_file = self.google_service_info_plist_file,
      android_package_name = self.android_package_name,
      apple_developer_team_id = self.apple_developer_team_id,
    )

  def _load_defaults_file(self) -> None:
    if not self.defaults_file.is_file():
      return

    logging.info("Loading flag default values from file: %s", self.defaults_file)
    with self.defaults_file.open("rt", encoding="utf8") as f:
      current_flag = None
      for line_number, line in enumerate(f, start=1):
        line = line.strip()
        if current_flag is None:
          if not line.startswith("--"):
            raise self.DefaultsFileParseError(
              f"line {line_number}: should start with --: {line}")
          flag_name = line[2:]
          current_flag = self._flag_from_flag_name(flag_name)
          if current_flag is None:
            raise self.DefaultsFileParseError(
              f"line {line_number}: unknown flag: {line}")
        else:
          self._set_flag_value(current_flag, line)
          current_flag = None

    if current_flag is not None:
      raise self.DefaultsFileParseError(
        f"line {line_number}: expected line after this line: {line}")

  def _load_flag_values(self) -> None:
    if FLAG_GIT_REPO_DIR.value:
      self._log_using_flag_from_command_line(FLAG_GIT_REPO_DIR)
      self.git_repo_dir = pathlib.Path(FLAG_GIT_REPO_DIR.value)
    if FLAG_DEST_DIR_2017.value:
      self._log_using_flag_from_command_line(FLAG_DEST_DIR_2017)
      self.dest_dir_2017 = pathlib.Path(FLAG_DEST_DIR_2017.value)
    if FLAG_DEST_DIR_2020.value:
      self._log_using_flag_from_command_line(FLAG_DEST_DIR_2020)
      self.dest_dir_2020 = pathlib.Path(FLAG_DEST_DIR_2017.value)
    if FLAG_GOOGLE_SERVICES_JSON_FILE.value:
      self._log_using_flag_from_command_line(FLAG_GOOGLE_SERVICES_JSON_FILE)
      self.google_services_json_file = pathlib.Path(FLAG_GOOGLE_SERVICES_JSON_FILE.value)
    if FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE.value:
      self._log_using_flag_from_command_line(FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE)
      self.google_service_info_plist_file = pathlib.Path(FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE.value)
    if FLAG_ANDROID_PACKAGE_NAME.value:
      self._log_using_flag_from_command_line(FLAG_ANDROID_PACKAGE_NAME)
      self.android_package_name = FLAG_ANDROID_PACKAGE_NAME.value
    if FLAG_APPLE_DEVELOPER_TEAM_ID.value:
      self._log_using_flag_from_command_line(FLAG_APPLE_DEVELOPER_TEAM_ID)
      self.apple_developer_team_id = FLAG_APPLE_DEVELOPER_TEAM_ID.value

  @classmethod
  def _log_using_flag_from_command_line(cls, flag: flags.Flag) -> None:
    logging.info("Using flag from command line: --%s=%s", flag.name, flag.value)

  @classmethod
  def _flag_from_flag_name(cls, flag_name: str) -> Optional[flags.Flag]:
    known_flags = (
      FLAG_GIT_REPO_DIR,
      FLAG_DEST_DIR_2017,
      FLAG_DEST_DIR_2020,
      FLAG_GOOGLE_SERVICES_JSON_FILE,
      FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE,
      FLAG_ANDROID_PACKAGE_NAME,
      FLAG_APPLE_DEVELOPER_TEAM_ID,
    )
    for known_flag in known_flags:
      if known_flag.name == flag_name:
        return known_flag
    else:
      return None

  def _set_flag_value(self, flag: flags.Flag, value: str) -> None:
    if flag is FLAG_GIT_REPO_DIR:
      self.git_repo_dir = pathlib.Path(value)
    elif flag is FLAG_DEST_DIR_2017:
      self.dest_dir_2017 = pathlib.Path(value)
    elif flag is FLAG_DEST_DIR_2020:
      self.dest_dir_2020 = pathlib.Path(value)
    elif flag is FLAG_GOOGLE_SERVICES_JSON_FILE:
      self.google_services_json_file = pathlib.Path(value)
    elif flag is FLAG_GOOGLE_SERVICE_INFO_PLIST_FILE:
      self.google_service_info_plist_file = pathlib.Path(value)
    elif flag is FLAG_ANDROID_PACKAGE_NAME:
      self.android_package_name = value
    elif flag is FLAG_APPLE_DEVELOPER_TEAM_ID:
      self.apple_developer_team_id = value
    else:
      raise RuntimeError(f"unknown flag: {flag.value}")

    logging.info("Loaded flag from %s: --%s=%s", self.defaults_file, flag.name, value)

  class Error(Exception):
    pass

  class DefaultsFileParseError(Error):
    pass


class UnityTestappCopier:

  def __init__(
    self,
    *,
    git_repo_dir: pathlib.Path,
    dest_dir_2017: Optional[pathlib.Path],
    dest_dir_2020: Optional[pathlib.Path],
    google_services_json_file: Optional[pathlib.Path],
    google_service_info_plist_file: Optional[pathlib.Path],
    android_package_name: Optional[str],
    apple_developer_team_id: Optional[str],
  ) -> None:
    self.git_repo_dir = git_repo_dir
    self.dest_dir_2017 = dest_dir_2017
    self.dest_dir_2020 = dest_dir_2020
    self.google_services_json_file = google_services_json_file
    self.google_service_info_plist_file = google_service_info_plist_file
    self.android_package_name = android_package_name
    self.apple_developer_team_id = apple_developer_team_id

  def run(self) -> None:
    something_done = False

    if self.dest_dir_2017 is not None:
      self._run(self.dest_dir_2017, 2017)
      something_done = True
    if self.dest_dir_2020 is not None:
      self._run(self.dest_dir_2020, 2020)
      something_done = True

    if not something_done:
      raise self.Error("Nothing to do; no destination directories specified")

  def _run(self, dest_dir: pathlib.Path, unity_version: int) -> None:
    if dest_dir.exists():
      self._rmtree(dest_dir)

    testapp_dir = self.git_repo_dir / "firestore" / "testapp"
    self._copy_tree(testapp_dir, dest_dir)

    # Delete the nunit tests, since they are not maintained.
    self._rmtree(dest_dir / "Assets" / "Tests")

    # Copy AutomatedTestRunner.cs
    automated_test_runner_cs_src = self.git_repo_dir / "scripts" / "gha" / \
      "integration_testing" / "automated_testapp" / "AutomatedTestRunner.cs"
    automated_test_runner_cs_dest = dest_dir / "Assets" / "Firebase" / \
      "Sample" / "AutomatedTestRunner.cs"
    self._copy_file(automated_test_runner_cs_src, automated_test_runner_cs_dest)

    # Copy ftl_testapp_files directory.
    ftl_testapp_files_src = self.git_repo_dir / "scripts" / "gha" / \
      "integration_testing" / "automated_testapp" / "ftl_testapp_files"
    ftl_testapp_files_dest = dest_dir / "Assets" / "Firebase" / \
      "Sample" / "FirebaseTestLab"
    self._copy_tree(ftl_testapp_files_src, ftl_testapp_files_dest)

    # Delete Builder.cs in Unity 2017 since it doesn't compile
    if unity_version == 2017:
      builder_cs_file = dest_dir / "Assets" / "Firebase" / "Editor" / "Builder.cs"
      builder_cs_file.unlink()

    if self.google_services_json_file is None:
      android_package_name = None
    else:
      google_services_json_dest_file = dest_dir / "Assets" / "Firebase" / \
        "Sample" / "Firestore" / "google-services.json"
      self._copy_file(self.google_services_json_file, google_services_json_dest_file)
      android_package_name = self._load_android_package_name(google_services_json_dest_file)

    if self.google_service_info_plist_file is None:
      bundle_id = None
    else:
      google_service_info_plist_dest_file = dest_dir / "Assets" / "Firebase" / \
        "Sample" / "Firestore" / "GoogleService-Info.plist"
      self._copy_file(self.google_service_info_plist_file, google_service_info_plist_dest_file)
      bundle_id = self._load_bundle_id(google_service_info_plist_dest_file)

    if android_package_name is not None or bundle_id is not None:
      project_settings_file = dest_dir / "ProjectSettings" / "ProjectSettings.asset"
      self._update_unity_app_info(project_settings_file, android_package_name, bundle_id)

  @classmethod
  def _copy_file(cls, src_file: pathlib.Path, dest_file: pathlib.Path) -> None:
    logging.info("Copying %s to %s", src_file, dest_file)
    shutil.copy(src_file, dest_file)

  @classmethod
  def _copy_tree(cls, src_dir: pathlib.Path, dest_dir: pathlib.Path) -> None:
    logging.info("Copying %s to %s", src_dir, dest_dir)
    shutil.copytree(src_dir, dest_dir)

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
