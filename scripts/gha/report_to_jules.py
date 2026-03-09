#!/usr/bin/env python

# Copyright 2024 Google LLC
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

"""A utility to report failed jobs to Jules.

USAGE:
  python3 scripts/gha/report_to_jules.py \
    --token ${{github.token}} \
    --jules_token ${{secrets.JULES_API_KEY}} \
    --run_id <github_workflow_run_id>
"""

import re
import json
import requests
import time
import firebase_github

from absl import app
from absl import flags
from absl import logging

FLAGS = flags.FLAGS

flags.DEFINE_string(
    "token", None,
    "github.token: A token to authenticate on your repository.")

flags.DEFINE_string(
    "jules_token", None,
    "Jules API Key.")

flags.DEFINE_string(
    "run_id", None,
    "Github's workflow run ID.")

flags.DEFINE_bool(
    "only_failed", True,
    "If true, only report failed jobs. Default is true.")

flags.DEFINE_string(
    "include_job_pattern", None,
    "Regex pattern to include jobs by name even if they succeeded (e.g. 'Build.*iOS').")

flags.DEFINE_string(
    "include_step_pattern", None,
    "Regex pattern to include log sections by step name (e.g. 'Run test.*'). Only content within matching ##[group] blocks is kept.")

flags.DEFINE_bool(
    "dryrun", False,
    "If true, just print what would be sent to Jules.")


def filter_log_by_step_pattern(log_content, pattern):
  """Filters log content to only include groups matching the pattern."""
  if not pattern:
    return log_content
    
  lines = log_content.split('\n')
  filtered_lines = []
  collecting = False
  
  # Regex to match "##[group]<Step Name>"
  # Note: Standard runner output is usually "##[group]Step Name"
  group_start_re = re.compile(r"^.*?##\[group\](.*)$")
  
  for line in lines:
    # Check for group start
    match = group_start_re.match(line)
    if match:
      step_name = match.group(1).strip()
      if re.search(pattern, step_name):
        collecting = True
        filtered_lines.append(line)
        continue
      else:
        collecting = False
    
    # Check for group end
    if "##[endgroup]" in line:
      if collecting:
        filtered_lines.append(line)
      collecting = False
      continue
      
    if collecting:
      filtered_lines.append(line)
      
  if not filtered_lines:
    return f"(No steps matched pattern '{pattern}')"
    
  return "\n".join(filtered_lines)

JULES_API_URL = "https://jules.googleapis.com/v1alpha"
# Hardcoded as per user request
SOURCE_NAME = "sources/github/firebase/firebase-unity-sdk" 

PROMPT = """
Role: You are a Senior QA Automation Engineer specializing in root cause analysis (RCA).

Task: Analyze the provided nightly integration test failure logs. Distill the failure into a concise entry for the integration_failures_flakes.md file.

Output Format: Provide the results in the following Markdown format, intended to be prepended to the top of the existing document:
--------------------------------------------------------------------------------
[Date] | Integration Test Failures and Flakes

Common Issues cross all tests:
{Common Issues}
---
FOR EACH FAILING TEST:
---
Test: {Test Name}
Faulting Workflow: {Workflow Name/File Path}

Summary: {3-sentence maximum technical summary of the fault, focusing on the "why" rather than just the "what".}
--------------------------------------------------------------------------------
Constraints:

Use clear, professional language.

If the error appears to be a network timeout or environmental flake, explicitly mention that.

Do not include raw stack traces; summarize the critical point of failure only.

Input Logs:
"""

# NOTE: The limit for the entire prompt string in bytes is around 10MB in Jules API.
# 1M characters * 4 bytes/char (worst case UTF-8) + overhead is < 10MB. 
# JSON escaping can double size of control chars, but 1M chars is usually safe enough.

MAX_INDIVIDUAL_LOG_LENGTH = 5000000


