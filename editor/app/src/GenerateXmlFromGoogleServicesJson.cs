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
//
// This Unity script performs various transformations on the Firebase config files,
// google-services.json and GoogleService-Info.plist.  When building for Android,
// it converts the google-services.json file into the resource file googleservices.xml.
//
// It also creates the google-services-desktop.json file, (used when building on
// desktop or running in the editor) based on either the google-services.json or
// GoogleService-Info.plist file, depending on your current build target.  (iOS+
// targets will prefer the GoogleService-Info.plist file, while Android build
// targets will prefer google-services.json.)
//
// Usage:
//   - Put this script, `generate_xml_from_google_services_json.exe`,
//     and `generate_xml_from_google_services_json.py` into the
//     `Assets/FirebaseUnityEditor/Editor` directory of your project.
//   - Copy the `google-services.json` file you downloaded from the
//     Firebase console anywhere into your `Assets` directory.  (Or
//     alternately, the GoogleService-Info.plist file.)
//   - The google-services-desktop.json file will automatically be
//     created in `Assets/StreamingAssets/`.
//   - If targeting Android, the googleservices.xml file will automatically
//     be created in `Assets/Plugins/Android/google-play-services/res/values`.

namespace Firebase.Editor {

  using Google;
  using GooglePlayServices;
  using System;
  using System.Collections.Generic;
  using System.IO;
  using UnityEngine;
  using UnityEditor;

  /// <summary>
  /// Asset processor that generates the Firebase configuration file from google-services.json or
  /// GoogleService-Info.plist files.
  /// </summary>
  [InitializeOnLoad]
  public class GenerateXmlFromGoogleServicesJson : AssetPostprocessor {
    private static PythonExecutor resourceGenerator =
      new PythonExecutor(Path.Combine(Path.Combine("Assets", "Firebase"), "Editor"),
                         "generate_xml_from_google_services_json.py",
                         "8f18ed76c0f04ce0a65736104f913ef8",
                         "generate_xml_from_google_services_json.exe",
                         "ae88c0972b7448b5b36def1716f1d711");

    // Hard coded directories and file names.
    private static string GOOGLE_SERVICES_FILE_BASENAME = "google-services";
    private static string GOOGLE_SERVICES_INPUT_FILE =
        GOOGLE_SERVICES_FILE_BASENAME + ".json";
    private static string GOOGLE_SERVICES_OUTPUT_FILE =
        GOOGLE_SERVICES_FILE_BASENAME + ".xml";
    private static string GOOGLE_SERVICES_BASE_OUTPUT_DIRECTORY =
        Path.Combine(Utility.FIREBASE_ANDROID_PLUGIN_BASE_DIRECTORY, "FirebaseApp.androidlib");
    private static string GOOGLE_SERVICES_OUTPUT_DIRECTORY =
        Path.Combine(GOOGLE_SERVICES_BASE_OUTPUT_DIRECTORY, Utility.ANDROID_RESOURCE_VALUE_DIRECTORY);
    private static string GOOGLE_SERVICES_OUTPUT_PATH =
        Path.Combine(GOOGLE_SERVICES_OUTPUT_DIRECTORY,
                     GOOGLE_SERVICES_OUTPUT_FILE);

    private static string GOOGLE_SERVICE_INFO_FILE_BASENAME = "GoogleService-Info";
    private static string GOOGLE_SERVICE_INFO_INPUT_FILE =
        GOOGLE_SERVICE_INFO_FILE_BASENAME + ".plist";

    private static string GOOGLE_SERVICES_DESKTOP_OUTPUT_FILE =
        GOOGLE_SERVICES_FILE_BASENAME + "-desktop.json";
    private static string GOOGLE_SERVICES_DESKTOP_OUTPUT_DIRECTORY =
        Path.Combine("Assets", "StreamingAssets");

    private static string GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH =
        Path.Combine(GOOGLE_SERVICES_DESKTOP_OUTPUT_DIRECTORY,
                     GOOGLE_SERVICES_DESKTOP_OUTPUT_FILE);

    // Used to parse the output of the command line tool.
    private static char[] NEWLINE_CHARS = new char[] { '\r', '\n' };
    private static char[] FIELD_DELIMITER = new char[] { '=' };
    // This flag, when set to true stops/guards the dialog from spamming the user.
    private static bool spamguard;

