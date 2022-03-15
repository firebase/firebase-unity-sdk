#!/bin/bash
#
# Copyright 2022 Google LLC
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

# Downloads a build from GitHub Actions and places it in a given output
# directory Verifies that the hash matches what is expected.

set -e

CURL_CMD=curl
JQ_CMD=jq

# Use this job for packaging the SDK.
PACKAGING_JOB_NAME="Package SDKs"
# Package the SDK zip from this artifact file.
ARTIFACT_SDK=firebase_unity_sdk.zip
# Check the hash from this artifact file.
ARTIFACT_HASH=firebase_unity_sdk_hash.txt

GITHUB_PROJECT="firebase/firebase-unity-sdk"
SDK_NAME="Firebase Unity SDK"
ZIPFILE_PREFIX="firebase_unity_sdk"

if [[ -z $(which curl) ]]; then
  echo "'curl' command not installed, please run: sudo apt-get install curl"
  exit 2
fi
if [[ -z $(which jq) ]]; then
  echo "'jq' command not installed, please run: sudo apt-get install jq"
  exit 2
fi

usage(){
  echo "
Usage: $0 -r <github run ID> -u <github username> -t <github token> \
-s <sha256 hash> -v <SDK version number> -o <output directory>

This script publishes a ${SDK_NAME} release from GitHub Actions to Lorry,
using the GitHub project https://github.com/${GITHUB_PROJECT}.

Required options:
  -r   Run ID from Github Actions to fetch SDK from
  -u   GitHub username
  -t   GitHub access token, go to https://github.com/settings/tokens to create
  -s   SHA256 hash for the SDK, viewable in the '${PACKAGING_JOB_NAME}' log
  -v   ${SDK_NAME} version to publish, e.g. '7.0.2'
  -o   Where to place the downloaded SDK

Other options:
  -d   Debug mode, log more verbosely
  -f   Force mode, proceed even if the GitHub job is in progress or failed
  -c   Only check that run passed, don't download anything.
"
}

run_id=
github_username=
github_token=
hash_to_verify=
version=
debug_mode=0
force_mode=0
checkonly_mode=0

while getopts "r:u:t:s:v:o:dfch" opt; do
  case $opt in
    r) run_id=$OPTARG ;;
    u) github_username=$OPTARG ;;
    t) github_token=$OPTARG ;;
    s) hash_to_verify=$OPTARG ;;
    v) version=$OPTARG ;;
    o) output="$(realpath "$OPTARG")" ;;
    d) debug_mode=1 ;;
    f) force_mode=1 ;;
    c) checkonly_mode=1 ;;
    h) usage
       exit 0
       ;;
    *) usage
       echo "Error: Invalid argument."
       exit 1
       ;;
  esac
done

if [[ -z "${run_id}" || -z "${github_username}" || -z "${github_token}" ||
        (
          ${checkonly_mode} -eq 0 &&
            (-z "${hash_to_verify}" || -z "${version}" || -z "${output}")
        ) ]]; then
  usage
  echo "Error: Missing argument, all of -r, -u, -t are required."
  echo "       Either -c, or all of -s, -v and -o are required."
  exit 1
fi

curl_opts=(-L -u "${github_username}:${github_token}")
github_run_url="https://github.com/${GITHUB_PROJECT}/actions/runs/${run_id}"
github_api_root="https://api.github.com/repos/${GITHUB_PROJECT}"
run_info_url="${github_api_root}/actions/runs/${run_id}"
run_jobs_url="${github_api_root}/actions/runs/${run_id}/jobs?per_page=100"
list_artifacts_url="${github_api_root}/actions/runs/${run_id}/artifacts"

echo "Fetching workflow info..."
# Fetch general info for the run.
run_info_json="$("${CURL_CMD}" ${curl_opts[*]} -s "${run_info_url}")"
# Use JQ to extract the job name, which will be null if the job was not found.
job_name=$("${JQ_CMD}" -e -r ".name" <<< "${run_info_json}" || true)
if [[ -z "${job_name}" || "${job_name}" == "null" ]]; then
  echo "Error: \
No run ID #${run_id} found in https://github.com/${GITHUB_PROJECT}"
  exit 95
fi
# Use JQ to extract job status (in_progress|completed), and
# job conclusion (success|failed|cancelled)
job_status=$("${JQ_CMD}" -r ".status" <<< "${run_info_json}")
job_conclusion=$("${JQ_CMD}" -r ".conclusion" <<< "${run_info_json}")

