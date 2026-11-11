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
//   - Copy the `google-services.json` file you downloaded from the
//     Firebase console anywhere into your `Assets` directory.  (Or
//     alternately, the GoogleService-Info.plist file.)
//   - The google-services-desktop.json file will automatically be
//     created in `Assets/StreamingAssets/`.
//   - If targeting Android, the google-services.xml file will automatically
//     be created in `Assets/Plugins/Android/FirebaseApp.androidlib/res/values`.

namespace Firebase.Editor {

  using Google;
  using Google.MiniJSON;
  using GooglePlayServices;
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using System.Xml;
  using UnityEngine;
  using UnityEditor;

  /// <summary>
  /// Asset processor that generates the Firebase configuration file from google-services.json or
  /// GoogleService-Info.plist files.
  /// </summary>
  [InitializeOnLoad]
  public class GenerateXmlFromGoogleServicesJson : AssetPostprocessor {
    private const int OAUTH_CLIENT_TYPE_ANDROID_APP = 1;
    private const int OAUTH_CLIENT_TYPE_WEB = 3;
    private const string ANDROID_TOOLS_NAMESPACE = "http://schemas.android.com/tools";

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

    private static string ResolveConfigFilePath(string configPath) {
      if (String.IsNullOrEmpty(configPath) || Path.IsPathRooted(configPath)) {
        return configPath;
      }
      return Path.Combine(Utility.GetProjectDir(), configPath);
    }

    private static Dictionary<string, object> GetMap(Dictionary<string, object> map, string key) {
      object value;
      if (map != null && map.TryGetValue(key, out value)) {
        return value as Dictionary<string, object>;
      }
      return null;
    }

    private static List<object> GetList(Dictionary<string, object> map, string key) {
      object value;
      if (map != null && map.TryGetValue(key, out value)) {
        return value as List<object>;
      }
      return null;
    }

    private static string GetString(Dictionary<string, object> map, string key) {
      object value;
      if (map != null && map.TryGetValue(key, out value) && value != null) {
        return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
      }
      return null;
    }

    private static object ReadPlistValue(XmlNode node) {
      if (node == null) return null;
      switch (node.Name) {
        case "string":
          return String.IsNullOrEmpty(node.InnerText) ? null : node.InnerText;
        case "integer":
          long intValue;
          return long.TryParse(node.InnerText, NumberStyles.Integer,
                               CultureInfo.InvariantCulture, out intValue)
              ? (object)intValue : null;
        case "real":
          double realValue;
          return double.TryParse(node.InnerText, NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out realValue)
              ? (object)realValue : null;
        case "false":
          return 0L;
        case "true":
          return 1L;
        default:
          return null;
      }
    }

    private static Dictionary<string, object> ConstructPlistDictionary(XmlElement xmlRoot) {
      if (xmlRoot == null) return null;
      XmlNode xmlDict = null;
      foreach (XmlNode child in xmlRoot.ChildNodes) {
        if (child.NodeType == XmlNodeType.Element && child.Name == "dict") {
          xmlDict = child;
          break;
        }
      }
      if (xmlDict == null) return null;

      var elementChildren = new List<XmlNode>();
      foreach (XmlNode child in xmlDict.ChildNodes) {
        if (child.NodeType == XmlNodeType.Element) {
          elementChildren.Add(child);
        }
      }

      var plistDict = new Dictionary<string, object>(StringComparer.Ordinal);
      for (int i = 0; i < elementChildren.Count; i++) {
        if (elementChildren[i].Name == "key") {
          string key = elementChildren[i].InnerText;
          i++;
          if (i < elementChildren.Count && !String.IsNullOrEmpty(key)) {
            object value = ReadPlistValue(elementChildren[i]);
            if (value != null) {
              plistDict[key] = value;
            }
          }
        }
      }
      return plistDict;
    }

