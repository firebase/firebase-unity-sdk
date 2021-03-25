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
Download Certificate Revocation List (CRL) for each cert in the specified file
if it's available.

Usage: $(basename "${0}") -i input -o output

-i: input
  File containing a list of certs in PEM format.
-o: output
  Output file to write a list of CRLs in PEM format.
-h:
  Print this message.
" >&2
  exit 1
}

main() {
  local certs_file=
  local output_crl_file=

  while getopts ":i:o:h" option; do
    case "${option}" in
      i) certs_file="${OPTARG}";;
      o) output_crl_file="${OPTARG}";;
      h) help;;
      \?) echo "Invalid Option: -${OPTARG}" >&2; exit 1;;
      :) echo "Option -${OPTARG} requires an argument." >&2; exit 1;;
      *) echo "Unimplemented Option: -${OPTARG}" >&2; exit 1;;
    esac
  done
  if [[ -z "${certs_file}" || -z "${output_crl_file}" ]]; then
    echo "Missing input or output file." >&2
    exit 1
  fi
  certs_file="$(readlink -f "${certs_file}")"
  if [[ ! -e "${certs_file}" ]]; then
    echo "${certs_file} not found.">&2
    exit 1
  fi

  local cert_dir="$(mktemp -d)"
  # shellcheck disable=SC2064
  trap "rm -rf '${cert_dir}'" SIGKILL SIGTERM SIGQUIT EXIT
  pushd "${cert_dir}" >/dev/null
  # Parse the certificate data from a file containing multiple certs.
  awk '
    BEGIN {
      cert_filename = "";
      cert_index = 0;
      print_cert = 0;
    }

    /-----BEGIN CERTIFICATE-----/ {
       cert_filename = "cert_" cert_index ".pem";
       print_cert = 1;
    }

    {
      if (print_cert) {
        print $0 > cert_filename;
      }
    }

    /-----END CERTIFICATE-----/ {
      print_cert = 0;
      cert_index ++;
    }' "${certs_file}"

  # For each cert, get the CRL distribution points and
  # download the CRL file.
  for cert in *.pem; do
    local cert_info=
    local distribution_point_uris=
    # Parse distribution point URIs from each cert.
    # shellcheck disable=SC1004
    eval "$(
      openssl x509 -noout -text -in "${cert}" | \
      awk '
        BEGIN {
          in_distribution_points = 0;
          in_distribution_point_name = 0;
          cert_info = "";
          num_distribution_point_uris = 0;
          cert_info_headers[0] = "Issuer: ";
          cert_info_headers[1] = "Subject: ";
        }

        {
          for (i in cert_info_headers) {
            header=cert_info_headers[i]
            re=".* " header
            if ($0 ~ re) {
              cert_info = cert_info header gensub(re, "", "", $0) "\n";
            }
          }

          if (in_distribution_points) {
            if (in_distribution_point_name) {
               if ($0 ~ /URI:/) {
                 distribution_point_uris[num_distribution_point_uris] = \
                   gensub(/.*URI:/, "", "", $0);
                 num_distribution_point_uris ++;
               }
               in_distribution_point_name = 0;
            } else if ($0 ~ /^$/) {
              # Empty line between fields.
            } else if ($0 ~ /Full Name:/) {
              in_distribution_point_name = 1;
            } else {
              in_distribution_points = 0;
            }
          }
        }

        /X509v3 CRL Distribution Points:/ {
          in_distribution_points = 1;
        }

        END {
          if (num_distribution_point_uris) {
             print "local cert_info='\''" cert_info "'\'';";
             printf "local distribution_point_uris=(";
             for (i = 0; i < num_distribution_point_uris; i++) {
               printf "'\''" distribution_point_uris[i] "'\'' ";
             }
             printf ");\n";
          }
        }'
      )"

    # If the cert has CRLs, download them.
    if [[ -n "${cert_info}" ]]; then
      local crl_index=0
      local downloaded_crl=0
      for uri in "${distribution_point_uris[@]}"; do
        # Download the CRL and convert to PEM format.
        local crl_basename="${cert}_crl_${crl_index}"
        local crl_der_filename="${crl_basename}.der"
        local crl_pem_filename="${crl_basename}.pem"
        wget -q -O "${crl_der_filename}" "${uri}" || continue
        openssl crl -inform DER -in "${crl_der_filename}" \
          -outform PEM -out "${crl_pem_filename}"
        # Add cert information in comments at the start of the CRL.
        echo -e "${cert_info}\nX509v3 CRL Distribution Point: ${uri}" | \
          sed 's/^/# /' > "${crl_pem_filename}.new"
        cat "${crl_pem_filename}" >> "${crl_pem_filename}.new"
        mv "${crl_pem_filename}.new" "${crl_pem_filename}"
        crl_index=$((crl_index+1))
        downloaded_crl=1
        break
      done
      if [[ $((downloaded_crl)) -eq 0 ]]; then
        echo "\
ERROR: Unable to download from any distribution points of cert.

${cert_info}

$(for uri in "${distribution_point_uris[@]}"; do echo "${uri}"; done)
" >&2
        exit 1
      fi
    fi
  done
  popd >/dev/null
  # Finally, generate a file of all concatenated CRL PEMs.
  cat "${cert_dir}/"*_crl_*.pem >> "${output_crl_file}"
}

main "$@"
