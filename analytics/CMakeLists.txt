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

# CMake file for the firebase analytics library

include(build_shared)

# Firebase Analytics Swig input files
set(firebase_analytics_swig
  src/swig/analytics.i
)

# Firebase Analytics CSharp files
set(firebase_analytics_src
)

firebase_swig_add_library(firebase_analytics_swig
  NAMESPACE
    Firebase.Analytics
  MODULE
    FirebaseCppAnalytics
  SOURCES
    ${firebase_analytics_swig}
  DEPENDS
    firebase_analytics
)

mono_add_library(firebase_analytics_cs
  MODULE
    Firebase.Analytics
  SOURCES
    ${firebase_analytics_src}
    ${firebase_analytics_swig_gen_src}
  REFERENCES
    unity_compat_cs
    unity_tasks_cs
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_analytics_swig
)

build_firebase_shared(
  analytics
  analytics
  FirebaseCppAnalytics
)

unity_pack_native(firebase_analytics_swig)
unity_pack_cs(firebase_analytics_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(Analytics
    IOS_DEPS
      "Firebase/Analytics"
    ANDROID_DEPS
      ${FIREBASE_ANALYTICS_ANDROID_DEPS}
    ANDROID_SPEC
      "analytics"
  )
endif()

set_property(TARGET firebase_analytics_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
