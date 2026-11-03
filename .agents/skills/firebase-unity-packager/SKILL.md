---
name: firebase-unity-packager
description:
  How to turn built platform zips into distributable Firebase Unity SDK
  artifacts (`.unitypackage` and UPM `.tgz`) with
  `scripts/build_scripts/build_package.py`, how `unity_packer/exports.json`
  describes package contents, how to inspect a package with
  `unpack_package.py`, and how the release workflow packages on CI. Use when
  adding a new file/folder to a shipped package, debugging what ends up in a
  package, or reproducing the CI packaging step locally.
---

# Packaging the Firebase Unity SDK

> [!IMPORTANT]
> Packaging can rewrite `unity_packer/guids.json` in your working tree. Read
> `firebase-unity-guid-policy` first: never commit those changes in a
> feature PR.

## Inputs

Build one zip per platform with `firebase-unity-builder`, then collect them in
one directory (call it `assets_zip_dir`):

```
assets_zip_dir/
├── firebase_unity-14.1.0-Android.zip
├── firebase_unity-14.1.0-Darwin.zip
├── firebase_unity-14.1.0-Linux.zip
├── firebase_unity-14.1.0-iOS.zip
├── firebase_unity-14.1.0-tvOS.zip
└── firebase_unity-14.1.0-win64.zip
```

A subset is fine for local testing; missing platforms simply produce packages
without those plugins.

## 1. `.unitypackage` (legacy asset packages)

```bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output_dir>
```

Produces `FirebaseAnalytics.unitypackage`, `FirebaseAuth.unitypackage`, ...,
one per product, in `<output_dir>` (the directory is wiped first).

## 2. UPM `.tgz` (Unity Package Manager)

```bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output_dir> --output_upm=True
```

Produces `com.google.firebase.app-<ver>.tgz`,
`com.google.firebase.auth-<ver>.tgz`, etc.

## 3. Only some products

```bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output_dir> --apis=auth,storage
```

Valid names: `analytics, app_check, auth, crashlytics, database, firebaseai,
firestore, functions, installations, messaging, remote_config, storage`.
This path uses the per-product configs in
`unity_packer/debug_single_export_json/<product>.json`, always emits
`.unitypackage` (not tgz), and does **not** run the GUID self-heal step, so it
can fail with `MissingGuidsError` for brand-new assets. That is expected for
local checks; use the full (no `--apis`) run if you need the package to build.

> [!WARNING]
> `--apis=database` and `--apis=storage` currently produce **no package** and
> still exit 0: their debug configs `include` `FirebaseAuth.unitypackage`
> without defining it (`Error while parsing project configuration ...
> includes missing packages ['FirebaseAuth.unitypackage']` in the log). Adding
> `auth` is not enough, each product is exported from its own JSON. Use the
> full run and pick the packages you need from `<output_dir>`.
> Always check the log and the output directory; `build_package.py` ignores
> the exporter's exit status.

## How it works

`build_package.py` is a thin wrapper around
`scripts/build_scripts/export_unity_package.py`:

- Reads the SDK version from `cmake/firebase_unity_version.cmake`
  (`FIREBASE_UNITY_SDK_VERSION`) and passes it as `--plugins_version`.
- Uses `unity_packer/exports.json` as the package manifest and
  `unity_packer/guids.json` as the GUID cache.
- Runs the exporter once; if it fails with `MissingGuidsError`, calls
  `gen_guids.py` for the listed paths (this edits `guids.json`) and re-runs.

### Known state of the GUID cache (verified 2026-11)

- The `.unitypackage` run needs no new GUIDs at HEAD (`No new guid
  generated.`).
- The UPM run always hits `MissingGuidsError` for 21 UPM-only entries
  (`com.google.firebase.app/Firebase/FirebaseApp[/Internal]`,
  `com.google.firebase.functions/Firebase/FirebaseFunctions[/Internal]`, and
  every `com.google.firebase.app-check/*` and `com.google.firebase.firebaseai/*`
  path), because `package.yml` commits `guids.json` **before** the tgz step,
  so UPM-only GUIDs minted on CI are never checked in. This is the
  "regenerated every build" gap; it is a release-process issue, not something
  to fix by adding entries in a feature PR (see `firebase-unity-guid-policy`).

### Python 3.13+ breaks the self-heal

Under Python 3.13 the uncaught `MissingGuidsError` prints as
`MissingGuidsError: <exception str() failed>` (its `__str__` reads `__file__`,
which 3.13 has already cleared from `__main__`). `build_package.py` then
parses garbage from stderr, `gen_guids.py` dies with
`FATAL Flags parsing error: Unknown command line flag '>'`, the retry fails
the same way, and the UPM run **silently emits 9 of 13 tarballs with exit
code 0** (missing `app`, `app-check`, `firebaseai`, `functions`). CI
(Python 3.9) is unaffected. Local workaround that keeps the repo clean:

