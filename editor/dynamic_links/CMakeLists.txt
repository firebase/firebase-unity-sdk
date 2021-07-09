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

# CMake file for the firebase dynamic links unity editor plugin

if(FIREBASE_UNITY_EAP)
  set(firebase_dynamic_links_editor_eap_src
    src/eap/DynamicLinksConfig.cs
    src/eap/Strings.Designer.cs
    src/eap/Strings.resx
  )
else()
  set(firebase_dynamic_links_editor_eap_src "")
endif()

set(firebase_dynamic_links_editor_src
    src/Empty.cs
    ${firebase_dynamic_links_editor_eap_src}
)

mono_add_library(firebase_dynamic_links_editor_cs
  MODULE
    Firebase.DynamicLinks.Editor
  SOURCES
    ${firebase_dynamic_links_editor_src}
  REFERENCES
    unity_editor_cs
    unity_engine_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

unity_pack_cs(firebase_dynamic_links_editor_cs
  PACK_PATH "Firebase/Editor/"
)