# Fetch list of jobs for this run.
run_jobs_json="$("${CURL_CMD}" ${curl_opts[*]} -s "${run_jobs_url}")"
# Use JQ to extract the names of all jobs whose conclusion == "failure"
failed_jobs=$("${JQ_CMD}" -r ".jobs[] |
               select(.conclusion == \"failure\") | .name" \
               <<< "${run_jobs_json}")

# Fetch the list of artifacts for this run.
artifact_list_json="$("${CURL_CMD}" ${curl_opts[*]} -s "${list_artifacts_url}")"

# Ensure that only the packaging job is used.
if [[ "${job_name}" != "${PACKAGING_JOB_NAME}" && ${checkonly_mode} -ne 1 ]]; then
  echo "Error, invalid workflow name '${job_name}'."
  echo "Only artifacts from '${PACKAGING_JOB_NAME}' workflows may be published."
  exit 96
fi

# If the job was unsuccessful or is still in progress, prompt the user whether
# to continue.
if [[ "${job_conclusion}" != "success" ]]; then
  if [[ "${job_status}" == "in_progress" ]]; then
    echo -n "Warning: Run #${run_id} is still in progress."
  else
    if [[ -n "${failed_jobs}" ]]; then
      echo ""
      echo "Failed jobs in this run:"
      echo "${failed_jobs}"
    fi
    echo ""
    echo -n "Warning: \
Run #${run_id} was unsuccessful (status: ${job_conclusion})."
  fi
  if [[ ${force_mode} -ne 1 ]]; then
    echo -n " Proceed with publish (y/n)? "
    read should_proceed
    if [[ "${should_proceed}" != "y"* && "${should_proceed}" != "Y"* ]]; then
      echo "Aborting."
      echo ""
      exit 1
    fi
    echo "Proceeding."
  else
    echo " Proceeding anyway, as -f was specified."
  fi
  echo ""
fi

if [[ ${checkonly_mode} -eq 1 ]]; then
  exit 0
fi

if [[ "${artifact_list_json}" != *"${ARTIFACT_SDK}"* ||
        "${artifact_list_json}" != *"${ARTIFACT_HASH}"* ]]; then
  echo "No artifacts found for this run."
  echo ""
  if [[ ${debug_mode} -eq 1 ]]; then
    echo "REST response for ${list_artifacts_url}"
    echo "${artifact_list_json}"
  fi
  exit 3
fi

# Use JQ to extract the archive_download_url from the matching JSON entries.
hash_url=$("${JQ_CMD}" -r "[.artifacts][][] |
            select(.name == \"${ARTIFACT_HASH}\").archive_download_url" \
            <<< "${artifact_list_json}")
sdk_url=$("${JQ_CMD}" -r "[.artifacts][][] |
           select(.name == \"${ARTIFACT_SDK}\").archive_download_url" \
           <<< "${artifact_list_json}")

# Create a temporary directory to work in.
tempdir=$(mktemp -d)
# On exit (or abort), delete the temp dir and all its contents.
trap "rm -rf \"\${tempdir}\"" SIGKILL SIGTERM SIGQUIT SIGINT EXIT

echo "Downloading SDK hash and zip to ${tempdir}"
output_hash="${tempdir}/${ARTIFACT_HASH}.zip"
output_sdk="${tempdir}/${ARTIFACT_SDK}.zip"

"${CURL_CMD}" ${curl_opts[*]} -s "${hash_url}" -o "${output_hash}"
# Check if the file downloaded, or if we got an error.
if [[ $(file "${output_hash}") != *'Zip archive'* ]]; then
  if [[ $(cat "${output_hash}") == *'must have the actions scope'* ]]; then
    echo "Error: Invalid GitHub credentials."
    echo "Check your username and access token and try again."
    echo ""
    echo "You can create a token here: https://github.com/settings/tokens"
    exit 97
  else
    echo "Failed to download SDK hash from ${hash_url}"
    exit 93
  fi
fi

"${CURL_CMD}" ${curl_opts[*]} "${sdk_url}" -o "${output_sdk}"
if [[ $(file "${output_sdk}") != *'Zip archive'* ]]; then
  echo "Failed to download SDK zip from ${sdk_url}"
  exit 92
fi
echo "Download complete."

pushd "${tempdir}" > /dev/null
unzip -q "${ARTIFACT_SDK}.zip" && rm -f "${ARTIFACT_SDK}.zip"
unzip -q "${ARTIFACT_HASH}.zip" && rm -f "${ARTIFACT_HASH}.zip"

ls -l "${ARTIFACT_SDK}" "${ARTIFACT_HASH}"

if [[ ! -r "${ARTIFACT_HASH}" || ! -r "${ARTIFACT_SDK}" ]]; then
  echo "Error: Bad filenames inside artifact."
  echo ""
  echo "Expected:"
  echo "${ARTIFACT_HASH}"
  echo "${ARTIFACT_SDK}"
  echo ""
  echo "Actual:"
  ls
  exit 3
fi

echo ""
echo "Verifying hash..."
if ! shasum --status --check "${ARTIFACT_HASH}"; then
  echo "Hash in ${ARTIFACT_HASH} failed to verify."
  exit 98
fi
# Create a SHA256 verification file with the cmdline-given hash.
echo "SHA256 (${ARTIFACT_SDK}) = ${hash_to_verify}" > "cmdline_${ARTIFACT_HASH}"
if ! shasum --status --check "cmdline_${ARTIFACT_HASH}"; then
  echo "Hash specified on command line failed to verify."
  exit 99
fi
echo "Hash OK."

mv -v "${ARTIFACT_SDK}" "${output}"
