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

# This file provides support for compiling CSharp source code into libraries

find_package(Unity REQUIRED)

set(MONO_PROJ_TEMPLATE ${CMAKE_CURRENT_LIST_DIR}/project.template)

define_property(TARGET
  PROPERTY
    MONO_LIBRARY_TYPE
  BRIEF_DOCS
    "Type of library/project to build [csproj|dll|exe]"
  FULL_DOCS
    "full"
)

define_property(TARGET
  PROPERTY
    MONO_LIBRARY_REFERENCES
  BRIEF_DOCS
    "Targets that this library depends on"
  FULL_DOCS
    "full"
)

# Adds an existing CSharp project as a target that can be used as a dependency
# and also is compiled into a library
#
# mono_add_external_library(<name> <project_path>
#                          [OUTPUT_NAME <output_name>]
#                          [SOURCES <sources>...]
#                          [XBUILD_EXE] <xbuild>]
#                          )
#
# Args:
#  name: Target name
#  project_path: Path to the .csproj file
#  output_name: Name of the dll (with out extension) created by this project
#  sources: Source files to add to target to show up in project generators like
#           visual studio solution
#  xbuild: Path to xbuild executable. Defaults to XBUILD_EXE var
#
# Notes:
#  * The project file name is expected to match the output library name.
#    For example: project_path a/Firebase.App.csproj is expected to output
#    Firebase.App.dll
#
macro(mono_add_external_library name projpath)

  set(single OUTPUT_NAME XBUILD_EXE)
  set(multi SOURCES)

  # Parse the arguments into UNITY_MONO_SOURCES and UNITY_MONO_DEPENDS.
  cmake_parse_arguments(UNITY_MONO "" "${single}" "${multi}" ${ARGN})

  if("${UNITY_CSHARP_BUILD_EXE}" STREQUAL "")
    set(UNITY_CSHARP_BUILD_EXE "${MONO_CSHARP_BUILD_EXE}")
  endif()

  get_filename_component(DLL_NAME ${projpath} NAME)
  string(REPLACE ".csproj" "" DLL_NAME ${DLL_NAME})

  if(NOT "${UNITY_MONO_OUTPUT_NAME}" STREQUAL "")
    set(DLL_NAME ${UNITY_MONO_OUTPUT_NAME})
  endif()

  set(outdir ${CMAKE_CURRENT_BINARY_DIR}/bin/${DLL_NAME})
  set(objdir ${CMAKE_CURRENT_BINARY_DIR}/obj/${DLL_NAME})

  add_custom_target(${name} ALL
    DEPENDS
      ${outdir}/${DLL_NAME}.dll
    SOURCES
      ${projpath}
      "${UNITY_MONO_SOURCES}"
  )

  if(NOT FIREBASE_64BIT_BUILD)
    set(platform "x86")
  else()
    set(platform "AnyCPU")
  endif()

  if(IS_ABSOLUTE ${projpath})
    set(absprojpath ${projpath})
  else()
    set(absprojpath ${CMAKE_CURRENT_LIST_DIR}/${projpath})
  endif()

  set(CSHARP_BUILD_EXE "${UNITY_CSHARP_BUILD_EXE}")
  if(MSVC)
    set(CSHARP_BUILD_EXE "${MONO_CSHARP_BUILD_EXE}")
  endif()

  add_custom_command(
    COMMAND
      ${CSHARP_BUILD_EXE}
      /p:AllowUnsafeBlocks=true
      /p:Configuration=Release
      /p:OutDir="${outdir}/"
      /p:OutputPath="bin\\Release"
      /p:Platform=${platform}
      /p:DebugSymbols=true
      /p:DebugType=Full
      ${absprojpath}
    DEPENDS
      ${projpath}
    OUTPUT
      ${outdir}/${DLL_NAME}.dll
    COMMENT
      "Building csproj for ${DLL_NAME}.dll with ${CSHARP_BUILD_EXE}"
  )

  set_target_properties(${name} PROPERTIES
    LIBRARY_OUTPUT_NAME
      ${DLL_NAME}.dll
  )

  set_target_properties(${name} PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY
      ${outdir}
  )

  set_target_properties(${name} PROPERTIES MONO_LIBRARY_TYPE "csproj")

endmacro()

# Adds an existing shared object as a target that can be used as a dependincy
#
# Args:
#  name: Target name
#  dllpath: Path to the dll file
#
macro(mono_add_external_dll name dllpath)

  if(FIREBASE_GENERATE_SWIG_ONLY)
    add_custom_target(${name})
    return()
  endif()

  if(NOT EXISTS ${dllpath})
    message(FATAL_ERROR "Expected ${name} file to already exist for mono_add_external_dll(${dllpath})")
  endif()

  get_filename_component(DLL_NAME ${dllpath} NAME)
  get_filename_component(DLL_DIR ${dllpath} DIRECTORY)

  if(IS_ABSOLUTE ${DLL_DIR})
    set(absdlldir ${DLL_DIR})
  else()
    set(absdlldir ${CMAKE_CURRENT_SOURCE_DIR}/${DLL_DIR})
  endif()

  add_custom_target(${name} ALL DEPENDS ${dllpath})

  set_target_properties(${name} PROPERTIES
    LIBRARY_OUTPUT_NAME
      ${DLL_NAME}
  )

  set_target_properties(${name} PROPERTIES
    LIBRARY_OUTPUT_DIRECTORY
      ${absdlldir}
  )

  set_target_properties(${name} PROPERTIES MONO_LIBRARY_TYPE "dll")
  set_target_properties(${name} PROPERTIES FOLDER "Mono External")
