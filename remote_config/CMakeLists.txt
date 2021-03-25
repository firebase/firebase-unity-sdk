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

# CMake file for the firebase remote_config library

include(build_shared)

# Firebase RemoteConfig Swig input files
set(firebase_remote_config_swig
  src/swig/remote_config.i
)

# Firebase RemoteConfig CSharp files
set(firebase_remote_config_src
  src/ConfigSettings.cs
  src/ConfigValue.cs
)

firebase_swig_add_library(firebase_remote_config_swig
  NAMESPACE
    Firebase.RemoteConfig
  MODULE
    FirebaseCppRemoteConfig
  SOURCES
    ${firebase_remote_config_swig}
  DEPENDS
    firebase_remote_config
)

mono_add_library(firebase_remote_config_cs
  MODULE
    Firebase.RemoteConfig
  SOURCES
    ${firebase_remote_config_src}
    ${firebase_remote_config_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_remote_config_swig
)

build_firebase_shared(
  remote_config
  config
  FirebaseCppRemoteConfig
)

unity_pack_native(firebase_remote_config_swig)
unity_pack_cs(firebase_remote_config_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(RemoteConfig
    IOS_DEPS
      "Firebase/RemoteConfig"
    ANDROID_DEPS
      ${FIREBASE_REMOTE_CONFIG_ANDROID_DEPS}
    ANDROID_SPEC
      "config"
  )
endif()

set_property(TARGET firebase_remote_config_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
