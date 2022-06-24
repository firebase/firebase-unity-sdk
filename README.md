
# Firebase Unity #

---


## Getting Started
You can clone the repo with the following command:

``` bash
git clone https://github.com/firebase/firebase-unity-sdk.git
```

## Prerequisites
The prerequisites for firebase cpp are required. Please install the following
packages outlined in [README.md for firebase cpp](https://github.com/firebase/firebase-cpp-sdk/blob/master/README.md#prerequisites).

The following prerequisites are required for all platforms.  Be sure to add any
directories to your PATH as needed.

- [CMake](https://cmake.org/) version 3.13.3, or newer.
- [Mono](https://www.mono-project.com/) version 5 or newer.
- [Unity](https://unity.com/) version 2019 or newer.
- [Swig](http://www.swig.org/) version 4 or newer.

### Prerequisites for Windows
On windows, to work around path length issues with google unity resolver enable
long path support in git:

> **git config --system core.longpaths true**

### Prerequisites for Mac
Home brew can be used to install required dependencies:

```bash
# https://github.com/protocolbuffers/protobuf/blob/master/kokoro/macos/prepare_build_macos_rc#L20
ruby -e "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/master/install)"
source $HOME/.rvm/scripts/rvm
brew install cmake protobuf python mono swig
sudo chown -R $(whoami) /usr/local
```

## Building
The build uses CMake to generate the necessary build files, and supports out of
source builds.

The following CMake options are avaliable:

* **FIREBASE_INCLUDE_UNITY**: Build for unity only (no mono support)
* **FIREBASE_INCLUDE_MONO**: Build for mono only (no unity support)
* **FIREBASE_UNI_LIBRARY**: Build all native modules as one dynamic lib
* **FIREBASE_CPP_SDK_DIR**: Local path to firebase cpp
* **FIREBASE_UNITY_SDK_VERSION**: Set version string of firebase unity package
* **UNITY_ROOT_DIR**: Local path to Unity's installation directory
  (path should end with version number)
* **MONO_DIR**: Local path to mono's xbuild executable directory
* **OPENSSL_ROOT_DIR**: Open ssl root directory
* **PROTOBUF_SRC_ROOT_FOLDER**: Protobuf root directory

> Note:<br/>
> &nbsp;&nbsp;&nbsp;**UNITY_ROOT_DIR** is a recommended setting for building
> unity, else CMake will make an effort to auto find unity.

> Note:<br/>
> &nbsp;&nbsp;&nbsp;On windows, **MONO_DIR** is a required setting.

Example build command for linux:

``` bash
mkdir build && cd build
cmake .. -DFIREBASE_INCLUDE_MONO=ON -DFIREBASE_CPP_SDK_DIR=../../firebase-cpp-sdk
make -j 8
```

> Note:<br/>
> &nbsp;&nbsp;&nbsp;There are build helper scripts in the root folder that will
> build the most common variants and can be used as examples on how to build
> firebase unity.

### Building for iOS
CMake needs an extra argument specifing the tool chain to use:

> -DCMAKE_TOOLCHAIN_FILE=../cmake/unity_ios.cmake -G Xcode

### Building for Android
Run script ./build_android.sh on linux machine.


## Unity Package Command ##

CPack is used to generate platform zip files with the same folder structure of
the Unity plugin. Once each platform zip file is built, run
*unity packaging tool* to generate a `.unitypackage` file.

```
cd build
cpack .
cd ../unity_packer
python export_unity_package.py --config_file=exports.json --guids_file=guids.json --assets_dir=.. --assets_zip=../build/*.zip --output_dir=../build/
```

