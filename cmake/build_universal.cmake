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

# Logic for building the universal library version of App.

include(build_firebase_aar)

# The template of the project list file.
set(PROJECT_LIST_TEMPLATE ${CMAKE_CURRENT_LIST_DIR}/project_list.template)

# Build the Universal shared library that contains all of the provided
# libraries to link with.
#
# Args:
#  TARGET_LINK_LIB_NAMES: The libraries to link into the universal library.
#  PROJECT_LIST_HEADER: The variable that has the define to include in the
#                       generated header file, used by the export_fix logic.
function(build_uni TARGET_LINK_LIB_NAMES PROJECT_LIST_HEADER_VARIABLE)
  set(UNI_LIBRARY_LIST_HEADER ${CMAKE_CURRENT_BINARY_DIR}/generated/firebase_project_list.h)
  string(JOIN " \\\n" PROJECT_LIST_HEADER_RAW ${${PROJECT_LIST_HEADER_VARIABLE}})

  # Generate the file that includes the defines the macros used by the export
  # fix logic.
  configure_file(${PROJECT_LIST_TEMPLATE} ${UNI_LIBRARY_LIST_HEADER}
    @ONLY
    NEWLINE_STYLE CRLF
  )

  if(ANDROID)
    set(platform_SRC
        app/src/unity_main.cpp)
  else()
    set(platform_SRC)
  endif()
  add_library(firebase_app_uni SHARED
              app/src/export_fix.cc
              "${platform_SRC}")

  # Use underscore in file name.
  set_target_properties(firebase_app_uni
    PROPERTIES
      OUTPUT_NAME "${FIREBASE_APP_UNI_VERSIONED}"
  )

  if(ANDROID)
    set_target_properties(firebase_app_uni PROPERTIES PREFIX "lib")
  else()
    # Do not use the default "lib" prefix
    set_target_properties(firebase_app_uni PROPERTIES PREFIX "")

    if(APPLE)
      # Other approach like set target link or set BUNDLE property fail due to
      # trying to treat the bundle as a directory instead of a file.
      # Only override suffix produces a single file bundle.
      set_target_properties(firebase_app_uni PROPERTIES SUFFIX ".bundle")
    endif()
  endif()

  target_include_directories(firebase_app_uni
    PRIVATE
      ${CMAKE_CURRENT_BINARY_DIR}/generated
      ${FIREBASE_CPP_SDK_DIR}
  )

  target_link_libraries(firebase_app_uni
    ${TARGET_LINK_LIB_NAMES}
    ${FIREBASE_SYSTEM_DEPS}
  )

  if(ANDROID)
    target_link_options(firebase_app_uni
      PRIVATE
        "-llog"
        "-Wl,-z,defs"
        "-Wl,--no-undefined"
        # Link against the static libc++, which is the default done by Gradle.
        "-static-libstdc++"
    )
    add_custom_command(TARGET firebase_app_uni POST_BUILD
      COMMAND "${ANDROID_TOOLCHAIN_PREFIX}strip" -g -S -d --strip-debug --verbose
      "lib${FIREBASE_APP_UNI_VERSIONED}.so"
      COMMENT "Strip debug symbols done on final binary. lib${FIREBASE_APP_UNI_VERSIONED}.so")
  endif()

  unity_pack_native(firebase_app_uni)

  set_property(TARGET firebase_app_uni
    PROPERTY FOLDER
    "Firebase ${FIREBASE_PLATFORM_NAME}"
  )

  if(ANDROID)
    build_firebase_aar(
      app
      app
      firebase_app_uni
      FIREBASE_CPP_APP_PROGUARD
    )
  endif()
endfunction()