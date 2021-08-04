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
if(FIREBASE_IOS_BUILD)
  mono_add_external_dll(unity_ios_extensions_xcode_cs "${UNITY_EDITOR_IOS_XCODE_DLL}")
endif()

# Dont pack libraries shipped with unity
unity_pack_skip(unity_editor_cs)
unity_pack_skip(unity_engine_cs)
if(FIREBASE_IOS_BUILD)
  unity_pack_skip(unity_ios_extensions_xcode_cs)
endif()

mono_add_library(google_minijson_cs
  MODULE
    Google.MiniJson
  SOURCES
    ${MINIJSON_SOURCE_DIR}/MiniJSON.cs
)

mono_add_library(unity_compat_cs
  MODULE
    Unity.Compat
  SOURCES
    ${PARSE_SOURCE_DIR}/Unity.Compat/Properties/AssemblyInfo.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/ConditionalWeakTable.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/ExceptionDispatchInfo.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/ExpressionVisitor.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/Func.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/IProgress.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/MissingExtensions.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/Progress.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/ThreadLocal.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/Tuple.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/Type.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat/Public/TypeInfo.cs
  REFERENCES
    unity_engine_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

mono_add_library(unity_tasks_cs
  MODULE
    Unity.Tasks
  SOURCES
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Properties/AssemblyInfo.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Internal/InternalExtensions.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/AggregateException.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/CancellationToken.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/CancellationTokenRegistration.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/CancellationTokenSource.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskCompletionSource.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskContinuationOptions.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskCreationOptions.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/Task.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskExtensions.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskFactory.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks/Public/TaskScheduler.cs
  REFERENCES
    unity_engine_cs
    unity_compat_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

unity_pack_cs(unity_compat_cs
  PACK_PATH "Parse/Plugins"
)

unity_pack_cs(unity_tasks_cs
  PACK_PATH "Parse/Plugins"
)

unity_pack_file(
  "${PARSE_SOURCE_DIR}/Unity.Tasks.UnityPlugin/UnityProjectTemplate/Assets/Parse/Plugins/Unity.Compat.dll.meta"
  PACK_PATH "Parse/Plugins"
)

unity_pack_file(
  "${PARSE_SOURCE_DIR}/Unity.Tasks.UnityPlugin/UnityProjectTemplate/Assets/Parse/Plugins/Unity.Tasks.dll.meta"
  PACK_PATH "Parse/Plugins"
)

# Moved to sub dir as it uses same file names as unity tasks/compat
# and that causes conflicts when in same CMakeLists.txt file
add_subdirectory(forwards)

if(FIREBASE_INCLUDE_UNITY)
  # Note: Project name has capital C in 'Code' and output name is lowercase c
  # Breaks on case sensitve file systems thus we have to override it

  set(JAR_RESOLVER_DLL_DIR
    ${GOOGLE_UNITY_JAR_RESOLVER_SOURCE_DIR}/exploded/Assets/ExternalDependencyManager/Editor/
  )

  mono_add_external_dll(google_version_handler_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.VersionHandler.dll"
  )

  unity_pack_cs(google_version_handler_cs
    PACK_PATH "ExternalDependencyManager/Editor"
  )

  mono_add_external_dll(google_version_handlerimpl_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.VersionHandlerImpl_v${FIREBASE_UNITY_JAR_RESOLVER_VERSION}.dll"
  )

  unity_pack_cs(google_version_handlerimpl_cs
    PACK_PATH "ExternalDependencyManager/Editor"
  )

  mono_add_external_dll(google_jar_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.JarResolver_v${FIREBASE_UNITY_JAR_RESOLVER_VERSION}.dll"
  )

  unity_pack_cs(google_jar_resolver_cs
    PACK_PATH "ExternalDependencyManager/Editor"
  )

  mono_add_external_dll(google_ios_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.IOSResolver_v${FIREBASE_UNITY_JAR_RESOLVER_VERSION}.dll"
  )

  unity_pack_cs(google_ios_resolver_cs
    PACK_PATH "ExternalDependencyManager/Editor"
  )

  mono_add_external_dll(google_package_manager_resolver_cs
    "${JAR_RESOLVER_DLL_DIR}/Google.PackageManagerResolver_v${FIREBASE_UNITY_JAR_RESOLVER_VERSION}.dll"
  )

  unity_pack_cs(google_package_manager_resolver_cs
    PACK_PATH "ExternalDependencyManager/Editor"
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
