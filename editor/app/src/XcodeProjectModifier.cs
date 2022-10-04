/*
 * Copyright 2019 Google LLC
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
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Firebase.Editor {

// This class is responisble for handling modifications to XCode projects post generation.
internal class XcodeProjectModifier {

  /// <summary>
  /// Class instance of the raw Xcode project file
  /// </summary>
  /// <remarks>
  /// The object's type is PBXProject.  We don't expose this in the interface so this can be
  /// loaded without referencing the UnityEditor.iOS.Xcode module.
  /// </remarks>
  internal object Project { get; private set; }

  /// <summary>
  /// Unity target guid in the XCode project
  /// </summary>
  internal string TargetGUID { get; private set; }

  /// <summary>
  /// Info.plist in the project directory
  /// </summary>
  /// <remarks>
  /// The object's type is PlistElementDict.  We don't expose this in the interface so this can be
  /// loaded without referencing the UnityEditor.iOS.Xcode module.
  /// </remarks>
  internal object ProjectInfo {
    get { return ((PlistDocument)projectInfoDoc).root; }
  }

  /// <summary>
  /// Capabilities plist in the project directory
  /// </summary>
  /// <remarks>
  /// The object's type is PlistElementDict.  We don't expose this in the interface so this can be
  /// loaded without referencing the UnityEditor.iOS.Xcode module.
  /// </remarks>
  internal object Capabilities {
    get { return ((PlistDocument)capabilitiesDoc).root; }
  }

  private object projectInfoDoc;
  private object capabilitiesDoc;

  private static readonly string projectInfoPListFile = "Info.plist";
  private static readonly string capabilitiesPListFile = "Unity-iPhone/dev.entitlements";

  private static readonly CategoryLogger logger = new CategoryLogger("XcodeProjectModifier");

  private readonly string xcodeProjDir;

  /// <summary>
  /// Constructs a XcodeProjectModifier that handles loading a Xcode project and realted plists,
  /// allowing modifications on the objects and saving them back to disk.
  /// </summary>
  /// <param name="xcodeProjDir">Xcode project directory</param>
  internal XcodeProjectModifier(string xcodeProjDir) {
    logger.LogDebug("Loading Xcode project '{0}'.", xcodeProjDir);
    this.xcodeProjDir = xcodeProjDir;

    var projectPath = PBXProject.GetPBXProjectPath(xcodeProjDir);

    var project = new PBXProject();
    Project = project;
    project.ReadFromString(File.ReadAllText(projectPath));

    // TargetGuidByName & GetUnityTargetName is deprecated after 2019.3; use reflection to determine if we can call it
    // or need to use GetUnityMainTargetGuid.
    MethodInfo getUnityMainTargetGuid = project.GetType().GetMethod("GetUnityMainTargetGuid");
    if (getUnityMainTargetGuid != null) {
      TargetGUID = (string) getUnityMainTargetGuid.Invoke(project, new object[] {});
    } else {
      MethodInfo getUnityTargetName = project.GetType().GetMethod("GetUnityTargetName");
      if (getUnityTargetName != null) {
        string targetName = (string)getUnityTargetName.Invoke(project, new object[] {});
        TargetGUID = project.TargetGuidByName(targetName);
      } else {
        logger.LogError("Impossible Unity version, failed to get target guid.");
      }
    }

    projectInfoDoc = ReadPList(projectInfoPListFile);
    capabilitiesDoc = ReadPList(capabilitiesPListFile);
  }

  /// <summary>
  /// Save changes to Xcode project objects to disk.
  /// </summary>
  internal void Save() {
    logger.LogDebug("Saving Xcode project '{0}'.", xcodeProjDir);

    var projectPath = PBXProject.GetPBXProjectPath(xcodeProjDir);
    File.WriteAllText(projectPath, ((PBXProject)Project).WriteToString());

    SavePList(projectInfoDoc, projectInfoPListFile);
    SavePList(capabilitiesDoc, capabilitiesPListFile);
  }


  // Map of Editor UI config types to set of callbacks
  private static Dictionary<Type, List<Action<XcodeProjectModifier, object>>> delegatesByType =
    new Dictionary<Type, List<Action<XcodeProjectModifier, object>>>();

  /// <summary>
  /// Register a delegate to be called when the Xcode project is being post processed. This should
  /// be called by a static class constructor with the class having a [InitializeOnLoad] attribute.
  /// </summary>
  /// <typeparam name="T">Type of Editor UI configuration class</typeparam>
  /// <param name="callback">
  /// Callback to invoke at time of Xcode project post process. First argument is instance of
  /// XcodeProjectModifier and second argument is instance of T with its fields loaded from
  /// saved configuration.
  /// </param>
  internal static void RegisterDelegate<T>(Action<XcodeProjectModifier, T> callback)
    where T : class {

      Action<XcodeProjectModifier, object> internalCallback =
        delegate(XcodeProjectModifier a, System.Object o) { callback(a, o as T); };

      List<Action<XcodeProjectModifier, object>> listOfCallbacks = null;

      if (delegatesByType.TryGetValue(typeof(T), out listOfCallbacks) == false) {
        listOfCallbacks = new List<Action<XcodeProjectModifier, object>>();
        delegatesByType.Add(typeof(T), listOfCallbacks);
      }

      listOfCallbacks.Add(internalCallback);
  }

  /// <summary>
  /// Register a custom URL scheme in the Xcode project's Info.plist. Custom URL schemes are
  /// analagous to Android's intent filters and allow the app to be opened by other apps.
  /// </summary>
  /// <param name="name">
  /// The CFBundleURLName to associate with this scheme.
  /// </param>
  /// <param name="scheme">
  /// The scheme to register.
  /// </param>
  internal void AddCustomUrlScheme(string name, string scheme) {
    PlistElementDict root = (PlistElementDict) ProjectInfo;
    PlistElement elem;
    PlistElementArray types;
    if (root.values.TryGetValue("CFBundleURLTypes", out elem)) {
      types = (PlistElementArray) elem;
    } else {
      types = root.CreateArray("CFBundleURLTypes");
    }
    if (ContainsUrlScheme(types, scheme)) {
      return;
    }
    PlistElementDict typeDict = types.AddDict();
    typeDict.SetString("CFBundleURLName", name);
    PlistElementArray schemes = typeDict.CreateArray("CFBundleURLSchemes");
    schemes.AddString(scheme);
  }

  // Helper method to determine if a given URL has been already registered in
  // the CFBundleURLTypes dict
  private static bool ContainsUrlScheme(object inputTypes, string scheme) {
    PlistElementArray types = (PlistElementArray)inputTypes;
    foreach (PlistElement typeElem in types.values) {
      PlistElementDict typeDict = typeElem.AsDict();
      PlistElementArray schemes = typeDict["CFBundleURLSchemes"].AsArray();
      foreach (PlistElement schemeElem in schemes.values) {
        if (string.Equals(schemeElem.AsString(), scheme)) {
          return true;
        }
      }
    }
    return false;
  }

  // This method is called by unity after it finishes project generation. It then loads the XCode
  // project and pLists files, creates class instances with methods having the XCodePostGen
  // Attribute, loads any Editor UI configuration object fields for those classes and then invokes
  // the methods. Once that is complete it saves the modifications back over the original files.
  [PostProcessBuild]
  internal static void PostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject) {
    if (buildTarget != BuildTarget.iOS && buildTarget != BuildTarget.tvOS) {
      logger.LogDebug("Skipping PostProcessBuild as target {0} is not for iOS+", buildTarget);
      return;
    }

    if (delegatesByType.Count() == 0) {
      logger.LogDebug("Skipping PostProcessBuild as no delegates are registered");
      return;
    }

    // Load Xcode project and plists
    var info = new XcodeProjectModifier(pathToBuiltProject);

    foreach (var t in delegatesByType) {
      // Create instance
      var classInstance = Activator.CreateInstance(t.Key);

      // Load any config fields in instance for easy access
      Utility.LoadConfig(classInstance);

      foreach (var callback in t.Value) {
        callback(info, classInstance);
      }
    }

    // Save changes to Xcode project and plists
    info.Save();
  }

  // Utility function to read plist file from a path.
  private object ReadPList(string relFilePath) {
    var plistPath = Path.Combine(xcodeProjDir, relFilePath);
    var doc = new PlistDocument();

    if (File.Exists(plistPath)) {
      doc.ReadFromString(File.ReadAllText(plistPath));
    }

    return doc;
  }

  // Utility function to save plist file to a path
  private void SavePList(object doc, string relFilePath) {
    var plistPath = Path.Combine(xcodeProjDir, relFilePath);
    File.WriteAllText(plistPath, ((PlistDocument)doc).WriteToString());
  }
}
}
