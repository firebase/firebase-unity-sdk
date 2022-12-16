# `unity-setup` GitHub Action

## Inputs
-  `version`: **[Required]** Unity Major Version Number. Currently supported values: [2019, 2020].

-  `platforms`: Platforms that you'd like to support, if not provided, build apps on certain platforms may encounter errors. Values: [Android,iOS,tvOS,Windows,macOS,Linux].

-  `username`: Required when Activate Unity license. See the [Usage](https://github.com/firebase/firebase-unity-sdk/tree/main/gha/unity#usage) section below.

-  `password`: Required when Activate Unity license. See the Usage section below.

-  `serial_ids`: Required when Activate Unity license. See the Usage section below.

-  `release_license`: If a license has been activated in the pervious step, then **must** add another step to release the license. Set **"ture"** to `release_license` input.

## Output

This GitHub Action will provide `UNITY_VERSION` (full unity version, e.g. `2020.3.34f1`) and `UNITY_ROOT_DIR` (Unity project directory, e.g. `/Applications/Unity/Hub/Editor/2020.3.34f1`) environment variables as outputs.

-   Output usage:
    ```yml
    - uses: ./gha/unity
      with:
        version: ${{ unity_version }}
        platforms: ${{ platforms }}
    - run: |
        echo '${{ env.UNITY_VERSION }}'
        echo '${{ env.UNITY_ROOT_DIR }}'
    ```

## Usage
-   Install Unity without Activation
    ```yml
    jobs:
      build_sdk:
        # ...

        steps:
          # ...
          - uses: ./gha/unity
            with:
              version: ${{ unity_version }}
              platforms: ${{ platforms }}
    ```

-   Install Unity, Activate and Release Unity License. Always release the license after usage.
    ```yml
    jobs:
      build_testapp:
        # ...

        steps:
          # ...
          - id: unity_setup_and_activate
            uses: ./gha/unity
            with:
              version: ${{ unity_version }}
              platforms: ${{ platforms }}
              username: ${{ secrets.UNITY_USERNAME }}
              password: ${{ secrets.UNITY_PASSWORD }}
              serial_ids: ${{ secrets.SERIAL_ID }}
          # ...
          - id: release_license
            uses: ./gha/unity
            with:
              version: ${{ unity_version }}
              release_license: "true"
    ```

## How to upgrade supported unity versions
**Background**

This GitHub Action leverages [Unity Hub](https://unity3d.com/get-unity/download), which is a standalone application that streamlines the way you navigate, download, and manage your Unity projects and installations. Unity Hub is with beta version CLI support, and we are using it for Unity versions management.

In this GitHub Action, supported Unity Versions are maintained by `SETTINGS` in [`gha/unity/unity_installer.py`](https://github.com/firebase/firebase-unity-sdk/blob/unity-readme/gha/unity/unity_installer.py#L89). 

**Add a new Unity version support**

1. Select your version from [Unity LTS versions list](https://unity3d.com/unity/qa/lts-releases):
  -   Make sure this version can be installed with Unity Hub. 
  -   Make sure this version works on you computer first.
  -   You may need to select different versions for different OS.

2. Generate a JSON string which contains the following information and added it to `UNITY_SETTINGS`:
  -   `Major_version_number`: unity major version number: `2020`, `2021`, etc.
  -   `Full_version_number`: unity full version number. e.g. `2020.3.34f1` for major version `2020`.
  -   `Changeset`: changeset locates at the bottom of this page https://unity3d.com/unity/whats-new/{unity_version}. Note: the version is neither `Major_version_number` nor `Full_version_number`. e.g. https://unity3d.com/unity/whats-new/2020.3.34
  -   `Platform`: Firebase Unity SDK supported platforms. Values of [Android,iOS,tvOS,Windows,macOS,Linux]
  -   `Modules`:[Unity Hub must been installed] Unity modules that required for certain platform. e.g. ["windows-mono"] module for "Windows" platform. To list avaliable modules on mac machines, run `"/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" -- --headless help` .

      Template:
      ```
      UNITY_SETTINGS = {
        Major_version_number: {
          OS: {
            "version": Full_version_number,
            "changeset": Changeset,
            "modules": {Platform: [Moudles], ...},
          },
          ...
        },
      }
      ```
      e.g.
      ```
      UNITY_SETTINGS = {
        "2020": {
          WINDOWS: {
            "version": "2020.3.34f1",
            "changeset": "9a4c9c70452b",
            "modules": {ANDROID: ["android", "ios"], IOS: ["ios"], TVOS: ["appletv"], WINDOWS: [], MACOS: ["mac-mono"], LINUX: ["linux-mono"], PLAYMODE: ["ios"]},
          },
          ...
        },
      }
      ```

**Common failures & solutions**

1. If you have problem with Android build. Make sure you are using the right version of NDK and JDK. Testapp building process is using a patch function `patch_android_env` in `build_testapp.py`. (Please refer [Unity Documentation](https://docs.unity3d.com/Manual/android-sdksetup.html) for Android environment setup).
