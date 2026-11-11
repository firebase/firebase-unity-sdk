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
# Provided as a pure C# source library (.asmdef).

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/remote_config/src/"
  PACK_PATH "Firebase/FirebaseRemoteConfig"
)

file(GLOB_RECURSE firebase_remote_config_doc_src "src/*.cs")
list(FILTER firebase_remote_config_doc_src EXCLUDE REGEX "src/internal/")
unity_pack_documentation_sources(remote_config
  DOCUMENTATION_SOURCES
    ${firebase_remote_config_doc_src}
)
