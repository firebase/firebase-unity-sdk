// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using UnityEditor;
using UnityEngine;

// Allows doing builds from the command line.
//
// For more information, see:
// https://docs.unity3d.com/Manual/CommandLineArguments.html
// https://docs.unity3d.com/ScriptReference/BuildOptions.html
// https://docs.unity3d.com/ScriptReference/BuildPipeline.BuildPlayer.html
public class Builder {

  public static void BuildIos() {
    var options = new BuildPlayerOptions();

    options.scenes = new string[] { "Assets/Firebase/Sample/Firestore/MainSceneAutomated.unity" };
    options.locationPathName = "ios-build";
    options.target = BuildTarget.iOS;
    // AcceptExternalModificationsToPlayer corresponds to "Append" in the Unity
    // UI -- it allows doing incremental iOS build.
    options.options = BuildOptions.AcceptExternalModificationsToPlayer;
    // Firebase Unity plugins don't seem to work on a simulator.
    PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

    BuildPipeline.BuildPlayer(options);
  }

}
