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

# Handles creation of the unity package zip using cpack

# Include CPack multiple times can cause it to break, to guard against that
if(DEFINED CPACK_GENERATOR)
  return()
endif()

include(firebase_unity_version)
include(unity_mono)

# iOS will get the platform name of Darwin if we dont change it here
if(FIREBASE_IOS_BUILD)
  if(FIREBASE_TVOS_VARIANT)
    set(CPACK_SYSTEM_NAME "tvOS")
  else()
    set(CPACK_SYSTEM_NAME "iOS")
  endif()
endif()

set(CPACK_GENERATOR "ZIP")
set(CPACK_PACKAGE_VERSION "${FIREBASE_UNITY_SDK_VERSION}")
set(CPACK_INCLUDE_TOPLEVEL_DIRECTORY 0) # DO NOT pack the root directory.
set(CPACK_ARCHIVE_COMPONENT_INSTALL ON)

include(CPack)

if(FIREBASE_IOS_BUILD)
  if(FIREBASE_TVOS_VARIANT)
    set(UNITY_PACK_NATIVE_DIR "Plugins/tvOS/Firebase")
  else()
    set(UNITY_PACK_NATIVE_DIR "Plugins/iOS/Firebase")
  endif()
elseif("${CMAKE_GENERATOR_PLATFORM}" STREQUAL "x64" OR
       "${CMAKE_GENERATOR_PLATFORM}" STREQUAL "")
  set(UNITY_PACK_NATIVE_DIR "Firebase/Plugins/x86_64")
else()
  set(UNITY_PACK_NATIVE_DIR "Firebase/Plugins/x86_32")
endif()

if(FIREBASE_IOS_BUILD)
  set(UNITY_PACK_DEFAULT_CS_PATH "Firebase/Plugins/iOS/")
else()
  set(UNITY_PACK_DEFAULT_CS_PATH "Firebase/Plugins/")
endif()

define_property(TARGET
  PROPERTY
    UNITY_PACK_INSTALL_PATH
  BRIEF_DOCS
    "install path for target added via unity_pack helper function"
  FULL_DOCS
    "full"
)


# Helper function to skip packing a target i.e. for libraries we cant ship
#
# unity_pack_skip(<name> <installpath>)
#
# Args:
#  name: Target name
#
function(unity_pack_skip name)

  set_target_properties(${name} PROPERTIES
    UNITY_PACK_INSTALL_PATH ":skip:"
  )

endfunction()


# Internal function to recursively pack dependencies of CSharp libraries
#
# unity_pack_references(<name> <installpath>)
#
# Args:
#  name: Target name
#  installpath: Parent install path to copy dependencies too
#
function(unity_pack_references name installpath)

  get_target_property(references ${name} MONO_LIBRARY_REFERENCES)
  get_target_property(type ${name} MONO_LIBRARY_TYPE)

  if("${references}" STREQUAL "references-NOTFOUND")
    return()
  endif()

  foreach(ref ${references})
    get_target_property(dllname ${ref} LIBRARY_OUTPUT_NAME)
    get_target_property(dllpath ${ref} LIBRARY_OUTPUT_DIRECTORY)
    get_target_property(refinstallpath ${ref} UNITY_PACK_INSTALL_PATH)
    get_target_property(type ${ref} MONO_LIBRARY_TYPE)

    # Skip ref's we have already setup install path for
    if(NOT "${refinstallpath}" STREQUAL "refinstallpath-NOTFOUND")
      continue()
    endif()

    # If ref doesnt have a MONO type skip it
    if("${type}" STREQUAL "type-NOTFOUND")
      continue()
    endif()

    unity_pack_cs(${ref}
      PACK_PATH "${installpath}"
    )

  endforeach()
endfunction()


