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

# CMake file for the firebase messaging unity editor plugin

set(firebase_messaging_editor_src
    ../../messaging/activity/AndroidManifest.xml
    src/AndroidManifestPatcher.cs
)

mono_add_library(firebase_messaging_editor_cs
  MODULE
    Firebase.Messaging.Editor
  SOURCES
    ${firebase_messaging_editor_src}
    REFERENCES
    unity_editor_cs
    unity_engine_cs
    google_version_handlerimpl_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

unity_pack_cs(firebase_messaging_editor_cs
  PACK_PATH "Firebase/Editor/"
)
