# Copyright 2019 Google
#
# Licensed under the Apache License, Version 2.0 (the   License  );
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an   AS IS   BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# CMake file for the firebase storage library

include(build_shared)

# Firebase Storage Swig input files
set(firebase_storage_swig
  src/swig/storage.i
)

# Firebase Storage CSharp files that should be included in reference docs
set(firebase_storage_src_documented
  src/DownloadState.cs
  src/FirebaseStorage.cs
  src/MetadataChange.cs
  src/StorageMetadata.cs
  src/StorageException.cs
  src/StorageProgress.cs
  src/StorageReference.cs
  src/UploadState.cs
)

# Firebase Storage CSharp files
set(firebase_storage_src
  ${firebase_storage_src_documented}
  src/internal/ModuleLogger.cs
  src/internal/Preconditions.cs
  src/internal/TransferState.cs
  src/internal/TransferStateUpdater.cs
)

firebase_swig_add_library(firebase_storage_swig
  NAMESPACE
    Firebase.Storage
  MODULE
    FirebaseCppStorage
  SOURCES
    ${firebase_storage_swig}
  DEPENDS
    firebase_storage
)

unity_pack_documentation_sources(storage
  DOCUMENTATION_SOURCES
    ${firebase_storage_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(storage
    DOCUMENTATION_SOURCES
      ${firebase_storage_swig_gen_src}
      ${firebase_storage_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_storage_cs
  MODULE
    Firebase.Storage
  SOURCES
    ${firebase_storage_src}
    ${firebase_storage_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_storage_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_storage libFirebaseCppStorage DEPS firebase_storage firebase_storage_swig)
else()
build_firebase_shared(
  storage
  storage
  FirebaseCppStorage
)
endif()

unity_pack_cs(firebase_storage_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Storage
    IOS_DEPS
      "Firebase/Storage"
    ANDROID_DEPS
      ${FIREBASE_STORAGE_ANDROID_DEPS}
    ANDROID_SPEC
      "storage"
  )
endif()

set_property(TARGET firebase_storage_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
