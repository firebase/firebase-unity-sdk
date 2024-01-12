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

"""A utility for integration test workflow.

This script helps to update PR/Issue comments and labels during testing process. 

For PR comment, this script will update (create if not exist) the "Test Result" in comment.
stage value: [start, progress, end]
USAGE:
  python scripts/gha/it_workflow.py --stage <stage> \
    --token ${{github.token}} \
    --issue_number ${{needs.check_trigger.outputs.pr_number}}\
    --actor ${{github.actor}} \
    --commit ${{needs.prepare_matrix.outputs.github_ref}} \
    --run_id ${{github.run_id}} \
    [--new_token ${{steps.generate-token.outputs.token}}]

For Daily Report, this script will update (create if not exist) the "Test Result" in Issue 
with title "Nightly Integration Testing Report" and label "nightly-testing".
stage value: [report]
USAGE:
  python scripts/gha/it_workflow.py --stage report \
    --token ${{github.token}} \
    --actor ${{github.actor}} \
    --commit ${{needs.prepare_matrix.outputs.github_ref}} \
    --run_id ${{github.run_id}}

"""

import datetime
import pytz

from absl import app
from absl import flags
from absl import logging

import firebase_github
import summarize_test_results as summarize

_REPORT_LABEL = "nightly-testing"
_REPORT_TITLE = "[Unity] Nightly Integration Testing Report"

_LABEL_TRIGGER_FULL = "tests-requested: full"
_LABEL_TRIGGER_QUICK = "tests-requested: quick"
_LABEL_PROGRESS = "tests: in-progress"
_LABEL_FAILED = "tests: failed"
_LABEL_SUCCEED = "tests: succeeded"

_COMMENT_TITLE_PROGESS = "### ⏳&nbsp; Integration test in progress...\n"
_COMMENT_TITLE_PROGESS_FLAKY = "### Integration test with FLAKINESS (but still ⏳&nbsp; in progress)\n" 
_COMMENT_TITLE_PROGESS_FAIL = "### ❌&nbsp; Integration test FAILED (but still ⏳&nbsp; in progress)\n" 
_COMMENT_TITLE_FLAKY = "### Integration test with FLAKINESS (succeeded after retry)\n"
_COMMENT_TITLE_FAIL = "### ❌&nbsp; Integration test FAILED\n"
_COMMENT_TITLE_SUCCEED = "### ✅&nbsp; Integration test succeeded!\n"

_COMMENT_IDENTIFIER = "integration-test-status-comment"
_COMMENT_HIDDEN_DIVIDER = f'\r\n<hidden value="{_COMMENT_IDENTIFIER}"></hidden>\r\n'

_COMMENT_IDENTIFIER_DASHBOARD = "build-dashboard-comment"
_COMMENT_DASHBOARD_START = f'\r\n<hidden value="{_COMMENT_IDENTIFIER_DASHBOARD}-start"></hidden>\r\n'
_COMMENT_DASHBOARD_END = f'\r\n<hidden value="{_COMMENT_IDENTIFIER_DASHBOARD}-end"></hidden>\r\n'
_COMMENT_SUFFIX = f'\n<hidden value="{_COMMENT_IDENTIFIER}"></hidden>'

_LOG_ARTIFACT_NAME = "log-artifact"
_LOG_OUTPUT_DIR = "test_results"

_BUILD_STAGES_START = "start"
_BUILD_STAGES_PROGRESS = "progress"
_BUILD_STAGES_END = "end"
_BUILD_STAGES_REPORT = "report"
_BUILD_STAGES = [_BUILD_STAGES_START, _BUILD_STAGES_PROGRESS, _BUILD_STAGES_END, _BUILD_STAGES_REPORT]

FLAGS = flags.FLAGS

flags.DEFINE_string(
    "stage", None,
    "Different stage while running the workflow. Valid values in _BUILD_STAGES.")

flags.DEFINE_string(
    "token", None, 
    "github.token: A token to authenticate on your repository.")

flags.DEFINE_string(
    "issue_number", None,
    "Github's issue # or pull request #.")

flags.DEFINE_string(
    "actor", None,
    "github.actor: The login of the user that initiated the workflow run.")

flags.DEFINE_string(
    "commit", None, "GitHub commit hash")

flags.DEFINE_string(
    "run_id", None,
    "github.run_id: A unique number for each workflow run within a repository.")