    // Attempt to generate Google Services resources when this class loads.
    static GenerateXmlFromGoogleServicesJson() {
        // Delay initialization until the editor is not in play mode.
        EditorInitializer.InitializeOnMainThread(
            condition: () => {
                return !EditorApplication.isPlayingOrWillChangePlaymode;
            }, initializer: () => {
                // We shouldn't be modifying assets on load, so wait for first editor update.
                CheckConfiguration();

                PlayServicesResolver.BundleIdChanged -= OnBundleIdChanged;
                PlayServicesResolver.BundleIdChanged += OnBundleIdChanged;

                return true;

            }, name: "GenerateXmlFromGoogleServicesJson");
    }

    // Whether the XML-generation side of this component is enabled.
    // (The creation of google-services-desktop.json is always enabled,
    // because we never know when they might want to go into emulation.)
    private static bool XMLGenerationEnabled {
      get {
        return (EditorUserBuildSettings.activeBuildTarget ==
                BuildTarget.Android) &&
            EditorPrefs.GetBool("Firebase.GenerateGoogleServicesXml", true);
      }
    }

    // Delegate which logs a message.
    private delegate void LogMessage(string message);

    // Method which logs nothing.
    private static void LogNoMessage(string message) { }

    // Log a message if this component is enabled.
    private static void LogInfoIfEnabled(string message) {
      if (XMLGenerationEnabled) Debug.Log(message);
    }

    // Log a warning if this component is enabled.
    private static void LogWarningIfEnabled(string message) {
      if (XMLGenerationEnabled) Debug.LogWarning(message);
    }

    // Log an error if this component is enabled.
    private static void LogErrorIfEnabled(string message) {
      if (XMLGenerationEnabled) Debug.LogError(message);
    }

    // Get the application ID for the Android build target.
    private static string GetAndroidApplicationId() {
      return UnityCompat.GetApplicationId(BuildTarget.Android);
    }

    // Search for files to process on startup.
    private static void UpdateJson(bool ignoreModificationDate,
                                   LogMessage logMessageForNoConfigFiles = null,
                                   LogMessage logMessageForMissingBundleId = null) {
      string googleServicesFile = FindGoogleServicesFile(ConfigFileType.Json,
             logMessageForNoConfigFiles: LogNoMessage,
             logMessageForMissingBundleId: LogNoMessage);

      string googleServiceInfoFile = FindGoogleServicesFile(ConfigFileType.Plist,
             logMessageForNoConfigFiles: LogNoMessage,
             logMessageForMissingBundleId: LogNoMessage);

      // Regenerate the XML for Android systems.
      // (Will do nothing if we're not targeting Android.)
      if (googleServicesFile != null) {
        GenerateXmlResources(googleServicesFile, ignoreModificationDate);
      }

      // Here, the script will attempt to generate a google-services-desktop file,
      // based on available package files.  If either a google-services.json file,
      // or a GoogleService-Info.plist file is available, (and has a bundle ID that
      // matches the project) then it will be used to generate the desktop package
      // file.

      // If the project is set to build for Android, the google-services.json file
      // is preferred.  If it is set to build for iOS+, the GoogleServices-Info.plist
      // file is preferred.  When set to standalone desktop, it tries to match the
      // current bundleid, and if it can't, it takes any plist/json file it can find.

      bool foundAtLeastOneFile = googleServicesFile != null || googleServiceInfoFile != null;

      if(!foundAtLeastOneFile) {
        // If we didn't find any files, and we're running in the editor (which is a given since
        // we're in an AssetPreprocessor), then try again, except this time we'll just accept
        // anything.
        string desktopFile = FindGoogleServicesFile(ConfigFileType.Any,
               mode: FindGoogleServicesFileMode.ReturnAll,
               logMessageForNoConfigFiles: LogNoMessage,
               logMessageForMissingBundleId: LogNoMessage);
        if (desktopFile != null) {
          foundAtLeastOneFile = true;
          if (IsFileOfType(desktopFile, ConfigFileType.Json)) {
            googleServicesFile = desktopFile;
          } else if (IsFileOfType(desktopFile, ConfigFileType.Plist)) {
            googleServiceInfoFile = desktopFile;
          }
          foundAtLeastOneFile = googleServicesFile != null || googleServiceInfoFile != null;
        }
      }

      if (foundAtLeastOneFile) {
        // Regenerate google-services-desktop file.
        switch (EditorUserBuildSettings.selectedBuildTargetGroup) {
          case BuildTargetGroup.iOS:
          case BuildTargetGroup.tvOS:
            // If we're on iOS+, generate the desktop file from the plist, if possible.
            if (googleServiceInfoFile != null) {
              CreateDesktopJsonFromPlist(googleServiceInfoFile);
            } else if (googleServicesFile != null) {
              CreateDesktopJsonFromJson(googleServicesFile);
            }
            break;
          case BuildTargetGroup.Android:
          case BuildTargetGroup.Standalone:
          default:
            // Anywhere else, favor the Android json.
            if (googleServicesFile != null) {
              CreateDesktopJsonFromJson(googleServicesFile);
            } else if (googleServiceInfoFile != null) {
              CreateDesktopJsonFromPlist(googleServiceInfoFile);
            }
            break;
        }
      } else {
        Debug.LogWarning(DocRef.CouldNotFindPlistOrJson);
        if (logMessageForNoConfigFiles != null) {
          Measurement.ReportWithBuildTarget("generateconfig/failed/noconfig", null,
                                            "Config File Missing");
        }
        logMessageForNoConfigFiles = logMessageForNoConfigFiles ?? LogErrorIfEnabled;
        logMessageForNoConfigFiles(
            String.Format(DocRef.GoogleServicesFileBundleIdMissing,
                GetAndroidApplicationId(),
                (EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.iOS ||
                 EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.tvOS) ?
                    "GoogleService-Info.plist" : "google-services.json",
                String.Join("\n", BundleIdsFromBundleIdsByConfigFile(
                    ConfigFileDirectory).ToArray()),
                Link.AndroidAddApp));
      }
    }

