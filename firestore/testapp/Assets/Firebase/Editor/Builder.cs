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

using System;

using UnityEditor;
using UnityEditor.Build.Reporting;
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

    // For iOS, this is the name of the folder containing the generated XCode
    // project.
    options.locationPathName = "ios-build";
    options.target = BuildTarget.iOS;
    // Firebase Unity plugins don't seem to work on a simulator.
    PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

    // AcceptExternalModificationsToPlayer corresponds to "Append" in the Unity
    // UI -- it allows doing incremental builds.
    options.options = BuildOptions.AcceptExternalModificationsToPlayer;

    Build(options);
  }

  public static void BuildAndroid() {
    var options = new BuildPlayerOptions();

    string[] args = Environment.GetCommandLineArgs();
    String apkName = args[args.Length - 1];
    if (!apkName.EndsWith(".apk")) {
      throw new Exception("No APK name given for the Android build.");
    }
    Debug.Log("Building " + apkName);

    // For Android, this is the name of the resulting apk.
    options.locationPathName = apkName;
    options.target = BuildTarget.Android;
    // Note that the Android build doesn't support incremental rebuilds.

    Build(options);
  }

  private static void Build(BuildPlayerOptions options) {
    options.scenes = new string[] { "Assets/Firebase/Sample/Firestore/MainSceneAutomated.unity" };

    BuildReport report = BuildPipeline.BuildPlayer(options);
    BuildSummary summary = report.summary;
    if (summary.result == BuildResult.Failed) {
      throw new Exception("Build error (see the Unity Editor log for more).");
    } else if (summary.result == BuildResult.Succeeded) {
      Debug.Log("Build succeeded: " + summary.outputPath);
    }
  }

}
