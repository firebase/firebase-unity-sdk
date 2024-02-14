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

# CMake file for the firebase app unity editor plugin

if(FIREBASE_UNITY_EAP)
  set(firebase_app_editor_eap_src
    src/eap/ConfigApi.cs
    src/eap/ConfigWindow.cs
  )
else()
  set(firebase_app_editor_eap_src "")
endif()

set(firebase_app_editor_src
    src/AnalyticsFixPropertyRemover.cs
    src/AndroidAPILevelChecker.cs
    src/ApiInfo.cs
    src/AssemblyInfo.cs
    src/DllLocationPatcher.cs
    src/GenerateXmlFromGoogleServicesJson.cs
    src/Settings.cs
    src/SettingsWindow.cs
    src/XcodeProjectPatcher.cs
    src/ChooserDialog.cs
    src/Measurement.cs
    src/NetworkRequest.cs
    src/PythonExecutor.cs
    src/CategoryLogger.cs
    src/Utility.cs
    src/AndroidManifestModifier.cs
    src/XcodeProjectModifier.cs
    src/GUILayout.cs
    src/GUIMenuSelector.cs
    src/GUITheme.cs
    src/DocRef.Designer.cs
    src/DocRef.resx
    src/Link.Designer.cs
    src/Link.resx
    ${firebase_app_editor_eap_src}
)

set(firebase_app_editor_reference
  unity_editor_cs
  unity_engine_cs  
  google_jar_resolver_cs
  google_ios_resolver_cs
  google_version_handlerimpl_cs
)

if(APPLE)
  list(APPEND firebase_app_editor_reference unity_ios_extensions_xcode_cs)
endif()

mono_add_library(firebase_app_editor_cs
  MODULE
    Firebase.Editor
  SOURCES
    ${firebase_app_editor_src}
  REFERENCES
    ${firebase_app_editor_reference}
  SYSTEM_REFERENCES
    System.Xml
  XBUILD_EXE
    ${XBUILD_EXE}
)

unity_pack_cs(firebase_app_editor_cs
  PACK_PATH "Firebase/Editor/"
)

unity_pack_folder(
  "${FIREBASE_UNITY_DIR}/editor/app/assets/"
  PACK_PATH "Editor Default Resources/Firebase"
)
