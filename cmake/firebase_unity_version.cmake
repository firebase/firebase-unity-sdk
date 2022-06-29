# Copyright 2019 Google
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

# This file defines the version numbers used by the Firebase Unity SDK.

set(FIREBASE_UNITY_SDK_VERSION "9.2.0"
    CACHE STRING "The version of the Unity SDK, used in the names of files.")

set(FIREBASE_IOS_POD_VERSION "9.2.0"
    CACHE STRING "The version of the top-level Firebase Cocoapod to use.")

# https://github.com/googlesamples/unity-jar-resolver
set(FIREBASE_UNITY_JAR_RESOLVER_VERSION "1.2.172"
   CACHE STRING
  "Version tag of Play Services Resolver to download and use (no trailing .0)"
)

# https://github.com/firebase/firebase-cpp-sdk
set(FIREBASE_CPP_SDK_PRESET_VERSION "v9.2.0"
   CACHE STRING
  "Version tag of Firebase CPP SDK to download (if no local or not passed in) and use (no trailing .0)"
)
