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

# CMake file for the firebase dynamic_links library

include(build_shared)

# Firebase DynamicLinks Swig input files
set(firebase_dynamic_links_swig
  src/swig/dynamic_links.i
)

# Firebase DynamicLinks CSharp files
set(firebase_dynamic_links_src
  src/AndroidParameters.cs
  src/DynamicLinkComponents.cs
  src/DynamicLinkOptions.cs
  src/DynamicLinkPathLength.cs
  src/DynamicLinks.cs
  src/GoogleAnalyticsParameters.cs
  src/IOSParameters.cs
  src/ITunesConnectAnalyticsParameters.cs
  src/ReceivedDynamicLink.cs
  src/ReceivedDynamicLinkEventArgs.cs
  src/ShortDynamicLink.cs
  src/SocialMetaTagParameters.cs
)

firebase_swig_add_library(firebase_dynamic_links_swig
  NAMESPACE
    Firebase.DynamicLinks
  MODULE
    FirebaseCppDynamicLinks
  SOURCES
    ${firebase_dynamic_links_swig}
  DEPENDS
    firebase_dynamic_links
)

unity_pack_documentation_sources(dynamic_links
  DOCUMENTATION_SOURCES
    ${firebase_dynamic_links_src}
    ${firebase_dynamic_links_swig_gen_src}
)
if (FIREBASE_GENERATE_SWIG_ONLY)
  unity_pack_documentation_sources(dynamic_links
    DOCUMENTATION_SOURCES
      ${firebase_dynamic_links_swig_gen_cpp_src}
  )
  return()
endif()

mono_add_library(firebase_dynamic_links_cs
  MODULE
    Firebase.DynamicLinks
  SOURCES
    ${firebase_dynamic_links_src}
    ${firebase_dynamic_links_swig_gen_src}
  REFERENCES
    ${FIREBASE_PLATFORM_REF}
  DEPENDS
    firebase_dynamic_links_swig
)

if(FIREBASE_IOS_BUILD)
  ios_pack(firebase_lib_dynamic_links libFirebaseCppDynamicLinks DEPS firebase_dynamic_links firebase_dynamic_links_swig)
else()
  build_firebase_shared(
    dynamic_links
    dynamic-links
    FirebaseCppDynamicLinks
  )
endif()

unity_pack_cs(firebase_dynamic_links_cs)

if (FIREBASE_INCLUDE_UNITY)
  generate_dependencies_xml(DynamicLinks
    IOS_DEPS
      "Firebase/DynamicLinks"
    ANDROID_DEPS
      ${FIREBASE_DYNAMIC_LINKS_ANDROID_DEPS}
    ANDROID_SPEC
      "dynamic-links"
  )
endif()

set_property(TARGET firebase_dynamic_links_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
