/*
 * Copyright 2024 Google LLC
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

namespace Firebase.Editor {
  using System;
  using System.IO;
  using System.Xml;
  using UnityEngine;
  using UnityEditor;
  using UnityEditor.Build;
  using UnityEditor.Build.Reporting;

  internal class AnalyticsFixPropertyRemover : IPreprocessBuildWithReport {
    private static string DefaultAndroidManifestContents = String.Join("\n",
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
      "<manifest",
      "    xmlns:android=\"http://schemas.android.com/apk/res/android\"",
      "    package=\"com.unity3d.player\"",
      "    xmlns:tools=\"http://schemas.android.com/tools\">",
      "  <application>",
      "    <activity android:name=\"com.unity3d.player.UnityPlayerActivity\"",
      "              android:theme=\"@style/UnityThemeSelector\">",
      "      <intent-filter>",
      "        <action android:name=\"android.intent.action.MAIN\" />",
      "        <category android:name=\"android.intent.category.LAUNCHER\" />",
      "      </intent-filter>",
      "      <meta-data android:name=\"unityplayer.UnityActivity\" android:value=\"true\" />",
      "    </activity>",
      "  </application>",
      "</manifest>"
    );

    private static string SearchTag = "AnalyticsFixPropertyRemover";
    private static string CommentToAdd = "This was added by the AnalyticsFixPropertyRemover. " + 
        "If you want to prevent the generation of this, have \"" + SearchTag +
        "\" included in a comment";

    public int callbackOrder { get { return 0; } }

    public void OnPreprocessBuild(BuildReport report) {
      // Only run this logic when building for Android.
      if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android) {
        return;
      }

      // If the gradle version is newer than 7.0.0, which should have support
      // for the property tag, there is no reason to add the removeAll logic.
      var versionComparer = new Google.JarResolver.Dependency.VersionComparer();
      if (versionComparer.Compare(
            GooglePlayServices.PlayServicesResolver.AndroidGradlePluginVersion,
            "7.0.0") <= 0) {
        return;
      }

      // Locate the AndroidManifest file.
      string androidPluginsDir = Path.Combine(Application.dataPath, "Plugins", "Android");
      string androidManifestPath = Path.Combine(androidPluginsDir, "AndroidManifest.xml");

      // If the AndroidManifest file doesn't exist, generate it.
      if (!File.Exists(androidManifestPath)) {
        File.WriteAllText(androidManifestPath, DefaultAndroidManifestContents);
      }

      // Check for the SearchTag, and if present there is nothing to do.
      if (File.ReadAllText(androidManifestPath).Contains(SearchTag)) {
        return;
      }

      XmlDocument doc = new XmlDocument();
      doc.Load(androidManifestPath);

      // Find the "application" node
      XmlNode applicationNode = doc.SelectSingleNode("/manifest/application");

      if (applicationNode != null) {
        // Create the new "property" node
        XmlElement propertyNode = doc.CreateElement("property");

        // Create and add the attribute (with namespace)
        XmlAttribute toolsAttribute = doc.CreateAttribute("tools", "node", "http://schemas.android.com/tools");
        toolsAttribute.Value = "removeAll";
        propertyNode.Attributes.Append(toolsAttribute);

        // Create a comment node, and add it before the property node
        XmlComment comment = doc.CreateComment(CommentToAdd);

        // Add the new node to the "application" node
        applicationNode.AppendChild(propertyNode);
        applicationNode.InsertBefore(comment, propertyNode);

        // Save the modified XML document
        doc.Save(androidManifestPath);
      }
      else {
        Debug.LogError("Could not find the 'application' node in the AndroidManifest.xml file.");
      }
    }
  }
}
