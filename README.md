# Firebase Unity Open Source Development

The repository contains the Firebase Unity SDK source, with support for Android,
iOS, and desktop platforms. Note that desktop is ***only supported for development
purposes***. It includes the following Firebase libraries:

- [Google Analytics for Firebase](https://firebase.google.com/docs/analytics/)
- [Firebase App Check](https://firebase.google.com/docs/app-check/)
- [Firebase Authentication](https://firebase.google.com/docs/auth/)
- [Firebase Crashlytics](https://firebase.google.com/docs/crashlytics)
- [Firebase Realtime Database](https://firebase.google.com/docs/database/)
- [Firebase Dynamic Links](https://firebase.google.com/docs/dynamic-links/)
- [Cloud Firestore](https://firebase.google.com/docs/firestore/)
- [Cloud Functions for Firebase](https://firebase.google.com/docs/functions/)
- [Firebase Invites](https://firebase.google.com/docs/invites/)
- [Firebase Cloud Messaging](https://firebase.google.com/docs/cloud-messaging/)
- [Firebase Remote Config](https://firebase.google.com/docs/remote-config/)
- [Cloud Storage for Firebase](https://firebase.google.com/docs/storage/)

Firebase is an app development platform with tools to help you build, grow and
monetize your app. More information about Firebase can be found at
<https://firebase.google.com>.

More information about the Firebase Unity SDK can be found at <https://firebase.google.com/docs/unity/setup>.  Samples on how to use the
Firebase Unity SDK can be found at <https://github.com/firebase/quickstart-unity>.

## Table of Contents

- [Firebase Unity Open Source Development](#firebase-unity-open-source-development)
  - [Table of Contents](#table-of-contents)
  - [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
    - [Install Unity](#install-unity)
  - [Building](#building)
    - [Building for certain library](#building-for-certain-library)
  - [Packaging](#packaging)
    - [Packaging unitypackage](#packaging-unitypackage)
    - [Packaging tgz](#packaging-tgz)
    - [Packing for certain library](#packing-for-certain-library)
  - [Including in Project](#including-in-project)
  - [Contributing](#contributing)
  - [License](#license)

## Getting Started

You can clone the repo with the following command:

``` bash
git clone https://github.com/firebase/firebase-unity-sdk.git
```

## Prerequisites

Please follow [Firebase C++ SDK Prerequisites](https://github.com/firebase/firebase-cpp-sdk/blob/main/README.md#prerequisites) first.

- [Swig](https://www.swig.org/), version 4 or newer

### Install Unity

- [Unity](https://unity.com/download), version 2019 or newer

While installing through UnityHub, when you decide which version to install, it will pop up a dialog to select necessary support modules. Please check the boxes based on your dev machine OS and dev platform. For most common case while dev on macOS, we should select Android + Android SDK & NDK Tool, iOS Build Support and Mac Build Support (IL2CPP)

## Building

Under the repo root folder, call

``` bash
python scripts/build_scripts/build_zips.py --platform=<target platform>
```

> **Note:** Supported target platform names: linux,macos,windows,ios,android

Expected output artifact is
[Repo Root]/<*platform_unity, eg macos_unity*>/firebase_unity-< *version* >-< *platform* >.zip

> **Note:**
>
> - Linux zip requires linux machine to build.
> - Windows zip requires windows machine to build
> - macOS, iOS and android zips can be built by mac.(Although android zip could be built on both linux and windows machine as well, but we recommend to run it with mac, to get align with our CI)

### Building for certain library

``` bash
python scripts/build_scripts/build_zips.py --platform=<target platform> --targets=<lib1> --targets=<lib2>
```

> **Note:** Supported library names: analytics, app_check, auth, crashlytics, database, dynamic_links, firestore, functions, installations, messaging, remote_config, storage

## Packaging

We can package the built artifacts to better imported by Unity Editor.

### Packaging unitypackage

Copy the zip file for each platforms to one folder, referred to as assets_zip_dir below, for example usually looks like this

- firebase_unity-< *version* >-Android.zip
- firebase_unity-< *version* >-Darwin.zip
- firebase_unity-< *version* >-Linux.zip
- firebase_unity-< *version* >-iOS.zip
- firebase_unity-< *version* >-win64.zip
  
And then run:

``` bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output dir>
```

### Packaging tgz

With the same assets_zip_dir, we can run:

``` bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output dir> --output_upm=True
```

### Packing for certain library

If we build only certain subset of the libraries like in [Building for certain library](#building-for-certain-library), we can copy the built artifacts into assets_zip_dir, and then run:

``` bash
python scripts/build_scripts/build_package.py --zip_dir=<assets_zip_dir> --output=<output dir> --apis=<lib1,lib2>
```

## Including in Project

We can refer to [Firebase Unity Installation Options](https://firebase.google.com/docs/unity/setup-alternative) to learn how to import the unitypacakge or tgz files that packaged.

## Contributing

We love contributions, but note that we are still working on setting up our
test infrastructure, so we may choose not to accept pull requests until we have
a way to validate those changes on GitHub. Please read our
[contribution guidelines](/CONTRIBUTING.md) to get started.

## License

The contents of this repository is licensed under the
[Apache License, version 2.0](http://www.apache.org/licenses/LICENSE-2.0).

Your use of Firebase is governed by the
[Terms of Service for Firebase Services](https://firebase.google.com/terms/).