    // Check the build configuration on startup.
    // This is called via the ApplicationUpdate callback in Unity 4.x because
    // checking the asset list in the constructor will crash it.  So we do it
    // on an update loop, and then immediately remove ourselves from the update
    // listener.
    private static void CheckConfiguration() {
      UpdateConfigFileDirectory();

      if (XMLGenerationEnabled) {
        // If we're generating XML (i. e. Android build) then we check the
        // project ID against existing Json files, and try to convince them to
        // change the bundle ID if none of them match.
        UpdateJsonWithBundleIdChooserDialog(GetAndroidApplicationId(), false);
      } else {
        // Even if not on on Android, we still need to check the status of
        // our json files for desktop/editor playback.
        UpdateJson(false);
      }
    }

    // Backing store for ConfigFileDirectory.
    private static SortedDictionary<string, List<string>> configFileDirectory = null;
    // Lock for configFileDirectory.
    private static object configFileDirectoryLock = new object();

    // Directory of all the config files in the project.  (google-services.json,
    // or GoogleService-Info.plist)  The keys are the file names, and the values
    // are lists of bundle IDs contained in them.  (plist files will only contain
    // one.)
    private static SortedDictionary<string, List<string>> ConfigFileDirectory {
      get {
        lock (configFileDirectoryLock) {
          if (configFileDirectory == null) UpdateConfigFileDirectory();
          return configFileDirectory;
        }
      }
    }

    // Goes through all the files in the asset database, finds any of them that are
    // google-services.json, or GoogleService-Info.plist files, parses them, and then
    // throws them all into dictionary, organized by bundleId.
    // Important!  This needs to be called before any FindGoogleServicesFile()
    // calls, because FindGoogleServicesFile just looks through the cached
    // list generated by this function.
    private static void UpdateConfigFileDirectory() {
      configFileDirectory = new SortedDictionary<string, List<string>>();
      foreach (var guid in AssetDatabase.FindAssets("google", new [] { "Assets"})) {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (Path.GetFileName(path) == GOOGLE_SERVICES_INPUT_FILE ||
            Path.GetFileName(path) == GOOGLE_SERVICE_INFO_INPUT_FILE) {
          configFileDirectory[path] = null;
        }
      }

      foreach (var path in new List<string>(ConfigFileDirectory.Keys)) {
        configFileDirectory[path] = ReadBundleIds(path);
      }
    }