    private static bool TryConvertPlistToJson(string plistString, string inputFilename,
                                              out string jsonString, out string errorMessage) {
      jsonString = null;
      errorMessage = null;
      XmlDocument doc = new XmlDocument();
      try {
        var settings = new XmlReaderSettings {
          DtdProcessing = DtdProcessing.Ignore,
          XmlResolver = null,
        };
        using (var stringReader = new StringReader(plistString))
        using (var xmlReader = XmlReader.Create(stringReader, settings)) {
          doc.Load(xmlReader);
        }
      } catch (Exception e) {
        errorMessage = String.Format(
            "Error parsing file {0}.\nIt does not appear to be valid XML.\n{1}",
            inputFilename, e.Message);
        return false;
      }

      var plistDict = ConstructPlistDictionary(doc.DocumentElement);
      if (plistDict == null) {
        errorMessage = String.Format(
            "In file {0}, could not locate a top-level 'dict' element.\n" +
            "File format should be plist XML, with a top-level dictionary containing " +
            "project settings as key-value pairs.\n",
            inputFilename);
        return false;
      }

      string[] requiredKeys = new string[] { "GOOGLE_APP_ID", "BUNDLE_ID", "API_KEY" };
      foreach (string requiredKey in requiredKeys) {
        if (!plistDict.ContainsKey(requiredKey)) {
          errorMessage = String.Format("Could not find key in plist file: [{0}]", requiredKey);
          return false;
        }
      }

      var projectInfo = new Dictionary<string, object>(StringComparer.Ordinal);
      var keyMap = new Dictionary<string, string>(StringComparer.Ordinal) {
        { "GCM_SENDER_ID", "project_number" },
        { "DATABASE_URL", "firebase_url" },
        { "PROJECT_ID", "project_id" },
        { "STORAGE_BUCKET", "storage_bucket" },
      };
      foreach (var entry in keyMap) {
        object val;
        if (plistDict.TryGetValue(entry.Key, out val) && val != null) {
          projectInfo[entry.Value] = val;
        }
      }

      object analyticsEnabled = 0L;
      plistDict.TryGetValue("IS_ANALYTICS_ENABLED", out analyticsEnabled);
      object appInviteEnabled = 0L;
      plistDict.TryGetValue("IS_APPINVITE_ENABLED", out appInviteEnabled);

      var clientEntry = new Dictionary<string, object>(StringComparer.Ordinal) {
        {
          "client_info",
          new Dictionary<string, object>(StringComparer.Ordinal) {
            { "mobilesdk_app_id", plistDict["GOOGLE_APP_ID"] },
            {
              "android_client_info",
              new Dictionary<string, object>(StringComparer.Ordinal) {
                { "package_name", plistDict["BUNDLE_ID"] }
              }
            }
          }
        },
        {
          "api_key",
          new List<object> {
            new Dictionary<string, object>(StringComparer.Ordinal) {
              { "current_key", plistDict["API_KEY"] }
            }
          }
        },
        {
          "services",
          new Dictionary<string, object>(StringComparer.Ordinal) {
            {
              "analytics_service",
              new Dictionary<string, object>(StringComparer.Ordinal) {
                { "status", analyticsEnabled ?? 0L }
              }
            },
            {
              "appinvite_service",
              new Dictionary<string, object>(StringComparer.Ordinal) {
                { "status", appInviteEnabled ?? 0L }
              }
            }
          }
        }
      };

      object clientId;
      if (plistDict.TryGetValue("CLIENT_ID", out clientId) && clientId != null) {
        clientEntry["oauth_client"] = new List<object> {
          new Dictionary<string, object>(StringComparer.Ordinal) {
            { "client_id", clientId }
          }
        };
      }

      var jsonStruct = new Dictionary<string, object>(StringComparer.Ordinal) {
        { "project_info", projectInfo },
        { "client", new List<object> { clientEntry } },
        { "configuration_version", "1" }
      };

      jsonString = Json.Serialize(jsonStruct);
      return true;
    }

