# Copyright 2019 Google
#
# Licensed under the Apache License Version 2.0 (the License);
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#      http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing software
# distributed under the License is distributed on an AS IS BASIS
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# CMake file for the firebase database library

include(build_shared)

# Firebase Database Swig input files
set(firebase_database_swig
  src/swig/database.i
)

# Firebase Database CSharp files
set(firebase_database_src
  src/ChildEventHandler.cs
  src/DataSnapshot.cs
  src/DatabaseError.cs
  src/DatabaseException.cs
  src/DatabaseReference.cs
  src/FirebaseDatabase.cs
  src/MutableData.cs
  src/OnDisconnect.cs
  src/Query.cs
  src/ServerValue.cs
  src/TransactionResult.cs
  src/ValueEventHandler.cs
  src/internal/InternalChildListener.cs
  src/internal/InternalListener.cs
  src/internal/InternalTransactionHandler.cs
  src/internal/InternalValueListener.cs
  src/internal/Utilities.cs
)

firebase_swig_add_library(firebase_database_swig
  NAMESPACE
    Firebase.Database
  MODULE
    FirebaseCppDatabase
  SOURCES
    ${firebase_database_swig}
  DEPENDS
    firebase_database
)

unity_pack_documentation_sources(database
  DOCUMENTATION_SOURCES
    ${firebase_database_src}
    ${firebase_database_swig_gen_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(database
    DOCUMENTATION_SOURCES
      ${firebase_database_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_database_cs
  MODULE
    Firebase.Database
  SOURCES
    ${firebase_database_src}
    ${firebase_database_swig_gen_src}
  REFERENCES
    google_minijson_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_database_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_database libFirebaseCppDatabase DEPS firebase_database firebase_database_swig)
else()
  build_firebase_shared(
    database
    database
    FirebaseCppDatabase
  )
endif()

unity_pack_cs(firebase_database_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Database
    IOS_DEPS
      "Firebase/Database"
    ANDROID_DEPS
      ${FIREBASE_DATABASE_ANDROID_DEPS}
    ANDROID_SPEC
      "database"
  )
endif()

set_property(TARGET firebase_database_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
