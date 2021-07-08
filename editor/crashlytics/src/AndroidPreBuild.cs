/*
 * Copyright 2018 Google LLC
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

namespace Firebase.Crashlytics.Editor {
  using Google;
  using Firebase.Editor;
  using UnityEngine;
  using UnityEditor;
  using UnityEditor.Callbacks;
  using System;
  using System.IO;

  /// <summary>
  /// Editor script that runs Crashlytics Android pre-build tasks, including:
  ///  > Generates a unique Crashlytics build_id as a string resource file.
  ///  > Saves the Unity version as a string resource file.
  /// </summary>
  internal class AndroidPreBuild {
    private const string BUILD_ID_RESOURCE = "crashlytics_build_id.xml";
    private const string UNITY_VERSION_RESOURCE = "crashlytics_unity_version.xml";
    private const string CRASHLYTICS_AUTO_INIT_RESOURCE = "crashlytics_auto_init.xml";

    private const string TemplateXML =
     "<?xml version=\"1.0\" encoding=\"utf-8\"?><resources>" +
     "<{0} name=\"{1}\" translatable=\"false\">{2}</{0}>" +
     "</resources>";

    private const string BOOL_RESOURCE = "bool";
    private const string STRING_RESOURCE = "string";

    // Flag to ensure we only write  once per build
    private static bool Generated = false;

    /// Once a scene is processed, call back to this method and write Android
    /// resources to a file. Only do this after the first scene (once per build).
    /// Don't do this when running the app in play mode.
    [PostProcessScene(0)]
    private static void UpdateResourceFiles() {
      if (Generated ||
          EditorApplication.isPlaying ||
          EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
        return;
      }

      bool successful = Utility.GenerateAndroidPluginResourceDirectory(
          PathConstants.ANDROID_PLUGIN_RELATIVE_DIRECTORY, "crashlytics");
      successful &= WriteBuildId();
      successful &= WriteUnityVersion();
      if (successful) {
        Measurement.analytics.Report("crashlytics/android/config/success",
                                     "Configure Android Crashlytics Successful");
      }

      // Refresh AssetDatabase after resources are generated.
      AssetDatabase.Refresh();
      Generated = true;
    }

    /// Once the build is complete, call back to this method and reset the
    /// Generated flag to set up for the next build.
    [PostProcessBuild(0)]
    private static void BuildComplete(BuildTarget target, string pathToBuiltProject) {
      Generated = false;
    }

     /// Write the given key and value to an XML resource file at the given path.
    private static bool WriteXmlResource(string FilePath, string Key, string Value,
                                         string ResourceType) {
      try {
        Directory.CreateDirectory(PathConstants.ANDROID_RESOURCE_FILE_PATH);
        using (StreamWriter writer = new StreamWriter(File.Create(FilePath))) {
          writer.WriteLine(String.Format(TemplateXML, ResourceType, Key, Value));
        }
        Debug.Log("Generated resource for " + Key + " : " + Value);
      }
      catch (Exception e) {
        Debug.Log("Could not write " + Key + " resource file." + e.Message);
        return false;
      }
      return true;
    }

    private static bool WriteBuildId() {
      string BuildIdPath = Path.Combine(PathConstants.ANDROID_RESOURCE_FILE_PATH,
                                        BUILD_ID_RESOURCE);
      string GeneratedBuildId = Guid.NewGuid().ToString();
      if (!WriteXmlResource(BuildIdPath, "com.crashlytics.android.build_id", GeneratedBuildId,
                            STRING_RESOURCE)) {
        Measurement.analytics.Report("android/config/failed/resource/buildid",
                                       "Writing Build ID File Failed");
        return false;
      }
      return true;
    }

    private static bool WriteUnityVersion() {
      string UnityVersionPath = Path.Combine(PathConstants.ANDROID_RESOURCE_FILE_PATH,
                                             UNITY_VERSION_RESOURCE);
      string unityVersion = Application.unityVersion;
      if (!WriteXmlResource(UnityVersionPath, "com.google.firebase.crashlytics.unity_version",
                            unityVersion, STRING_RESOURCE)) {
        Measurement.analytics.Report("android/config/failed/resource/unityversion",
                                       "Writing Unity Version File Failed");
        return false;
      }
      return true;
    }
  }
}
