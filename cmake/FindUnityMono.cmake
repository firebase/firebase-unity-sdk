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

# Handles locating required files and executables for unity mono builds

if(EXISTS "${MONO_DIR}")
  find_program(MONO_EXE mono
          PATHS ${MONO_DIR} ${MONO_DIR}/lib/mono
          PATH_SUFFIXES bin
          NO_DEFAULT_PATH)
  find_program(MONO_XBUILD_EXE xbuild
          PATHS ${MONO_DIR} ${MONO_DIR}/lib/mono
          PATH_SUFFIXES bin
          NO_DEFAULT_PATH)
endif()

if(NOT EXISTS "${MONO_EXE}")
  find_program(MONO_EXE mono)
  find_program(MONO_XBUILD_EXE xbuild)
endif()

if(EXISTS "${MONO_EXE}")
  execute_process(
    COMMAND ${MONO_EXE} -V
    OUTPUT_VARIABLE mono_version_str
  )
  string(REGEX MATCH "([0-9]*)([.])([0-9]*)([.]*)([0-9]*)"
      mono_version_tmp "${mono_version_str}")
  set(MONO_VERSION ${mono_version_tmp} CACHE STRING "Mono version")
else()
  message(STATUS "Cannot find mono.")
endif()

if (NOT EXISTS "${UNITY_ROOT_DIR}")
  # Make our best attempt to find the latest unity installed on the system.
  # Note that Unity <=2018 include mono 2.x which causes compilation errors.
  # Minimum supported version of mono is 3.5.
  if(MSVC)
    set(editordir "C:/Program Files/Unity/Hub/Editor")
  elseif(APPLE)
    set(editordir "/Applications/Unity/Hub/Editor")
  elseif(UNIX) # Linux
    set(editordir "$ENV{HOME}/Unity/Hub/Editor")
  endif()
  file(GLOB unity_versions RELATIVE ${editordir} ${editordir}/*)
  list(SORT unity_versions ORDER DESCENDING)
  list(GET unity_versions 0 latest_unity_version)
  set(UNITY_ROOT_DIR ${editordir}/${latest_unity_version})
  message(STATUS "Auto detected latest Unity version: ${UNITY_ROOT_DIR}")
endif()

if(FIREBASE_INCLUDE_UNITY)
  message(STATUS "Using Unity root directory: ${UNITY_ROOT_DIR}")
  set(UNITY_PATH_HUB_HINT "${UNITY_ROOT_DIR}")
  # Platform specific directories to search for various dlls and tools.
  # These directories can also differ between Unity versions (eg: MonoBleedingEdge/bin or Mono/bin)
  if(MSVC)
    set(UNITY_PATH_SUFFIXES "Editor/Data/Managed")
  elseif(APPLE)
    set(UNITY_PATH_SUFFIXES "Unity.app/Contents/Managed" "PlaybackEngines/iOSSupport"
                            "Unity.app/Contents/MonoBleedingEdge/bin" "Unity.app/Contents/Mono/bin")
  elseif(UNIX) # Linux
    set(UNITY_PATH_SUFFIXES "Editor/Data/Managed" "Editor/Data/PlaybackEngines/iOSSupport"
                            "Editor/Data/MonoBleedingEdge/bin" "Editor/Data/Mono/bin")
  endif()

  find_file(UNITY_ENGINE_DLL
    NAMES UnityEngine.dll
    PATHS ${UNITY_PATH_HUB_HINT}
    PATH_SUFFIXES ${UNITY_PATH_SUFFIXES}
    NO_DEFAULT_PATH
    NO_CMAKE_FIND_ROOT_PATH
  )

  find_file(UNITY_EDITOR_DLL
    NAMES UnityEditor.dll
    PATHS ${UNITY_PATH_HUB_HINT}
    PATH_SUFFIXES ${UNITY_PATH_SUFFIXES}
    NO_DEFAULT_PATH
    NO_CMAKE_FIND_ROOT_PATH
  )

  find_file(UNITY_EDITOR_IOS_XCODE_DLL
    NAMES UnityEditor.iOS.Extensions.Xcode.dll
    PATHS ${UNITY_PATH_HUB_HINT}
    PATH_SUFFIXES ${UNITY_PATH_SUFFIXES}
    NO_DEFAULT_PATH
    NO_CMAKE_FIND_ROOT_PATH
  )

  find_program(UNITY_XBUILD_EXE xbuild
    PATHS ${UNITY_PATH_HUB_HINT}
    PATH_SUFFIXES ${UNITY_PATH_SUFFIXES}
    NO_DEFAULT_PATH
    NO_CMAKE_FIND_ROOT_PATH
  )

  if(NOT EXISTS ${UNITY_ENGINE_DLL} OR NOT EXISTS ${UNITY_EDITOR_DLL})
    message(FATAL_ERROR "Fail to find UnityEngine.dll or UnityEditor.dll. \
      Please set valid path with -DUNITY_ROOT_DIR or check that Unity \
      is installed in system default place ${UNITY_PATH_HUB_HINT}. \
      See the readme.md for more information.")
  endif()

  if(NOT EXISTS ${UNITY_EDITOR_IOS_XCODE_DLL})
    message(FATAL_ERROR "Fail to find UnityEditor.iOS.Extensions.Xcode.dll. \
      Please install iOS build support from Unity.")
  endif()
endif()

if(EXISTS ${UNITY_XBUILD_EXE})
  message("Using unity xbuild instead of mono xbuild")

  # Mono that is packaged with unity has a bug in resgen.bat where it incorrect
  # forwards arguments. Resgen.bat gets passed an argument like 'a,b' and
  # incorrectly forwards it as 'a b'. Copy the mono folder from unity
  # (less than 100 Mb) and then fix the resgen.bat files to forward arguments
  # properly (using '$*' instead of '$1 $2') thus allowing resx files to compile
  # correctly.
  if(MSVC)
    get_filename_component(mono_dir ${UNITY_XBUILD_EXE} DIRECTORY)

    set(dest_mono_dir "${CMAKE_CURRENT_BINARY_DIR}/external/mono")
    file(COPY ${mono_dir}/../../mono DESTINATION ${dest_mono_dir}/..)

    set(resgen "@echo off\n\"%~dp0cli.bat\" %MONO_OPTIONS% \"%~dp0..\\lib\\mono\\2.0\\resgen.exe\" %* \nexit \\b %ERRORLEVEL%")

    file(WRITE ${dest_mono_dir}/bin/resgen.bat ${resgen})
    file(WRITE ${dest_mono_dir}/bin/resgen2.bat ${resgen})
    set(XBUILD_EXE ${dest_mono_dir}/bin/xbuild CACHE STRING "")
  else()
    set(XBUILD_EXE ${UNITY_XBUILD_EXE} CACHE STRING "")
  endif()
else()
  set(XBUILD_EXE ${MONO_XBUILD_EXE} CACHE STRING "")
endif()

include(FindPackageHandleStandardArgs)
find_package_handle_standard_args(MONO DEFAULT_MSG MONO_EXE MONO_XBUILD_EXE UNITY_XBUILD_EXE)
mark_as_advanced(MONO_EXE MONO_XBUILD_EXE UNITY_XBUILD_EXE XBUILD_EXE MONO_VERSION)
