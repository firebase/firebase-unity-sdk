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

# Logic for building the aar file and maven logic used by the Android build.

include(unity_pack)

# The template of the pom file.
set(POM_TEMPLATE ${CMAKE_CURRENT_LIST_DIR}/pom.template)
# The template of the maven manifest file.
set(MAVEN_TEMPLATE ${CMAKE_CURRENT_LIST_DIR}/maven.template)

# Calls the python script to build the aar file.
#
# Args:
#  LIBRARY_NAME: The name of the library to build the aar for.
#  LIBRARY_TARGET: The target that outputs the shared libary to include.
#  PROGUARD_TARGET: The target that outputs the proguard file.
#  ARTIFACT_ID: The artifact id to use with the generated files.
#  VERSION: The version number to tag the generated files with.
function(build_aar LIBRARY_NAME LIBRARY_TARGET PROGUARD_TARGET
                   ARTIFACT_ID VERSION)
  set(AAR_NAME "${ARTIFACT_ID}-${VERSION}")
  set(OUTPUT_AAR "${CMAKE_CURRENT_BINARY_DIR}/${AAR_NAME}.srcaar")

  add_custom_command(
    OUTPUT "${OUTPUT_AAR}"
    COMMAND python "${FIREBASE_SOURCE_DIR}/aar_builder/build_aar.py"
      "--output_file=${OUTPUT_AAR}"
      "--library_file=$<TARGET_FILE:${LIBRARY_TARGET}>"
      "--architecture=${ANDROID_ABI}"
      "--proguard_file=${${PROGUARD_TARGET}}"
    DEPENDS
      "${LIBRARY_TARGET}"
      "${PROGUARD_TARGET}"
    COMMENT "Generating the aar file for ${LIBRARY_TARGET}"
  )

  # Add a custom target to build the aar, and include it as part of ALL targets.
  add_custom_target(
    "${LIBRARY_TARGET}_aar"
    ALL
    DEPENDS ${OUTPUT_AAR}
  )

  set(OUTPUT_POM ${CMAKE_CURRENT_BINARY_DIR}/${AAR_NAME}.pom)
  configure_file(${POM_TEMPLATE} ${OUTPUT_POM}
    @ONLY
    NEWLINE_STYLE CRLF
  )

  set(OUTPUT_MAVEN ${CMAKE_CURRENT_BINARY_DIR}/maven-metadata.xml)
  configure_file(${MAVEN_TEMPLATE} ${OUTPUT_MAVEN}
    @ONLY
    NEWLINE_STYLE CRLF
  )

  # Add it to the pack logic
  unity_pack_aar(
    ${OUTPUT_AAR}
    ${OUTPUT_POM}
    ${OUTPUT_MAVEN}
    ${ARTIFACT_ID}
    ${VERSION}
  )
endfunction()