# Packs a CSharp target and dependencies into the unity zip
#
# unity_pack_cs(<name>
#               [PACK_PATH <pack_path>]
#               [PACK_NAME <pack_name>]
#              )
#
# Args:
#  name: Target name
#  pack_path: Path files should be copied too in the unity package
#  pack_name: Rename the files to this in the unity package
#
# Notes:
#  * Default path is "Firebase/Plugins"
#  * If mdb and/or meta files exist with the dll file they will be packed too
#
function(unity_pack_cs name)

  if(NOT FIREBASE_PACK_DOTNET)
    return()
  endif()

  set(multi PACK_PATH PACK_NAME)
  # Parse the arguments into UNITY_MONO_SOURCES and UNITY_MONO_DEPENDS.
  cmake_parse_arguments(UNITY_PACK "" "" "${multi}" ${ARGN})

  get_target_property(dllname ${name} LIBRARY_OUTPUT_NAME)
  get_target_property(dllpath ${name} LIBRARY_OUTPUT_DIRECTORY)

  if("${UNITY_PACK_PACK_PATH}" STREQUAL "")
    set(UNITY_PACK_PACK_PATH ${UNITY_PACK_DEFAULT_CS_PATH})
  endif()

  if("${UNITY_PACK_PACK_NAME}" STREQUAL "")
    set(dllrename ${dllname})
  else()
    set(dllrename ${UNITY_PACK_PACK_NAME})
  endif()

  string(REPLACE ".dll" "" dllbasename ${dllname})
  string(REPLACE ".dll" "" dllbaserename ${dllrename})
  # Dll file
  install(
    FILES "${dllpath}/${dllname}"
    DESTINATION "${UNITY_PACK_PACK_PATH}"
    RENAME ${dllrename}
  )

  unity_pack_optional(${dllpath} ${dllbaserename}
    POTENTIAL_FILES
      ${dllbasename}.meta
      ${dllbasename}.pdb
      ${dllbasename}.mdb
      ${dllname}.meta
      ${dllname}.mdb
      ${dllname}.mdb.meta
      ${dllname}.pdb
      ${dllname}.pdb.meta
  )

  set_target_properties(${name} PROPERTIES
    UNITY_PACK_INSTALL_PATH ${UNITY_PACK_PACK_PATH}
  )

  unity_pack_references(${name} ${UNITY_PACK_PACK_PATH})
endfunction()


# Packs optional files that exist next to a target
#
# unity_pack_optional(<root_dir> <dest_name>
#                     POTENTIAL_FILES <file>...
#                     )
#
# Args:
#  root_dir: Root directory to find files in
#  dest_name: Destination base name of file (i.e. for renaming)
#  file: List of files to check to see if they should be packed
#
function(unity_pack_optional root_dir dest_name)
  set(multi POTENTIAL_FILES)
  # Parse the arguments into UNITY_PACK_file.
  cmake_parse_arguments(UNITY_PACK "" "" "${multi}" ${ARGN})

  get_filename_component(rename ${dest_name} NAME_WE)

  foreach(file ${UNITY_PACK_POTENTIAL_FILES})
    get_filename_component(ext ${file} EXT)
    install(
      FILES "${root_dir}/${file}"
      DESTINATION "${UNITY_PACK_PACK_PATH}"
      RENAME ${rename}${ext}
      OPTIONAL
    )
  endforeach()
endfunction()


# Packs a native target into the unity zip
#
# unity_pack_native(<name>)
#
# Args:
#  name: Target name
#
# Notes:
#  * Libraries are put into build arch folder
#
function(unity_pack_native name)
  if(NOT FIREBASE_PACK_NATIVE)
    return()
  endif()

  message("unity_pack_native pack ${name}")

  get_target_property(target_type ${name} TYPE)

  if(NOT "${target_type}" STREQUAL "SHARED_LIBRARY")
    message("unity_pack_native ${target_type} not equal SHARED_LIBRARY, returned")
    return()
  endif()

  set(dll_dest "${UNITY_PACK_NATIVE_DIR}/")

  if(FIREBASE_IOS_BUILD)
    set(lib_dest "${dll_dest}")
  else()
    # Win32 will try and pack the dll link library. Set path to nothing to
    # have it be ignored
    set(lib_dest "")
  endif()

  # Win32 treats Dll's as 'runtime' other platforms as 'library'
  install(
    TARGETS
      ${name}
    LIBRARY
      DESTINATION "${dll_dest}"
      COMPONENT "runtime"
    RUNTIME
      DESTINATION "${dll_dest}"
      COMPONENT "runtime"
    ARCHIVE
      DESTINATION "${lib_dest}"
      COMPONENT "runtime"
  )

endfunction()

