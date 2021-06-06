# Copyright 2021 Google
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

# Locates the Mono launcher and build tool.
#
# Optional variable arguments for this module:
#  MONO_DIR: Variable to specify a search path for Mono.
#  MONO_DISABLE_VISUAL_STUDIO_FALLBACK: Do not use Visual Studio's msbuild
#   as a fallback on Windows when using the Visual Studio project generator.
#
# Cache variables set by this module:
#  MONO_EXE: Location of the Mono interpreter executable.
#  MONO_VERSION: Version of the detected Mono installation.
#  MONO_CSHARP_BUILD_EXE: Location of the tool (e.g msbuild or xbuild) that
#    can build .csproj projects.

# If MONO_DIR is specified, only search that path.
set(FIND_MONO_OPTIONS "")
if(EXISTS "${MONO_DIR}")
  set(FIND_MONO_OPTIONS
    PATHS
      "${MONO_DIR}"
      "${MONO_DIR}/lib/mono"
    PATH_SUFFIXES
      bin
    NO_DEFAULT_PATH
  )
endif()

# Search for Mono tools.
find_program(MONO_EXE mono
  ${FIND_MONO_OPTIONS}
)
find_program(MONO_CSHARP_BUILD_EXE
  NAMES
    msbuild
    xbuild
  ${FIND_MONO_OPTIONS}
)

if(CMAKE_HOST_WIN32 AND
   NOT MONO_DISABLE_VISUAL_STUDIO_FALLBACK AND
   EXISTS "${CMAKE_VS_MSBUILD_COMMAND}")
  if(NOT MONO_CSHARP_BUILD_EXE)
    # If Mono's build tool isn't found, fallback to Visual Studio.
    message(STATUS "Mono installation not found, trying to fallback "
      "to Visual Studio's msbuild ${CMAKE_VS_MSBUILD_COMMAND}."
    )
    set(MONO_CSHARP_BUILD_EXE "${CMAKE_VS_MSBUILD_COMMAND}"
      CACHE STRING "" FORCE
    )
  endif()
  if(NOT MONO_EXE)
    # Just use cmake to launch the C# executable which should run on Windows if
    # the .NET framework is installed.
    # NOTE: This does not use cmd as CTest passes a path with / path separators
    # to CreateProcess() causing cmd to fail to start.
    set(MONO_EXE "${CMAKE_COMMAND};-E;env" CACHE STRING "" FORCE)
  endif()
endif()

# If the mono executable is found, and retrieve the version.
if(MONO_EXE)
  if(EXISTS "${MONO_EXE}")
    execute_process(
      COMMAND ${MONO_EXE} -V
      OUTPUT_VARIABLE MONO_VERSION_STRING
      RESULT_VARIABLE RESULT
    )
    if(NOT ${RESULT} EQUAL "0")
      message(FATAL_ERROR "${MONO_EXE} -V returned ${RESULT}.")
    endif()
    string(REGEX MATCH "([0-9]*)([.])([0-9]*)([.]*)([0-9]*)"
        MONO_VERSION_MATCH "${MONO_VERSION_STRING}")
    set(MONO_VERSION "${MONO_VERSION_MATCH}" CACHE STRING "Mono version.")
  else()
    set(MONO_VERSION "0.0.0" CACHE STRING "Mono version.")
  endif()
endif()

if(NOT MONO_EXE OR NOT MONO_CSHARP_BUILD_EXE)
  message(FATAL_ERROR "Cannot find mono and msbuild/xbuild executables.")
endif()

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(
  Mono
  DEFAULT_MSG
  MONO_EXE
  MONO_CSHARP_BUILD_EXE
  MONO_VERSION
)
mark_as_advanced(
  MONO_EXE
  MONO_CSHARP_BUILD_EXE
  MONO_VERSION
)
