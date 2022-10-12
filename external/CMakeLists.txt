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

# This file configures projects and libraries required by Firebase Unity
# provided by external sources

mono_add_external_dll(unity_editor_cs "${UNITY_EDITOR_DLL}")
mono_add_external_dll(unity_engine_cs "${UNITY_ENGINE_DLL}")
if(APPLE)
  mono_add_external_dll(unity_ios_extensions_xcode_cs "${UNITY_EDITOR_IOS_XCODE_DLL}")
endif()

# Dont pack libraries shipped with unity
unity_pack_skip(unity_editor_cs)
unity_pack_skip(unity_engine_cs)
if(APPLE)
  unity_pack_skip(unity_ios_extensions_xcode_cs)
endif()

mono_add_library(google_minijson_cs
  MODULE
    Google.MiniJson
  SOURCES
    ${MINIJSON_SOURCE_DIR}/MiniJSON.cs
)

if(FIREBASE_INCLUDE_UNITY)
  # Note: Project name has capital C in 'Code' and output name is lowercase c
  # Breaks on case sensitve file systems thus we have to override it

  set(JAR_RESOLVER_DLL_DIR
    ${GOOGLE_UNITY_JAR_RESOLVER_SOURCE_DIR}/exploded/Assets/ExternalDependencyManager/Editor/
  )

  mono_add_external_dll(google_version_handler_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.VersionHandler.dll"
  )

  mono_add_external_dll(google_version_handlerimpl_cs
    "${JAR_RESOLVER_DLL_DIR}/${FIREBASE_UNITY_JAR_RESOLVER_VERSION}/Google.VersionHandlerImpl.dll"
  )

  mono_add_external_dll(google_jar_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/${FIREBASE_UNITY_JAR_RESOLVER_VERSION}/Google.JarResolver.dll"
  )

  mono_add_external_dll(google_ios_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/${FIREBASE_UNITY_JAR_RESOLVER_VERSION}/Google.IOSResolver.dll"
  )

  mono_add_external_dll(google_package_manager_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/${FIREBASE_UNITY_JAR_RESOLVER_VERSION}/Google.PackageManagerResolver.dll"
  )

  unity_pack_folder(
    "${JAR_RESOLVER_DLL_DIR}/"
    PACK_PATH "ExternalDependencyManager/Editor/"
  )

  unity_pack_file(
    "${FIREBASE_CPP_SDK_DIR}/generate_xml_from_google_services_json.exe"
    PACK_PATH "Firebase/Editor"
  )

  unity_pack_file(
    "${FIREBASE_CPP_SDK_DIR}/generate_xml_from_google_services_json.py"
    PACK_PATH "Firebase/Editor"
  )

  unity_pack_file(
    "${FIREBASE_UNITY_DIR}/network_request.exe"
    PACK_PATH "Firebase/Editor"
  )

  unity_pack_file(
    "${FIREBASE_UNITY_DIR}/network_request.py"
    PACK_PATH "Firebase/Editor"
  )
endif()
