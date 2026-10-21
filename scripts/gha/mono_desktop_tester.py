# Copyright 2026 Google LLC
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

r"""Runs and validates Mono desktop integration testapps.

Usage:
  python scripts/gha/mono_desktop_tester.py --mono_build_dir mono_build
"""

import os
import platform
import shutil
import subprocess
import threading
import time

from absl import app
from absl import flags
from absl import logging
import attr

from integration_testing import test_validation

FLAGS = flags.FLAGS

flags.DEFINE_string(
    "mono_build_dir", "mono_build",
    "Path to the CMake Mono build directory.")
flags.DEFINE_string(
    "repo_dir", os.getcwd(),
    "Path to the firebase-unity-sdk repository root.")
flags.DEFINE_string(
    "apis",
    "analytics,app_check,auth,crashlytics,database,firebaseai,firestore,"
    "functions,installations,messaging,remote_config,storage",
    "Comma-separated list of Firebase APIs to test.")
flags.DEFINE_string(
    "logfile_name", "mono-desktop",
    "Create test log artifact test-results-$logfile_name.log.")
flags.DEFINE_integer(
    "timeout_seconds", 300,
    "Maximum time in seconds to allow each product testapp to run.")
flags.DEFINE_integer(
    "test_timeout_seconds", 0,
    "Optional per-test timeout override in seconds (MONO_TEST_TIMEOUT_SECONDS).")
flags.DEFINE_integer(
    "max_tests", 0,
    "Optional cap on number of tests per product (MONO_TEST_MAX_TESTS).")
flags.DEFINE_bool(
    "parallel", False,
    "Run product testapps in parallel threads instead of sequentially.")

API_TO_TARGET = {
    "analytics": ("Analytics", "MonoAnalyticsTestApp"),
    "app_check": ("AppCheck", "MonoAppCheckTestApp"),
    "auth": ("Auth", "MonoAuthTestApp"),
    "crashlytics": ("Crashlytics", "MonoCrashlyticsTestApp"),
    "database": ("Database", "MonoDatabaseTestApp"),
    "firebaseai": ("FirebaseAI", "MonoFirebaseAITestApp"),
    "firestore": ("Firestore", "MonoFirestoreTestApp"),
    "functions": ("Functions", "MonoFunctionsTestApp"),
    "installations": ("Installations", "MonoInstallationsTestApp"),
    "messaging": ("Messaging", "MonoMessagingTestApp"),
    "remote_config": ("RemoteConfig", "MonoRemoteConfigTestApp"),
    "storage": ("Storage", "MonoStorageTestApp"),
}

NATIVE_LIB_SUBDIRS = (
    "",
    "analytics",
    "app_check",
    "auth",
    "crashlytics",
    "database",
    "firestore",
    "installations",
    "messaging",
    "remote_config",
    "storage",
)

DUMMY_GOOGLE_SERVICES_JSON = """{
  "project_info": {
    "project_number": "123456789000",
    "firebase_url": "https://austinbenoit-sandbox-778231-default-rtdb.firebaseio.com",
    "project_id": "austinbenoit-sandbox-778231",
    "storage_bucket": "austinbenoit-sandbox-778231.appspot.com"
  },
  "client": [
    {
      "client_info": {
        "mobilesdk_app_id": "1:123456789000:android:abcdef1234567890",
        "android_client_info": {
          "package_name": "com.google.firebase.unity.testapp"
        }
      },
      "api_key": [
        {
          "current_key": "AIzaSyDummyKeyForLocalMonoTestRunner00000"
        }
      ]
    }
  ],
  "configuration_version": "1"
}
"""


def _build_library_env(mono_build_dir):
  """Constructs environment variables with native library search paths."""
  env = os.environ.copy()
  lib_dirs = [
      os.path.join(mono_build_dir, subdir) if subdir else mono_build_dir
      for subdir in NATIVE_LIB_SUBDIRS
  ]
  lib_dirs = [d for d in lib_dirs if os.path.isdir(d)]
  joined = os.pathsep.join(lib_dirs)

  if platform.system() == "Darwin":
    existing = env.get("DYLD_LIBRARY_PATH", "")
    env["DYLD_LIBRARY_PATH"] = f"{joined}:{existing}" if existing else joined
  elif platform.system() == "Windows":
    existing = env.get("PATH", "")
    env["PATH"] = f"{joined};{existing}" if existing else joined
  else:
    existing = env.get("LD_LIBRARY_PATH", "")
    env["LD_LIBRARY_PATH"] = f"{joined}:{existing}" if existing else joined

  env["MONO_TEST_MAX_TOTAL_SECONDS"] = str(FLAGS.timeout_seconds)
  if FLAGS.test_timeout_seconds > 0:
    env["MONO_TEST_TIMEOUT_SECONDS"] = str(FLAGS.test_timeout_seconds)
  if FLAGS.max_tests > 0:
    env["MONO_TEST_MAX_TESTS"] = str(FLAGS.max_tests)
  return env