    // Retrive a sorted list of all available bundle IDs from a dictionary of bundle IDs bucketed
    // by config file name.
    private static List<string> BundleIdsFromBundleIdsByConfigFile(
            SortedDictionary<string, List<string>> bundleIdsByConfigFile) {
      var bundleIds = new SortedDictionary<string, string>();
      foreach (var configFileBundleIds in bundleIdsByConfigFile.Values) {
        foreach (var bundleId in configFileBundleIds) {
          bundleIds[bundleId] = bundleId;
        }
      }
      return new List<string>(bundleIds.Keys);
    }

    enum ConfigFileType {
      Plist,
      Json,
      Any
    }

    // Utility function to check if a file is a json or plist file.
    // (We can't just do strict name compares, because they will often have
    // paths.)
    private static bool IsFileOfType(string fileName, ConfigFileType fileType) {
      switch (fileType) {
        case ConfigFileType.Json:
          return fileName.EndsWith(".json");
        case ConfigFileType.Plist:
          return fileName.EndsWith(".plist");
        case ConfigFileType.Any:
          return true;
        default:
          return false;
      }
    }

    // Different behaviors for FindGoogleServiceFiles.
    enum FindGoogleServicesFileMode {
      // Only return service files if they have a matching bundleId.
      ReturnBundleIdMatches,
      // Return all bundle files, regardless of their bundleId.
      // (Note that other qualifiers like fileType are still honored.)
      ReturnAll
    }

    // Find the .json file which matches the specified bundle ID within the ConfigFileDirectory
    // dictionary.
    private static string FindGoogleServicesFile(
            ConfigFileType fileType,
            string bundleId = null,
            FindGoogleServicesFileMode mode = FindGoogleServicesFileMode.ReturnBundleIdMatches,
            LogMessage logMessageForNoConfigFiles = null,
            LogMessage logMessageForMissingBundleId = null) {
      bundleId = bundleId ?? GetAndroidApplicationId();
      if (ConfigFileDirectory.Count == 0) {
        string message = String.Format(DocRef.GoogleServicesAndroidFileMissing,
            GOOGLE_SERVICES_INPUT_FILE, GOOGLE_SERVICES_OUTPUT_FILE,
            Link.AndroidSetup);
        logMessageForNoConfigFiles = logMessageForNoConfigFiles ?? LogErrorIfEnabled;
        logMessageForNoConfigFiles(message);
        return null;
      }

      string selectedFile = null;
      // Search files for the first file matching the project's bundle identifier.
      int fileCount = 0;
      foreach (var configFileAndBundleIds in ConfigFileDirectory) {
        if (IsFileOfType(configFileAndBundleIds.Key, fileType) &&
            (mode == FindGoogleServicesFileMode.ReturnAll ||
                (new HashSet<string>(configFileAndBundleIds.Value)).Contains(bundleId))) {
          selectedFile = configFileAndBundleIds.Key;
          fileCount++;
        }
      }
      if (selectedFile == null) {
        // If no config file is found log an error.
        logMessageForMissingBundleId = logMessageForMissingBundleId ?? LogErrorIfEnabled;
        logMessageForMissingBundleId(
            String.Format(DocRef.GoogleServicesFileBundleIdMissing,
                bundleId,
                fileType == ConfigFileType.Json ? "google-services.json" :
                    "GoogleService-Info.plist",
                String.Join("\n", BundleIdsFromBundleIdsByConfigFile(
                    ConfigFileDirectory).ToArray()),
                Link.AndroidAddApp));
      } else if (fileCount > 1 && mode != FindGoogleServicesFileMode.ReturnAll) {
        // If more than one config file is present notify the user of the file we selected.
        LogInfoIfEnabled(String.Format(DocRef.GoogleServicesFileMultipleFiles,
            fileType == ConfigFileType.Plist ?
                GOOGLE_SERVICES_INPUT_FILE : GOOGLE_SERVICE_INFO_INPUT_FILE,
                selectedFile, bundleId,
            String.Join("\n", (new List<string>(ConfigFileDirectory.Keys)).ToArray())));
      }
      return selectedFile;
    }

