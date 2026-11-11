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

namespace Firebase.Editor {

  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Text;
  using System.Xml;
  using Google.MiniJSON;

  /// <summary>
  /// Raised when a Firebase configuration file can't be read or converted.
  /// </summary>
  internal class GoogleServicesConfigException : Exception {
    public GoogleServicesConfigException(string message) : base(message) { }
    public GoogleServicesConfigException(string message, Exception innerException) :
        base(message, innerException) { }
  }

  /// <summary>
  /// Reads and converts Firebase configuration files (google-services.json and
  /// GoogleService-Info.plist).
  /// </summary>
  /// <remarks>
  /// This is a C# implementation of the logic that used to live in
  /// generate_xml_from_google_services_json.py (and its Windows executable build), which is a
  /// stand-alone implementation of the Gradle google-services plugin. It is intentionally free of
  /// UnityEditor / UnityEngine dependencies so that it can be tested outside of the Unity Editor.
  /// </remarks>
  internal static class GoogleServicesConfigConverter {

    private const string ANDROID_TOOLS_NAMESPACE = "http://schemas.android.com/tools";
    private const long OAUTH_CLIENT_TYPE_ANDROID_APP = 1;
    private const long OAUTH_CLIENT_TYPE_WEB = 3;

    // Maps GoogleService-Info.plist keys to google-services.json project_info keys.
    private static readonly string[,] PLIST_TO_PROJECT_INFO_KEYS = new string[,] {
      { "GCM_SENDER_ID", "project_number" },
      { "DATABASE_URL", "firebase_url" },
      { "PROJECT_ID", "project_id" },
      { "STORAGE_BUCKET", "storage_bucket" },
    };

    // UTF-8 without a byte order mark.
    private static readonly Encoding UTF8_NO_BOM = new UTF8Encoding(false);

    #region File based API

    /// <summary>
    /// Read the bundle / package IDs referenced by a config file.
    /// </summary>
    /// <param name="configPath">Path to a google-services.json or GoogleService-Info.plist
    /// file.</param>
    /// <returns>Sorted list of unique bundle IDs.</returns>
    /// <exception cref="GoogleServicesConfigException">If the file can't be parsed.</exception>
    public static List<string> ReadBundleIds(string configPath) {
      string contents = ReadFile(configPath);
      return IsPlistPath(configPath) ?
          ReadBundleIdsFromPlist(contents, configPath) :
          ReadBundleIdsFromJson(contents, configPath);
    }

    /// <summary>
    /// Read the project_info fields from a google-services.json file.
    /// </summary>
    /// <param name="jsonPath">Path to a google-services.json file.</param>
    /// <returns>Dictionary of project_info field names to values.</returns>
    /// <exception cref="GoogleServicesConfigException">If the file can't be parsed or doesn't
    /// contain project_info.</exception>
    public static Dictionary<string, string> ReadProjectFields(string jsonPath) {
      return ReadProjectFieldsFromJson(ReadFile(jsonPath), jsonPath);
    }

    /// <summary>
    /// Convert a GoogleService-Info.plist file into a google-services-desktop.json file.
    /// </summary>
    /// <param name="plistPath">Path to the GoogleService-Info.plist file to read.</param>
    /// <param name="outputPath">Path of the JSON file to write.</param>
    /// <exception cref="GoogleServicesConfigException">If the file can't be converted.</exception>
    public static void ConvertPlistToDesktopJson(string plistPath, string outputPath) {
      WriteFile(outputPath, ConvertPlistToDesktopJsonString(ReadFile(plistPath), plistPath));
    }

    /// <summary>
    /// Generate an Android resources XML file (google-services.xml) from a
    /// google-services.json file.
    /// </summary>
    /// <param name="jsonPath">Path to the google-services.json file to read.</param>
    /// <param name="outputPath">Path of the XML file to write.</param>
    /// <param name="packageName">Package name of the client to select from the file. If null or
    /// empty, the first client with a package name is selected.</param>
    /// <exception cref="GoogleServicesConfigException">If the file can't be converted or the
    /// package name isn't present in the file.</exception>
    public static void GenerateAndroidResources(string jsonPath, string outputPath,
                                                string packageName) {
      WriteFile(outputPath,
                GenerateAndroidResourcesXml(ReadFile(jsonPath), packageName, jsonPath));
    }