```bash
# 1. scratch copy of the packer config, outside the repo
P=~/tmp/packer && mkdir -p $P && cp unity_packer/exports.json unity_packer/guids.json $P/ \
  && cp -r unity_packer/debug_single_export_json $P/
# 2. list the missing paths (import the exporter so __file__ is defined)
python3 - <<'EOF' > ~/tmp/missing.txt
import os, sys; sys.path.insert(0, 'scripts/build_scripts')
H = os.path.expanduser('~')
sys.argv = ['x', f'--assets_dir={H}/tmp/zips', f'--config_file={H}/tmp/packer/exports.json',
            f'--guids_file={H}/tmp/packer/guids.json', f'--output_dir={H}/tmp/tgz',
            '--output_upm=True', f'--assets_zip={H}/tmp/zips/firebase_unity-<ver>-Linux.zip',
            '--plugins_version=<ver>', '--output_unitypackage=False']
from absl import flags, logging; import export_unity_package as e
logging.set_verbosity(logging.ERROR); flags.FLAGS(sys.argv)
try: e.main(sys.argv[:1])
except e.MissingGuidsError as ex: print('\n'.join(ex.missing_guid_paths))
EOF
# 3. mint throwaway GUIDs into the scratch copy only, then package from it
python3 scripts/build_scripts/gen_guids.py --guids_file=$P/guids.json --version=<ver> $(cat ~/tmp/missing.txt)
python3 scripts/build_scripts/build_package.py --zip_dir=~/tmp/zips --output=~/tmp/tgz --output_upm=True --script_folder=$P
git status --short unity_packer   # must be empty
```

(`--script_folder` accepts an absolute path because `os.path.join(cwd, P, ...)`
discards `cwd`.) Alternatively run the packager under Python <= 3.12.

### `unity_packer/exports.json`

One entry per package under `"packages"`. Each has a `name`
(`FirebaseStorage.unitypackage`), `imports` (groups of asset path globs with
an `importer` of `DefaultImporter` or `PluginImporter`, optional `platforms`,
`cpu`, `labels`), `includes` (dependent packages such as
`FirebaseAuth.unitypackage`, `SampleCommon.unitypackage`), `exclude_paths`,
readme/changelog/license paths, a `common_manifest` with the UPM `name`
(`com.google.firebase.storage`), display name, description and keywords, plus
`export_upm` / `upm_package_config`. Asset paths are the
paths **inside the platform zips** (e.g. `Firebase/Plugins/Firebase.Storage.dll`,
`Firebase/FirebaseAI/*`, `Plugins/iOS/Firebase/libFirebaseCppStorage.xcframework`).

When you add a new shipped file:

1. Make sure CMake packs it into the zip (`unity_pack_cs`, `unity_pack_native`,
   `unity_pack_file`, `unity_pack_folder`, ... in `cmake/unity_pack.cmake`).
2. Add or extend an `imports` entry in `exports.json` if an existing glob does
   not already cover it.
3. Do **not** add it to `guids.json`; the release-prep run does that.

### Source-shipped C# products

Firebase AI, Functions, and the `Firebase.App.Internal` core are shipped as C#
source with `.asmdef` files (`unity_pack_folder(...)` packs the whole
`<product>/src/` directory, including the committed `.meta` files, which is
how their file GUIDs stay stable). See the README section
"Unity Assembly Definitions (.asmdef)".

## 4. Inspecting a package

```bash
python scripts/build_scripts/unpack_package.py --folder=<dir with .unitypackage files> --output=<output_dir>
```

Unpacks each `.unitypackage` into
`<output_dir>/<name of --folder's parent dir>/<folder>_<Package>/Assets/...`
and writes a sibling `tree_lists/<folder>_<Package>.txt` (a `tree`-style
listing with sizes), handy for diffing
two releases.

## 5. What CI does

`.github/workflows/package.yml` (called by `build_starter.yml`) downloads the
platform zips produced by the build jobs into `built_artifact/` and runs:

```bash
python scripts/build_scripts/build_package.py --zip_dir=built_artifact [--apis=<csv>]
python scripts/build_scripts/build_package.py --zip_dir=built_artifact --output_upm=True --output=output_tgz
```

Artifacts: `firebase_unity_sdk.zip` (all `.unitypackage` files + `docs/readme.md`
+ `LICENSE`), `firebase_unity_sdk_tgz`, and `firebase_unity_sdk_hash.txt`
(SHA-256 of each artifact). If `apis` is a strict subset of the default list,
the run is treated as "package for checks" and uses `--apis`.

When `package.yml` is given `working_branch` (release branch) or
`create_new_branch=1`, it commits any resulting `guids.json` changes as
`Update Guids ID` / opens an `Update Unity SDK dependencies - <date>` PR as
`firebase-workflow-trigger[bot]`. This is the only sanctioned way GUIDs enter
the repository.

## Importing locally built packages into a project

- `.unitypackage`: Assets > Import Package > Custom Package in the Editor.
- `.tgz`: Window > Package Manager > "+" > Add package from tarball, or add a
  `file:` entry to `Packages/manifest.json`. Import `com.google.firebase.app`
  first; EDM4U (External Dependency Manager) is bundled and resolves the
  Android/iOS native dependencies.
- After re-importing a package that changed native libraries, restart Unity.
