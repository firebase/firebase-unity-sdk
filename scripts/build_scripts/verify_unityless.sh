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
# Builds (or reuses) the Linux Unity-less SDK inside an isolated sandbox where
# ~/Unity is masked out, inspects every built DLL and native .so for zero Unity
# references, and executes the unified MonoTestApp suite across all products.
#
# Usage:
#   ./scripts/build_scripts/verify_unityless.sh [--build] [--cpp-dir=/path/to/firebase-cpp-sdk]

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
DO_BUILD=0
CPP_SDK_DIR="${FIREBASE_CPP_SDK_DIR:-}"
APIS="analytics,app_check,auth,crashlytics,database,firebaseai,firestore,functions,installations,messaging,remote_config,storage"

for arg in "$@"; do
  case "$arg" in
    --build) DO_BUILD=1 ;;
    --cpp-dir=*) CPP_SDK_DIR="${arg#*=}" ;;
    *) echo "Unknown argument: $arg" >&2; exit 2 ;;
  esac
done

if [[ -n "$CPP_SDK_DIR" ]]; then
  if [[ ! -d "$CPP_SDK_DIR" ]]; then
    echo "ERROR: C++ SDK directory not found: $CPP_SDK_DIR" >&2
    exit 1
  fi
  CPP_SDK_DIR="$(cd "$CPP_SDK_DIR" && pwd)"
fi

# Resolve a real (non-Unity-symlinked) .NET SDK directory so we can mask ~/Unity
# completely under bwrap --tmpfs "$HOME/Unity".
DOTNET_BIN="$(command -v dotnet || true)"
if [[ -z "$DOTNET_BIN" ]]; then
  echo "ERROR: 'dotnet' not found on PATH." >&2
  exit 1
fi
DOTNET_REAL_DIR="$(dirname "$(readlink -f "$DOTNET_BIN")")"

RUN_IN_SANDBOX() {
  if command -v bwrap >/dev/null 2>&1 && [[ -d "$HOME/Unity" ]]; then
    bwrap \
      --dev-bind / / \
      --ro-bind "$DOTNET_REAL_DIR" /tmp/dotnet-sdk \
      --tmpfs "$HOME/Unity" \
      --setenv DOTNET_ROOT /tmp/dotnet-sdk \
      --setenv DOTNET_CLI_TELEMETRY_OPTOUT 1 \
      --setenv DOTNET_SKIP_FIRST_TIME_EXPERIENCE 1 \
      --setenv UNITY_ROOT_DIR /nonexistent \
      --setenv PATH "/tmp/dotnet-sdk:$PATH" \
      "$@"
  else
    UNITY_ROOT_DIR=/nonexistent "$@"
  fi
}

cd "$REPO_ROOT"

if [[ "$DO_BUILD" -eq 1 ]] || ! compgen -G "linux_unityless_build/firebase_unity-*-Linux.zip" >/dev/null; then
  echo "==> [1/4] Building Linux SDK with --unityless (~/Unity masked out)..."
  rm -f linux_unityless_build/firebase_unity-*-Linux.zip
  SWIG_FLAG=()
  if command -v swig >/dev/null 2>&1; then
    SWIG_FLAG=("--swig_dir=$(swig -swiglib)")
  fi
  CMAKE_EXTRA_FLAG=()
  if [[ -n "$CPP_SDK_DIR" ]]; then
    CMAKE_EXTRA_FLAG=("--cmake_extras=-DFIREBASE_CPP_SDK_DIR=$CPP_SDK_DIR")
  fi
  MAKEFLAGS="-j$(getconf _NPROCESSORS_ONLN)" RUN_IN_SANDBOX \
    python3 scripts/build_scripts/build_zips.py \
      --platform=linux \
      --unityless \
      --apis="$APIS" \
      "${SWIG_FLAG[@]}" \
      "${CMAKE_EXTRA_FLAG[@]}"
else
  echo "==> [1/4] Using existing linux_unityless_build/firebase_unity-*-Linux.zip (pass --build to rebuild)"
fi

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