    #endregion

    #region String based API

    /// <summary>
    /// Read the bundle ID from the contents of a GoogleService-Info.plist file.
    /// </summary>
    /// <param name="plistContents">Contents of the plist file.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>List containing the bundle ID, or an empty list if it isn't present.</returns>
    public static List<string> ReadBundleIdsFromPlist(string plistContents, string sourceName) {
      var bundleIds = new List<string>();
      var bundleId = AsString(GetValue(ParsePlist(plistContents, sourceName), "BUNDLE_ID"));
      if (!String.IsNullOrEmpty(bundleId)) bundleIds.Add(bundleId);
      return bundleIds;
    }

    /// <summary>
    /// Read all package names from the contents of a google-services.json file.
    /// </summary>
    /// <param name="jsonContents">Contents of the json file.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>Sorted list of unique package names.</returns>
    public static List<string> ReadBundleIdsFromJson(string jsonContents, string sourceName) {
      var packages = new SortedDictionary<string, bool>(StringComparer.Ordinal);
      var clients = GetList(ParseJson(jsonContents, sourceName), "client");
      if (clients != null) {
        foreach (var client in clients) {
          var packageName = GetClientPackageName(client as Dictionary<string, object>);
          if (!String.IsNullOrEmpty(packageName)) packages[packageName] = true;
        }
      }
      return new List<string>(packages.Keys);
    }

    /// <summary>
    /// Read the project_info fields from the contents of a google-services.json file.
    /// </summary>
    /// <param name="jsonContents">Contents of the json file.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>Dictionary of project_info field names to values.</returns>
    public static Dictionary<string, string> ReadProjectFieldsFromJson(string jsonContents,
                                                                      string sourceName) {
      var projectInfo = GetDictionary(ParseJson(jsonContents, sourceName), "project_info");
      if (projectInfo == null || projectInfo.Count == 0) {
        throw new GoogleServicesConfigException(
            String.Format("No project info found in {0}.", sourceName));
      }
      var fields = new Dictionary<string, string>();
      foreach (var kv in projectInfo) {
        fields[kv.Key] = AsString(kv.Value) ?? "";
      }
      return fields;
    }

    /// <summary>
    /// Convert the contents of a GoogleService-Info.plist file into a JSON configuration
    /// suitable for use on desktop platforms (google-services-desktop.json).
    /// </summary>
    /// <param name="plistContents">Contents of the plist file.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>JSON string.</returns>
    public static string ConvertPlistToDesktopJsonString(string plistContents,
                                                         string sourceName) {
      var plist = ParsePlist(plistContents, sourceName);

      var projectInfo = new Dictionary<string, object>();
      for (int i = 0; i < PLIST_TO_PROJECT_INFO_KEYS.GetLength(0); ++i) {
        object value;
        if (plist.TryGetValue(PLIST_TO_PROJECT_INFO_KEYS[i, 0], out value)) {
          projectInfo[PLIST_TO_PROJECT_INFO_KEYS[i, 1]] = value;
        }
      }

      var services = new Dictionary<string, object>();
      object analyticsEnabled;
      if (plist.TryGetValue("IS_ANALYTICS_ENABLED", out analyticsEnabled)) {
        services["analytics_service"] = new Dictionary<string, object> {
          { "status", analyticsEnabled }
        };
      }
      object appInviteEnabled;
      if (plist.TryGetValue("IS_APPINVITE_ENABLED", out appInviteEnabled)) {
        services["appinvite_service"] = new Dictionary<string, object> {
          { "status", appInviteEnabled }
        };
      }

      var client = new Dictionary<string, object> {
        { "client_info", new Dictionary<string, object> {
            { "mobilesdk_app_id", GetRequiredPlistValue(plist, "GOOGLE_APP_ID", sourceName) },
            { "android_client_info", new Dictionary<string, object> {
                { "package_name", GetRequiredPlistValue(plist, "BUNDLE_ID", sourceName) }
              }
            }
          }
        },
        { "api_key", new List<object> {
            new Dictionary<string, object> {
              { "current_key", GetRequiredPlistValue(plist, "API_KEY", sourceName) }
            }
          }
        },
        { "services", services },
      };
      // OAuth client is optional, but include it if present.
      object clientId;
      if (plist.TryGetValue("CLIENT_ID", out clientId)) {
        client["oauth_client"] = new List<object> {
          new Dictionary<string, object> { { "client_id", clientId } }
        };
      }

      var root = new Dictionary<string, object> {
        { "project_info", projectInfo },
        { "client", new List<object> { client } },
        { "configuration_version", "1" },
      };
      return SerializeJson(root);
    }

