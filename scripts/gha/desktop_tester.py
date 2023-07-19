# Copyright 2020 Google LLC
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

r"""Runs and validates Unity standalone testapps.

Usage:
  python desktop_tester.py --testapp_dir ~/Downloads/testapps

This will search --testapp_dir for integration testapps whose name is given by
--testapp_name (default 'testapp').

"""

import os
import platform
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
    "testapp_dir", None, "Look for testapps recursively in this directory.")
flags.DEFINE_string(
    "testapp_name", "testapp",
    "Name of the testapps. Behaviour differs on MacOS and Windows. For"
    " 'app', this will look for a file named 'app' on Linux, an 'app.app'"
    " directory on Mac, and a file named 'app.exe' on Windows.")
flags.DEFINE_string(
    "logfile_name", "ftl-test",
    "Create test log artifact test-results-$logfile_name.log."
    " logfile will be created and placed in testapp_dir.")  


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  testapp_dir = _fix_path(FLAGS.testapp_dir)
  testapp_name = FLAGS.testapp_name

  if platform.system() == "Windows":
    testapp_name += ".exe"

  logging.info("Searching for file named '%s' in %s", testapp_name, testapp_dir)
  testapps = []
  for file_dir, _, file_names in os.walk(testapp_dir):
    for file_name in file_names:
      # match Testapp names, e.g. "Firebase Analytics Unity Testapp"
      if testapp_name in file_name.lower():
        testapps.append(os.path.join(file_dir, file_name))

  if not testapps:
    logging.error("No testapps found.")
    test_validation.write_summary(testapp_dir, "Desktop tests: none found.")
    return 1
  logging.info("Testapps found: %s\n", "\n".join(testapps))

  logging.info("Running tests...")
  tests = [Test(testapp_path=testapp) for testapp in testapps]
  threads = []
  for test in tests:
    thread = threading.Thread(target=test.run)
    threads.append(thread)
    thread.start()
  for thread in threads:
    thread.join()
  logging.info("Finished running tests.")

  return test_validation.summarize_test_results(
      tests, 
      test_validation.UNITY, 
      testapp_dir,
      file_name="test-results-" + FLAGS.logfile_name + ".log")


def _fix_path(path):
  """Expands ~, normalizes slashes, and converts relative paths to absolute."""
  return os.path.abspath(os.path.expanduser(path))


@attr.s(frozen=False, eq=False)
class Test(object):
  """Holds data related to the testing of one testapp."""
  testapp_path = attr.ib()
  # This will be populated after the test completes, instead of initialization.
  logs = attr.ib(init=False, default=None)

  # This runs in a separate thread, so instead of returning values we store
  # them as fields so they can be accessed from the main thread.
  def run(self):
    """Executes this testapp."""
    log_path = self.testapp_path + ".log"
    try:
      os.remove(log_path)  # Remove log file from previous runs.
    except FileNotFoundError:
      pass
    os.chmod(self.testapp_path, 0o777)
    args = [
        self.testapp_path, "-batchmode", "-nographics",
        "-TestLabManager.logPath", log_path]
    # Unity testapps do not exit when they're done, so we need more control
    # over the process than subprocess.run gives us. We kill the process
    # and declare the test finished when we see the final summary in the logs.
    open_process = subprocess.Popen(args=args)
    test_running = True
    time_until_timeout = 300  # Timeout of 300 seconds, or 5 minutes.
    while test_running and time_until_timeout > 0:
      time.sleep(5)
      time_until_timeout -= 5
      if os.path.exists(log_path):
        with open(log_path) as f:
          self.logs = f.read()
        test_running = "All tests finished" not in self.logs
    open_process.kill()
    if platform.system() == 'Linux':
      # Linux seems to have a problem printing out too much information, so truncate it
      logging.info("Test result: %s (Log might be truncated)", self.logs[:64000])
    else:
      logging.info("Test result: %s", self.logs)
    logging.info("Finished running %s", self.testapp_path)


if __name__ == "__main__":
  flags.mark_flag_as_required("testapp_dir")
  app.run(main)
