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

# CMake file for the firebase platform library

set(firebase_common_platform_src
  ExceptionAggregator.cs
  IAppConfigExtensions.cs
  IClockService.cs
  ILoggingService.cs
  Services.cs
  Dispatcher.cs
  PlatformLogLevel.cs
  IFirebaseAppUtils.cs
  IFirebaseAppPlatform.cs
  MainThreadProperty.cs
  Default/AppConfigExtensions.cs
  Default/SystemClock.cs
)

set(firebase_unity_platform_src
  Unity/AssemblyInfo.cs
  Unity/UnityConfigExtensions.cs
  Unity/UnityLoggingService.cs
  Unity/UnityPlatformServices.cs
  Unity/UnitySynchronizationContext.cs
  Unity/FirebaseHandler.cs
  Unity/PlatformInformation.cs
  Unity/FirebaseLogger.cs
  Unity/FirebaseMonoBehaviour.cs
  Unity/FirebaseEditorDispatcher.cs
)

set(firebase_mono_platform_src
  Unity/AssemblyInfo.cs
  Mono/FirebaseHandler.cs
  Mono/FirebaseLogger.cs
  Mono/MonoPlatformServices.cs
  Mono/PlatformInformation.cs
)

if(FIREBASE_INCLUDE_UNITY)
  set(firebase_platform_src ${firebase_unity_platform_src})
else()
  set(firebase_platform_src ${firebase_mono_platform_src})
endif()

mono_add_library(firebase_platform_cs
  MODULE
    Firebase.Platform
  SOURCES
    ${firebase_common_platform_src}
    ${firebase_platform_src}
  REFERENCES
    google_minijson_cs
    ${firebase_platform_task_lib}
)

unity_pack_cs(firebase_platform_cs)

set_property(TARGET firebase_platform_cs
  PROPERTY FOLDER
  "Firebase ${FIREBASE_PLATFORM_NAME}"
)