    /// <summary>
    /// Generate the contents of an Android resources XML file (google-services.xml) from the
    /// contents of a google-services.json file.
    /// </summary>
    /// <param name="jsonContents">Contents of the json file.</param>
    /// <param name="packageName">Package name of the client to select from the file. If null or
    /// empty, the first client with a package name is selected.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>XML string.</returns>
    public static string GenerateAndroidResourcesXml(string jsonContents, string packageName,
                                                     string sourceName) {
      var json = ParseJson(jsonContents, sourceName);
      // Ordered list of (resource name, value) pairs.
      var resources = new List<KeyValuePair<string, string>>();

      var projectInfo = GetDictionary(json, "project_info");
      if (projectInfo != null) {
        AddResource(resources, "firebase_database_url", GetValue(projectInfo, "firebase_url"));
        AddResource(resources, "gcm_defaultSenderId", GetValue(projectInfo, "project_number"));
        AddResource(resources, "google_storage_bucket", GetValue(projectInfo, "storage_bucket"));
        AddResource(resources, "project_id", GetValue(projectInfo, "project_id"));
      }

      var clients = GetList(json, "client");
      if (clients != null && clients.Count > 0) {
        // Search for the requested package in the file, defaulting to the first package found.
        string selectedPackageName = "";
        var selectedClient = clients[0] as Dictionary<string, object>;
        var packages = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var clientObject in clients) {
          var client = clientObject as Dictionary<string, object>;
          var clientPackageName = GetClientPackageName(client);
          if (String.IsNullOrEmpty(clientPackageName)) continue;
          if (String.IsNullOrEmpty(selectedPackageName) || clientPackageName == packageName) {
            selectedPackageName = clientPackageName;
            selectedClient = client;
          }
          packages[clientPackageName] = true;
        }

        if (!String.IsNullOrEmpty(packageName) && selectedPackageName != packageName) {
          throw new GoogleServicesConfigException(String.Format(
              "No packages found in {0} which match the package name {1}\n\n" +
              "Found the following:\n{2}\n",
              sourceName, packageName, String.Join("\n", new List<string>(packages.Keys).ToArray())));
        }

        if (selectedClient != null) {
          var apiKeys = GetList(selectedClient, "api_key");
          if (apiKeys != null && apiKeys.Count > 0) {
            var currentKey = GetValue(apiKeys[0] as Dictionary<string, object>, "current_key");
            AddResource(resources, "google_api_key", currentKey);
            AddResource(resources, "google_crash_reporting_api_key", currentKey);
          }

          var clientInfo = GetDictionary(selectedClient, "client_info");
          if (clientInfo != null) {
            AddResource(resources, "google_app_id", GetValue(clientInfo, "mobilesdk_app_id"));
          }

          // Only include the first matching OAuth client ID per type.
          bool clientIdWebParsed = false;
          bool clientIdAndroidParsed = false;
          var oauthClients = GetList(selectedClient, "oauth_client");
          if (oauthClients != null) {
            foreach (var oauthClientObject in oauthClients) {
              var oauthClient = oauthClientObject as Dictionary<string, object>;
              long clientType;
              var clientId = AsString(GetValue(oauthClient, "client_id"));
              if (!TryGetLong(GetValue(oauthClient, "client_type"), out clientType) ||
                  String.IsNullOrEmpty(clientId)) {
                continue;
              }
              if (clientType == OAUTH_CLIENT_TYPE_WEB && !clientIdWebParsed) {
                AddResource(resources, "default_web_client_id", clientId);
                clientIdWebParsed = true;
              }
              if (clientType == OAUTH_CLIENT_TYPE_ANDROID_APP && !clientIdAndroidParsed) {
                AddResource(resources, "default_android_client_id", clientId);
                clientIdAndroidParsed = true;
              }
            }
          }

          var services = GetDictionary(selectedClient, "services");
          if (services != null) {
            var adsService = GetDictionary(services, "ads_service");
            if (adsService != null) {
              AddResource(resources, "test_banner_ad_unit_id",
                          GetValue(adsService, "test_banner_ad_unit_id"));
              AddResource(resources, "test_interstitial_ad_unit_id",
                          GetValue(adsService, "test_interstitial_ad_unit_id"));
            }
            var analyticsService = GetDictionary(services, "analytics_service");
            if (analyticsService != null) {
              var analyticsProperty = GetDictionary(analyticsService, "analytics_property");
              if (analyticsProperty != null) {
                AddResource(resources, "ga_trackingId",
                            GetValue(analyticsProperty, "tracking_id"));
              }
            }
          }
        }
      }

