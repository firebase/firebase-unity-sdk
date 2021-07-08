/*
 * Copyright 2020 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
// AndroidManifestPatcher will create Plugin/Android/AndroidManifest.xml required to build Android
// app which is using Firebase Cloud Messaging.
// This is a temporary solution before we introduce better method to configure AndroidManifest.xml
// for Firebase Cloud Messaging.

namespace Firebase.Messaging.Editor {
  // Google.RunOnMainThread() from VersionHandlerImpl
  using Google;
  using System;
  using System.Reflection;
  using System.IO;
  using UnityEngine;
  using UnityEditor;

  /// <summary>
  /// </summary>
  [InitializeOnLoad]
  public class AndroidManifestPatcher : AssetPostprocessor {
    // Hard coded directories and file names.
    private static string ANDROID_MANIFEST_OUTPUT_DIRECTORY =
        Path.Combine(Path.Combine("Assets", "Plugins"), "Android");
    private static string ANDROID_MANIFEST_FILE = "AndroidManifest.xml";
    private static string ANDROID_MANIFEST_OUTPUT_PATH =
        Path.Combine(ANDROID_MANIFEST_OUTPUT_DIRECTORY, ANDROID_MANIFEST_FILE);
    private static string ANDROID_MANIFEST_RESOURCE_NAME =
        "Firebase.Messaging.Editor.AndroidManifest.xml";

    // Log message template when failed to generate AndroidManifest.xml
    private static string GENERATE_FAIL_MESSAGE =
        "Failed to generate {0} for Firebase Messaging due to {1}.";
    // Log message template after generated AndroidManifest.xml
    private static string GENERATE_SUCCESS_MESSAGE =
        "Generated {0} for Firebase Messaging.";

    // Attempt to generate AndroidManifest.xml when this class loads.
    static AndroidManifestPatcher() {
      // Do not run check if this assembly reloads due to play mode start.
      if (!EditorApplication.isPlayingOrWillChangePlaymode) {
        // We shouldn't be modifying assets on load, so wait for first editor update.
        RunOnMainThread.Run(() => { CheckAndroidManifest(); }, runNow: false);
      }
    }

    /// <summary>
    /// Regenerate the manifest if it's deleted.
    /// </summary>
    /// <param name="importedAssets">Imported assets. (unused)</param>
    /// <param name="deletedAssets">Deleted assets. (unused)</param>
    /// <param name="movedAssets">Moved assets. (unused)</param>
    /// <param name="movedFromAssetPaths">Moved from asset paths. (unused)</param>
   private static void OnPostprocessAllAssets(string[] importedAssets,
                                              string[] deletedAssets,
                                              string[] movedAssets,
                                              string[] movedFromAssetPaths) {
       CheckAndroidManifest();
   }

    // Check and generate AndroidManifest.xml if not in project.
    private static void CheckAndroidManifest() {
      if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
        return;
      }

      string projectDir = Path.Combine(Application.dataPath, "..");
      string outputDir = Path.Combine(projectDir, ANDROID_MANIFEST_OUTPUT_DIRECTORY);
      if (!Directory.Exists(outputDir)) {
        try {
          Directory.CreateDirectory(outputDir);
        } catch (Exception e) {
          Debug.LogError(
              String.Format(GENERATE_FAIL_MESSAGE, ANDROID_MANIFEST_OUTPUT_PATH, "exception"));
          Debug.LogException(e);
          return;
        }
      }

      string manifestName = Path.Combine(projectDir, ANDROID_MANIFEST_OUTPUT_PATH);
      if (!File.Exists(manifestName)) {
        string manifestContent = null;
        try {
          // Get AndroidManifest.xml content from embedded resource.
          var resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
              ANDROID_MANIFEST_RESOURCE_NAME);
          using (var resourceReader = new StreamReader(resourceStream)) {
            manifestContent = resourceReader.ReadToEnd();
          }
        } catch (Exception e) {
          Debug.LogError(
              String.Format(GENERATE_FAIL_MESSAGE, ANDROID_MANIFEST_OUTPUT_PATH,
                  String.Format("resouce {0} not available", ANDROID_MANIFEST_RESOURCE_NAME)));
          Debug.LogException(e);
          return;
        }
        if (manifestContent != null) {
          try {
            File.WriteAllText(manifestName, manifestContent);
            Debug.Log(String.Format(GENERATE_SUCCESS_MESSAGE, ANDROID_MANIFEST_OUTPUT_PATH));
          } catch (Exception e) {
            Debug.LogError(
                String.Format(GENERATE_FAIL_MESSAGE, ANDROID_MANIFEST_OUTPUT_PATH, "exception"));
            Debug.LogException(e);
          }
        }
      }
    }
  }
}  // namespace Firebase.Messaging.Editor