    // Read bundle IDs from the config file.
    private static List<string> ReadBundleIds(string googleServicesFile) {
      // If the file we're reading is a plist file, we have to add a flag on
      // to the tool's command-line invocation.

      var args = new List<string> {
        "-i",
        String.Format("\"{0}\"", googleServicesFile),
        "-l",
      };
      if (IsFileOfType(googleServicesFile, ConfigFileType.Plist)) args.Add("--plist");

      var bundleIds = new SortedDictionary<string, string>();
      var result = RunResourceGenerator(args, googleServicesFile, showCommandLine: false);
      if (result.exitCode == 0) {
        foreach (var bundleId in result.stdout.Split(NEWLINE_CHARS)) {
          if (String.IsNullOrEmpty(bundleId)) {
            continue;
          }
          bundleIds[bundleId] = bundleId;
        }
      } else {
        LogWarningIfEnabled(result.message);
      }
      return new List<string>(bundleIds.Keys);
    }

    // Read the project fields from the config file.
    private static Dictionary<string, string> ReadProjectFields(
            string googleServicesFile) {
      var fields = new Dictionary<string, string>();
      var result = RunResourceGenerator(new List<string> {
          "-i",
          String.Format("\"{0}\"", googleServicesFile),
          "-f",
        },
        googleServicesFile,
        showCommandLine: false);
      if (result.exitCode == 0) {
        foreach (var line in result.stdout.Split(NEWLINE_CHARS)) {
          string[] tokens = line.Split(FIELD_DELIMITER);
          if (tokens.Length == 2) {
            fields[tokens[0]] = tokens[1];
          }
        }
      } else {
        LogWarningIfEnabled(result.message);
      }
      return fields;
    }

    // Used by the Android build window:
    internal static Dictionary<string, string> ReadProjectFields() {
      string googleServicesFile = FindGoogleServicesFile(ConfigFileType.Json);
      if (googleServicesFile != null) {
        return ReadProjectFields(googleServicesFile);
      }
      return new Dictionary<string, string>();
    }

    // Called when the bundle ID is updated.
    private static void OnBundleIdChanged(
            object sender,
            PlayServicesResolver.BundleIdChangedEventArgs args) {
      UpdateJsonWithBundleIdChooserDialog(args.BundleId, true);
    }

    // Check the current specified bundle ID against the config files in the project popping up
    // a dialog that allows the user to choose a valid bundle ID
    private static void UpdateJsonWithBundleIdChooserDialog(string bundleId,
                                                            bool ignoreModificationDate) {
      ConfigFileType fileType;
      if (EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.Android) {
        fileType = ConfigFileType.Json;
      } else if (EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.iOS ||
                 EditorUserBuildSettings.selectedBuildTargetGroup == BuildTargetGroup.tvOS) {
        fileType = ConfigFileType.Plist;
      } else {
        // Don't need to be here, if we're not on iOS+ or Android.
        return;
      }
      var configFile = FindGoogleServicesFile(fileType, bundleId,
                                              logMessageForNoConfigFiles: LogNoMessage,
                                              logMessageForMissingBundleId: LogNoMessage);
      if (configFile != null || UnityCompat.InBatchMode) {
        UpdateJson(UnityCompat.InBatchMode);
        return;
      }
      var bundleIds = BundleIdsFromBundleIdsByConfigFile(ConfigFileDirectory);
      if (spamguard || bundleIds.Count == 0) return;

      ChooserDialog.Show(
          "Please fix your Bundle ID",
          "Select a valid Bundle ID from your Firebase configuration.",
          String.Format("Your bundle ID {0} is not present in your " +
                        "Firebase configuration.  A mismatched bundle ID will " +
                        "result in your application to fail to initialize.\n\n" +
                        "New Bundle ID:",
                        bundleId),
          bundleIds.ToArray(),
          0,
          "Apply",
          "Cancel",
          selectedBundleId => {
            if (!String.IsNullOrEmpty(selectedBundleId)) {
              // If we have a valid value, the user hit apply.
              UnityCompat.SetApplicationId(BuildTarget.Android,
                                           selectedBundleId);
              Measurement.ReportWithBuildTarget("bundleidmismatch/apply", null,
                                                "Mismatched Bundle ID: Apply");
            } else {
              Measurement.ReportWithBuildTarget("bundleidmismatch/cancel", null,
                                                "Mismatched Bundle ID: Cancel");
              // If the user hits cancel, we disable the dialog to
              // avoid spamming the user.
              spamguard = true;
            }
            UpdateJson(true);
          });
    }

