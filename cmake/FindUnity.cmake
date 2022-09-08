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

# Handles locating required files and executables for unity builds

find_package(Mono REQUIRED)

if (NOT EXISTS "${UNITY_ROOT_DIR}")
  # Make our best attempt to find the latest unity installed on the system.
  # Note that Unity <=2018 include mono 2.x which causes compilation errors.
  # Minimum supported version of mono is 3.5.
  if (EXISTS "$ENV{UNITY_ROOT_DIR}")
    set(UNITY_ROOT_DIR "$ENV{UNITY_ROOT_DIR}")
    message(STATUS "Detected Unity version from env-var 'UNITY_ROOT_DIR': ${UNITY_ROOT_DIR}")
  else()
    if(CMAKE_HOST_WIN32)
        set(editordir "C:/Program Files/Unity/Hub/Editor")
    elseif(CMAKE_HOST_APPLE)
        set(editordir "/Applications/Unity/Hub/Editor")
    elseif(CMAKE_HOST_UNIX) # Linux
        set(editordir "$ENV{HOME}/Unity/Hub/Editor")
    endif()
    file(GLOB unity_versions RELATIVE ${editordir} ${editordir}/*)
    list(SORT unity_versions ORDER DESCENDING)
    list(GET unity_versions 0 latest_unity_version)
    set(UNITY_ROOT_DIR ${editordir}/${latest_unity_version})
    message(STATUS "Auto detected latest Unity version: ${UNITY_ROOT_DIR}")
  endif()
endif()

if(FIREBASE_INCLUDE_UNITY)
  message(STATUS "Using Unity root directory: ${UNITY_ROOT_DIR}")
  set(UNITY_PATH_HUB_HINT "${UNITY_ROOT_DIR}")
  # Platform specific directories to search for various dlls and tools.
  # These directories can also differ between Unity versions (eg: MonoBleedingEdge/bin or Mono/bin)
  if(CMAKE_HOST_WIN32)
    set(UNITY_PATH_SUFFIXES "Editor/Data/Managed" "Editor/Data/MonoBleedingEdge/bin"
                            "Editor/Data/Mono/bin")
  elseif(CMAKE_HOST_APPLE)
    set(UNITY_PATH_SUFFIXES "Unity.app/Contents/Managed" "PlaybackEngines/iOSSupport"
                            "Unity.app/Contents/MonoBleedingEdge/bin" "Unity.app/Contents/Mono/bin")
  elseif(CMAKE_HOST_UNIX) # Linux
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

  if(APPLE AND NOT EXISTS ${UNITY_EDITOR_IOS_XCODE_DLL})
    message(FATAL_ERROR "Fail to find UnityEditor.iOS.Extensions.Xcode.dll. \
      Please install iOS build support from Unity.")
  endif()

  message(STATUS "UNITY_MONO_EXE is ${UNITY_MONO_EXE}")
  message(STATUS "UNITY_CSHARP_BUILD_EXE is ${UNITY_CSHARP_BUILD_EXE}")

  if(NOT EXISTS UNITY_MONO_EXE OR
    NOT EXISTS UNITY_CSHARP_BUILD_EXE)
    set(FIND_TOOL_OPTIONS
      PATHS
        ${UNITY_PATH_HUB_HINT}
      PATH_SUFFIXES
        ${UNITY_PATH_SUFFIXES}
      NO_DEFAULT_PATH
      NO_CMAKE_FIND_ROOT_PATH
    )
    find_program(UNITY_MONO_EXE mono
      ${FIND_TOOL_OPTIONS}
    )
    if(CMAKE_HOST_WIN32)
      set(BUILD_TOOL_EXTENSION ".bat")
    endif()
    find_program(UNITY_CSHARP_BUILD_EXE
      NAMES
        msbuild${BUILD_TOOL_EXTENSION}
        xbuild${BUILD_TOOL_EXTENSION}
      ${FIND_TOOL_OPTIONS}
    )

    # msbuild / xbuild do not work in some versions of Unity.
    if(EXISTS "${UNITY_CSHARP_BUILD_EXE}")
      execute_process(
        COMMAND "${UNITY_CSHARP_BUILD_EXE}" /version
        OUTPUT_VARIABLE VERSION_STRING
        RESULT_VARIABLE RESULT
      )
      if(NOT ${RESULT} EQUAL "0")
        message(STATUS "${UNITY_CSHARP_BUILD_EXE} /version returned ${RESULT}")
        set(UNITY_CSHARP_BUILD_EXE "" CACHE STRING "" FORCE)
      endif()
    endif()

    if(EXISTS "${UNITY_MONO_EXE}" AND
      EXISTS "${UNITY_CSHARP_BUILD_EXE}")
      get_filename_component(UNITY_CSHARP_BUILD_EXE_NAME
          "${UNITY_CSHARP_BUILD_EXE}" NAME
      )

      if(CMAKE_HOST_WIN32)
        # Mono that is packaged with unity has a bug in resgen.bat where it
        # incorrectly forwards arguments. Resgen.bat gets passed an argument
        # like 'a,b' and incorrectly forwards it as 'a b'. Copy the mono folder
        # from unity (less than 100 Mb) and then fix the resgen.bat files to
        # forward arguments properly (using '$*' instead of '$1 $2') thus allowing
        # resx files to compile correctly.
        get_filename_component(UNITY_MONO_EXE_NAME "${UNITY_MONO_EXE}" NAME)
        get_filename_component(MONO_BIN_DIR "${UNITY_CSHARP_BUILD_EXE}" DIRECTORY)

        # Read resgen2.bat to determine whether it's broken.
        file(READ "${MONO_BIN_DIR}/resgen2.bat" RESGEN)
        if("${RESGEN}" MATCHES "resgen\\.exe.* +%1 +%2")
          # Copy Mono.
          set(PATCHED_MONO_DIR
            "${CMAKE_CURRENT_BINARY_DIR}/patched_unity_mono/mono"
          )
          file(COPY "${MONO_BIN_DIR}/.."
            DESTINATION "${PATCHED_MONO_DIR}"
          )

          # Patch resgen.
          string(REGEX REPLACE " +%1 +%2[^\\n]" " %*" RESGEN)
          file(WRITE "${PATCHED_MONO_DIR}/bin/resgen.bat" "${RESGEN}")
          file(WRITE "${PATCHED_MONO_DIR}/bin/resgen2.bat" "${RESGEN}")

          # Redirect to the patched distribution.
          set(UNITY_CSHARP_BUILD_EXE
            "${PATCHED_MONO_DIR}/bin/${UNITY_CSHARP_BUILD_EXE_NAME}"
            CACHE STRING "" FORCE
          )
          set(UNITY_MONO_EXE "${PATCHED_MONO_DIR}/bin/${UNITY_MONO_EXE_NAME}"
            CACHE STRING "" FORCE
          )
        endif()
      endif()
    endif()

    if (NOT EXISTS "${UNITY_MONO_EXE}")
      message(STATUS "UNITY_MONO_EXE:${UNITY_MONO_EXE} not exist.")
    endif()

    if (NOT EXISTS "${UNITY_CSHARP_BUILD_EXE}")
      message(STATUS "UNITY_CSHARP_BUILD_EXE:${UNITY_CSHARP_BUILD_EXE} not exist.")
    endif()

    # If Mono tools aren't found in Unity try using the vanilla Mono distribution.
    if(NOT UNITY_DISABLE_MONO_TOOLS_FALLBACK AND (
        NOT EXISTS "${UNITY_MONO_EXE}" OR
        NOT EXISTS "${UNITY_CSHARP_BUILD_EXE}"))
      message(STATUS "No working Mono tools not found in ${UNITY_PATH_HUB_HINT} "
        "trying the system installation. MONO_EXE: ${MONO_EXE}, MONO_CSHARP_BUILD_EXE: ${MONO_CSHARP_BUILD_EXE}"
      )
      set(UNITY_MONO_EXE "${MONO_EXE}" CACHE STRING "" FORCE)
      set(UNITY_CSHARP_BUILD_EXE "${MONO_CSHARP_BUILD_EXE}"
        CACHE STRING "" FORCE
      )
    endif()
  endif()

  # If Mono tools still aren't found report an error.
  if(NOT EXISTS "${UNITY_MONO_EXE}" OR
    NOT EXISTS "${UNITY_CSHARP_BUILD_EXE}")
    message(FATAL_ERROR "Mono tools not found.")
  endif()

  include(FindPackageHandleStandardArgs)
  find_package_handle_standard_args(
    Unity
    DEFAULT_MSG
    UNITY_ENGINE_DLL
    UNITY_EDITOR_DLL
    UNITY_MONO_EXE
    UNITY_CSHARP_BUILD_EXE
  )
  mark_as_advanced(
    UNITY_ENGINE_DLL
    UNITY_EDITOR_DLL
    UNITY_MONO_EXE
    UNITY_CSHARP_BUILD_EXE
  )
endif()