flags.DEFINE_string(
    "new_token", None,
    "Only used with --stage end"
    "Use a different token to remove the \"in-progress\" label,"
    "to allow the removal to trigger the \"Check Labels\" workflow.")   


def test_start(token, issue_number, actor, commit, run_id):
  """In PR, when start testing, add comment and label \"tests: in-progress\""""
  firebase_github.add_label(token, issue_number, _LABEL_PROGRESS)
  for label in [_LABEL_TRIGGER_FULL, _LABEL_TRIGGER_QUICK, _LABEL_FAILED, _LABEL_SUCCEED]:
    firebase_github.delete_label(token, issue_number, label)

  comment = (_COMMENT_TITLE_PROGESS +
             _get_description(actor, commit, run_id) +
             _COMMENT_HIDDEN_DIVIDER)
  _update_comment(token, issue_number, comment)


def test_progress(token, issue_number, actor, commit, run_id):
  """In PR, when some test failed, update failure info and 
  add label \"tests: failed\""""
  success_or_only_flakiness, log_summary = _get_summary_table(token, run_id)
  if success_or_only_flakiness and not log_summary:
    # succeeded (without flakiness)
    return
  else:
    if success_or_only_flakiness:
      # all failures/errors are due to flakiness (succeeded after retry)
      title = _COMMENT_TITLE_PROGESS_FLAKY
    else:
      # failures/errors still exist after retry
      title = _COMMENT_TITLE_PROGESS_FAIL
      firebase_github.add_label(token, issue_number, _LABEL_FAILED)
    comment = (title +
               _get_description(actor, commit, run_id) +
               log_summary +
               _COMMENT_HIDDEN_DIVIDER)
    _update_comment(token, issue_number, comment)


def test_end(token, issue_number, actor, commit, run_id, new_token):
  """In PR, when some test end, update Test Result Report and 
  update label: add \"tests: failed\" if test failed, add label
  \"tests: succeeded\" if test succeed"""
  success_or_only_flakiness, log_summary = _get_summary_table(token, run_id)
  if success_or_only_flakiness and not log_summary:
    # succeeded (without flakiness)
    firebase_github.add_label(token, issue_number, _LABEL_SUCCEED)
    comment = (_COMMENT_TITLE_SUCCEED +
               _get_description(actor, commit, run_id) +
               _COMMENT_HIDDEN_DIVIDER)
    _update_comment(token, issue_number, comment)
  else:
    if success_or_only_flakiness:
      # all failures/errors are due to flakiness (succeeded after retry)
      title = _COMMENT_TITLE_FLAKY
      firebase_github.add_label(token, issue_number, _LABEL_SUCCEED)
    else:
      # failures/errors still exist after retry
      title = _COMMENT_TITLE_FAIL
      firebase_github.add_label(token, issue_number, _LABEL_FAILED)
    comment = (title +
               _get_description(actor, commit, run_id) +
               log_summary +
               _COMMENT_HIDDEN_DIVIDER)
    _update_comment(token, issue_number, comment)

  firebase_github.delete_label(new_token, issue_number, _LABEL_PROGRESS)


def test_report(token, actor, commit, run_id):
  """Update (create if not exist) a Daily Report in Issue. 
  The Issue with title _REPORT_TITLE and label _REPORT_LABEL:
  https://github.com/firebase/firebase-unity-sdk/issues?q=is%3Aissue+label%3Anightly-testing
  """
  issue_number = _get_issue_number(token, _REPORT_TITLE, _REPORT_LABEL)
  previous_comment = firebase_github.get_issue_body(token, issue_number)
  [previous_prefix, previous_comment_test_result] = previous_comment.split(_COMMENT_HIDDEN_DIVIDER) # TODO add more content
  logging.info("Previous prefix: %s", previous_prefix)
  prefix = ""
  # If there is a build dashboard, preserve it.
  if (_COMMENT_DASHBOARD_START in previous_prefix and
      _COMMENT_DASHBOARD_END in previous_prefix):
    logging.info("Found dashboard comment, preserving.")
    [_, previous_dashboard_plus_the_rest] = previous_prefix.split(_COMMENT_DASHBOARD_START)
    [previous_dashboard, _] = previous_dashboard_plus_the_rest.split(_COMMENT_DASHBOARD_END)
    prefix = prefix + _COMMENT_DASHBOARD_START + previous_dashboard + _COMMENT_DASHBOARD_END
    logging.info("New prefix: %s", prefix)
  else:
    logging.info("No dashboard comment '%s' or '%s'", _COMMENT_DASHBOARD_START, _COMMENT_DASHBOARD_END)

  success_or_only_flakiness, log_summary = _get_summary_table(token, run_id)
  if success_or_only_flakiness:
    if not log_summary:
      # succeeded (without flakiness)
      title = _COMMENT_TITLE_SUCCEED
      test_result = title + _get_description(actor, commit, run_id)
    else:
      title = _COMMENT_TITLE_FLAKY
      test_result = title + _get_description(actor, commit, run_id) + log_summary
  else:
    title = _COMMENT_TITLE_FAIL
    test_result = title + _get_description(actor, commit, run_id) + log_summary

  comment = prefix + _COMMENT_HIDDEN_DIVIDER + test_result

  if title == _COMMENT_TITLE_SUCCEED:
    firebase_github.close_issue(token, issue_number)
  else:
    firebase_github.open_issue(token, issue_number)
    
  firebase_github.update_issue_comment(token, issue_number, comment)


