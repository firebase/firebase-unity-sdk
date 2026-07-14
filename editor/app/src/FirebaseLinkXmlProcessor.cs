/*
 * Copyright 2026 Google LLC
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace Firebase.Editor {
  public class FirebaseLinkXmlProcessor : IUnityLinkerProcessor {
    public int callbackOrder { get { return 0; } }

    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) {
      Debug.Log("FirebaseLinkXmlProcessor: Starting additional link.xml generation...");

      // Initialize the merged XML document that will hold all linker rules.
      var mergedLinkxmlDoc = new XmlDocument();
      var root = mergedLinkxmlDoc.CreateElement("linker");
      mergedLinkxmlDoc.AppendChild(root);

      var filesToProcess = new HashSet<string>();

      // Get all link.xml files from Firebase UPM packages.
      foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()) {
        if (package.name.StartsWith("com.google.firebase.")) {
          FindLinkXmlFiles(package.resolvedPath, filesToProcess);
        }
      }

      // Get all link.xml files from Assets/Firebase.
      string assetsFirebasePath = Path.GetFullPath("Assets/Firebase");
      if (Directory.Exists(assetsFirebasePath)) {
        FindLinkXmlFiles(assetsFirebasePath, filesToProcess);
      }

      if (filesToProcess.Count == 0) {
        Debug.Log("FirebaseLinkXmlProcessor: No additional link.xml files found.");
        return null;
      }

      bool hasAssemblies = false;
      foreach (var file in filesToProcess) {
        try {
          var doc = new XmlDocument();
          doc.Load(file);
          if (doc.DocumentElement != null) {
            // Traverse the child elements under the root <linker> element of each link.xml.
            // NOTE: We only support and extract `<assembly>` elements at this time. Root-level
            // `<type>` or `<linker>` attributes other than assembly declarations are ignored.
            foreach (XmlNode child in doc.DocumentElement.ChildNodes) {
              if (child.Name == "assembly") {
                XmlNode importedNode = mergedLinkxmlDoc.ImportNode(child, true);
                root.AppendChild(importedNode);
                hasAssemblies = true;
              }
            }
          }
        } catch (Exception e) {
          Debug.LogWarning($"FirebaseLinkXmlProcessor: Failed to parse XML from {file}: {e.Message}");
        }
      }

      if (!hasAssemblies) {
        Debug.Log("FirebaseLinkXmlProcessor: No assembly blocks matched in found link.xml files.");
        return null;
      }

      string tempLinkXmlPath = Path.GetFullPath("Temp/FirebaseAdditionalLink.xml");
      try {
        string directory = Path.GetDirectoryName(tempLinkXmlPath);
        if (!Directory.Exists(directory)) {
          Directory.CreateDirectory(directory);
        }
        mergedLinkxmlDoc.Save(tempLinkXmlPath);
        return tempLinkXmlPath;
      } catch (Exception e) {
        Debug.LogWarning($"FirebaseLinkXmlProcessor: Failed to write merged link.xml to {tempLinkXmlPath}: {e.Message}");
      }
      return null;
    }

    private void FindLinkXmlFiles(string dir, HashSet<string> files) {
      if (!Directory.Exists(dir)) return;
      try {
        foreach (var file in Directory.GetFiles(dir, "*link.xml", SearchOption.AllDirectories)) {
          files.Add(Path.GetFullPath(file));
        }
      } catch (Exception e) {
        Debug.LogWarning($"FirebaseLinkXmlProcessor: Failed to scan directory {dir}: {e.Message}");
      }
    }
  }
}
