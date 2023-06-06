/*
 * Copyright 2016 Google LLC
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

using Google;
using GooglePlayServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

[InitializeOnLoad]
internal class XcodeProjectPatcher : AssetPostprocessor {
    // When in the build process the project should be patched.
    private const int BUILD_ORDER_ADD_CONFIG = 1;
    private const int BUILD_ORDER_PATCH_PROJECT = 2;
    // Firebase configuration filename.
    private const string GOOGLE_SERVICES_INFO_PLIST_BASENAME =
        "GoogleService-Info";
    private const string GOOGLE_SERVICES_INFO_PLIST_FILE =
        "GoogleService-Info.plist";

    // Keys this module needs from the config file.
    private static string[] PROJECT_KEYS = new string[] {
        "CLIENT_ID",
        "REVERSED_CLIENT_ID",
        "BUNDLE_ID",
        "PROJECT_ID",
        "STORAGE_BUCKET",
        "DATABASE_URL",
    };

    // Configuration values of PROJECT_KEYS read from the config file.
    private static Dictionary<string, string> configValues =
        new Dictionary<string, string>();
    // Developers can have multiple plist config files each containing a different bundleid.
    // Any one of them could be valid, so when we show a list to select one, it needs to come from parsing
    // every plist file.  Fortunately, we already do this in our scan to find the "correct" config.
    private static HashSet<string> allBundleIds = new HashSet<string>();
    // Path to the previously read config file.
    private static string configFile = null;
    // Flag for avoiding dialog spam
    private static bool spamguard;

    // Whether this component is enabled.
    private static bool Enabled {
        get {
            return (EditorUserBuildSettings.activeBuildTarget ==
                    BuildTarget.iOS ||
                    EditorUserBuildSettings.activeBuildTarget ==
                    BuildTarget.tvOS) && Google.IOSResolver.Enabled;
        }
    }

    static XcodeProjectPatcher() {
        // Delay initialization until the build target is iOS+ and the
        // editor is not in play mode.
        EditorInitializer.InitializeOnMainThread(
            condition: () => {
                return (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ||
                        EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS) &&
                        !EditorApplication.isPlayingOrWillChangePlaymode;
            }, initializer: () => {
                // We attempt to read the config even when the target platform isn't
                // iOS+ as the project settings are surfaced in the settings window.
                Google.IOSResolver.RemapXcodeExtension();
                ReadConfigOnUpdate();
                PlayServicesResolver.BundleIdChanged -= OnBundleIdChanged;
                if (Enabled) {
                    PlayServicesResolver.BundleIdChanged += OnBundleIdChanged;
                    CheckConfiguration();
                }

                return true;
            }, name: "XcodeProjectPatcher");
    }

    // Some versions of Unity 4.x crash when you to try to read the asset database from static
    // class constructors.
    internal static void ReadConfigOnUpdate() {
        ReadConfig(errorOnNoConfig: false);
    }

    // Get the iOS+ bundle / application ID.
    private static string GetIosPlusApplicationId() {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.tvOS) {
            return UnityCompat.GetApplicationId(BuildTarget.tvOS);
        } else {
            return UnityCompat.GetApplicationId(BuildTarget.iOS);
        }
    }

    // Check the editor environment on the first update after loading this
    // module.
    private static void CheckConfiguration() {
        CheckBundleId(GetIosPlusApplicationId());
        CheckBuildEnvironment();
    }

    // Read the configuration file, storing the filename in configFile and
    // values of keys specified by PROJECT_KEYS in configValues.
    internal static void ReadConfig(bool errorOnNoConfig = true, string filename = null) {
        try {
            ReadConfigInternal(errorOnNoConfig, filename: filename);
        } catch (Exception exception) {
            // FileNotFoundException and TypeInitializationException can be
            // thrown *before* ReadConfigInternal is entered if the iOS+ Xcode
            // assembly can't be loaded so we catch them here and only report
            // a warning if this module is enabled and iOS+ is the selected
            // platform.
            if (exception is FileNotFoundException ||
                exception is TypeInitializationException) {
                if (Enabled) {
                    // It's likely we failed to load the iOS Xcode extension.
                    Debug.LogWarning(DocRef.FailedToLoadIOSExtensions);
                }
            } else {
                throw exception;
            }
        }
    }

    // Implementation of ReadConfig().
    // NOTE: This is separate from ReadConfig() so it's possible to catch
    // a missing UnityEditor.iOS.Xcode assembly exception.
    internal static void ReadConfigInternal(bool errorOnNoConfig, string filename = null) {
        configValues = new Dictionary<string, string>();
        configFile = filename ?? FindConfig(errorOnNoConfig: errorOnNoConfig);
        if (configFile == null) {
            return;
        }
        var plist = new UnityEditor.iOS.Xcode.PlistDocument();
        plist.ReadFromString(File.ReadAllText(configFile));
        var rootDict = plist.root;
        foreach (var key in PROJECT_KEYS) {
            var item = rootDict[key];
            if (item == null) continue;
            configValues[key] = item.AsString();
            if (Equals(key, "BUNDLE_ID")) {
              allBundleIds.Add(item.AsString());
            }
        }
    }

    // Get all fields in PROJECT_KEYS previously read from the config file.
    internal static Dictionary<string, string> GetConfig() {
        return configValues;
    }

    // Verify that the build environment *really* supports iOS+.
    private static void CheckBuildEnvironment() {
        // If iOS+ is the selected build target but we're not on a OSX
        // machine report an error as pod installation will fail among other
        // things.
        BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
        if ((buildTarget == BuildTarget.iOS || buildTarget == BuildTarget.tvOS) &&
            Application.platform == RuntimePlatform.WindowsEditor) {
            Debug.LogWarning(DocRef.IOSNotSupportedOnWindows);
        }
    }

    // Called when the bundle ID is updated.
    private static void OnBundleIdChanged(
            object sender,
            PlayServicesResolver.BundleIdChangedEventArgs args) {
        ReadConfig(errorOnNoConfig: false);
        CheckBundleId(GetIosPlusApplicationId());
    }

    // Check the bundle ID
    private static string CheckBundleId(string bundleId,
                                        bool promptUpdate = true,
                                        bool logErrorOnMissingBundleId = true) {
        if (configFile == null) {
            return null;
        }
        var configDict = GetConfig();
        string configBundleId;
        if (!configDict.TryGetValue("BUNDLE_ID", out configBundleId)) {
            return null;
        }
        if (!configBundleId.Equals(bundleId) && logErrorOnMissingBundleId
            && allBundleIds.Count > 0) {
            // Report an error, prompt user to use the bundle ID
            // from the plist.
            string[] bundleIds = allBundleIds.ToArray();
            string errorMessage =
                String.Format(DocRef.GoogleServicesFileBundleIdMissing,
                    bundleId, "GoogleServices-Info.plist", String.Join(", ", bundleIds),
                    Link.IOSAddApp);
            if (promptUpdate && !spamguard) {
                ChooserDialog.Show(
                    "Please fix your Bundle ID",
                    "Select a valid Bundle ID from your Firebase " +
                    "configuration.",
                    String.Format("Your bundle ID {0} is not present in your " +
                                  "Firebase configuration.  A mismatched bundle ID " +
                                  "will result in your application to fail to " +
                                  "initialize.\n\n" +
                                  "New Bundle ID:", bundleId),
                    bundleIds,
                    0,
                    "Apply",
                    "Cancel",
                    selectedBundleId => {
                        if (!String.IsNullOrEmpty(selectedBundleId)) {
                            switch(EditorUserBuildSettings.activeBuildTarget) {
                                case BuildTarget.iOS:
                                    UnityCompat.SetApplicationId(BuildTarget.iOS, selectedBundleId);
                                    break;
                                case BuildTarget.tvOS:
                                    UnityCompat.SetApplicationId(BuildTarget.tvOS, selectedBundleId);
                                    break;
                                default:
                                    throw new Exception("unsupported iOS+ version");
                            }
                        } else {
                            Measurement.ReportWithBuildTarget("bundleidmismatch/cancel", null,
                                                              "Mismatched Bundle ID: Cancel");
                            // If the user hits cancel, we disable the dialog to
                            // avoid spamming the user.
                            spamguard = true;
                            Debug.LogError(errorMessage);
                        }
                        ReadConfig();
                    });
            } else {
                Debug.LogError(errorMessage);
            }
        }
        return configBundleId;
    }

    // Called when any asset is imported, deleted, or moved.
    private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromPath) {
        // We track the config file state even when the target isn't iOS+
        // as the project settings are surfaced in the settings window.
        if (!Enabled) return;
        bool configFilePresent = false;
        foreach (string asset in importedAssets) {
            if (Path.GetFileName(asset) == GOOGLE_SERVICES_INFO_PLIST_FILE) {
                configFilePresent = true;
                break;
            }
        }
        if (configFilePresent) {
            spamguard = false; // Reset our spamguard to show a dialog.
            ReadConfig(errorOnNoConfig: false);
            CheckBundleId(GetIosPlusApplicationId());
        }
    }

    /// <summary>
    /// Search for the Firebase config file.
    /// </summary>
    /// <returns>String with a path to the file if found, null if not
    /// found or more than one config file is present in the project.</returns>
    internal static string FindConfig(bool errorOnNoConfig = true) {
        string previousConfigFile = configFile;
        var plists = new SortedDictionary<string, string>();
        allBundleIds.Clear();

        foreach (var guid in AssetDatabase.FindAssets(
                     String.Format("{0}", GOOGLE_SERVICES_INFO_PLIST_BASENAME),
                     new [] { "Assets"})) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) == GOOGLE_SERVICES_INFO_PLIST_FILE) {
                plists[path] = path;
            }
        }
        string[] files = new string[plists.Keys.Count];
        plists.Keys.CopyTo(files, 0);
        string selectedFile = files.Length >= 1 ? files[0] : null;
        if (files.Length == 0) {
            if (errorOnNoConfig && Enabled) {
                Debug.LogError(
                    String.Format(DocRef.GoogleServicesIOSFileMissing,
                        GOOGLE_SERVICES_INFO_PLIST_FILE, Link.IOSAddApp));
            }
        } else if (files.Length > 1) {
            var bundleId = GetIosPlusApplicationId();
            string selectedBundleId = null;
            // Search files for the first file matching the project's bundle identifier.
            foreach (var filename in files) {
                ReadConfig(filename: filename);
                var currentBundleId = CheckBundleId(bundleId, logErrorOnMissingBundleId: false);
                selectedBundleId = selectedBundleId ?? currentBundleId;
                if (currentBundleId == bundleId) {
                    selectedFile = filename;
                    selectedBundleId = bundleId;
                }
            }
            // If the config file changed, warn the user about the selected file.
            if (String.IsNullOrEmpty(previousConfigFile) ||
                !selectedFile.Equals(previousConfigFile)) {
                Debug.LogWarning(
                    String.Format(DocRef.GoogleServicesFileMultipleFiles,
                        GOOGLE_SERVICES_INFO_PLIST_FILE,
                        selectedFile, selectedBundleId, String.Join("\n", files)));
            }
        }
        return selectedFile;
    }

    /// <summary>
    /// Add the Firebase config file to the generated Xcode project.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_ADD_CONFIG)]
    internal static void OnPostProcessAddGoogleServicePlist(
            BuildTarget buildTarget, string pathToBuiltProject) {
        if (!Enabled) return;
        string platform = (buildTarget == BuildTarget.iOS) ? "iOS" : "tvOS";
        Measurement.analytics.Report("ios/xcodepatch",
            platform + " Xcode Project Patcher: Start");
        AddGoogleServicePlist(buildTarget, pathToBuiltProject);
    }

    // Implementation of OnPostProcessAddGoogleServicePlist().
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    internal static void AddGoogleServicePlist(
            BuildTarget buildTarget, string pathToBuiltProject) {
        ReadConfig();
        if (configFile == null) {
            Measurement.analytics.Report("ios/xcodepatch/config/failed",
                                         "Add Firebase Configuration File Failure");
            return;
        }

        CheckBundleId(GetIosPlusApplicationId(), promptUpdate: false);

        // Copy the config file to the Xcode project folder.
        string configFileBasename = Path.GetFileName(configFile);
        File.Copy(configFile, Path.Combine(pathToBuiltProject,
                                           configFileBasename), true);

        // Add the config file to the Xcode project.
        string pbxprojPath =
            Google.IOSResolver.GetProjectPath(pathToBuiltProject);
        var project = new UnityEditor.iOS.Xcode.PBXProject();
        project.ReadFromString(File.ReadAllText(pbxprojPath));
        foreach (var targetGuid in
                 Google.IOSResolver.GetXcodeTargetGuids(project, includeAllTargets: true)) {
            project.AddFileToBuild(
                targetGuid,
                project.AddFile(configFileBasename,
                                configFileBasename,
                                UnityEditor.iOS.Xcode.PBXSourceTree.Source));
        }
        File.WriteAllText(pbxprojPath, project.WriteToString());
        Measurement.analytics.Report("ios/xcodepatch/config/success",
                                     "Add Firebase Configuration File Successful");
    }

    /// <summary>
    /// Patch the Xcode project with Firebase settings and workarounds.
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_PATCH_PROJECT)]
    internal static void OnPostProcessPatchProject(
            BuildTarget buildTarget, string pathToBuiltProject) {
        if (!Enabled) return;
        ReadAndApplyFirebaseConfig(buildTarget, pathToBuiltProject);
        ApplyNsUrlSessionWorkaround(buildTarget, pathToBuiltProject);
    }

    // Apply the Firebase configuration to the Xcode project.
    // NOTE: This is separate from the post-processing method to prevent the
    // Mono runtime from loading the Xcode API before calling the post
    // processing step.
    internal static void ReadAndApplyFirebaseConfig(
            BuildTarget buildTarget, string pathToBuiltProject) {
        // DLLs that trigger post processing by this method.
        // We use the DLL names here as:
        // * Pod dependencies may not be present if frameworks are manually
        //   being included.
        // * DLLs may not be loaded in the Unity Editor App domain so we can't
        //   detect the module using reflection.
        var invitesDll = "Firebase.Invites.dll";
        var dllsThatRequireReversedClientId = new HashSet<string> {
            "Firebase.Auth.dll",
            "Firebase.DynamicLinks.dll",
            invitesDll
        };
        bool reversedClientIdRequired = false;
        bool invitesPresent = false;
        // Search the asset database for the DLLs to handle projects where
        // users move files around.
        foreach (var assetGuid in AssetDatabase.FindAssets("t:Object")) {
            var filename = Path.GetFileName(
                AssetDatabase.GUIDToAssetPath(assetGuid));
            if (dllsThatRequireReversedClientId.Contains(filename)) {
                reversedClientIdRequired = true;
                invitesPresent = filename == invitesDll;
            }
        }
        if (!(invitesPresent || reversedClientIdRequired)) {
            return;
        }
        ReadConfig();
        // Read required data from the config file.
        var configDict = GetConfig();
        if (configDict.Count == 0) return;
        string reversedClientId = null;
        string bundleId = null;
        if (!configDict.TryGetValue("REVERSED_CLIENT_ID",
                                    out reversedClientId)) {
            Measurement.analytics.Report("ios/xcodepatch/reversedclientid/failed",
                                         "Add Reversed Client ID Failed");
            Debug.LogError(
                String.Format(DocRef.PropertyMissingForGoogleSignIn,
                    GOOGLE_SERVICES_INFO_PLIST_FILE, "REVERSED_CLIENT_ID",
                    Link.IOSAddApp));
        }
        if (!configDict.TryGetValue("BUNDLE_ID", out bundleId)) {
            Debug.LogError(
                String.Format(DocRef.PropertyMissingForGoogleSignIn,
                    GOOGLE_SERVICES_INFO_PLIST_FILE, "BUNDLE_ID", Link.IOSAddApp));
        }

        // Update the Xcode project's Info.plist.
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new UnityEditor.iOS.Xcode.PlistDocument();
        plist.ReadFromString(File.ReadAllText(plistPath));
        var rootDict = plist.root;
        UnityEditor.iOS.Xcode.PlistElementArray urlTypes = null;
        if (rootDict.values.ContainsKey("CFBundleURLTypes")) {
            urlTypes = rootDict["CFBundleURLTypes"].AsArray();
        }
        if (urlTypes == null) {
            urlTypes = rootDict.CreateArray("CFBundleURLTypes");
        }
        if (reversedClientId != null) {
            var googleType = urlTypes.AddDict();
            googleType.SetString("CFBundleTypeRole", "Editor");
            googleType.SetString("CFBundleURLName", "google");
            googleType.CreateArray("CFBundleURLSchemes").AddString(
                reversedClientId);
            Measurement.analytics.Report("ios/xcodepatch/reversedclientid/success",
                                         "Add Reversed Client ID Successful");
        }
        if (bundleId != null) {
            var bundleType = urlTypes.AddDict();
            bundleType.SetString("CFBundleTypeRole", "Editor");
            bundleType.SetString("CFBundleURLName", bundleId);
            bundleType.CreateArray("CFBundleURLSchemes").AddString(bundleId);
        }
        // Invites needs usage permission to access Contacts.
        if (invitesPresent) {
          if (!rootDict.values.ContainsKey("NSContactsUsageDescription")) {
            rootDict.SetString("NSContactsUsageDescription", "Invite others to use the app.");
          }
        }
        // Finished, Write to File
        File.WriteAllText(plistPath, plist.WriteToString());
    }

    // Patch the Xcode project to workaround a bug in NSURLSession in some iOS versions.
    internal static void ApplyNsUrlSessionWorkaround(
            BuildTarget buildTarget, string pathToBuiltProject) {
        const string WorkaroundNotAppliedMessage =
            "Unable to apply NSURLSession workaround. If " +
            "NSAllowsArbitraryLoads is set to a different value than " +
            "NSAllowsArbitraryLoadsInWebContent in your Info.plist " +
            "network operations will randomly fail on some versions of iOS";
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new UnityEditor.iOS.Xcode.PlistDocument();
        plist.ReadFromString(File.ReadAllText(plistPath));
        var rootDict = plist.root;
        // Some versions of iOS exhibit buggy behavior when using NSURLSession if
        // NSAppTransportSecurity is present and NSAllowsArbitraryLoadsInWebContent is not
        // set.  This manifests as connections being closed with error
        // "Domain=kCFErrorDomainCFNetwork Code=-1005".  The theory is that when
        // NSAllowsArbitraryLoadsInWebContent isn't initialized the framework is reading
        // uninitialized memory which sporadically blocks encrypted connections opened with
        // NSURLSession.
        if (rootDict.values.ContainsKey("NSAppTransportSecurity")) {
            UnityEditor.iOS.Xcode.PlistElementDict transportSecurity;
            try {
                transportSecurity = rootDict["NSAppTransportSecurity"].AsDict();
            } catch (InvalidCastException e) {
                Debug.LogWarning(String.Format(
                    "Unable to parse NSAppTransportSecurity as a dictionary. ({0})\n{1}\n" +
                    "To fix this issue make sure NSAppTransportSecurity is a dictionary in your " +
                    "Info.plist", e, WorkaroundNotAppliedMessage));
                Measurement.analytics.Report("ios/xcodepatch/nsurlsessionworkaround/failed",
                                             "NSURLSession workaround Failed");
                return;
            }
            if (transportSecurity.values.ContainsKey("NSAllowsArbitraryLoads") &&
                !transportSecurity.values.ContainsKey("NSAllowsArbitraryLoadsInWebContent")) {
                try {
                    transportSecurity.SetBoolean(
                        "NSAllowsArbitraryLoadsInWebContent",
                        transportSecurity["NSAllowsArbitraryLoads"].AsBoolean());
                } catch (InvalidCastException e) {
                    Debug.LogWarning(String.Format(
                        "Unable to parse NSAllowsArbitraryLoads as a boolean value. ({0})\n{1}\n" +
                        "To fix this problem make sure NSAllowsArbitraryLoads is YES or NO in " +
                        "your Info.plist or NSAllowsArbitraryLoadsInWebContent is set.",
                        e, WorkaroundNotAppliedMessage));
                    Measurement.analytics.Report("ios/xcodepatch/nsurlsessionworkaround/failed",
                                                 "NSURLSession workaround Failed");
                    return;
                }
                Measurement.analytics.Report("ios/xcodepatch/nsurlsessionworkaround/success",
                                                 "NSURLSession workaround Successful");

            }
        }

        File.WriteAllText(plistPath, plist.WriteToString());
    }

}

}  // namespace Firebase.Editor
