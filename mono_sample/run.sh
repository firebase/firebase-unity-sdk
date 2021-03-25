#!/bin/bash -eux
#
# Copyright 2018 Google LLC
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

# Unzip a Mono SDK distribution, compile the application in the current
# directory against it and run the application.
main() {
  if [[ $# -ne 1 ]]; then
    echo "Usage: $(basename "$0") sdk_zip_file" >&2
    exit 1
  fi
  local -r executable=out.exe
  local -r mono_sdk_zip_file="${1}"
  local -r tmp_dir=$(mktemp -d)
  local -r this_dir=$(dirname "$0")
  # shellcheck disable=SC2064
  trap "rm -rf \"${tmp_dir}\"" TERM EXIT
  # Unpack the zip file.
  unzip -q -d "${tmp_dir}" "${mono_sdk_zip_file}"
  # Compile source files in the current directory referencing managed DLLs in
  # the Mono SDK distribution.
  local -r input_files=("${this_dir}"/*.cs)
  local -r managed_dlls=("${tmp_dir}"/{Firebase.*.dll,Unity.*.dll})
  local managed_dll_reference_args=(
    $(echo "${managed_dlls[@]}" | tr ' ' '\n' | xargs -I@ echo -reference:@))
  mcs "${input_files[@]}" "${managed_dll_reference_args[@]}" \
    -out:"${executable}"
    cp --no-preserve=all "${this_dir}/google-services.json" .
  # Run the application.
  LD_LIBRARY_PATH="${tmp_dir}" MONO_PATH="${tmp_dir}" mono "${executable}"
}

main "${@}"
