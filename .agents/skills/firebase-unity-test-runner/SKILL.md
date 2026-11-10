---
name: firebase-unity-test-runner
description:
  Workflows for validating Firebase Unity SDK changes with the per-product
  testapps: running them in the Unity Editor, building standalone/mobile
  testapps and in-editor Playmode runs with `scripts/gha/build_testapps.py`,
  running desktop builds with `desktop_tester.py`, and triggering the CI
  integration-test workflow. Use when verifying a new feature or bug fix in the
  Unity SDK.
---

# Testing the Firebase Unity SDK

There are no hermetic unit tests for most of the SDK; validation is done with
the per-product **testapps** (Unity projects with an automated test
`UIHandlerAutomated.cs` plus a manual `UIHandler.cs`) run against a locally
built or CI-built package.

## Where the testapps live

| Location | Use |
|---|---|
| `<product>/testapp/` in this repo (e.g. `auth/testapp`) | The integration-test projects CI uses. Contain `Assets/Firebase/Sample/<Product>/UIHandler.cs`, `UIHandlerAutomated.cs`, `MainScene.unity`, `MainSceneAutomated.unity`. |
| [firebase/quickstart-unity](https://github.com/firebase/quickstart-unity) | External-facing samples (manual UI only). |

Product short names (`--t` values and `build_testapps.json` entries):
`analytics, app_check, auth, crashlytics, database, firebaseai, firestore,
functions, installations, messaging, remote_config, storage`.

## Prerequisites

- Unity Editor **2022.3 LTS or newer** installed (CI's default matrix is the
  `2022` series). Android/iOS/tvOS module support as needed; `ANDROID_HOME`
  and `JAVA_HOME` for Android; Xcode for iOS/tvOS/macOS.
- `pip install -r scripts/gha/requirements.txt`.
- Firebase config files for the test project: put `google-services.json`
  (Android/desktop) and `GoogleService-Info.plist` (iOS/tvOS) in
  `<product>/testapp/Assets/Firebase/Sample/<Product>/` (both names are
  gitignored). CI decrypts them from `scripts/gha-encrypted/<product>/` via
  `python scripts/gha/restore_secrets.py --passphrase <secret>`; for local
  testing without the CI passphrase, download them from a Firebase test
  project in the Firebase console.
- Fill in the placeholders `restore_secrets.py` would patch (and revert
  those edits before committing):
  `REPLACE_WITH_YOUR_DATABASE_URL` (database `UIHandlerAutomated.cs`),
  `REPLACE_WITH_YOUR_STORAGE_BUCKET` (storage `UIHandler.cs`),
  `REPLACE_WITH_APP_CHECK_TOKEN` (app_check `UIHandler*.cs`, functions and
  firebaseai `UIHandlerAutomated.cs`/`UIHandler.cs`).
- **Firestore Emulator (required for `firestore` on Desktop / Playmode)**:
  `firestore/testapp/Assets/Firebase/Sample/Firestore/UIHandler.cs`
  (`GetTargetBackend()`) targets `localhost:8080` whenever `UNITY_EDITOR` or
  `UNITY_STANDALONE` is defined (unless `RUN_AGAINST_PRODUCTION` is set).
  Without a running emulator on port `8080`, `firestore` hangs until the 600s
  per-attempt timeout (× 3 retries = 30 min). Start the emulator first and
  **redirect stdout/stderr to a file** (Node puts shared stdout pipes into
  non-blocking mode, which causes Python log writes to fail with `EAGAIN` /
  exit code 120):
  ```bash
  firebase emulators:start --only firestore > firestore-emulator.log 2>&1 &
  ```
- A built package: either a local `build_package.py` output
  (see `firebase-unity-packager`) or the `firebase_unity_sdk.zip` /
  `firebase_unity_sdk_tgz` artifact from a **Firebase Unity SDK Build** run.

> [!IMPORTANT]
> A package built from a **Linux- or Windows-only zip cannot drive the
> testapps**: `build_testapps.py` runs `PluginToggler.Enable` on
> `Firebase.Editor.dll`, and the Editor tools (`editor/`, packed as
> `Firebase/Editor/Firebase.Editor.dll` + `Editor Default Resources/`) are
> built only on macOS (`DESKTOP AND APPLE AND FIREBASE_INCLUDE_EDITOR_TOOL`
> in `CMakeLists.txt`). Symptom: `InvalidOperationException: Plugin not
> found: Firebase.Editor.dll` in `enable_editor_plugins.build.log` and no
> Playmode run. Fix: add a macOS zip to the `--zip_dir` before packaging, e.g.
> take `macos_unity` from the last green
> [build_starter run](https://github.com/firebase/firebase-unity-sdk/actions/workflows/build_starter.yml)
> (`gh run download <run-id> -R firebase/firebase-unity-sdk -n macos_unity`)
> and, to avoid mixing C# assemblies from two builds, copy only the
> `Firebase/Editor/Firebase.Editor.*` and `Editor Default Resources/*`
> entries into a small extra zip in the same directory (any `*.zip` there is
> consumed).

## 1. Fastest loop: run a testapp in the Editor

1. Open `<product>/testapp` in the Unity Editor.
2. Import the product's `.unitypackage` (Assets > Import Package > Custom
   Package) or add the `.tgz` via Package Manager. EDM4U will resolve native
   deps when you switch to Android/iOS.
3. Open `MainScene.unity` (manual UI) and press Play. For the automated
   scene (`MainSceneAutomated.unity`) first copy
   `scripts/gha/integration_testing/automated_testapp/AutomatedTestRunner.cs`
   into `Assets/Firebase/Sample/` (CI's `build_testapps.py` does this for
   you); the runner then logs each test and a final pass/fail summary to the
   Console.
4. Set `FirebaseApp.LogLevel = LogLevel.Verbose;` early in the handler to get
   both the C# and C++ layer logs.

To iterate on a single product's native code, rebuild just that product's
zip (`build_zips.py --platform=<host> --apis=<product>`), package with
`--apis=<product>`, re-import, and **restart the Editor** (native plugins are
not hot-reloaded). On macOS, Gatekeeper may prompt for each new `.bundle`;
allow them under System Settings > Privacy & Security.

Device builds: switch platform in File > Build Settings, "Add Open Scenes",
Build (Android: Build and Run). For iOS open the generated
`Unity-iPhone.xcworkspace`, set signing, run. For an iOS simulator set
Project Settings > Player > iOS > Target SDK to *Simulator SDK* first.
Firestore on Android additionally needs minification enabled
(Project Settings > Player > Android > Publishing Settings > Minify) to
exercise the ProGuard rules.

## 2. Scripted builds and Playmode runs: `build_testapps.py`

Runs headless Unity to import the plugins into the testapp project, applies
`scripts/gha/integration_testing/build_testapps.json`, and builds (or, for
`Playmode`, runs the automated tests in the Editor and validates the log).

```bash
# Build Android + iOS testapps for auth and storage with Unity 2022.3.x
python scripts/gha/build_testapps.py --t auth,storage --u 2022.3.76f1 --p Android,iOS \
  --plugin_dir <dir containing *.unitypackage>

# In-editor Playmode test run (no build), desktop host
python scripts/gha/build_testapps.py --t auth --u 2022.3.76f1 --p Playmode \
  --plugin_dir <dir containing *.unitypackage>

# Use UPM tarballs instead of .unitypackage files
python scripts/gha/build_testapps.py --t auth --u 2022.3.76f1 --p Playmode \
  --use_local_packages <dir containing *.tgz>
```

Key flags (`--help` for all):

| Flag | Meaning |
|---|---|
| `--t` / `--testapps` | CSV of products, or `all`. |
| `--u` / `--unity_versions` | Unity version(s); must match a Unity Hub install folder name. Use `--unity_folder_override` for non-standard install paths. |
| `--p` / `--platforms` | CSV of `Android,iOS,tvOS,Windows,macOS,Linux,Playmode`. Desktop platforms build standalone players; `Playmode` runs in-editor. |
| `--plugin_dir` | Directory of `.unitypackage` files (default `~/Downloads/client_unity_plugins`). |
| `--use_local_packages` | Directory of `.tgz` UPM packages (alternative to `--plugin_dir`). Requires `com.google.external-dependency-manager-*.tgz` in the directory (pack `<build_dir>/external/src/google_unity_jar_resolver/upm` as `package/` into `com.google.external-dependency-manager-<ver>.tgz`), and note that `build_testapps.json`'s `"com.google.firebase.app-*.tgz"` glob collides with `com.google.firebase.app-check-<ver>.tgz` (`Multiple matches for package glob`) if both are in the folder—either omit `com.google.firebase.app-check-*.tgz` when not testing `app_check`/`firebaseai`/`functions` or pass a custom `--config_path` with `com.google.firebase.app-[0-9]*.tgz`. |
| `--ios_sdk` | `real` (device) or `virtual` (simulator) for iOS/tvOS. |
| `--output_directory`, `--artifact_name`, `--notimestamp` | Control the `testapps*/<unity_version>-NET<runtime>/<API>` output tree. |
| `--force_latest_runtime` | CI passes this; harmless on modern Unity. |
| `--enable_firebase=false`, `--enable_edm4u=false` | Diagnostic builds without the SDK / without EDM4U resolution. |

Outputs: Android `.apk`, iOS `.app`/`.ipa` under `app_ios_build`, desktop
players under the output tree. Build logs are written as
`build-results-<artifact_name>.log`; Playmode results as
`test-results-<artifact_name>.log` (+ `.json`) with a `TEST SUMMARY`, and the
raw Unity log per product at
`<output>/testapps/Unity<ver>-NET4.6/<FullName>/build_Playmode.build.log`
(look for `All tests finished` and `PASS: n, FAIL: m`). A single-product
Playmode run (import + runtime switch + tests) takes ~1.5 min on a 24-core
Linux box with Unity 2022.3; it runs headless (`-batchmode -nographics`), no
display needed.

## 3. Running built desktop testapps

```bash
python scripts/gha/desktop_tester.py --testapp_dir <output tree from build_testapps.py> \
  --logfile_name <name>
```

Finds every built `testapp` (`testapp` / `testapp.app` / `testapp.exe`), runs
it, and validates the automated-test log; results go to
`test-results-<logfile_name>.log`. Simulator runs use
`scripts/gha/test_simulator.py`; CI runs real devices on Firebase Test Lab.

## 4. CI: integration tests

- A PR automatically runs **Firebase Unity SDK Build**
  (`build_starter.yml`): build all platforms -> `package.yml` ->
  `integration_tests.yml` (build testapps per platform, Playmode on desktop,
  FTL for devices). Expect ~1.5h build + several hours of tests.
- To test a subset, trigger `build_starter.yml` manually on your branch and set
  `apis` / `platforms`; set `firebase_cpp_sdk_version` to a C++ branch, tag,
  or SHA to validate against a pending C++ PR.
- `integration_tests.yml` can be dispatched alone against an existing packaged
  run (`packaged_sdk_run_id`) with its own `unity_versions`, `platforms`,
  `build_os`, `mobile_test_on` inputs. Failed targets can be retried with the
  `retry-test-failures.yml` workflow.
- Test results are summarized on the run page by
  `scripts/gha/summarize_test_results.py`; the per-API Unity log files are
  uploaded as artifacts.

## 5. Adding tests

Add cases to `<product>/testapp/Assets/Firebase/Sample/<Product>/UIHandlerAutomated.cs`:
each test is a `Func<Task>` listed in `Start()` and handed to
`AutomatedTestRunner.CreateTestRunner(testsToRun:, logFunc:)`; a test fails
when its `Task` faults (throw / `TaskCompletionSource.SetException`) or times
out (`testRunner.TestTimeoutSeconds`). `AutomatedTestRunner.cs` lives in
`scripts/gha/integration_testing/automated_testapp/` and is copied into the
project by `build_testapps.py` (copy it manually when running in the Editor).
Keep `UIHandler.cs` (manual) and the
automated handler in sync when adding new API surface. When a testapp needs a
new plugin or scene, update `scripts/gha/integration_testing/build_testapps.json`.
Do not commit `google-services.json` / `GoogleService-Info.plist` (only the
encrypted `.gpg` copies under `scripts/gha-encrypted/` belong in git).
