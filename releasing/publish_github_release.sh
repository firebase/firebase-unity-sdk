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
#
# Downloads a build from GitHub Actions and stages it to Lorry.
# Verifies that the hash matches what is expected, and edits the URL redirect
# on devsite as well.

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

COLOSSUS_USER=firebase-releaser
COLOSSUS_STAGING_PATH=/cns/is-d/home/firebase-releaser/firebase_unity
COLOSSUS_TTL_HOURS=72

LORRY_URL_BASE="https://dl.google.com/firebase/sdk/unity"

# Edit this file to set the redirect to point to the new SDK.
DEVSITE_REDIRECT_FILE=third_party/devsite/firebase/en/download/_redirects.yaml
# This is the URL that is redirected to the SDK.
DEVSITE_REDIRECT_URL=/download/unity

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
-s <sha256 hash> -v <SDK version number>

This script publishes a ${SDK_NAME} release from GitHub Actions to Lorry,
using the GitHub project https://github.com/${GITHUB_PROJECT}.

Required options:
  -r   Run ID from GitHub Actions to fetch SDK from.
  -u   GitHub username
  -t   GitHub access token, go to https://github.com/settings/tokens to create
  -s   SHA256 hash for the SDK, viewable in the '${PACKAGING_JOB_NAME}' log
  -v   ${SDK_NAME} version to publish, e.g. '7.0.2'

Other options:
  -d   Debug mode, log more verbosely
  -f   Force mode, proceed even if the GitHub job is in progress or failed
  -T   Run ID from GitHub Actions to verify tests passed on (defaults to
       the run ID passed via -r, if unspecified).
"
}

run_id=
test_run_id=
github_username=
github_token=
hash_to_verify=
version=
debug_mode=0
force_mode=0

while getopts "r:u:t:T:s:v:dfh" opt; do
  case $opt in
    r) run_id=$OPTARG ;;
    u) github_username=$OPTARG ;;
    t) github_token=$OPTARG ;;
    T) test_run_id=$OPTARG ;;
    s) hash_to_verify=$OPTARG ;;
    v) version=$OPTARG ;;
    d) debug_mode=1 ;;
    f) force_mode=1 ;;
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
        -z "${hash_to_verify}" || -z "${version}" ]]; then
  usage
  echo "Error: Missing argument, all of -r, -u, -t, -s, and -v are required."
  exit 1
fi

this_script="$(readlink -f "${0}")"
google3_dir="${this_script/google3*/google3}"

if [[ ! -d "${google3_dir}" ]]; then
  echo "Error: This script must be run from a google3 client."
  exit 4
fi

if [[ -z "${test_run_id}" ]]; then
  test_run_id="${run_id}"
fi

curl_opts=(-L -u ${github_username}:${github_token})
github_run_url="https://github.com/${GITHUB_PROJECT}/actions/runs/${run_id}"
github_test_run_url="https://github.com/${GITHUB_PROJECT}/actions/runs/${test_run_id}"
github_api_root="https://api.github.com/repos/${GITHUB_PROJECT}"
run_info_url="${github_api_root}/actions/runs/${run_id}"
run_jobs_url="${github_api_root}/actions/runs/${run_id}/jobs?per_page=100"
list_artifacts_url="${github_api_root}/actions/runs/${run_id}/artifacts"
zip_filename="${ZIPFILE_PREFIX}_${version}.zip"

# Create a temporary directory to work in.
tempdir=$(mktemp -d)
# On exit (or abort), delete the temp dir and all its contents.
trap "rm -rf \"\${tempdir}\"" SIGKILL SIGTERM SIGQUIT SIGINT EXIT

zip_file_path="${tempdir}/${zip_filename}"

echo ""
echo "${SDK_NAME} publishing script"
echo "SDK version: ${version}"
echo "GitHub run URL for packaging: ${github_run_url}"
echo "GitHub run URL for tests: ${github_test_run_url}"
echo "Publish URL: ${LORRY_URL_BASE}/${zip_filename}"
echo ""

# Check that the tests succeeded first.
"${google3_dir}/third_party/firebase/unity/releasing/fetch_github_release.sh" \
   -r "${test_run_id}" \
   -u "${github_username}" \
   -t "${github_token}" -c || exit 1

# Fetch the actual run.
"${google3_dir}/third_party/firebase/unity/releasing/fetch_github_release.sh" \
   -r "${run_id}" \
   -u "${github_username}" \
   -t "${github_token}" \
   -s "${hash_to_verify}" \
   -v "${version}" \
   -o "${zip_file_path}" || exit 1

echo ""
echo "Setting up devsite redirect..."
if [[ ! -e ${google3_dir}/${DEVSITE_REDIRECT_FILE} ]]; then
  echo "${DEVSITE_REDIRECT_FILE} does not exist, unable to set redirect URL."
else
  download_redirects=${google3_dir}/${DEVSITE_REDIRECT_FILE}
  zip_file_lorry_url="${LORRY_URL_BASE}/${zip_filename}"
  g4 edit "${download_redirects}"
  awk '
  /- from: '"${DEVSITE_REDIRECT_URL//\//\\/}"'/ {
    found_redirect = 1;
  }
  /^ *to:/ {
    if (found_redirect) {
      print gensub(/:.*/, ": ", "1", $0) "'"${zip_file_lorry_url}"'";
      found_redirect = 0;
      next;
    }
  }
  { print $0; }' "${download_redirects}" > "${download_redirects}.new"
  mv "${download_redirects}.new" "${download_redirects}"
  # Show the changed file.
  grep -B 1 -F "${zip_filename}" "${download_redirects}"

  # Create a CL to add the redirect. The CL description will look like:
  #
  # Add ${SDK_NAME} <version number>
  #
  # Packaged via <github run URL>
  # SHA256 hash = <sha256 hash>
  g4 change -o -c default | \
    sed "s@\([[:space:]]*\)<ent.*here>@\1Add ${SDK_NAME} ${version}.\n\n\1Packaged via ${github_run_url}\n\1Tested via ${github_test_run_url}\n\1SHA256 hash = ${hash_to_verify}@" | \
    g4 change -i
fi

echo ""
echo "Copying SDK zip to staging location..."
fileutil -gfs_user="${COLOSSUS_USER}" mkdir -m 755 -p \
  "${COLOSSUS_STAGING_PATH}"
# Move the zip file to the staging path, ensuring it's readable.
fileutil -gfs_user="${COLOSSUS_USER}" cp -f -m 644 \
  "${zip_file_path}" \
  "${COLOSSUS_STAGING_PATH}/${zip_filename}%ttl=${COLOSSUS_TTL_HOURS}h"
if fileutil test -f "${COLOSSUS_STAGING_PATH}/${zip_filename}"; then
  # Remove the original zip file.
  rm ${zip_file_path}
  echo "
*** Release zip staged on Colossus.
***
*** Provide the following path to Lorry to load the file's data:
*** ${COLOSSUS_STAGING_PATH}/${zip_filename}
***
*** (before the file expires in ${COLOSSUS_TTL_HOURS} hours)
***
*** IMPORTANT: Your Lorry approver must verify that the file's SHA-256 hash
*** matches the one listed in this GitHub run:
*** ${github_run_url}
***
*** They can either download the staged file and run 'sha256sum' on it, or just
*** verify that the GitHub hash matches the SHA-256 hash listed on the Lorry
*** approval page.
"
fi
