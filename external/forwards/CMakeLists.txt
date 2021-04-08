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

# This file configures projects for forwarding types that are provided in
# .dotnet 45

mono_add_library(unity_compat_fwd_cs
  FRAMEWORK_VERSION
    4.5
  MODULE
    Unity.CompatFwd
  ASSEMBLY_NAME
    Unity.Compat
  SOURCES
    ${PARSE_SOURCE_DIR}/Unity.Compat.TypeForwards/Properties/AssemblyInfo.cs
    ${PARSE_SOURCE_DIR}/Unity.Compat.TypeForwards/Public/CompatTypeForwards.cs
  REFERENCES
    unity_engine_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

mono_add_library(unity_tasks_fwd_cs
  FRAMEWORK_VERSION
    4.5
  MODULE
    Unity.TasksFwd
  ASSEMBLY_NAME
    Unity.Tasks
  SOURCES
    ${PARSE_SOURCE_DIR}/Unity.Tasks.TypeForwards/Properties/AssemblyInfo.cs
    ${PARSE_SOURCE_DIR}/Unity.Tasks.TypeForwards/Public/TaskTypeForwards.cs
  REFERENCES
    unity_engine_cs
    unity_compat_cs
  XBUILD_EXE
    ${XBUILD_EXE}
)

unity_pack_cs(unity_compat_fwd_cs
  PACK_PATH "Parse/Plugins/dotNet45"
)

unity_pack_cs(unity_tasks_fwd_cs
  PACK_PATH "Parse/Plugins/dotNet45"
)

unity_pack_file(
  "${PARSE_SOURCE_DIR}/Unity.Tasks.UnityPlugin/UnityProjectTemplate/Assets/Parse/Plugins/dotNet45/Unity.Compat.dll.meta"
  PACK_PATH "Parse/Plugins/dotNet45"
)

unity_pack_file(
  "${PARSE_SOURCE_DIR}/Unity.Tasks.UnityPlugin/UnityProjectTemplate/Assets/Parse/Plugins/dotNet45/Unity.Tasks.dll.meta"
  PACK_PATH "Parse/Plugins/dotNet45"
)

