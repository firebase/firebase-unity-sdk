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

include(build_shared)

# Firebase firestore Swig input files
set(firebase_firestore_swig
  src/swig/firestore.i
)

# Firebase Firestore CSharp files that should be included in reference docs
set(firebase_firestore_src_documented
  src/Blob.cs
  src/CollectionReference.cs
  src/DocumentChange.cs
  src/DocumentReference.cs
  src/DocumentSnapshot.cs
  src/FieldPath.cs
  src/FieldValue.cs
  src/FirebaseFirestore.cs
  src/FirebaseFirestoreSettings.cs
  src/FirestoreDataAttribute.cs
  src/FirestoreDocumentIdAttribute.cs
  src/FirestoreEnumNameConverter.cs
  src/FirestoreError.cs
  src/FirestoreException.cs
  src/FirestorePropertyAttribute.cs
  src/GeoPoint.cs
  src/ListenerRegistration.cs
  src/LoadBundleTaskProgress.cs
  src/MetadataChanges.cs
  src/Query.cs
  src/QuerySnapshot.cs
  src/ServerTimestampAttribute.cs
  src/ServerTimestampBehavior.cs
  src/SetOptions.cs
  src/SnapshotMetadata.cs
  src/Source.cs
  src/Timestamp.cs
  src/Transaction.cs
  src/TransactionOptions.cs
  src/UnknownPropertyHandling.cs
  src/WriteBatch.cs
)

# Firebase Firestore CSharp files
set(firebase_firestore_src
  ${firebase_firestore_src_documented}
  src/Converters/AnonymousTypeConverter.cs
  src/Converters/ArrayConverter.cs
  src/Converters/AttributedIdAssigner.cs
  src/Converters/AttributedTypeConverter.cs
  src/Converters/ConverterBase.cs
  src/Converters/ConverterCache.cs
  src/Converters/CustomConverter.cs
  src/Converters/DictionaryConverter.cs
  src/Converters/EnumConverter.cs
  src/Converters/EnumerableConverter.cs
  src/Converters/EnumerableConverterBase.cs
  src/Converters/IFirestoreInternalConverter.cs
  src/Converters/MapConverterBase.cs
  src/Converters/SimpleConverters.cs
  src/internal/AssertFailedException.cs
  src/internal/Enums.cs
  src/internal/EnvironmentVersion.cs
  src/internal/Hash.cs
  src/internal/Preconditions.cs
  src/internal/Util.cs
  src/BuildStubs.cs
  src/ConverterRegistry.cs
  src/DeserializationContext.cs
  src/FirestoreConverter.cs
  src/ListenerRegistrationMap.cs
  src/MonoPInvokeCallbackAttribute.cs
  src/SerializationContext.cs
  src/TransactionManager.cs
  src/ValueDeserializer.cs
  src/ValueSerializer.cs
)

add_subdirectory(src/swig)

firebase_swig_add_library(firebase_firestore_swig
  NAMESPACE
    Firebase.Firestore
  MODULE
    FirebaseCppFirestore
  SOURCES
    ${firebase_firestore_swig}
  DEPENDS
    firebase_firestore
    firebase_firestore_swig_cpp
)

set(FIRESTORE_SOURCE_DIR ${PROJECT_BINARY_DIR}/bin/external/src/firestore)

# Additional public headers come from the Firestore core
if(IOS)
  target_include_directories(firebase_firestore_swig
    PUBLIC
      # The Firestore core on iOS comes via the FirebaseFirestore CocoaPod, and
      # public headers should come from there.
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore/Firestore/core/include
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore/Firestore/Protos/nanopb
    PRIVATE
      # Additionally, the core C++ API is not declared publicly within the
      # FirebaseFirestore pod, so depend on headers available in the source
      # distribution.
      ${FIREBASE_POD_DIR}/Pods/FirebaseFirestore
  )
else()
  # Desktop and Android get their public headers from the CMake build in
  # firebase-ios-sdk.
  target_include_directories(firebase_firestore_swig
    PUBLIC
      ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
      ${FIRESTORE_SOURCE_DIR}/Firestore/Protos/nanopb
    PRIVATE
      ${FIRESTORE_SOURCE_DIR}
  )
endif()

mono_add_library(firebase_firestore_cs
  MODULE
    Firebase.Firestore
  SOURCES
    ${firebase_firestore_src}
    ${firebase_firestore_swig_gen_src}
  REFERENCES
    google_minijson_cs
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_firestore_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_firestore libFirebaseCppFirestore DEPS firebase_firestore firebase_firestore_swig firebase_firestore_swig_cpp)
else()
  build_firebase_shared(
    firestore
    firestore
    FirebaseCppFirestore
  )

  target_include_directories(firebase_firestore_shared
    PUBLIC
      ${FIRESTORE_SOURCE_DIR}/Firestore/core/include
      ${FIRESTORE_SOURCE_DIR}/Firestore/Protos/nanopb
    PRIVATE
      ${FIRESTORE_SOURCE_DIR}
  )
endif()

unity_pack_cs(firebase_firestore_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Firestore
    IOS_DEPS
      "FirebaseFirestore,9.4.0"
    ANDROID_DEPS
      ${FIREBASE_FIRESTORE_ANDROID_DEPS}
    ANDROID_SPEC
      "firestore"
  )
endif()

unity_pack_documentation_sources(firestore
  DOCUMENTATION_SOURCES
    ${firebase_firestore_src_documented}
)

set_property(TARGET firebase_firestore_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
