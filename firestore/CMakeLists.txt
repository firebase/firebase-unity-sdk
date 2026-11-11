# Copyright 2021 Google
#
# Licensed under the Apache License Version 2.0 (the License);
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      https://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing software
# distributed under the License is distributed on an AS IS BASIS
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# CMake file for the firebase firestore library
# Provided as a pure C# source library with an assembly definition.

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/firestore/src/"
  PACK_PATH "Firebase/FirebaseFirestore"
)

# Firebase Firestore CSharp files that should be included in reference docs
file(GLOB_RECURSE firebase_firestore_doc_src "src/*.cs")
list(FILTER firebase_firestore_doc_src EXCLUDE REGEX "src/(internal|Converters)/")
unity_pack_documentation_sources(firestore
  DOCUMENTATION_SOURCES
    ${firebase_firestore_doc_src}
)