    // Run the resource generator script.
    private static CommandLine.Result RunResourceGenerator(
            IEnumerable<string> arguments, string inputPath, bool showCommandLine = true) {
      // Start the import process and wait for it to return.
      string commandLineForLog = resourceGenerator.GetCommand(arguments);
      var result = new CommandLine.Result { exitCode = 1 };
      try {
        result = resourceGenerator.Run(resourceGenerator.GetArguments(arguments));
      } catch (System.ComponentModel.Win32Exception exception) {
        Measurement.ReportWithBuildTarget("generateconfig/failed/toolmissing", null,
                                          "Config File Tool Missing");
        Debug.LogError(
            String.Format(DocRef.GoogleServicesToolMissing,
                          resourceGenerator.Executable, GOOGLE_SERVICES_OUTPUT_FILE, inputPath,
                          exception.ToString()));
        return result;
      }
      if (result.exitCode == 0) {
        Measurement.ReportWithBuildTarget("generateconfig/success", null,
                                          "Config File Generation Successful");
        if (showCommandLine) {
          Debug.Log(
              String.Format(DocRef.GoogleServicesAndroidGenerateXml,
                  GOOGLE_SERVICES_OUTPUT_PATH, inputPath, commandLineForLog));
        }
      } else {
        Measurement.ReportWithBuildTarget("generateconfig/failed/toolfailed", null,
                                          "Config File Tool Failed");
        Debug.LogError(
            String.Format(DocRef.GoogleServicesAndroidGenerationFailed,
                GOOGLE_SERVICES_OUTPUT_FILE, inputPath, commandLineForLog,
                result.stdout + "\n" + result.stderr + "\n"));
      }
      return result;
    }

    // Generate Android resources from the json config file.
    private static void GenerateXmlResources(string googleServicesFile,
                                             bool ignoreModificationDate) {
      if (!XMLGenerationEnabled) return;
      Measurement.ReportWithBuildTarget("generateconfig", null, "Generate Config");

      // Create the output directory.
      string projectDir = Utility.GetProjectDir();
      string outputDir = Path.Combine(projectDir,
                                      GOOGLE_SERVICES_OUTPUT_DIRECTORY);
      if (!Directory.Exists(outputDir)) {
        try {
          Directory.CreateDirectory(outputDir);
        } catch (Exception e) {
          Measurement.ReportWithBuildTarget("generateconfig/failed/ioerror", null,
                                            "Config File Generation Failed");
          Debug.LogError(
              String.Format(DocRef.GoogleServicesAndroidGenerationFailed,
                  GOOGLE_SERVICES_OUTPUT_PATH, googleServicesFile,
                  String.Format(DocRef.UnableToCreateDirectory, outputDir), ""));
          Debug.LogException(e);
          return;
        }
      }

      // Run the script to process `asset` into our fixed output location.
      string inputFile = Path.Combine(projectDir, googleServicesFile);
      string outputFile = Path.Combine(projectDir,
                                       GOOGLE_SERVICES_OUTPUT_PATH);
      // If the output file exists and it's up to date, don't regenerate
      // it.
      if (ignoreModificationDate ||
          !File.Exists(outputFile) ||
          File.GetLastWriteTime(outputFile).CompareTo(
              File.GetLastWriteTime(inputFile)) < 0) {
        RunResourceGenerator(new List<string> {
            "-i",
            String.Format("\"{0}\"", inputFile),
            "-o",
            String.Format("\"{0}\"", outputFile),
            "-p",
            String.Format("\"{0}\"", GetAndroidApplicationId()),
          }, inputFile);
      }

      // Generate AndroidManifest.xml and project.properties for generated google-services.xml.
      if (File.Exists(outputFile)) {
          Utility.GenerateAndroidPluginResourceDirectory(
              GOOGLE_SERVICES_BASE_OUTPUT_DIRECTORY, "app");
      }
    }