# Packs an external executable file into the unity zip
#
# unity_pack_program(<file>)
#
# Args:
#  file: Path of file to pack
#
# Notes:
#  * Files are put into build arch folder
#
function(unity_pack_program file)
  set(dll_dest "${UNITY_PACK_NATIVE_DIR}/")

  install(
    PROGRAMS
      ${file}
    DESTINATION
      ${dll_dest}
  )
endfunction()

# Packs an external data file into the unity zip
#
# unity_pack_file(<file>
#                 PACK_PATH
#                 RENAME_TO
# )
#
# Args:
#  file: Path of file to pack
#  PACK_PATH: Destination to put the file in.
#  RENAME_TO: Optional parameter to rename the file.
#
# Notes:
#  * Files are put into build arch folder
#
function(unity_pack_file file)
  set(single PACK_PATH RENAME_TO)
  # Parse the arguments into UNITY_PACK_*.
  cmake_parse_arguments(UNITY_PACK "" "${single}" "" ${ARGN})

  if("${UNITY_PACK_PACK_PATH}" STREQUAL "")
    set(UNITY_PACK_PACK_PATH ${UNITY_PACK_DEFAULT_CS_PATH})
  endif()

  # Install the file to the PACK_PATH, renaming it if given RENAME_TO
  if ("${UNITY_PACK_RENAME_TO}" STREQUAL "")
    # Install the file to the PACK_PATH
    install(
      FILES ${file}
      DESTINATION ${UNITY_PACK_PACK_PATH}
    )
  else()
    install(
      FILES ${file}
      DESTINATION ${UNITY_PACK_PACK_PATH}
      RENAME ${UNITY_PACK_RENAME_TO}
    )
  endif()
endfunction()

# Packs a aar file into the unity zip
#
# Args:
#  AAR_FILE: The path to the aar to be packed
#  POM_FILE: The path to the pom to be packed
#  MAVEN_FILE: The path to the maven manifest to be packed
#  ARTIFACT_ID: The id of the artifact, used in the destination path
#  VERSION: The version of the SDK, used in the destination path
function(unity_pack_aar AAR_FILE POM_FILE MAVEN_FILE ARTIFACT_ID VERSION)
  set(basepath "Firebase/m2repository/com/google/firebase")
  set(maven_path "${basepath}/${ARTIFACT_ID}")
  set(versioned_path "${maven_path}/${VERSION}")
  install(
    FILES ${AAR_FILE}
    DESTINATION "${versioned_path}/"
  )
  install(
    FILES ${POM_FILE}
    DESTINATION "${versioned_path}/"
  )
  install(
    FILES ${MAVEN_FILE}
    DESTINATION "${maven_path}/"
  )
endfunction()

# Packs an folder into the unity zip
#
# unity_pack_folder(<folder>
#                 PACK_PATH
# )
#
# Args:
#  folder: Path of folder to pack
#  pack_path: Destination to put the folder in.
#
function(unity_pack_folder folder)
  set(multi PACK_PATH)
  # Parse the arguments into UNITY_MONO_SOURCES and UNITY_MONO_DEPENDS.
  cmake_parse_arguments(UNITY_PACK "" "" "${multi}" ${ARGN})

  if("${UNITY_PACK_PACK_PATH}" STREQUAL "")
    set(UNITY_PACK_PACK_PATH ${UNITY_PACK_DEFAULT_CS_PATH})
  endif()
  
  install(
    DIRECTORY ${folder}
    DESTINATION ${UNITY_PACK_PACK_PATH}
  )
endfunction()

# Packs files intended for documentation, under the documentation component
#
# unity_pack_documentation_sources(<name>
#                 DOCUMENTATION_SOURCES
# )
#
# Args:
#  name: Name of the subfolder to put it under
#  DOCUMENTATION_SOURCES: Files to package
#
function(unity_pack_documentation_sources name)
  set(multi DOCUMENTATION_SOURCES)
  cmake_parse_arguments(UNITY_PACK "" "" "${multi}" ${ARGN})

  install(
    FILES ${UNITY_PACK_DOCUMENTATION_SOURCES}
    DESTINATION ${name}
    COMPONENT documentation
    EXCLUDE_FROM_ALL
  )
endfunction()