      return WriteAndroidResourcesXml(resources);
    }

    #endregion

    #region Plist parsing

    /// <summary>
    /// Parse the top level dictionary of a plist XML document.
    /// </summary>
    /// <param name="plistContents">Contents of the plist file.</param>
    /// <param name="sourceName">Name of the file, used in error messages.</param>
    /// <returns>Dictionary of the supported (string, integer, real, boolean) values in the
    /// top level dictionary. Booleans are stored as 1 or 0.</returns>
    internal static Dictionary<string, object> ParsePlist(string plistContents,
                                                          string sourceName) {
      var document = new XmlDocument();
      // GoogleService-Info.plist files reference Apple's DTD, make sure it isn't fetched.
      document.XmlResolver = null;
      var settings = new XmlReaderSettings {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
      };
      try {
        using (var stringReader = new StringReader(plistContents ?? ""))
        using (var xmlReader = XmlReader.Create(stringReader, settings)) {
          document.Load(xmlReader);
        }
      } catch (XmlException exception) {
        throw new GoogleServicesConfigException(
            String.Format("Error parsing file {0}.\nIt does not appear to be valid XML.\n{1}",
                          sourceName, exception.Message), exception);
      }

      XmlElement dictElement = null;
      if (document.DocumentElement != null) {
        foreach (XmlNode node in document.DocumentElement.ChildNodes) {
          if (node.NodeType == XmlNodeType.Element && node.Name == "dict") {
            dictElement = (XmlElement)node;
            break;
          }
        }
      }
      if (dictElement == null) {
        throw new GoogleServicesConfigException(String.Format(
            "In file {0}, could not locate a top-level 'dict' element.\n" +
            "File format should be plist XML, with a top-level dictionary containing " +
            "project settings as key-value pairs.\n", sourceName));
      }

      var elements = new List<XmlElement>();
      foreach (XmlNode node in dictElement.ChildNodes) {
        if (node.NodeType == XmlNodeType.Element) elements.Add((XmlElement)node);
      }
      var plist = new Dictionary<string, object>();
      for (int i = 0; i < elements.Count; ++i) {
        if (elements[i].Name != "key") continue;
        string key = elements[i].InnerText;
        if (++i >= elements.Count) break;
        var value = ReadPlistValue(elements[i]);
        if (value != null) plist[key] = value;
      }
      return plist;
    }

