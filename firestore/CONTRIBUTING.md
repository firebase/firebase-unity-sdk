# Overview

This describes how Firestore Unity SDK works, and how to develop and test 
the SDK, targeting desktop/Android/iOS.

# Prerequisites

Building the Unity SDK requires building the underlying C++ SDK. Refer to
[][this doc] for what the prerequisites are.

On top of above, you also need Unity installed (obviously). If you use an
apple silicon machine as host, be sure to install the right version of
Unity!

# Building Firestore Unity SDK

Building Firestore into Unity Packages is a very involved process, mixed with
multiple build tools working together. Therefore, we will rely on Python scripts
to automate the process. The scripts live under `$REPO_ROOT/scripts/build_scripts`.

```zsh
# all scripts are run from the repo root.

# Building for Mac. The build tools will try to find Unity automatically
python scripts/build_scripts/build_zips.py -platform=macos -targets=auth -targets=firestore -use_boringssl

# If above does not work, try specify Unity path direcly
python scripts/build_scripts/build_zips.py -platform=macos -unity_root=<PATH_TO_UNITY> -targets=auth -targets=firestore -use_boringssl

# Building for Android
python scripts/build_scripts/build_zips.py -platform=android -targets=auth -targets=firestore -use_boringssl

# Building for iOS. Incremental build for iOS is broken, so we use clean_build here.
python scripts/build_scripts/build_zips.py -platform=android -targets=auth -targets=firestore -use_boringssl -clean_build

# Other supported platforms are tvos,linux,windows
```

After running above commands, some zip files for each platform are created under
`$PLATFORM_unity` directories. Run below to put all of them into Unity packages:

```zsh
# Built Unity packages for all platforms are stored under ./package_dir
python scripts/build_scripts/zips_to_packages.py --output package_dir
```

# Running Firestore Desktop TestApp

Test app for Firestore is under `firestore/testapp`, we need to copy a 
`google-services.json` or `GoogleServices-Info.plist` to `firestore/testapp/Assets/Firebase/Sample/Firestore`
before we can run the test app.

The testapp depends on a custom test runner, which is needs to be copied over unfortunately:

```zsh
cp ./scripts/gha/integration_testing/automated_testapp/AutomatedTestRunner.cs firestore/testapp/Assets/Firebase/Sample/
cp -r ./scripts/gha/integration_testing/automated_testapp/ftl_testapp_files firestore/testapp/Assets/Firebase/Sample/
```

To run the test app, open `firestore/testapp` from Unity Editor, and load the Unity packages we built above.
Then open up `firestore/testapp/Assets/Firebase/Sample/Firestore/MainSceneAutomated.unity`, you should be
able to run this scene which in turn runs all integration tests for Firestore.

# Running Firestore Android TestApp

You *probably* need to use `IL2CPP` as scripting backend instead of `Mono` for Android. To do this,
you can go to `Edit->Project Setting->Player->Android->Scripting Backend` and select `IL2CPP`.

You also need to turn on `minification` under on the same setting page, by turning on `R8` under `publish
settings`.

To run the Android testapp, go to `File->Build Settings`, select `Android` then click `Switch Platform`. After
assets are loaded, click `Build and Run`.

# Running Firestore iOS TestApp

Similarly for iOS, go to `File-Build Settings` and select `iOS`. After you click `Build and Run`, it will prompt
you to select a directory to save generated code and XCode project.

After the code generation is done, go under the directory, and run `pod install` to generate
a `xcworkspace`, then open it via `XCode`. From `XCode` you should be able to sign the testapp, build and run/debug
the app with an actual iOS device, or as an iPad App on an Apple Silicon mac.