endmacro()

# Creates a target that can be used as a dependincy and also compiles into a
# shared library
#
#  mono_add_library(<name>
#                     MODULE <module>
#                     SOURCES <src_files>...
#                     REFERENCES <references>...
#                     [SYSTEM_REFERENCES <sys_references>...]
#                     [DEPENDS <dep_targets>...]
#                     [PROJECT_GUID <guid>]
#                     [FRAMEWORK_VERSION <framework_version>]
#                     [DEFINES <defines>...]
#                     [ASSEMBLY_NAME <assembly_name>]
#                     [XBUILD_EXE] <xbuild>
#                    )
#
# Args:
#  name: Target name
#  module: CSharp library output name
#  src_files: CSharp source code files
#  references: CMake CSharp libraries this depends on
#  sys_references: System CSharp libraries this depends on
#  dep_targets: Targets this target depends on and need to be linked/built first
#  guid: MSBuild project guid (for use with solution files)
#  framework_version: .net framework version, defaults to 4.5
#  defines: Extra defines to add to the project file
#  assembly_name: Name of the output assembly. Defaults to module
#  xbuild: Path to xbuild executable. Defaults to XBUILD_EXE global var
#
# Notes:
#  * This creates a .csproj file with the input data and then calls
#    mono_add_external_library
#  * If no guid is provided a new guid will be generated each time
#
macro(mono_add_library name)
  if (NOT FIREBASE_GENERATE_SWIG_ONLY)
    mono_add_internal(${name} "Library" ${ARGN})
    set_target_properties(${name} PROPERTIES FOLDER "Mono Dll")
  endif()
endmacro()

# Creates and executable library. See mono_add_library for args
macro(mono_add_executable name)
  if (NOT FIREBASE_GENERATE_SWIG_ONLY)
    mono_add_internal(${name} "Exe" ${ARGN})
    set_target_properties(${name} PROPERTIES FOLDER "Mono Bin")
  endif()
endmacro()