echo "==> [2/4] Inspecting PE AssemblyRef metadata across all built DLLs..."
EXPECTED_DLLS=(
  Firebase.Analytics.dll
  Firebase.App.dll
  Firebase.App.Internal.dll
  Firebase.AppCheck.dll
  Firebase.Auth.dll
  Firebase.Crashlytics.dll
  Firebase.Database.dll
  Firebase.FirebaseAI.dll
  Firebase.Firestore.dll
  Firebase.Functions.dll
  Firebase.Installations.dll
  Firebase.Messaging.dll
  Firebase.Platform.dll
  Firebase.RemoteConfig.dll
  Firebase.Storage.dll
  Firebase.TaskExtension.dll
  Google.MiniJson.dll
)
for expected_dll in "${EXPECTED_DLLS[@]}"; do
  if [[ ! -f "$WORK_DIR/unpacked/Firebase/Plugins/$expected_dll" ]]; then
    echo "FAIL: Missing expected DLL in zip: $expected_dll (pass --build to rebuild all APIs)" >&2
    exit 1
  fi
done
if command -v monodis >/dev/null 2>&1; then
  for dll in "$WORK_DIR"/unpacked/Firebase/Plugins/*.dll; do
    refs="$(monodis --assemblyref "$dll" | awk -F= '/Name=/{print $2}' | paste -sd, -)"
    printf "  %-32s -> [%s]\n" "$(basename "$dll")" "$refs"
    if grep -qi "unity" <<<"$refs"; then
      echo "FAIL: Unity assembly reference found in $dll" >&2
      exit 1
    fi
  done
else
  echo "  monodis not found; skipping (the test suite checks the same references)."
fi

echo "==> [3/4] Checking native shared library symbols (ldd -r & nm -D)..."
shopt -s nullglob
APP_SOS=("$WORK_DIR"/unpacked/Firebase/Plugins/x86_64/FirebaseCppApp-*.so)
ALL_SOS=("$WORK_DIR"/unpacked/Firebase/Plugins/x86_64/*.so)
shopt -u nullglob
if [[ "${#APP_SOS[@]}" -ne 1 ]] || [[ "${#ALL_SOS[@]}" -eq 0 ]]; then
  echo "FAIL: Expected 1 FirebaseCppApp-*.so and >=1 .so in Plugins/x86_64 (found ${#APP_SOS[@]} and ${#ALL_SOS[@]})" >&2
  exit 1
fi
UNI_SO="${APP_SOS[0]}"
ldd_out="$(ldd -r "$UNI_SO" 2>&1 || true)"
if grep -q "undefined symbol" <<<"$ldd_out"; then
  echo "FAIL: Undefined symbols in $UNI_SO" >&2
  printf '%s\n' "$ldd_out" >&2
  exit 1
fi
for so in "${ALL_SOS[@]}"; do
  syms="$(nm -D "$so")"
  if grep -qi "unity" <<<"$syms"; then
    echo "FAIL: Unexpected Unity symbol in $so" >&2
    grep -i "unity" <<<"$syms" >&2
    exit 1
  fi
done
echo "  PASS: $(basename "$UNI_SO") links cleanly and ${#ALL_SOS[@]} native .so file(s) have 0 Unity symbols."

echo "==> [4/4] Running unified MonoTestApp product suites against unpacked zip (~/Unity masked)..."
CACHED_DOTNET="$(awk -F= '/^DOTNET_EXE:FILEPATH=/{print $2}' "$REPO_ROOT/linux_unityless_build/CMakeCache.txt" 2>/dev/null || true)"
if [[ -n "$CACHED_DOTNET" ]] && ! RUN_IN_SANDBOX test -e "$CACHED_DOTNET"; then
  RUN_IN_SANDBOX cmake "$REPO_ROOT/linux_unityless_build" >/dev/null
fi
RUN_IN_SANDBOX cmake --build "$REPO_ROOT/linux_unityless_build" --target mono_testapp_cs -j "$(getconf _NPROCESSORS_ONLN)"
cp "$REPO_ROOT/linux_unityless_build/samples/mono_app/bin/MonoTestApp"/MonoTestApp* "$WORK_DIR/unpacked/Firebase/Plugins/"
LD_LIBRARY_PATH="$WORK_DIR/unpacked/Firebase/Plugins/x86_64" RUN_IN_SANDBOX \
  dotnet "$WORK_DIR/unpacked/Firebase/Plugins/MonoTestApp.dll" \
    --check-no-unity-refs \
    --offline \
    --native-dir="$WORK_DIR/unpacked/Firebase/Plugins/x86_64"
