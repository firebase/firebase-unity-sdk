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
  using UnityEngine;
  using UnityEditor;
  using UnityEditor.Callbacks;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Reflection;
  using System.Text;
  using UnityEditor.iOS.Xcode;

  /// <summary>
  /// Container for reflection-based method calls to add a Run Script Build Phase for the generated
  /// Xcode project to call the run script and upload symbols.
  /// </summary>
  public class iOSBuildPhaseMethodCall {
    public string MethodName;
    public Type [] ArgumentTypes;
    public object [] Arguments;
  }

  /// <summary>
  /// Editor script that generates a Run Script Build Phase for the Xcode project with the
  /// Crashlytics run script, and sets the Debug Information Format to Dwarf with DSYM to make it
  /// easier for customers to onboard.
  /// </summary>
  public class iOSPostBuild {

    private const string CRASHLYTICS_UNITY_VERSION_KEY = "CrashlyticsUnityVersion";

    // In Unity 2019.3 - PODS_ROOT is no longer an environment variable that is exposed.
    //                   Use ${PROJECT_DIR}/Pods.
    private const string RunScriptBody = "chmod u+x \"${PROJECT_DIR}/Pods/FirebaseCrashlytics/run\"\n" +
        "chmod u+x \"${PROJECT_DIR}/Pods/FirebaseCrashlytics/upload-symbols\"\n" +
        "\"${PROJECT_DIR}/Pods/FirebaseCrashlytics/run\"";

    private const string GooglePlistPath = "${PROJECT_DIR}/GoogleService-Info.plist";

    private const string RunScriptName = "Crashlytics Run Script";

    private const string ShellPath = "/bin/sh -x";

    /// <summary>
    /// When building out to iOS, write Firebase specific values to the appropriate Plist files.
    /// </summary>
    /// <param name="buildTarget">
    /// The platform that we are targeting to build
    /// </param>
    /// <param name="buildPath">
    /// The path to which we are building out
    /// </param>
    [PostProcessBuild(100)]
    public static void OnPostprocessBuild(BuildTarget buildTarget, string buildPath) {
      // BuiltTarget.iOS is not defined in Unity 4, so we just use strings here
      if (buildTarget.ToString() == "iOS" || buildTarget.ToString() == "iPhone") {
        string projectPath = Path.Combine(buildPath, "Unity-iPhone.xcodeproj/project.pbxproj");
        string plistPath = Path.Combine(buildPath, "Info.plist");

        IFirebaseConfigurationStorage configurationStorage = StorageProvider.ConfigurationStorage;

        PrepareProject(projectPath, configurationStorage);
        AddCrashlyticsDevelopmentPlatformToPlist(plistPath);
      }
    }

    private static void PrepareProject(string projectPath, IFirebaseConfigurationStorage configurationStorage) {
      // Return if we have already added the script
      var xcodeProjectLines = File.ReadAllLines(projectPath);
      foreach (var line in xcodeProjectLines) {
        if (line.Contains("FirebaseCrashlytics/run")) {
          return;
        }
      }

      Debug.Log("Adding Crashlytics Run Script to the Xcode project's Build Phases");

      var pbxProject = new UnityEditor.iOS.Xcode.PBXProject();
      pbxProject.ReadFromFile(projectPath);

      string completeRunScriptBody = GetRunScriptBody(configurationStorage);

      string appGUID = iOSPostBuild.GetMainUnityProjectTargetGuid(pbxProject);
      SetupGUIDForSymbolUploads(pbxProject, completeRunScriptBody, appGUID);

      // In older versions of Unity there is no separate framework GUID, so this can
      // be empty or null.
      string frameworkGUID = iOSPostBuild.GetUnityFrameworkTargetGuid(pbxProject);
      if (!String.IsNullOrEmpty(frameworkGUID)) {
        SetupGUIDForSymbolUploads(pbxProject, completeRunScriptBody, frameworkGUID);
      }

      pbxProject.WriteToFile(projectPath);
    }

    private static void SetupGUIDForSymbolUploads(object pbxProjectObj,
                                                  string completeRunScriptBody, string targetGuid) {
      var pbxProject = (UnityEditor.iOS.Xcode.PBXProject) pbxProjectObj;
      try {
        // Use reflection to append a Crashlytics Run Script
        BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
        CallingConventions callingConventions = CallingConventions.Any;
        ParameterModifier [] paramModifiers = new ParameterModifier [] { };

        iOSBuildPhaseMethodCall methodCall = GetBuildPhaseMethodCall(Application.unityVersion, targetGuid, completeRunScriptBody);
        MethodInfo appendMethod = pbxProject.GetType().GetMethod(
            methodCall.MethodName, bindingFlags, null, callingConventions,
            methodCall.ArgumentTypes, paramModifiers);
        appendMethod.Invoke(pbxProject, methodCall.Arguments);

      } catch (Exception e) {
        Debug.LogWarning("Failed to add Crashlytics Run Script: '" + e.Message + "'. " +
            "You can manually fix this by adding a Run Script Build Phase to your Xcode " +
            "project with the following script:\n" + completeRunScriptBody);

      } finally {
        // Set debug information format to DWARF with DSYM
        pbxProject.SetBuildProperty(targetGuid, "DEBUG_INFORMATION_FORMAT", "dwarf-with-dsym");
      }
    }

    internal static iOSBuildPhaseMethodCall GetBuildPhaseMethodCall(string unityVersion, string targetGuid, string completeRunScriptBody) {
      if (VersionInfo.GetUnityMajorVersion(unityVersion) >= 2019) {
        Debug.Log("Using AddShellScriptBuildPhase to add the Crashlytics Run Script");
        return new iOSBuildPhaseMethodCall {
          MethodName = "AddShellScriptBuildPhase",
          ArgumentTypes = new [] { typeof(string), typeof(string), typeof(string), typeof(string) },
          Arguments = new object [] { targetGuid, RunScriptName, ShellPath, completeRunScriptBody },
        };
      } else {
        Debug.Log("Using AppendShellScriptBuildPhase to add the Crashlytics Run Script");
        return new iOSBuildPhaseMethodCall {
          MethodName = "AppendShellScriptBuildPhase",
          ArgumentTypes = new [] { typeof(IEnumerable<string>), typeof(string), typeof(string), typeof(string) },
          Arguments = new object [] { new [] { targetGuid }, RunScriptName, ShellPath, completeRunScriptBody },
        };
      }
    }

    private static string GetUnityFrameworkTargetGuid(object projectObj) {
      var project = (UnityEditor.iOS.Xcode.PBXProject)projectObj;

      MethodInfo getUnityFrameworkTargetGuid =
          project.GetType().GetMethod("GetUnityFrameworkTargetGuid");

      // Starting in Unity 2019.3, TargetGuidByName is deprecated
      // Use reflection to call the GetUnityFrameworkTargetGuid method if it exists (it was added
      // ub 2019.3).
      if (getUnityFrameworkTargetGuid != null) {
        return (string)getUnityFrameworkTargetGuid.Invoke(project, new object[] {});
      } else {
        // Hardcode the main target name "UnityFramework" because there isn't a way to get the
        // Unity Framework target name.
        string targetName = "UnityFramework";
        MethodInfo targetGuidByName = project.GetType().GetMethod("TargetGuidByName");
        if (targetGuidByName != null) {
          return (string)targetGuidByName.Invoke(project, new object[] { (object)targetName });
        } else {
          return "";
        }
      }
    }

    /// <summary>
    /// Get the main Unity project's target GUID
    /// The GUID is needed to manipulate the target, for example to add the run script build phase.
    /// </summary>
    /// <param name="projectObj">The project where the main target GUID will be read from</param>
    /// <returns>The main target GUID</returns>
    private static string GetMainUnityProjectTargetGuid(object projectObj) {
      var project = (UnityEditor.iOS.Xcode.PBXProject)projectObj;
      MethodInfo getUnityMainTargetGuid =  project.GetType().GetMethod("GetUnityMainTargetGuid");

      // Starting in Unity 2019.3, TargetGuidByName is deprecated
      // Use reflection to call the GetUnityMainTargetGuid method if it exists
      if (getUnityMainTargetGuid != null) {
        return (string)getUnityMainTargetGuid.Invoke(project, new object[] {});
      } else {
        // Hardcode the main target name "Unity-iPhone" by default, just in case
        // GetUnityTargetName() is not available.
        string targetName = "Unity-iPhone";
        MethodInfo getUnityTargetName =  project.GetType().GetMethod("GetUnityTargetName");
        if (getUnityTargetName != null) {
          targetName = (string) getUnityTargetName.Invoke(null, new object[] {});
        }

        MethodInfo targetGuidByName = project.GetType().GetMethod("TargetGuidByName");
        if (targetGuidByName != null) {
          return (string)targetGuidByName.Invoke(project, new object[] { (object)targetName });
        }
      }
      return "";
    }

    /// <summary>
    /// Generate the body of the iOS post build run script used to upload symbols
    /// </summary>
    /// <returns>Body of the iOS post build run script</returns>
    public static string GetRunScriptBody(IFirebaseConfigurationStorage configurationStorage) {
      string completeRunScriptBody = RunScriptBody;
      completeRunScriptBody = String.Format("{0} -gsp \"{1}\"", RunScriptBody, GooglePlistPath);
      return completeRunScriptBody;
    }

    private static void AddCrashlyticsDevelopmentPlatformToPlist(string plistPath) {
      string unityVersion = Application.unityVersion;
      Debug.Log(String.Format("Adding Unity Editor Version ({0}) to the Xcode project's Info.plist", unityVersion));
      PlistDocument plist = new PlistDocument();

      plist.ReadFromFile(plistPath);
      plist.root.SetString(CRASHLYTICS_UNITY_VERSION_KEY, unityVersion);
      plist.WriteToFile(plistPath);
    }
  }
}
