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

# Logic for building the Firebase aar file and maven logic used by the Android
# build.

include(build_aar)

# Calls the python script to build the aar file.
#
# Args:
#  LIBRARY_NAME: The name of the library to build the aar for.
#  ARTIFACT_NAME: The name to use in the artifact id.
#  LIBRARY_TARGET: The target that outputs the shared libary to include.
#  PROGUARD_TARGET: The target that outputs the proguard file.
#
# Optional Args:
#  ANDROID_MANIFEST: The custom AndroidManifest file to include.
#  CLASSES_JAR: The custom classes.jar file to include.
function(build_firebase_aar LIBRARY_NAME ARTIFACT_NAME LIBRARY_TARGET
                            PROGUARD_TARGET)
  # Parse the additional arguments
  set(single ANDROID_MANIFEST CLASSES_JAR)
  cmake_parse_arguments(FIREBASE_AAR_ARGS "" "${single}" "" ${ARGN})

  # Set the variables expected by the templates
  set(ARTIFACT_ID "firebase-${ARTIFACT_NAME}-unity")
  set(VERSION "${FIREBASE_UNITY_SDK_VERSION}")

  build_aar(${LIBRARY_NAME}
    ${LIBRARY_TARGET}
    ${PROGUARD_TARGET}
    ${ARTIFACT_ID}
    ${VERSION}
    ANDROID_MANIFEST
      ${FIREBASE_AAR_ARGS_ANDROID_MANIFEST}
    CLASSES_JAR
      ${FIREBASE_AAR_ARGS_CLASSES_JAR}
    MANIFEST_PACKAGE_NAME
      "com.google.firebase.unity.${LIBRARY_NAME}"
  )
endfunction()