    // Read a supported plist value, returning null for unsupported types (data, date, array,
    // dict) or empty values.
    private static object ReadPlistValue(XmlElement element) {
      switch (element.Name) {
        case "string":
          return String.IsNullOrEmpty(element.InnerText) ? null : element.InnerText;
        case "integer": {
          long value;
          return Int64.TryParse(element.InnerText.Trim(), NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out value) ? (object)value : null;
        }
        case "real": {
          double value;
          return Double.TryParse(element.InnerText.Trim(), NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out value) ? (object)value : null;
        }
        case "true":
          return 1L;
        case "false":
          return 0L;
        default:
          return null;
      }
    }

    private static object GetRequiredPlistValue(Dictionary<string, object> plist, string key,
                                                string sourceName) {
      object value;
      if (!plist.TryGetValue(key, out value)) {
        throw new GoogleServicesConfigException(String.Format(
            "Could not find key in plist file {0}: [{1}]", sourceName, key));
      }
      return value;
    }

    #endregion

    #region JSON helpers

    // Parse a JSON document, which must have an object at the root.
    private static Dictionary<string, object> ParseJson(string jsonContents, string sourceName) {
      object parsed = null;
      try {
        parsed = Json.Deserialize(jsonContents ?? "");
      } catch (Exception exception) {
        throw new GoogleServicesConfigException(
            String.Format("Error parsing file {0}. It does not appear to be valid JSON.",
                          sourceName), exception);
      }
      var root = parsed as Dictionary<string, object>;
      if (root == null) {
        throw new GoogleServicesConfigException(String.Format(
            "Error parsing file {0}. It does not appear to be a valid JSON object.",
            sourceName));
      }
      return root;
    }

    // Get the package name associated with a google-services.json client entry.
    private static string GetClientPackageName(Dictionary<string, object> client) {
      if (client == null) return null;
      var packageName = AsString(GetValue(GetDictionary(GetDictionary(
          client, "client_info"), "android_client_info"), "package_name"));
      if (!String.IsNullOrEmpty(packageName)) return packageName;

      // Fall back to the Android OAuth client info, if present.
      object oauthClient = GetValue(client, "oauth_client");
      var oauthClientDict = oauthClient as Dictionary<string, object>;
      if (oauthClientDict != null) {
        return AsString(GetValue(GetDictionary(oauthClientDict, "android_info"),
                                 "package_name"));
      }
      var oauthClientList = oauthClient as List<object>;
      if (oauthClientList != null) {
        foreach (var entry in oauthClientList) {
          packageName = AsString(GetValue(GetDictionary(
              entry as Dictionary<string, object>, "android_info"), "package_name"));
          if (!String.IsNullOrEmpty(packageName)) return packageName;
        }
      }
      return null;
    }

    private static object GetValue(Dictionary<string, object> dictionary, string key) {
      object value;
      return dictionary != null && dictionary.TryGetValue(key, out value) ? value : null;
    }

    private static Dictionary<string, object> GetDictionary(Dictionary<string, object> dictionary,
                                                            string key) {
      return GetValue(dictionary, key) as Dictionary<string, object>;
    }

    private static List<object> GetList(Dictionary<string, object> dictionary, string key) {
      return GetValue(dictionary, key) as List<object>;
    }

    private static string AsString(object value) {
      if (value == null) return null;
      if (value is bool) return (bool)value ? "True" : "False";
      return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool TryGetLong(object value, out long result) {
      result = 0;
      if (value is long) {
        result = (long)value;
        return true;
      }
      if (value is double) {
        double doubleValue = (double)value;
        if (Math.Floor(doubleValue) != doubleValue) return false;
        result = (long)doubleValue;
        return true;
      }
      var stringValue = value as string;
      return stringValue != null &&
          Int64.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture,
                         out result);
    }

