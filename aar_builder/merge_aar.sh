#!/bin/bash -eu
#
# Copyright 2016 Google inc. All rights Reserved.

help() {
  echo "
Usage: $(basename "$0") -i aar_list -o aar -h
Merges a set of Android Archives (AARs) into a single AAR.
-i aar_list
  Space separated list of aars to merge.
  When conflicting empty / non-empty files are found in the set of AARs
  the non-empty files are copied to the output AAR.  If multiple
  non-empty files are found and conflicting the first file in the list
  is selected.
-o aar
  Where to place the output aar.
-h
  Display this help message.
" >&1
  exit 1
}

main() {
  local -a input=
  local output=
  while getopts "i:o:h" option "$@"; do
    case "${option}" in
      i ) input=(${OPTARG});;
      o ) output="${OPTARG}";;
      h ) help;;
      * ) help;;
    esac
  done
  if [[ -z "${input[*]}" || \
        -z "${output}" ]]; then
    echo "Required argument not specified." >&2
    help
  fi
  local -r aar_temp="$(mktemp -d)"
  local -r remove_aar_temp="rm -rf \"${aar_temp}\""
  # shellcheck disable=SC2064
  trap "${remove_aar_temp}" SIGKILL SIGTERM SIGQUIT EXIT

  # Copy each AAR's contents into the staging folder.
  local -r aar_staging="${aar_temp}/staging"
  local -r aar_unzipped="${aar_temp}/unzipped"
  mkdir -p "${aar_staging}"
  mkdir -p "${aar_unzipped}"
  for input_aar in "${input[@]}"; do
    # Ignore empty AARs.
    if [[ $(($(stat --printf="%s" "${input_aar}"))) -eq 0 ]]; then
      continue
    fi
    unzip -q -d "${aar_unzipped}" "${input_aar}"
    (
      cd "${aar_unzipped}"
      # shellcheck disable=SC2044
      for filename in $(find . -type f); do
        target_filename="${aar_staging}/${filename}"
        # Copy an empty file to the staging area if a file does not
        # already exist there.
        if [[ $(($(stat --printf="%s" "${filename}"))) -eq 0 ]]; then
          if [[ -e "${target_filename}" ]]; then
            continue
          fi
        fi
        chmod +w "${filename}"
        mkdir -p "$(dirname "${target_filename}")"
        cp -a "${filename}" "${target_filename}"
      done
      rm -rf *
    )
  done
  # Create the output AAR.
  if [[ -e "${output}" ]]; then
    output="$(cd "$(dirname "${output}")" &&
              echo "$(pwd)/$(basename "${output}")")"
  fi
  (
    cd "${aar_staging}"
    mkdir -p "$(dirname "${output}")"
    zip -q -X -r "${output}_output" .
    chmod +w "${output}"
    mv "${output}_output" "${output}"
  )
}

main "$@"
