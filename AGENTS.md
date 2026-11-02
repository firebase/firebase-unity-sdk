# Firebase Unity SDK: agent guide

This file gives AI agents the minimum context for working in
`firebase/firebase-unity-sdk`. Detailed, task-specific instructions live in
`.agents/skills/*/SKILL.md`; read the relevant one before starting.

| Skill | Read it when you... |
|---|---|
| `.agents/skills/firebase-unity-guid-policy/SKILL.md` | touch `unity_packer/guids.json`, any `.meta` file, or run packaging. **Read first for any change that adds/moves shipped files.** |
| `.agents/skills/firebase-unity-builder/SKILL.md` | build the SDK (`scripts/build_scripts/build_zips.py`), pick a C++ SDK checkout or Unity install, or regenerate SWIG output. |
| `.agents/skills/firebase-unity-packager/SKILL.md` | produce `.unitypackage`/`.tgz` files (`build_package.py`), edit `unity_packer/exports.json`, or inspect a package. |
| `.agents/skills/firebase-unity-test-runner/SKILL.md` | validate a change with the `<product>/testapp` projects, `build_testapps.py`, Playmode, or the CI integration tests. |
| `.agents/skills/firebase-unity-coding-standards/SKILL.md` | write C#, SWIG `.i`, or CMake; add release notes; prepare a PR. |

## Non-negotiable rules

1. **Never generate or commit Unity asset GUIDs.** Do not edit
   `unity_packer/guids.json`, do not run `gen_guids.py`, do not hand-write or
   edit `guid:` lines in `.meta` files. If `build_package.py` modified
   `guids.json` locally, run `git checkout -- unity_packer/guids.json` before
   committing. The release workflow (`.github/workflows/package.yml`) is the
   only thing that commits GUIDs.
2. Never commit `google-services.json`, `GoogleService-Info.plist`, build
   output (`*_unity/`), or generated SWIG sources.
3. Version pins (`cmake/firebase_unity_version.cmake`,
   `cmake/android_dependencies.cmake`) are updated by the release tooling
   (`scripts/update_versions.py` / `update_versions.yml`), not in feature PRs,
   unless the user explicitly asks.
4. The Firebase C++ SDK is a source dependency: `../firebase-cpp-sdk`,
   `FIREBASE_CPP_SDK_DIR`, or the tag in `cmake/firebase_unity_version.cmake`.
   Paired changes should be tested with `FIREBASE_CPP_SDK_DIR` pointing at the
   matching C++ branch.

## Quick reference

```bash
pip install -r scripts/gha/requirements.txt
python scripts/build_scripts/build_zips.py --platform=<linux|macos|windows|ios|tvos|android> [--apis=auth,storage]
python scripts/build_scripts/build_package.py --zip_dir=<zips> --output=<out> [--output_upm=True] [--apis=auth]
python scripts/gha/build_testapps.py --t auth --u <unity version> --p Playmode --plugin_dir <out>
```

Products: `analytics, app_check, auth, crashlytics, database, firebaseai,
firestore, functions, installations, messaging, remote_config, storage`.
Minimum Unity: 2022.3 LTS. Release notes: `docs/readme.md`.
