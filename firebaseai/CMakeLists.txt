# Copyright 2025 Google
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

# CMake file for the Firebase AI library

# Firebase AI is different from the other Firebase libraries,
# as instead of prebuilding, we provide it as a source library.

# Package every file under src, including subfolders, since we want them all.
unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/firebaseai/src/"
  PACK_PATH "Firebase/FirebaseAI"
)

# For documentation, get all C# files recursively,
# which make up the public API.
file(GLOB_RECURSE firebase_firebaseai_doc_src "src/*.cs")
# Filter out any files located in src/Internal
list(FILTER firebase_firebaseai_doc_src EXCLUDE REGEX "src/Internal/")
unity_pack_documentation_sources(firebaseai
  DOCUMENTATION_SOURCES
    ${firebase_firebaseai_doc_src}
)

if (FIREBASE_INCLUDE_MONO)
  file(GLOB_RECURSE firebase_firebaseai_src "src/*.cs")
  mono_add_library(firebase_firebaseai_cs
    MODULE
      Firebase.FirebaseAI
    SOURCES
      ${firebase_firebaseai_src}
    SYSTEM_REFERENCES
      System.Net.Http
    REFERENCES
      ${FIREBASE_PLATFORM_REF}
      firebase_app_internal_cs
      google_minijson_cs
  )
  unity_pack_cs(firebase_firebaseai_cs)
  set_property(TARGET firebase_firebaseai_cs
    PROPERTY FOLDER
    "Firebase ${FIREBASE_PLATFORM_NAME}"
  )
endif()
