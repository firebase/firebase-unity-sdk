// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

/* Switches the .NET runtime version from 3.5 to something higher.
 *
 * Note that this should only be copied into a Unity project if using Unity 2017 or higher.
 * If it appears in an earlier version, it will cause a compiler error even if not being
 * used. Must be inside of an Editor folder anywhere in the Assets directory. After changing
 * runtimes, the editor must be restarted, and this does not appear to occur automatically.
 */

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class RuntimeSwitcher {
  public static void SwitchToLatest(){
    PlayerSettings.scriptingRuntimeVersion = ScriptingRuntimeVersion.Latest;
  }
}
