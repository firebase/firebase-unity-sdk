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

include(ExternalProject)

if(TARGET google_unity_jar_resolver OR NOT DOWNLOAD_GOOGLE_UNITY_JAR_RESOLVER)
  return()
endif()

ExternalProject_Add(
  google_unity_jar_resolver

  GIT_REPOSITORY https://github.com/googlesamples/unity-jar-resolver.git
  GIT_TAG        am-spm_support_test
  GIT_SHALLOW    1

  PREFIX ${PROJECT_BINARY_DIR}

  CONFIGURE_COMMAND ""
  BUILD_COMMAND ""
  INSTALL_COMMAND ""
  TEST_COMMAND ""
)