# Internal helper function for mono_add_library and mono_add_executable
macro(mono_add_internal name output_type)

  set(multi
    MODULE
    SOURCES
    REFERENCES
    DEPENDS
    PROJECT_GUID
    FRAMEWORK_VERSION
    SYSTEM_REFERENCES
    DEFINES
    ASSEMBLY_NAME
    XBUILD_EXE
  )

  # Parse the arguments into UNITY_MONO_SOURCES and UNITY_MONO_DEPENDS.
  cmake_parse_arguments(UNITY_MONO "" "" "${multi}" ${ARGN})

  set(MONO_PROJ_FILE ${CMAKE_CURRENT_BINARY_DIR}/${UNITY_MONO_MODULE}.csproj)

  if ("${UNITY_MONO_PROJECT_GUID}" STREQUAL "")
    string(UUID UNITY_MONO_PROJECT_GUID
      NAMESPACE "efe0ef8c-b556-437b-8e72-8cc4afda60f5"  # RNG FTW
      NAME ${name}
      TYPE MD5
    )
  endif()

  if ("${UNITY_MONO_FRAMEWORK_VERSION}" STREQUAL "")
    set(UNITY_MONO_FRAMEWORK_VERSION "4.5")
  endif()

  if ("${UNITY_MONO_ASSEMBLY_NAME}" STREQUAL "")
    set(UNITY_MONO_ASSEMBLY_NAME ${UNITY_MONO_MODULE})
  endif()

  set(VAR_PROJECT_GUID ${UNITY_MONO_PROJECT_GUID})
  set(VAR_ROOT_NAMESPACE ${UNITY_MONO_MODULE})
  set(VAR_ASSEMBLY_NAME ${UNITY_MONO_ASSEMBLY_NAME})
  set(VAR_FRAMEWORK_VERSION ${UNITY_MONO_FRAMEWORK_VERSION})
  set(VAR_REFERENCE "")
  set(VAR_COMPILE "")
  set(VAR_DEFINES "TRACE")
  set(VAR_OUTPUT_TYPE ${output_type})

  string(APPEND VAR_REFERENCE "    <Reference Include=\"netstandard\" />\n")
  string(APPEND VAR_REFERENCE "    <Reference Include=\"System\" />\n")
  string(APPEND VAR_REFERENCE "    <Reference Include=\"System.Core\" />\n")

  if(NOT "${UNITY_MONO_SYSTEM_REFERENCES}" STREQUAL "")
    foreach(ref ${UNITY_MONO_SYSTEM_REFERENCES})
      string(APPEND VAR_REFERENCE "    <Reference Include=\"${ref}\" />\n")
    endforeach()
  endif()

  foreach(ref ${UNITY_MONO_REFERENCES})
    get_target_property(dllname ${ref} LIBRARY_OUTPUT_NAME)
    get_target_property(dllpath ${ref} LIBRARY_OUTPUT_DIRECTORY)

    set(includename ${dllname})
    string(REPLACE ".dll" "" includename ${dllname})
    string(REPLACE "_" ";" includename ${includename})
	list(GET includename 0 includename)

    string(APPEND VAR_REFERENCE "    <!-- ${ref} -->\n")
    string(APPEND VAR_REFERENCE "    <Reference Include=\"${includename}\" >\n")
    string(APPEND VAR_REFERENCE "      <SpecificVersion>False</SpecificVersion>\n")
    string(APPEND VAR_REFERENCE "      <HintPath>${dllpath}/${dllname}</HintPath>\n")
    string(APPEND VAR_REFERENCE "    </Reference>\n")
  endforeach()

  string(REPLACE "/" "\\" VAR_CURRENT_DIR ${CMAKE_CURRENT_SOURCE_DIR})

  foreach(file ${UNITY_MONO_SOURCES})
    string(REPLACE "/" "\\" VAR_FILE ${file})

    if(NOT IS_ABSOLUTE ${file})
      set(VAR_FILE "${VAR_CURRENT_DIR}\\${VAR_FILE}")
    endif()

    if(${file} MATCHES "\.resx$")
      string(REPLACE ".resx" ".Designer.cs" temp ${VAR_FILE})
      get_filename_component(tempname ${temp} NAME)

      string(APPEND VAR_COMPILE "    <EmbeddedResource Include=\"${VAR_FILE}\">\n")
      string(APPEND VAR_COMPILE "      <Generator>ResXFileCodeGenerator</Generator>\n")
      string(APPEND VAR_COMPILE "      <LastGenOutput>${tempname}</LastGenOutput>\n")
      string(APPEND VAR_COMPILE "    </EmbeddedResource>\n")
    elseif(${file} MATCHES "\.Designer\.cs$")
      string(REPLACE ".Designer.cs" ".resx" temp ${VAR_FILE})
      get_filename_component(tempname ${temp} NAME)

      string(APPEND VAR_COMPILE "    <Compile Include=\"${VAR_FILE}\">\n")
      string(APPEND VAR_COMPILE "      <AutoGen>True</AutoGen>\n")
      string(APPEND VAR_COMPILE "      <DesignTime>True</DesignTime>\n")
      string(APPEND VAR_COMPILE "      <DependentUpon>${tempname}</DependentUpon>\n")
      string(APPEND VAR_COMPILE "    </Compile>\n")
    elseif(${file} MATCHES "\.cs$")
      string(APPEND VAR_COMPILE "    <Compile Include=\"${VAR_FILE}\" />\n")
    else()
      # Assume all the non-cs file is an embedded resource.
      string(APPEND VAR_COMPILE "    <EmbeddedResource Include=\"${VAR_FILE}\" />\n")
    endif()
  endforeach()

  if(FIREBASE_INCLUDE_UNITY)
    string(APPEND VAR_DEFINES ";UNITY")
  endif()

  if(NOT "${UNITY_MONO_DEFINES}" STREQUAL "")
    foreach(define ${UNITY_MONO_DEFINES})
      string(APPEND VAR_DEFINES ";${define}")
    endforeach()
  endif()

  configure_file(${MONO_PROJ_TEMPLATE} ${MONO_PROJ_FILE}
    @ONLY
    NEWLINE_STYLE CRLF
  )

  unset(VAR_PROJECT_GUID)
  unset(VAR_ROOT_NAMESPACE)
  unset(VAR_ASSEMBLY_NAME)
  unset(VAR_FRAMEWORK_VERSION)
  unset(VAR_COMPILE)
  unset(VAR_DEFINES)
  unset(VAR_OUTPUT_TYPE)

  mono_add_external_library(${name} ${MONO_PROJ_FILE}
    OUTPUT_NAME
      ${UNITY_MONO_ASSEMBLY_NAME}
    SOURCES
      ${UNITY_MONO_SOURCES}
    XBUILD_EXE
      ${UNITY_CSHARP_BUILD_EXE}
  )

  if(UNITY_MONO_DEPENDS)
    add_dependencies(${name} ${UNITY_MONO_DEPENDS})
  endif()

  foreach(ref ${UNITY_MONO_REFERENCES})
    add_dependencies(${name} ${ref})
  endforeach()

  set_target_properties(${name} PROPERTIES
    MONO_LIBRARY_REFERENCES "${UNITY_MONO_REFERENCES}"
  )

endmacro()
