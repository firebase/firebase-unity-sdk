---
name: firebase-unity-builder
description:
  Instructions and workflows for building the Firebase Unity SDK from source
  with `scripts/build_scripts/build_zips.py`. Use this skill when you need to
  compile the per-platform SDK zip (Linux, macOS, Windows, iOS, tvOS, Android),
  limit the build to specific products (e.g. auth, firestore), point the build
  at a local Firebase C++ SDK checkout, pick a Unity installation, or only
  regenerate the SWIG C# bindings.
---

# Building the Firebase Unity SDK

The Unity SDK wraps the [Firebase C++ SDK](https://github.com/firebase/firebase-cpp-sdk)
with SWIG-generated C# bindings plus hand-written C#. The build is driven by
CMake and wrapped by `scripts/build_scripts/build_zips.py`, which produces one
zip per platform. Those zips are later turned into `.unitypackage` / `.tgz`
files by the packager (see `firebase-unity-packager`).

## Prerequisites

- Everything in the [C++ SDK prerequisites](https://github.com/firebase/firebase-cpp-sdk/blob/main/README.md#prerequisites)
  (CMake, Python 3, OpenSSL unless using BoringSSL, Android SDK/NDK for Android,
  Xcode for iOS/tvOS/macOS).
- **SWIG 4.x** (`brew install swig` / `sudo apt install swig`). If CMake cannot
  find it, pass `--swig_dir=$(swig -swiglib)`.
- **Unity Editor 2022.3 LTS or newer** (the minimum supported by the SDK). The
  build needs Unity's managed assemblies (`UnityEngine.dll`, etc.); CMake
  guesses the install location, or set `UNITY_ROOT_DIR` / pass `--unity_root`.
- Python deps: `pip install -r scripts/gha/requirements.txt` (absl-py, attrs,
  pyyaml, packaging, ...). CI uses Python 3.9.
- **Mono** on macOS/Linux for the C# compile.

## Which Firebase C++ SDK source is used

The Unity build compiles the C++ SDK from source. `build_zips.py` locates it,
in order:

1. A sibling checkout at `../firebase-cpp-sdk` (relative to the Unity repo
   root, i.e. the directory you run the script from). This is the recommended
   layout and is what CI does.
2. The `FIREBASE_CPP_SDK_DIR` environment variable (read by `CMakeLists.txt`).
3. Otherwise CMake downloads the tag named by `FIREBASE_CPP_SDK_PRESET_VERSION`
   in `cmake/firebase_unity_version.cmake` into `<platform>_unity/external/src/`.

> [!WARNING]
> If a sibling `../firebase-cpp-sdk` exists, `build_zips.py` passes it as
> `-DFIREBASE_CPP_SDK_DIR=...` explicitly, which **overrides the environment
> variable**. On a machine where the sibling clone is checked out on some
> other branch (e.g. a release branch), pin the C++ source instead with
> `--cmake_extras=-DFIREBASE_CPP_SDK_DIR=/path/to/firebase-cpp-sdk-worktree`;
> `--cmake_extras` is appended after the sibling argument, so the last `-D`
> wins. Verify in the first log line (`cmake_setup_args is: ...`).

To test paired C++/Unity changes, point it at your C++ worktree this way.
CI's default is the C++ `main` branch head (`firebase_cpp_sdk_version` input
empty); the pinned tag is only the fallback when nothing local is found.

> [!NOTE]
> C++ `main` can be ahead of the Unity SWIG layer. A new C++ public type that
> SWIG wraps automatically but the `.i` files don't `%ignore`/`%rename`
> surfaces as a C# compile error in `<product>/swig/Firebase.<Product>_fixed.cs`
> (e.g. `error CS0053: Inconsistent accessibility ... IdTokenResult.claims`
> after firebase-cpp-sdk #1938). When that happens and you are not working on
> that binding, build against the tag in `FIREBASE_CPP_SDK_PRESET_VERSION`
> (`git -C <cpp-worktree> checkout v14.1.0`) rather than C++ `main`.

Other pinned versions live in the same file: `FIREBASE_UNITY_SDK_VERSION`,
`FIREBASE_SPM_VERSION` (Firebase Apple SDK, consumed as Swift Packages),
`FIREBASE_UNITY_JAR_RESOLVER_VERSION` (EDM4U). Android dependency versions are
in `cmake/android_dependencies.cmake`.

## 1. Build a platform zip

Run from the repo root:

```bash
python scripts/build_scripts/build_zips.py --platform=<linux|macos|windows|ios|tvos|android>
```

> [!TIP]
> On Linux/macOS the script runs plain `make` with no `-j`. Export
> `MAKEFLAGS=-j$(nproc)` first, otherwise a full build is single-threaded
> (the whole SDK, Firestore/gRPC included, takes ~10 min at `-j20` on a
> 24-core machine versus hours serial).

Known-benign noise: early in the log you will see
`error: patch failed: CMakeLists.txt:197 ... leveldb-patch] Error 1` from the
C++ SDK's external-download sub-make. Its `cmake/external/leveldb.cmake`
only sets `patch_file` on MSVC, so on other hosts the variable leaks from a
previously included external (`flatbuffers.cmake`) and the wrong patch is
applied. The main build continues; only errors after
`Building external project dependencies...` matter.

### Linux toolchain gotchas (Debian/Ubuntu with GCC 15, OpenSSL 3.x)

- **`cannot find -lzstd` when linking `FirebaseCppApp-<ver>.so`**: system
  OpenSSL 3.x's `pkg-config --static --libs openssl` includes `-lzstd`, but
  only the `libzstd1` runtime is installed. Fix: `sudo apt install
  libzstd-dev`, or without root
  `mkdir -p ~/tmp/zstd-shim && ln -s /usr/lib/x86_64-linux-gnu/libzstd.so.1
  ~/tmp/zstd-shim/libzstd.so && export LIBRARY_PATH=~/tmp/zstd-shim`.
- **`--use_boringssl` fails with `-Werror=discarded-qualifiers` in
  `crypto/fipsmodule/bcm.c`**: the pinned BoringSSL (`fips-20220613`) does not
  compile with GCC 15. CI's ubuntu-22.04 toolchain is fine; locally use
  system OpenSSL instead.

Output: `<platform>_unity/firebase_unity-<version>-<Platform>.zip`, e.g.
`macos_unity/firebase_unity-14.1.0-Darwin.zip`,
`linux_unity/firebase_unity-14.1.0-Linux.zip`,
`windows_unity/firebase_unity-14.1.0-win64.zip`,
`ios_unity/firebase_unity-14.1.0-iOS.zip`,
`android_unity/firebase_unity-14.1.0-Android.zip`.

Host requirements: Linux zip needs a Linux machine, Windows zip needs Windows,
macOS/iOS/tvOS need a Mac. Android can be built anywhere but CI builds it on
macOS. Only the **macOS** zip contains the Editor tooling
(`Firebase/Editor/Firebase.Editor.dll`, `Editor Default Resources/`), so a
package built from a Linux/Windows zip alone imports fine but cannot run the
scripted testapps (see `firebase-unity-test-runner`).

## 2. Build only some products (fastest iteration)

```bash
python scripts/build_scripts/build_zips.py --platform=macos --apis=auth,storage
# equivalent:
python scripts/build_scripts/build_zips.py --platform=macos --targets=auth --targets=storage
```

Valid product names: `analytics, app_check, auth, crashlytics, database,
firebaseai, firestore, functions, installations, messaging, remote_config,
storage`. `app` is always built. `--apis` and `--targets` are mutually
exclusive.

## 3. Useful flags

| Flag | Purpose |
|---|---|
| `--unity_root=/Applications/Unity/Hub/Editor/2022.3.x` | Pick a Unity install (or set `UNITY_ROOT_DIR`). With several versions installed the newest is used by default. |
| `--device=device` / `--device=simulator` | iOS/tvOS/Android: build only that flavor (default: both). |
| `--architecture=<arch>` (repeatable) | iOS device `arm64`; iOS simulator `arm64`/`x86_64`; Android `armeabi-v7a,arm64-v8a,x86,x86_64`; macOS `x86_64,arm64`. |
| `--cmake_extras=-DCMAKE_BUILD_TYPE=Debug` (repeatable) | Extra CMake args, e.g. debug symbols for attaching a debugger to the Editor. |
| `--use_boringssl` | Build with BoringSSL instead of system OpenSSL (CI Linux uses this). |
| `--clean_build` | Delete `<platform>_unity/` first. **Required whenever you switch the C++ SDK revision**: the downloaded externals (curl, zlib, ...) under `<platform>_unity/bin/external/` are pinned per C++ revision and a stale cache fails at configure time (e.g. `Could NOT find Libpsl` from a newer curl). |
| `--verbose` | `CMAKE_VERBOSE_MAKEFILE=1`. |
| `--gen_documentation_zip` | Also emit `documentation_sources.zip` (used by the docs pipeline). |
| `--gen_swig_only` | Configure and run only the `firebase_swig_targets` target; no library compile. |

Run `python scripts/build_scripts/build_zips.py --help` for the full list.

## 4. Inspecting SWIG output

The SWIG interface files are `<product>/src/swig/*.i` (plus shared helpers in
`app/src/swig/`). To see the generated C#/C++ without a full build:

```bash
python scripts/build_scripts/build_zips.py --platform=linux --apis=auth --gen_swig_only
```

On GitHub the same thing is the **Generate SWIG files** workflow
(`.github/workflows/generate_swig.yml`), run on your branch; narrow `apis` to
the product you care about to keep it fast. C++ Doxygen comments are converted
to C# XML docs by `swig_commenter.py` (see `cmake/firebase_swig.cmake`), so
`<SWIG>`/`@if swig_examples` blocks in C++ headers matter for Unity docs.

## 5. CI equivalents

`.github/workflows/build_starter.yml` fans out to `build_<platform>.yml`, each
of which checks out `firebase-cpp-sdk` next to the Unity repo and runs e.g.:

```bash
python scripts/build_scripts/build_zips.py --gha --platform=linux --use_boringssl=true \
  --unity_root=$UNITY_ROOT_DIR --apis=<csv> --gen_documentation_zip
```

You can trigger **Firebase Unity SDK Build** manually from the Actions tab,
choosing `unity_version`, `firebase_cpp_sdk_version` (any C++ branch/tag/SHA,
so Unity can be tested against a pending C++ PR), `platforms`, `apis`,
`additional_cmake_flags`, and whether to package / run integration tests.

## Notes and gotchas

- iOS/tvOS dependencies come from Swift Packages (`FIREBASE_SPM_VERSION`) and
  are shipped as `.xcframework`s; CocoaPods is no longer part of the Unity
  build.
- The Windows x86_64 / macOS / Linux desktop libraries are a single universal
  `FirebaseCppApp-<ver>.dll/.bundle/.so` plus per-product libraries (see
  `cmake/build_universal.cmake`, `cmake/build_shared.cmake`).
- When you rebuild C++ libraries and re-import into an open Unity project,
  restart the Editor; native plugins are not reloaded in-place.
- Running the packager afterwards may touch `unity_packer/guids.json`; read
  `firebase-unity-guid-policy` before committing anything.