    private static void CreateDesktopJsonFromPlist(string sourceFilename) {
      string projectDir = Utility.GetProjectDir();
      string inputFile = Path.Combine(projectDir, sourceFilename);
      string outputFile = Path.Combine(projectDir, GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH);

      // If the output file exists and it's up to date, don't regenerate it.
      if (File.Exists(outputFile) &&
          File.GetLastWriteTime(outputFile).CompareTo(
              File.GetLastWriteTime(inputFile)) >= 0) {
        return;
      }

      if (PrepareJsonDirectory()) {
        var result = RunResourceGenerator(new List<string> {
            "-i",
            String.Format("\"{0}\"", inputFile),
            "-o",
            String.Format("\"{0}\"", outputFile),
            "--plist",
          },
          sourceFilename,
          showCommandLine: false);
        if (result.exitCode != 0) {
          Debug.LogError(String.Format(DocRef.CouldNotTranslatePlist, sourceFilename));
        }
      }
    }

    private static void CreateDesktopJsonFromJson(string sourceFilename) {
      string projectDir = Utility.GetProjectDir();
      string inputFile = Path.Combine(projectDir, sourceFilename);
      string outputFile = Path.Combine(projectDir, GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH);

      // If the output file exists and it's up to date, don't regenerate it.
      if (File.Exists(outputFile) &&
          File.GetLastWriteTime(outputFile).CompareTo(
              File.GetLastWriteTime(inputFile)) >= 0) {
        return;
      }

      if (PrepareJsonDirectory()) {
        try {
          // Note:  CopyAsset paths are local to the project, so we don't need
          // to join them with the project directory.
          AssetDatabase.CopyAsset(sourceFilename,
              GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH);
        } catch {
          Debug.LogError(String.Format(DocRef.CouldNotCopyFile,
              Path.Combine(projectDir, sourceFilename),
              Path.Combine(projectDir, GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH)));
        }
      }
    }

    // Makes sure that the json directory is present.
    private static bool PrepareJsonDirectory() {
      // Create the output directory.
      string outputDir = Path.Combine(Utility.GetProjectDir(),
                                      GOOGLE_SERVICES_DESKTOP_OUTPUT_DIRECTORY);
      if (!Directory.Exists(outputDir)) {
        try {
          Directory.CreateDirectory(outputDir);
        } catch (Exception e) {
          Debug.LogError(String.Format(DocRef.UnableToCreateDirectory, outputDir));
          Debug.LogException(e);
          return false;
        }
      }
      return true;
    }

    // Called when any asset is imported, deleted, or moved.
    private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromPath) {
      bool regenerateFirebaseConfig = false;
      // Was a plist or json file imported?
      foreach (string asset in importedAssets) {
        string fileName = Path.GetFileName(asset);
        if (fileName == GOOGLE_SERVICES_INPUT_FILE ||
            fileName == GOOGLE_SERVICE_INFO_INPUT_FILE) {
          regenerateFirebaseConfig = true;
          break;
        }
      }
      // Was a google-services-desktop file deleted?
      foreach (string asset in deletedAssets) {
        if (asset == GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH) {
            regenerateFirebaseConfig = true;
          break;
        }
      }
      // Was a google-services-desktop file moved from the
      // standard location?
      foreach (string asset in movedAssets) {
        if (asset == GOOGLE_SERVICES_DESKTOP_OUTPUT_PATH) {
            regenerateFirebaseConfig = true;
          break;
        }
      }
      // If any of the above are true, we need to regenerate the directory
      // of files, and the google-services-desktop file.
      if (regenerateFirebaseConfig) {
        UpdateConfigFileDirectory();
        spamguard = false;
        UpdateJson(true);
      }
    }

    /// <summary>
    /// Public utility function to force a refresh of the json/plist file.
    /// This will also include a rebuild of the config file directory.
    /// Useful for building while in batch mode.  (Where the update function
    /// is not called.)
    /// </summary>
    public static void ForceJsonUpdate(bool canPromptToChangePackageId = false) {
      spamguard = !canPromptToChangePackageId;
      UpdateConfigFileDirectory();
      UpdateJson(true);
    }
  }

}  // namespace Firebase.Editor
