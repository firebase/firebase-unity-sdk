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

# This file provides support for generating the Dependencies.xml files used by
# the Unity Play Services Resolver plug-in.
# https://github.com/googlesamples/unity-jar-resolver

# The template of the Dependencies file.
set(DEPENDENCIES_TEMPLATE ${CMAKE_CURRENT_LIST_DIR}/dependencies.template)

# The default minimum target SDK to use.
set(DEFAULT_MIN_TARGET_SDK "8.0")


# A function that generates the <module>Dependencies.xml file, used by the
# Unity Jar Resolver.
#
# generate_dependencies_xml(
#   module
#   IOS_DEPS ios_deps...
#   ANDROID_DEPS android_deps...
#   ANDROID_SPEC android_spec...
# )
#
# Args:
#   module: The name of the module, used in the name of the outfile file.
#   IOS_DEPS: The iOS dependencies. A Pod can be comma separated to specify a
#       specific version and minTargetSdk, otherwise default values are
#       used, based on the Firebase Pod version.
#   ANDROID_DEPS: The Android dependencies to include, in the format
#       group:name:version.
#   ANDROID_SPEC: The name to use to link in the module's Android library.
#   SKIP_INSTALL: Option that if set, skips the installation
function(generate_dependencies_xml module)

  set(options SKIP_INSTALL)
  set(single ANDROID_SPEC)
  set(multi IOS_DEPS ANDROID_DEPS)
  # Parse the arguments into GEN_DEPS_IOS_DEPS, etc.
  cmake_parse_arguments(GEN_DEPS "${options}" "${single}" "${multi}" ${ARGN})

  set(MODULE_NAME ${module})
  set(IOS_PODS "")
  set(INDENT "    ")

  foreach(IOS_DEP ${GEN_DEPS_IOS_DEPS})
    string(REPLACE "," ";" DEP_LIST ${IOS_DEP})
    list(GET DEP_LIST 0 DEP_NAME)
    list(LENGTH DEP_LIST DEP_LIST_LENGTH)
    if (${DEP_LIST_LENGTH} GREATER 1)
      list(GET DEP_LIST 1 DEP_VERSION)
    else()
      set(DEP_VERSION "${FIREBASE_IOS_POD_VERSION}")
    endif()
    if (${DEP_LIST_LENGTH} GREATER 2)
      list(GET DEP_LIST 2 TARGET_SDK)
    else()
      set(TARGET_SDK "${DEFAULT_MIN_TARGET_SDK}")
    endif()
    string(CONCAT IOS_PODS ${IOS_PODS}
           "\r\n${INDENT}<iosPod name=\"${DEP_NAME}\""
           " version=\"${DEP_VERSION}\" minTargetSdk=\"${TARGET_SDK}\">"
           "\r\n${INDENT}</iosPod>")
  endforeach(IOS_DEP)

  set(ANDROID_PACKAGES "")
  foreach(ANDROID_DEP ${GEN_DEPS_ANDROID_DEPS})
    string(CONCAT ANDROID_PACKAGES ${ANDROID_PACKAGES}
           "\r\n${INDENT}<androidPackage spec=\"${ANDROID_DEP}\">"
           "\r\n${INDENT}</androidPackage>")
  endforeach(ANDROID_DEP)

  string(CONCAT ANDROID_SPEC "com.google.firebase:firebase-"
                             "${GEN_DEPS_ANDROID_SPEC}-unity:"
                             "${FIREBASE_UNITY_SDK_VERSION}")

  set(OUT_FILE "${PROJECT_BINARY_DIR}/${module}Dependencies.xml")

  configure_file(${DEPENDENCIES_TEMPLATE} ${OUT_FILE}
    @ONLY
    NEWLINE_STYLE CRLF
  )

  if (NOT ${GEN_DEPS_SKIP_INSTALL})
    install(
      FILES "${OUT_FILE}"
      DESTINATION "Firebase/Editor/"
    )
  endif()

endfunction()

# Helper function to test if the above function works. Done in a function so
# that variables can be adjusted without affecting the build.
function(test_generate_xml)

  set(FIREBASE_IOS_POD_VERSION "1.2.3")
  set(FIREBASE_UNITY_SDK_VERSION "4.5.6")
  set(DEFAULT_MIN_TARGET_SDK "7.8")

  generate_dependencies_xml(Test
    IOS_DEPS
      "TestPod"
      "PinnedTestPod,7.8.9,1.0"
    ANDROID_DEPS
      "test.group:test-name:2.3.4"
    ANDROID_SPEC
      "test"
    SKIP_INSTALL
  )

  file(READ "${CMAKE_CURRENT_LIST_DIR}/ExpectedTestDependencies.xml" EXPECTED)
  file(READ "${PROJECT_BINARY_DIR}/TestDependencies.xml" ACTUAL)

  if (NOT ACTUAL STREQUAL EXPECTED)
    message(FATAL_ERROR
        "The generated Dependencies.xml failed to match the expected.\n"
        "EXPECTED:\n"
        "${EXPECTED}"
        "------\n"
        "ACTUAL:\n"
        "${ACTUAL}")
  endif()

endfunction()

# If tests are enabled, run the above test.
if (FIREBASE_UNITY_BUILD_TESTS)
  test_generate_xml()
endif()
