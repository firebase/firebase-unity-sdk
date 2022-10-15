# Copyright 2018 Google
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

# This CMake file is used by the Firebase Unity SDK at configure time to
# download external dependencies used by the libraries. This is done by
# setting up ExternalProjects for each dependency, and then building this
# project, which is done via external_rules.cmake, one directory above.

cmake_minimum_required(VERSION 3.1)
project(firebase_unity_download C CXX)

list(APPEND CMAKE_MODULE_PATH ${CMAKE_CURRENT_LIST_DIR})

include(firebase_cpp_sdk)
include(google_unity_jar_resolver)
include(minijson)
