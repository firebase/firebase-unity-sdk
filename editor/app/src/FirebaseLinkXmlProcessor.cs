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
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;
using UnityEngine;

namespace Firebase.Editor {
  public class FirebaseLinkXmlProcessor : IUnityLinkerProcessor {
    public int callbackOrder { get { return 0; } }

    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data) {
      Debug.Log("FirebaseLinkXmlProcessor: Starting additional link.xml generation...");
      var linkXmlContents = new List<string>();
      var processedFiles = new HashSet<string>();
      var processedContents = new HashSet<string>();

      ScanDirectory(Path.GetFullPath("Packages"), true, linkXmlContents, processedFiles, processedContents);
      ScanDirectory(Path.GetFullPath("Library/PackageCache"), true, linkXmlContents, processedFiles, processedContents);
      ScanDirectory(Path.GetFullPath("Assets/Firebase"), false, linkXmlContents, processedFiles, processedContents);

      if (linkXmlContents.Count == 0) {
        Debug.Log("FirebaseLinkXmlProcessor: No additional link.xml files found.");
        return null;
      }

      var assemblyElements = new List<string>();
      foreach (var content in linkXmlContents) {
        int startIndex = 0;
        while (true) {
          int assemblyStart = content.IndexOf("<assembly ", startIndex);
          if (assemblyStart == -1) break;
          int assemblyEnd = content.IndexOf("</assembly>", assemblyStart);
          if (assemblyEnd == -1) break;
          assemblyEnd += "</assembly>".Length;
          string assemblyBlock = content.Substring(assemblyStart, assemblyEnd - assemblyStart);
          assemblyElements.Add(assemblyBlock);
          startIndex = assemblyEnd;
        }
      }

      if (assemblyElements.Count == 0) {
        Debug.Log("FirebaseLinkXmlProcessor: No assembly blocks matched in found link.xml files.");
        return null;
      }

      string tempLinkXmlPath = Path.GetFullPath("Temp/FirebaseAdditionalLink.xml");
      try {
        string directory = Path.GetDirectoryName(tempLinkXmlPath);
        if (!Directory.Exists(directory)) {
          Directory.CreateDirectory(directory);
        }
        using (var writer = new StreamWriter(tempLinkXmlPath)) {
          writer.WriteLine("<linker>");
          foreach (var block in assemblyElements) {
            writer.WriteLine(block);
          }
          writer.WriteLine("</linker>");
        }
        return tempLinkXmlPath;
      } catch (Exception e) {
        Debug.LogWarning($"FirebaseLinkXmlProcessor: Failed to write merged link.xml to {tempLinkXmlPath}: {e.Message}");
      }
      return null;
    }

    private void ScanDirectory(string rootDir, bool filterFirebase, List<string> linkXmlContents, HashSet<string> processedFiles, HashSet<string> processedContents) {
      if (!Directory.Exists(rootDir)) return;

      string[] searchDirs = filterFirebase 
        ? Directory.GetDirectories(rootDir, "com.google.firebase.*") 
        : new string[] { rootDir };

      foreach (var dir in searchDirs) {
        foreach (var file in Directory.GetFiles(dir, "*link.xml", SearchOption.AllDirectories)) {
          string fullPath = Path.GetFullPath(file);
          if (!processedFiles.Add(fullPath)) continue;

          try {
            string content = File.ReadAllText(fullPath);
            if (processedContents.Add(content)) {
              Debug.Log($"FirebaseLinkXmlProcessor: Merging link.xml: {fullPath}");
              linkXmlContents.Add(content);
            }
          } catch (Exception e) {
            Debug.LogWarning($"FirebaseLinkXmlProcessor: Failed to read {fullPath}: {e.Message}");
          }
        }
      }
    }
  }
}
