#!/usr/bin/env bash
# Copyright 2026 Google LLC
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
#
# Builds the Linux Unity-less SDK and runs the offline tests of the product
# testapps with MonoTestApp against the unpacked zip. The build always runs (it
# is incremental, in linux_unityless_build/), so the zip under test is never a
# leftover from an earlier run.
#
# Usage:
#   ./scripts/build_scripts/verify_unityless.sh
#
# Set FIREBASE_CPP_SDK_DIR to build against a specific firebase-cpp-sdk checkout.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CPP_SDK_DIR="${FIREBASE_CPP_SDK_DIR:-}"
APIS="analytics,app_check,auth,crashlytics,database,firebaseai,firestore,functions,installations,messaging,remote_config,storage"

if [[ $# -gt 0 ]]; then
  echo "Unknown argument: $1" >&2
  exit 2
fi

cd "$REPO_ROOT"

echo "==> [1/2] Building Linux SDK with --unityless..."
rm -f linux_unityless_build/firebase_unity-*-Linux.zip
SWIG_FLAG=()
if command -v swig >/dev/null 2>&1; then
  SWIG_FLAG=("--swig_dir=$(swig -swiglib)")
fi
CMAKE_EXTRA_FLAG=()
if [[ -n "$CPP_SDK_DIR" ]]; then
  # Passed explicitly because build_zips.py prefers a ../firebase-cpp-sdk
  # checkout, when one exists, over the environment variable.
  CMAKE_EXTRA_FLAG=("--cmake_extras=-DFIREBASE_CPP_SDK_DIR=$CPP_SDK_DIR")
fi
MAKEFLAGS="-j$(getconf _NPROCESSORS_ONLN)" \
  python3 scripts/build_scripts/build_zips.py \
    --platform=linux \
    --unityless \
    --apis="$APIS" \
    "${SWIG_FLAG[@]}" \
    "${CMAKE_EXTRA_FLAG[@]}"

shopt -s nullglob
ZIP_FILES=(linux_unityless_build/firebase_unity-*-Linux.zip)
shopt -u nullglob
if [[ "${#ZIP_FILES[@]}" -ne 1 ]]; then
  echo "ERROR: Expected exactly 1 linux_unityless_build/firebase_unity-*-Linux.zip, found ${#ZIP_FILES[@]}." >&2
  exit 1
fi
ZIP_FILE="${ZIP_FILES[0]}"
WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
unzip -q "$ZIP_FILE" -d "$WORK_DIR/unpacked"

echo "==> [2/2] Running unified MonoTestApp product suites against unpacked zip..."
cmake --build "$REPO_ROOT/linux_unityless_build" --target mono_testapp_cs -j "$(getconf _NPROCESSORS_ONLN)"
cp "$REPO_ROOT/linux_unityless_build/samples/mono_app/bin/MonoTestApp"/MonoTestApp* "$WORK_DIR/unpacked/Firebase/Plugins/"
LD_LIBRARY_PATH="$WORK_DIR/unpacked/Firebase/Plugins/x86_64" \
  dotnet "$WORK_DIR/unpacked/Firebase/Plugins/MonoTestApp.dll" \
    --check-no-unity-refs \
    --offline
