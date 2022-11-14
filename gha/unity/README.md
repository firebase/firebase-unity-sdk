# `unity-setup` GitHub Action

## Inputs
-  `version`: **[Required]** Unity Major Version Number. Currently supported values: [2019, 2020].

-  `platforms`: Platforms that you'd like to support, if not provided, some platforms may encounter errors. Values: [Android,iOS,tvOS,Windows,macOS,Linux].

-  `username`: Required when Activate Unity license. Refer to the Usage section below.

-  `password`: Required when Activate Unity license. Refer to the Usage section below.

-  `serial_ids`: Required when Activate Unity license. Refer to the Usage section below.

-  `release_license`: If a license has been activated in the pervious step, then **must** add anothe step and set **"ture"** to `release_license` input.

## Output

This GitHub Action will provide `unity_version` (full unity version) output and `UNITY_ROOT_DIR` (Unity project directory) environment variable 

-   Output usage:
    ```yml
    - id: unity_setup
      uses: firebase/firebase-unity-sdk/gha/unity@main
      with:
        version: ${{ unity_version }}
        platforms: ${{ platforms }}
    - run: |
        echo '${{ steps.unity_setup.outputs.unity_version }}'
        echo '$UNITY_ROOT_DIR'
    ```

## Usage
-   Install Unity without Activation
    ```yml
    jobs:
      build_sdk:
        # ...

        steps:
          # ...
          - id: unity_setup
            uses: firebase/firebase-unity-sdk/gha/unity@main
            with:
              version: ${{ unity_version }}
              platforms: ${{ platforms }}
    ```

-   Install Unity, Activate and Release Unity License
    ```yml
    jobs:
      build_testapp:
        # ...

        steps:
          # ...
          - id: unity_setup
            uses: firebase/firebase-unity-sdk/gha/unity@main
            with:
              version: ${{ unity_version }}
              platforms: ${{ platforms }}
              username: ${{ secrets.UNITY_USERNAME }}
              password: ${{ secrets.UNITY_PASSWORD }}
              serial_ids: ${{ secrets.SERIAL_ID }}
          # ...
          - id: release_license
            uses: firebase/firebase-unity-sdk/gha/unity@main
            with:
              version: ${{ unity_version }}
              release_license: "true"
    ```

## [Deprecated] How to upgrade supported unity versions
**Background**

This GitHub Action leverages [U3D](github.com/DragonBox/u3d), which is a command line tool for working with Unity from the command line on all three operating systems. 

In this GitHub Action, supported Unity Versions are maintained by `UNITY_SETTINGS` in `gha/unity/unity_installer.py`. 

**Add a new Unity version support**

1. Install [U3D](github.com/DragonBox/u3d).

2. Generate new JSON string and added it to `UNITY_SETTINGS`:
  -   `Major_version_number`: unity major version number: 2020, 2021, etc.
  -   `Full_version_number`: unity full version number. e.g. 2020.3.34f1 for major version 2020. Run `u3d available` and select [Unity LTS versions](https://unity3d.com/unity/qa/lts-releases).
  -   `Platform`: Values of [Android,iOS,tvOS,Windows,macOS,Linux]
  -   `Package`:[Unity Hub must **not** been installed] Unity Packages that required for certain platform. e.g. ["Windows-mono"] pakcages for "Windows" platform. To list avaliable packages, run `u3d available -u $unity_version -p`.

      ```
      UNITY_SETTINGS = {
        Major_version_number: {
          OS: {
            "version": Full_version_number,
            "packages": {Platform: [Package], ...},
          },
          ...
        },
      }
      ```

**Common failures & solutions**

1. If you met problem with `u3d` cmd (e.g. `u3d available -u $unity_version -p`), please install older version of `u3d` and disable the `u3d` version check. Then try it again.
    ```
    gem install u3d -v 1.2.3
    export U3D_SKIP_UPDATE_CHECK=1
    ``` 

2. If you have problem with Android build. Make sure you are using the right version of NDK and JDK. Testapp building process is using a patch function `patch_android_env` in `build_testapp.py`. (Please refer [Unity Documentation](https://docs.unity3d.com/Manual/android-sdksetup.html) for Android environment setup).