def _prepare_config_files(repo_dir, api, sample_dir_name, app_dir):
  """Copies decrypted google-services.json into app_dir (or writes dummy config)."""
  src_json = os.path.join(
      repo_dir, api, "testapp", "Assets", "Firebase", "Sample",
      sample_dir_name, "google-services.json")
  dest_json = os.path.join(app_dir, "google-services.json")
  dest_desktop_json = os.path.join(app_dir, "google-services-desktop.json")

  if os.path.exists(src_json):
    shutil.copyfile(src_json, dest_json)
    shutil.copyfile(src_json, dest_desktop_json)
    logging.info("Copied %s to %s", src_json, app_dir)
  elif not os.path.exists(dest_json) and not os.path.exists(dest_desktop_json):
    logging.warning(
        "No decrypted google-services.json found at %s; writing dummy config to %s",
        src_json, app_dir)
    with open(dest_json, "w") as f:
      f.write(DUMMY_GOOGLE_SERVICES_JSON)
    with open(dest_desktop_json, "w") as f:
      f.write(DUMMY_GOOGLE_SERVICES_JSON)


@attr.s(frozen=False, eq=False)
class MonoTest(object):
  """Holds data related to the testing of one Mono testapp."""
  api = attr.ib()
  testapp_path = attr.ib()
  env = attr.ib()
  timeout_seconds = attr.ib()
  logs = attr.ib(init=False, default=None)

  def run(self):
    """Executes this Mono testapp."""
    app_dir = os.path.dirname(self.testapp_path)
    log_path = self.testapp_path + ".log"
    stdout_log_path = self.testapp_path + ".stdout.log"
    for p in (log_path, stdout_log_path):
      try:
        os.remove(p)
      except FileNotFoundError:
        pass

    mono_bin = shutil.which("mono") or "/usr/bin/mono"
    args = [
        mono_bin,
        self.testapp_path,
        "-batchmode",
        "-nographics",
        "-TestLabManager.logPath",
        log_path,
    ]
    logging.info("Starting Mono testapp for %s: %s", self.api, " ".join(args))
    with open(stdout_log_path, "w") as stdout_file:
      open_process = subprocess.Popen(
          args=args,
          cwd=app_dir,
          env=self.env,
          stdout=stdout_file,
          stderr=subprocess.STDOUT)
      time_until_timeout = self.timeout_seconds
      while open_process.poll() is None and time_until_timeout > 0:
        time.sleep(2)
        time_until_timeout -= 2
        if os.path.exists(log_path):
          with open(log_path, errors="replace") as f:
            current_logs = f.read()
          if "All tests finished" in current_logs:
            # Give the process a moment to exit cleanly.
            try:
              open_process.wait(timeout=5)
            except subprocess.TimeoutExpired:
              open_process.kill()
            break

      if open_process.poll() is None:
        logging.warning("Mono testapp %s timed out after %ds; killing.",
                        self.api, self.timeout_seconds)
        open_process.kill()

    if os.path.exists(log_path):
      with open(log_path, errors="replace") as f:
        self.logs = f.read()
    elif os.path.exists(stdout_log_path):
      with open(stdout_log_path, errors="replace") as f:
        self.logs = f.read()
    else:
      self.logs = ""

    logging.info("Finished running %s (log size: %d bytes)",
                 self.testapp_path, len(self.logs or ""))


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  mono_build_dir = os.path.abspath(os.path.expanduser(FLAGS.mono_build_dir))
  repo_dir = os.path.abspath(os.path.expanduser(FLAGS.repo_dir))
  requested_apis = [
      a.strip() for a in FLAGS.apis.replace("'", "").replace('"', "").split(",")
      if a.strip()
  ]

  env = _build_library_env(mono_build_dir)
  tests = []

  for api in requested_apis:
    if api not in API_TO_TARGET:
      raise app.UsageError(f"Unknown API '{api}'. Valid APIs: {list(API_TO_TARGET.keys())}")
    sample_dir_name, target_name = API_TO_TARGET[api]
    exe_path = os.path.join(
        mono_build_dir, "samples", "mono_app", "bin", target_name,
        target_name + ".exe")
    if not os.path.exists(exe_path):
      logging.error("Mono testapp executable not found for %s: %s", api, exe_path)
      return 1
    _prepare_config_files(repo_dir, api, sample_dir_name, os.path.dirname(exe_path))
    tests.append(MonoTest(
        api=api,
        testapp_path=exe_path,
        env=env,
        timeout_seconds=FLAGS.timeout_seconds))

  if FLAGS.parallel:
    threads = [threading.Thread(target=t.run) for t in tests]
    for t in threads:
      t.start()
    for t in threads:
      t.join()
  else:
    for t in tests:
      t.run()

  return test_validation.summarize_test_results(
      tests,
      test_validation.UNITY,
      mono_build_dir,
      file_name="test-results-" + FLAGS.logfile_name + ".log")


if __name__ == "__main__":
  app.run(main)
