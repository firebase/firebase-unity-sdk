/*
 * Copyright 2023 Google LLC
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
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Firebase.Messaging.Editor {

// Handles the generation of the MessagingUnityPlayerActivity java file.
// Note this regenerates the file every time an Android build occurs,
// but local changes can be preserved by using the PreserveTag below.
// This is needed because the source code needs to be present to work across
// different Unity versions, due to changes in mUnityPlayer.
// It also adjusts the base class of the file based on if GameActivity is being
// used (a new feature in Unity 2023).
public class FirebaseMessagingActivityGenerator : IPreprocessBuildWithReport {
  private readonly string TemplateTag = "FirebaseMessagingActivityTemplate";
  private readonly string GeneratedFileTag = "FirebaseMessagingActivityGenerated";
  // If this tag is present on the generated file, it will not be replaced.
  private readonly string PreserveTag = "FirebasePreserve";

  private readonly string OutputPath =
        Path.Combine(Path.Combine("Assets", "Plugins"), "Android");
  private readonly string OutputFilename = "MessagingUnityPlayerActivity.java";

  public int callbackOrder { get { return 0; } }
  public void OnPreprocessBuild(BuildReport report) {
    // Only run this logic when building for Android.
    if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
      return;
    }

    // Check if the file has already been generated.
    string[] oldAssetGuids = AssetDatabase.FindAssets("l:" + GeneratedFileTag);
    if (oldAssetGuids != null && oldAssetGuids.Length > 0) {
      if (oldAssetGuids.Length != 1) {
        Debug.LogWarning("FirebaseMessagingActivityEditor found multiple generated files with the label: " +
                         GeneratedFileTag + " \nNo changes will be made.");
        return;
      }
      string oldAssetPath = AssetDatabase.GUIDToAssetPath(oldAssetGuids[0]);
      Object oldAsset = AssetDatabase.LoadMainAssetAtPath(oldAssetPath);
      // If the generated file has been tagged to be preserved, don't change it.
      string[] labelList = AssetDatabase.GetLabels(oldAsset);
      if (labelList.Contains(PreserveTag)) {
        return;
      }
      // Delete the old asset.
      AssetDatabase.DeleteAsset(oldAssetPath);
    }

    // Find the template file.
    string[] guids = AssetDatabase.FindAssets("l:" + TemplateTag);
    if (guids == null || guids.Length == 0) {
      Debug.LogWarning("Unable to find MessagingUnityPlayerActivity template with tag: " + TemplateTag +
                       "\nFirebase Messaging will likely not work correctly.");
      return;
    }
    string templatePath = AssetDatabase.GUIDToAssetPath(guids[0]);
    string newAssetPath = Path.Combine(OutputPath, OutputFilename);
    if (AssetDatabase.CopyAsset(templatePath, newAssetPath)) {
      Object newAsset = AssetDatabase.LoadMainAssetAtPath(newAssetPath);
      AssetDatabase.SetLabels(newAsset, new[]{GeneratedFileTag});

#if UNITY_2023_1_OR_NEWER
      // If using the new GameActivity logic, we want to change the Java file to use that.
      if (PlayerSettings.Android.applicationEntry.HasFlag(AndroidApplicationEntry.GameActivity)) {
        string fileContents = File.ReadAllText(newAssetPath);
        // Be slightly more precise in the replacements, otherwise the class name can get changed.
        fileContents = fileContents.Replace("com.unity3d.player.UnityPlayerActivity", "com.unity3d.player.UnityPlayerGameActivity");
        fileContents = fileContents.Replace("extends UnityPlayerActivity", "extends UnityPlayerGameActivity");
        File.WriteAllText(newAssetPath, fileContents);
      }
#endif
    }
  }
}

}  // namespace Firebase.Messaging.Editor
