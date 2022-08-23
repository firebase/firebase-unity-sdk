import os
import attr
import subprocess
import json

from absl import app
from absl import flags
from absl import logging

from integration_testing import test_validation
from integration_testing import gcs

FLAGS = flags.FLAGS

flags.DEFINE_string("test_result", None, "FTL test result in JSON format.")
flags.DEFINE_enum("output_path", None, "Log will be write into this path.")


@attr.s(frozen=False, eq=False)
class Test(object):
  """Holds data related to the testing of one testapp."""
  testapp_path = attr.ib()
  logs = attr.ib()


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  test_result = json.loads(FLAGS.test_result)
  tests = []
  for app in test_result.get("apps"):
    app_path = app.get("testapp_path")
    return_code = app.get("return_code")
    logging.info("testapp: %s\nreturn code: %s" % (app_path, return_code))
    if return_code == 0:
      gcs_dir = app.get("raw_result_link").replace("https://console.developers.google.com/storage/browser/", "gs://")
      logging.info("gcs_dir: %s" % gcs_dir)
      logs = _get_testapp_log_text_from_gcs(gcs_dir)
      logging.info("Test result: %s", logs)
      tests.append(Test(testapp_path=app_path, logs=logs))

  (output_dir, file_name) = os.path.split(os.path.abspath(FLAGS.output_path))
  FLAGS.output_path
  return test_validation.summarize_test_results(
    tests, 
    "unity", 
    output_dir, 
    file_name=file_name)


def _get_testapp_log_text_from_gcs(gcs_path):
  """Gets the testapp log text generated by game loops."""
  try:
    gcs_contents = _gcs_list_dir(gcs_path)
  except subprocess.CalledProcessError as e:
    logging.error("Unexpected error searching GCS logs:\n%s", e)
    return None
  # The path to the testapp log depends on the platform, device, and scenario
  # being tested. Search for a json file with 'results' in the name to avoid
  # hard-coding too many assumptions about the path. The testapp log should be
  # the only json, but 'results' adds some redundancy in case this changes.
  matching_gcs_logs = [
    line for line in gcs_contents
    if line.endswith(".json") and "results" in line.lower()
  ]
  if not matching_gcs_logs:
    logging.error("Failed to find results log on GCS.")
    return None
  # This assumes testapps only have one scenario. Could change in the future.
  if len(matching_gcs_logs) > 1:
    logging.warning("Multiple scenario logs found.")
  gcs_log = matching_gcs_logs[0]
  try:
    logging.info("Found results log: %s", gcs_log)
    log_text = _gcs_read_file(gcs_log)
    if not log_text:
      logging.warning("Testapp log is empty. App may have crashed.")
    return log_text
  except subprocess.CalledProcessError as e:
    logging.error("Unexpected error reading GCS log:\n%s", e)
    return None


def _gcs_list_dir(gcs_path):
  """Recursively returns a list of contents for a directory on GCS."""
  args = [gcs.GSUTIL, "ls", "-r", gcs_path]
  logging.info("Listing GCS contents: %s", " ".join(args))
  result = subprocess.run(args=args, capture_output=True, text=True, check=True)
  return result.stdout.splitlines()


def _gcs_read_file(gcs_path):
  """Extracts the contents of a file on GCS."""
  gcs_path = gcs.relative_path_to_gs_uri(gcs_path)
  args = [gcs.GSUTIL, "cat", gcs_path]
  logging.info("Reading GCS file: %s", " ".join(args))
  result = subprocess.run(args=args, capture_output=True, text=True, check=True)
  return result.stdout


if __name__ == '__main__':
  flags.mark_flag_as_required("test_result")
  flags.mark_flag_as_required("output_path")
  app.run(main)
