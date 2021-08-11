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

/* Allows testapps to be executed within the Unity editor without explicitly building.
 *
 * The EditorRun method can be called from the command line. It will open the automated scene,
 * MainSceneAutomated.unity, and enter play mode. It will not quit automatically, and
 * must instead be externally terminated.
 *
 * It's important to NOT use the -quit flag for this to work correctly. The quit flag will
 * close Unity once the invoked function returns, but the call to enter playmode won't take effect
 * until the frame after the function returns.
 */

using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[InitializeOnLoad]
public sealed class InEditorRunner {
  /// <summary>
  /// Runs the testapp in the Editor. Will not terminate on its own. Should not be used with the
  /// -quit flag.
  /// </summary>
  public static void EditorRun() {
    // Unity's api updater messes up the application id uniquely for standalone. See b/133860007
    string applicationId = GooglePlayServices.UnityCompat.GetApplicationId(BuildTarget.iOS);
    GooglePlayServices.UnityCompat.ApplicationId = applicationId;

    var scenes = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
                          .Where(path => Path.GetFileName(path) == "MainSceneAutomated.unity")
                          .ToArray();
    if (scenes.Length == 0) {
      Debug.LogError("Could not find automated scene: MainSceneAutomated.unity");
      EditorApplication.Exit(1);
    }
    if (scenes.Length > 1) {
      Debug.LogWarning("Found multiple automated scenes named 'MainSceneAutomated.unity'");
    }
    EditorBuildSettings.scenes = new [] { new EditorBuildSettingsScene(scenes[0], true) };
    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenes[0]);
    // This won't take effect until the end of the current frame, which is why we can't exit
    // at the end of the method, and also why this cannot be used with the -quit flag.
    EditorApplication.isPlaying = true;
  }
}