    private static bool TryLoadConfigJsonObject(
        string configFilePath,
        bool isPlist,
        out Dictionary<string, object> jsonObject,
        out string jsonString,
        out string errorMessage) {
      jsonObject = null;
      jsonString = null;
      errorMessage = null;

      string fullPath = ResolveConfigFilePath(configFilePath);
      string fileContent;
      try {
        fileContent = File.ReadAllText(fullPath, Encoding.UTF8);
        if (!String.IsNullOrEmpty(fileContent) && fileContent[0] == '\uFEFF') {
          fileContent = fileContent.Substring(1);
        }
      } catch (Exception e) {
        errorMessage = e.ToString();
        return false;
      }

      if (isPlist) {
        if (!TryConvertPlistToJson(fileContent, configFilePath, out jsonString, out errorMessage)) {
          return false;
        }
      } else {
        jsonString = fileContent;
      }

      jsonObject = Json.Deserialize(jsonString) as Dictionary<string, object>;
      if (jsonObject == null) {
        errorMessage = String.Format("Failed to parse Firebase configuration JSON in {0}.", configFilePath);
        return false;
      }
      return true;
    }

    // Read bundle IDs from the config file.
    private static List<string> ReadBundleIds(string googleServicesFile) {
      var bundleIds = new SortedDictionary<string, string>(StringComparer.Ordinal);
      Dictionary<string, object> jsonObject;
      string unusedJson;
      string errorMessage;
      if (!TryLoadConfigJsonObject(
              googleServicesFile,
              IsFileOfType(googleServicesFile, ConfigFileType.Plist),
              out jsonObject, out unusedJson, out errorMessage)) {
        LogWarningIfEnabled(errorMessage);
        return new List<string>();
      }

      var clientList = GetList(jsonObject, "client");
      if (clientList != null) {
        foreach (object clientObj in clientList) {
          var client = clientObj as Dictionary<string, object>;
          if (client == null) continue;
          string packageName = GetString(GetMap(GetMap(client, "client_info"), "android_client_info"),
                                         "package_name");
          if (String.IsNullOrEmpty(packageName)) {
            packageName = GetString(GetMap(GetMap(client, "oauth_client"), "android_info"),
                                    "package_name");
          }
          if (!String.IsNullOrEmpty(packageName)) {
            bundleIds[packageName] = packageName;
          }
        }
      }
      return new List<string>(bundleIds.Keys);
    }

