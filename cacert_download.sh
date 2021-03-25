#!/bin/bash -eu
#
# Copyright 2016 Google LLC
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

help() {
  echo "\
Download root and intermediate certs from a set of hosts.

Usage: $(basename "$0") -q hosts -r root_certs_output \
  -i intermediate_certs_output

-q: hosts
  Space separated list of hosts to query.
-r: root_certs_output
  Location of the root certs output file.
-i:intermediate_certs_output
  Location of the intermediate certs output file.
-h:
  Print this message.
" >&2
  exit 1
}

main() {
  local -a hosts=
  local port=443 # https
  local root_certs_file=
  local intermediate_certs_file=

  while getopts ":q:r:i:h" option; do
    case "${option}" in
      q) hosts=(${OPTARG});;
      r) root_certs_file="${OPTARG}";;
      i) intermediate_certs_file="${OPTARG}";;
      h) help;;
      \?) echo "Invalid Option: -${OPTARG}" >&2; exit 1;;
      :) echo "Option -${OPTARG} requires an argument." >&2; exit 1;;
      *) echo "Unimplemented Option: -${OPTARG}" >&2; exit 1;;
    esac
  done

  if [[ -z "${hosts[*]}" || \
        -z "${root_certs_file}" || \
        -z "${intermediate_certs_file}" ]]; then
    echo "Missing argument." >&2
    help
  fi

  echo > "${root_certs_file}"
  echo >"${intermediate_certs_file}"

  for host in "${hosts[@]}"; do
    openssl s_client -connect "${host}:${port}" -showcerts </dev/null 2>/dev/null | \
      awk '
        BEGIN {
          cert_chain_index = 0;
        }

        / [0-9][0-9]* s:/ {
          subject = gensub(/.* s:/, "", "", $0);
        }

        /  * i:/ {
          issuer = gensub(/.* i:/, "", "", $0);
        }

        {
          if (cert) cert = cert $0 "\n"
        }

        /-----BEGIN CERTIFICATE-----/ {
          cert = $0 "\n";
        }

        /-----END CERTIFICATE-----/ {
          cert_chain_subject[cert_chain_index] = subject;
          cert_chain_issuer[cert_chain_index] = issuer;
          cert_chain_cert[cert_chain_index] = cert;
          subject = "";
          issuer = "";
          cert = "";
          cert_chain_index++;
        }

        END {
          for (i = 0; i < cert_chain_index; i ++) {
            if (i < cert_chain_index - 1) {
              output_file = "'"${intermediate_certs_file}"'";
            } else {
              output_file = "'"${root_certs_file}"'";
            }

            print "# Subject: " cert_chain_subject[i] >> output_file;
            print "# Issuer: " cert_chain_issuer[i] >> output_file;
            print cert_chain_cert[i] > output_file;
          }
        }
        '
    if [[ $((${PIPESTATUS[0]})) -ne 0 ]]; then
      echo "Cert query of ${host} failed." >&2
      exit 1
    fi
  done
}

main "$@"
