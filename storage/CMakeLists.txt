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

# CMake file for the Firebase Storage library

# Firebase Storage is different from the other Firebase libraries,
# as instead of prebuilding, we provide it as a source library.

# Package every file under src, including subfolders, since we want them all.
unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/storage/src/"
  PACK_PATH "Firebase/FirebaseStorage"
)

# For documentation, get all C# files recursively,
# which make up the public API.
file(GLOB_RECURSE firebase_storage_doc_src "src/*.cs")
# Filter out any files located in src/internal
list(FILTER firebase_storage_doc_src EXCLUDE REGEX "src/internal/")
unity_pack_documentation_sources(storage
  DOCUMENTATION_SOURCES
    ${firebase_storage_doc_src}
)