    // Read the project fields from the config file.
    private static Dictionary<string, string> ReadProjectFields(
            string googleServicesFile) {
      var fields = new Dictionary<string, string>(StringComparer.Ordinal);
      Dictionary<string, object> jsonObject;
      string unusedJson;
      string errorMessage;
      if (!TryLoadConfigJsonObject(
              googleServicesFile,
              IsFileOfType(googleServicesFile, ConfigFileType.Plist),
              out jsonObject, out unusedJson, out errorMessage)) {
        LogWarningIfEnabled(errorMessage);
        return fields;
      }

      var projectInfo = GetMap(jsonObject, "project_info");
      if (projectInfo == null) {
        LogWarningIfEnabled(String.Format("No project info found in {0}.", googleServicesFile));
        return fields;
      }
      foreach (var entry in projectInfo) {
        if (entry.Value != null) {
          fields[entry.Key] = Convert.ToString(entry.Value, CultureInfo.InvariantCulture);
        }
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

    private static void AppendXmlString(XmlDocument doc, XmlElement root, List<string> keepStrings,
                                        string name, string text) {
      if (String.IsNullOrEmpty(text)) return;
      keepStrings.Add("@string/" + name);
      XmlElement child = doc.CreateElement("string");
      child.SetAttribute("name", name);
      child.SetAttribute("translatable", "false");
      child.InnerText = text;
      root.AppendChild(child);
    }

    private static bool TryGenerateXmlFromJsonFile(string inputPath, string outputPath,
                                                   string targetPackageName,
                                                   out string errorMessage) {
      Dictionary<string, object> jsonObject;
      string unusedJson;
      if (!TryLoadConfigJsonObject(inputPath, false, out jsonObject, out unusedJson, out errorMessage)) {
        return false;
      }

      var doc = new XmlDocument();
      doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
      XmlElement root = doc.CreateElement("resources");
      root.SetAttribute("xmlns:tools", ANDROID_TOOLS_NAMESPACE);
      doc.AppendChild(root);

      var keepStrings = new List<string>();
      var projectInfo = GetMap(jsonObject, "project_info");
      if (projectInfo != null) {
        AppendXmlString(doc, root, keepStrings, "firebase_database_url",
                        GetString(projectInfo, "firebase_url"));
        AppendXmlString(doc, root, keepStrings, "gcm_defaultSenderId",
                        GetString(projectInfo, "project_number"));
        AppendXmlString(doc, root, keepStrings, "google_storage_bucket",
                        GetString(projectInfo, "storage_bucket"));
        AppendXmlString(doc, root, keepStrings, "project_id",
                        GetString(projectInfo, "project_id"));
      }

      var clientList = GetList(jsonObject, "client");
      if (clientList != null && clientList.Count > 0) {
        string selectedPackageName = "";
        var selectedClient = clientList[0] as Dictionary<string, object>;
        var packages = new List<string>();
        var packageSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (object clientObj in clientList) {
          var client = clientObj as Dictionary<string, object>;
          if (client == null) continue;
          string packageName = GetString(GetMap(GetMap(client, "client_info"), "android_client_info"),
                                         "package_name");
          if (String.IsNullOrEmpty(packageName)) {
            packageName = GetString(GetMap(GetMap(client, "oauth_client"), "android_info"),
                                    "package_name");
          }
          if (!String.IsNullOrEmpty(packageName)) {
            if (String.IsNullOrEmpty(selectedPackageName)) {
              selectedPackageName = packageName;
              selectedClient = client;
            }
            if (packageName == targetPackageName) {
              selectedPackageName = packageName;
              selectedClient = client;
            }
            if (packageSet.Add(packageName)) {
              packages.Add(packageName);
            }
          }
        }

        if (!String.IsNullOrEmpty(targetPackageName) && selectedPackageName != targetPackageName) {
          errorMessage = String.Format(
              "No packages found in {0} which match the package name {1}\n\nFound the following:\n{2}\n",
              inputPath, targetPackageName, String.Join("\n", packages.ToArray()));
          return false;
        }

        if (selectedClient != null) {
          var clientApiKeys = GetList(selectedClient, "api_key");
          if (clientApiKeys != null && clientApiKeys.Count > 0) {
            var apiKey0 = clientApiKeys[0] as Dictionary<string, object>;
            string apiKey = GetString(apiKey0, "current_key");
            AppendXmlString(doc, root, keepStrings, "google_api_key", apiKey);
            AppendXmlString(doc, root, keepStrings, "google_crash_reporting_api_key", apiKey);
          }

          var clientInfo = GetMap(selectedClient, "client_info");
          if (clientInfo != null) {
            AppendXmlString(doc, root, keepStrings, "google_app_id",
                            GetString(clientInfo, "mobilesdk_app_id"));
          }

          bool clientIdWebParsed = false;
          bool clientIdAndroidParsed = false;
          var oauthClientList = GetList(selectedClient, "oauth_client");
          if (oauthClientList != null) {
            foreach (object oauthObj in oauthClientList) {
              var oauthClient = oauthObj as Dictionary<string, object>;
              if (oauthClient == null) continue;
              object clientTypeObj;
              if (!oauthClient.TryGetValue("client_type", out clientTypeObj) || clientTypeObj == null) {
                continue;
              }
              long clientType = Convert.ToInt64(clientTypeObj, CultureInfo.InvariantCulture);
              string clientId = GetString(oauthClient, "client_id");
              if (clientType == 0 || String.IsNullOrEmpty(clientId)) continue;
              if (clientType == OAUTH_CLIENT_TYPE_WEB && !clientIdWebParsed) {
                AppendXmlString(doc, root, keepStrings, "default_web_client_id", clientId);
                clientIdWebParsed = true;
              }
              if (clientType == OAUTH_CLIENT_TYPE_ANDROID_APP && !clientIdAndroidParsed) {
                AppendXmlString(doc, root, keepStrings, "default_android_client_id", clientId);
                clientIdAndroidParsed = true;
              }
            }
          }

          var services = GetMap(selectedClient, "services");
          if (services != null) {
            var adsService = GetMap(services, "ads_service");
            if (adsService != null) {
              AppendXmlString(doc, root, keepStrings, "test_banner_ad_unit_id",
                              GetString(adsService, "test_banner_ad_unit_id"));
              AppendXmlString(doc, root, keepStrings, "test_interstitial_ad_unit_id",
                              GetString(adsService, "test_interstitial_ad_unit_id"));
            }
            var analyticsService = GetMap(services, "analytics_service");
            if (analyticsService != null) {
              var analyticsProperty = GetMap(analyticsService, "analytics_property");
              if (analyticsProperty != null) {
                AppendXmlString(doc, root, keepStrings, "ga_trackingId",
                                GetString(analyticsProperty, "tracking_id"));
              }
            }
          }
        }
      }

      if (keepStrings.Count > 0) {
        XmlAttribute keepAttr = doc.CreateAttribute("tools", "keep", ANDROID_TOOLS_NAMESPACE);
        keepAttr.Value = String.Join(",", keepStrings.ToArray());
        root.Attributes.Append(keepAttr);
      }

      try {
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!String.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory)) {
          Directory.CreateDirectory(outputDirectory);
        }
        var writerSettings = new XmlWriterSettings {
          Indent = true,
          IndentChars = "  ",
          Encoding = new UTF8Encoding(false),
        };
        using (var writer = XmlWriter.Create(outputPath, writerSettings)) {
          doc.Save(writer);
        }
        return true;
      } catch (Exception e) {
        errorMessage = e.ToString();
        return false;
      }
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

      // Convert google-services.json into google-services.xml in-process.
      string inputFile = Path.Combine(projectDir, googleServicesFile);
      string outputFile = Path.Combine(projectDir,
                                       GOOGLE_SERVICES_OUTPUT_PATH);
      // If the output file exists and it's up to date, don't regenerate
      // it.
      if (ignoreModificationDate ||
          !File.Exists(outputFile) ||
          File.GetLastWriteTime(outputFile).CompareTo(
              File.GetLastWriteTime(inputFile)) < 0) {
        string errorMessage;
        if (TryGenerateXmlFromJsonFile(inputFile, outputFile, GetAndroidApplicationId(),
                                       out errorMessage)) {
          Measurement.ReportWithBuildTarget("generateconfig/success", null,
                                            "Config File Generation Successful");
          Debug.Log(
              String.Format(DocRef.GoogleServicesAndroidGenerateXml,
                  GOOGLE_SERVICES_OUTPUT_PATH, inputFile, "pure-C# resource generator"));
        } else {
          Measurement.ReportWithBuildTarget("generateconfig/failed/toolfailed", null,
                                            "Config File Tool Failed");
          Debug.LogError(
              String.Format(DocRef.GoogleServicesAndroidGenerationFailed,
                  GOOGLE_SERVICES_OUTPUT_FILE, inputFile, "pure-C# resource generator",
                  errorMessage ?? ""));
        }
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
        Dictionary<string, object> unusedObj;
        string jsonString;
        string errorMessage;
        if (TryLoadConfigJsonObject(inputFile, true, out unusedObj, out jsonString, out errorMessage)) {
          try {
            File.WriteAllText(outputFile, jsonString, new UTF8Encoding(false));
            Measurement.ReportWithBuildTarget("generateconfig/success", null,
                                              "Config File Generation Successful");
            return;
          } catch (Exception e) {
            errorMessage = e.ToString();
          }
        }
        Measurement.ReportWithBuildTarget("generateconfig/failed/toolfailed", null,
                                          "Config File Tool Failed");
        if (!String.IsNullOrEmpty(errorMessage)) {
          Debug.LogWarning(errorMessage);
        }
        Debug.LogError(String.Format(DocRef.CouldNotTranslatePlist, sourceFilename));
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
