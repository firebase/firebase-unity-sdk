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

# CMake file for the firebase crashlytics unity editor plugin

if(FIREBASE_UNITY_EAP)
  set(firebase_crashlytics_editor_eap_src
    src/eap/CrashlyticsConfig.cs
    src/eap/Strings.Designer.cs
    src/eap/Strings.resx
  )
else()
  set(firebase_crashlytics_editor_eap_src "")
endif()

mono_add_library(firebase_crashlytics_editor_cs
  MODULE
    Firebase.Crashlytics.Editor
  SOURCES
    src/AndroidPreBuild.cs
    src/ApplicationIdentifierProvider.cs
    src/BuildEvent.cs
    src/BuildEventPostBuild.cs
    src/IApplicationIdentifierProvider.cs
    src/iOSPostBuild.cs
    src/PathConstants.cs
    src/VersionInfo.cs
    src/Storage/AssociationConfigurationStorage.cs
    src/Storage/Associations.cs
    ${firebase_crashlytics_editor_eap_src}
  REFERENCES
    unity_editor_cs
    unity_engine_cs
    unity_ios_extensions_xcode_cs
    google_jar_resolver_cs
    google_ios_resolver_cs
    google_version_handler_cs
    google_version_handlerimpl_cs
    firebase_app_editor_cs
  SYSTEM_REFERENCES
    System.Xml
  DEFINES
    UNITY_EDITOR
)

unity_pack_cs(firebase_crashlytics_editor_cs
  PACK_PATH "Firebase/Editor/"
)

set_property(TARGET firebase_crashlytics_editor_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