def create_session(jules_token, title_suffix=""):
  url = f"{JULES_API_URL}/sessions"
  headers = {
      "Content-Type": "application/json",
      "x-goog-api-key": jules_token
  }
  
  data = {
      "prompt": PROMPT + "\n\nI will send the logs in the next messages. I will send the 'GENERATE REPORT' command when I am done sending logs.",
      "sourceContext": {
          "source": SOURCE_NAME,
          "githubRepoContext": {
              "startingBranch": "main" 
          }
      },
      "title": f"Integration Test Failure: {title_suffix}"[:100]
  }

  if FLAGS.dryrun:
    logging.info("Dry run: Would create session POST to %s with data: %s", url, json.dumps(data, indent=2))
    return "DRY_RUN_SESSION_ID"

  response = requests.post(url, headers=headers, json=data)
  if response.status_code != 200:
    logging.error("Failed to create session: %s %s", response.status_code, response.text)
    return None
  
  session_data = response.json()
  session_name = session_data.get('name')
  logging.info("Created Jules session: %s", session_name)
  return session_name

def send_message(jules_token, session_id, message):
  if not session_id: return
  
  # session_id usually comes as "sessions/12345", URL expects "sessions/12345:sendMessage"
  url = f"{JULES_API_URL}/{session_id}:sendMessage"
  headers = {
      "Content-Type": "application/json",
      "x-goog-api-key": jules_token
  }
  data = {"prompt": message}

  if FLAGS.dryrun:
    logging.info("Dry run: --------------------------------------------------------")
    logging.info("Dry run: Would send message to %s with length %d", url, len(message))
    #logging.info("Dry run: Would send message to %s with data: %s", url, json.dumps(data, indent=2))
    return

  backoff = 2
  for i in range(5):
    try:
      response = requests.post(url, headers=headers, json=data)
      if response.status_code == 200:
        logging.info("Sent message successfully")
        return
      
      logging.warning("Failed to send message (attempt %d/5): %s %s. Retrying in %ds...", i+1, response.status_code, response.text, backoff)
    except requests.RequestException as e:
      logging.warning("Request failed (attempt %d/5): %s. Retrying in %ds...", i+1, e, backoff)

    if i < 4:
      time.sleep(backoff)
      backoff *= 2
    
  logging.error("Failed to send message after 5 attempts.")


def main(argv):
  if len(argv) > 1:
    raise app.UsageError("Too many command-line arguments.")

  workflow_jobs = firebase_github.list_jobs_for_workflow_run(
      FLAGS.token, FLAGS.run_id, attempt='latest')
  
  target_jobs = []
  for job in workflow_jobs:
    if job['status'] != 'completed': continue
    
    is_failed = (job['conclusion'] == 'failure')
    is_included_by_pattern = (FLAGS.include_job_pattern and re.search(FLAGS.include_job_pattern, job['name']))
    
    if FLAGS.only_failed and not is_failed and not is_included_by_pattern:
      continue
      
    target_jobs.append(job)

  if not target_jobs:
    logging.info("No jobs found for run %s (only_failed=%s, include_job_pattern=%s)", 
                 FLAGS.run_id, FLAGS.only_failed, FLAGS.include_job_pattern)
    return


  job_names = [job['name'] for job in target_jobs]
  title_suffix = ", ".join(job_names)
  
  # 1. Create Session
  session_id = create_session(FLAGS.jules_token, title_suffix)
  if not session_id and not FLAGS.dryrun:
    return

  # 2. Send Logs
  for job in target_jobs:
    logging.info("Downloading logs for failed job: %s", job['name'])
    log_content = firebase_github.download_job_logs(FLAGS.token, job['id'])
    
    if FLAGS.include_step_pattern:
      log_content = filter_log_by_step_pattern(log_content, FLAGS.include_step_pattern)
    
    # Truncate if too long (keep head and tail)
    if len(log_content) > MAX_INDIVIDUAL_LOG_LENGTH:
      keep_head = int(MAX_INDIVIDUAL_LOG_LENGTH * 0.2)
      keep_tail = int(MAX_INDIVIDUAL_LOG_LENGTH * 0.8)
      truncated_log = log_content[:keep_head] + "\n...[middle truncated]...\n" + log_content[-keep_tail:]
    else:
      truncated_log = log_content
      
    message = f"Logs for Test: {job['name']}\n{'-'*40}\n{truncated_log}\n"
    
    send_message(FLAGS.jules_token, session_id, message)

  # 3. Final Report
  send_message(FLAGS.jules_token, session_id, "GENERATE REPORT")

if __name__ == "__main__":
  flags.mark_flag_as_required("token")
  flags.mark_flag_as_required("jules_token")
  flags.mark_flag_as_required("run_id")
  app.run(main)