def _get_issue_number(token, title, label):
  issues = firebase_github.search_issues_by_label(label)
  for issue in issues:
    if issue["title"] == title:
      return issue["number"]

  empty_comment = (" " +
                   _COMMENT_DASHBOARD_START + " " +
                   _COMMENT_DASHBOARD_END + " " +
                   _COMMENT_HIDDEN_DIVIDER + " " +
                   _COMMENT_HIDDEN_DIVIDER + " " +
                   _COMMENT_HIDDEN_DIVIDER + " "
                   )
  return firebase_github.create_issue(token, title, label, empty_comment)["number"]


def _update_comment(token, issue_number, comment):
  comment_id = _get_comment_id(token, issue_number, _COMMENT_HIDDEN_DIVIDER)
  if not comment_id:
    firebase_github.add_comment(token, issue_number, comment)
  else:
    firebase_github.update_comment(token, comment_id, comment)

  
def _get_comment_id(token, issue_number, comment_identifier):
  comments = firebase_github.list_comments(token, issue_number)
  for comment in comments:
    if comment_identifier in comment['body']:
      return comment['id']
  return None


def _get_description(actor, commit, run_id):
  """Test Result Report Title and description"""
  return ("Requested by @%s on commit %s\n" % (actor, commit) +
          "Last updated: %s \n" % _get_datetime() +
          "**[View integration test log & download artifacts](https://github.com/firebase/firebase-unity-sdk/actions/runs/%s)**\n" % run_id)


def _get_datetime():
  """Date time when Test Result Report updated"""
  pst_now = datetime.datetime.utcnow().astimezone(pytz.timezone("America/Los_Angeles"))
  return pst_now.strftime("%a %b %e %H:%M %Z %G")


def _get_summary_table(token, run_id):
  """Test Result Report Body, which is failed test table with markdown format"""
  return summarize.summarize_logs(dir=_LOG_OUTPUT_DIR, markdown=True)


def _get_artifact_id(token, run_id, name):
  artifacts = firebase_github.list_artifacts(token, run_id)
  for artifact in artifacts:
    if artifact["name"] == name:
      return artifact["id"]


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  if FLAGS.stage == _BUILD_STAGES_START:
    test_start(FLAGS.token, FLAGS.issue_number, FLAGS.actor, FLAGS.commit, FLAGS.run_id)
  elif FLAGS.stage == _BUILD_STAGES_PROGRESS:
    test_progress(FLAGS.token, FLAGS.issue_number, FLAGS.actor, FLAGS.commit, FLAGS.run_id)
  elif FLAGS.stage == _BUILD_STAGES_END:
    test_end(FLAGS.token, FLAGS.issue_number, FLAGS.actor, FLAGS.commit, FLAGS.run_id, FLAGS.new_token)
  elif FLAGS.stage == _BUILD_STAGES_REPORT:
    test_report(FLAGS.token, FLAGS.actor, FLAGS.commit, FLAGS.run_id)
  else:
    print("Invalid stage value. Valid value: " + ",".join(_BUILD_STAGES))


if __name__ == "__main__":
  flags.mark_flag_as_required("stage")
  flags.mark_flag_as_required("token")
  flags.mark_flag_as_required("actor")
  flags.mark_flag_as_required("commit")
  flags.mark_flag_as_required("run_id")
  app.run(main)