    /// <summary>
    /// Serialize a JSON value (dictionaries, lists, strings, numbers, booleans, null) to an
    /// indented string.
    /// </summary>
    internal static string SerializeJson(object value) {
      var builder = new StringBuilder();
      WriteJsonValue(builder, value, 0);
      builder.Append('\n');
      return builder.ToString();
    }

    private static void WriteJsonValue(StringBuilder builder, object value, int depth) {
      var dictionary = value as IDictionary<string, object>;
      if (dictionary != null) {
        if (dictionary.Count == 0) {
          builder.Append("{}");
          return;
        }
        builder.Append("{\n");
        bool first = true;
        foreach (var kv in dictionary) {
          if (!first) builder.Append(",\n");
          first = false;
          builder.Append(' ', (depth + 1) * 2);
          builder.Append(Json.Serialize(kv.Key));
          builder.Append(": ");
          WriteJsonValue(builder, kv.Value, depth + 1);
        }
        builder.Append('\n');
        builder.Append(' ', depth * 2);
        builder.Append('}');
        return;
      }
      var list = value as IList<object>;
      if (list != null) {
        if (list.Count == 0) {
          builder.Append("[]");
          return;
        }
        builder.Append("[\n");
        for (int i = 0; i < list.Count; ++i) {
          if (i > 0) builder.Append(",\n");
          builder.Append(' ', (depth + 1) * 2);
          WriteJsonValue(builder, list[i], depth + 1);
        }
        builder.Append('\n');
        builder.Append(' ', depth * 2);
        builder.Append(']');
        return;
      }
      builder.Append(Json.Serialize(value));
    }

    #endregion

    #region Android resources

    // Add a string resource if the value is non-empty.
    private static void AddResource(List<KeyValuePair<string, string>> resources, string name,
                                    object value) {
      var text = AsString(value);
      if (!String.IsNullOrEmpty(text)) {
        resources.Add(new KeyValuePair<string, string>(name, text));
      }
    }

    // Write the Android resources XML document, marking every resource with tools:keep so it
    // isn't stripped by the resource shrinker.
    private static string WriteAndroidResourcesXml(
        List<KeyValuePair<string, string>> resources) {
      var keep = new List<string>();
      foreach (var resource in resources) keep.Add("@string/" + resource.Key);

      var settings = new XmlWriterSettings {
        Encoding = UTF8_NO_BOM,
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\n",
        NewLineHandling = NewLineHandling.Replace,
      };
      using (var stream = new MemoryStream()) {
        using (var writer = XmlWriter.Create(stream, settings)) {
          writer.WriteStartDocument();
          writer.WriteStartElement("resources");
          writer.WriteAttributeString("xmlns", "tools", null, ANDROID_TOOLS_NAMESPACE);
          if (keep.Count > 0) {
            writer.WriteAttributeString("tools", "keep", ANDROID_TOOLS_NAMESPACE,
                                        String.Join(",", keep.ToArray()));
          }
          foreach (var resource in resources) {
            writer.WriteStartElement("string");
            writer.WriteAttributeString("name", resource.Key);
            writer.WriteAttributeString("translatable", "false");
            writer.WriteString(resource.Value);
            writer.WriteEndElement();
          }
          writer.WriteEndElement();
          writer.WriteEndDocument();
        }
        return UTF8_NO_BOM.GetString(stream.ToArray()) + "\n";
      }
    }

    #endregion

    #region File helpers

    private static bool IsPlistPath(string path) {
      return path.EndsWith(".plist", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadFile(string path) {
      try {
        return File.ReadAllText(path);
      } catch (Exception exception) {
        throw new GoogleServicesConfigException(
            String.Format("Unable to read {0}: {1}", path, exception.Message), exception);
      }
    }

    private static void WriteFile(string path, string contents) {
      try {
        var directory = Path.GetDirectoryName(path);
        if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
          Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, contents, UTF8_NO_BOM);
      } catch (Exception exception) {
        throw new GoogleServicesConfigException(
            String.Format("Unable to write {0}: {1}", path, exception.Message), exception);
      }
    }

    #endregion
  }

}  // namespace Firebase.Editor
