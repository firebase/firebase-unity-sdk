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
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace Firebase.Editor {


/// <summary>
/// Marks a class as being a Firebase Editor config asset for loading and saving.
///
/// Note: Class must inherit from ScriptableObject
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
internal class ConfigAssetAttribute : Attribute {
  internal readonly string configAssetName;

  internal ConfigAssetAttribute(string configAssetName) {
    this.configAssetName = configAssetName;
  }
}

// This class is an Api that can be used to pragmatically configure Firebase extensions for Unity.
internal static class ConfigApi {

  // Token asset to find to work out default place to save configuration assets.
  static readonly string tokenAssestGuid = "";  // TODO(markchandler) update to actual asset guid

  // Default location to save settings assets too if the token asset can't be found.
  static readonly string defaultAssetPath = "Assets/Resources/Editor/Firebase";

  // Logger
  static readonly CategoryLogger logger = new CategoryLogger("ConfigApi");

  /// <summary>
  /// Checks if a Firebase Config object has a saved asset file
  /// </summary>
  /// <param name="configType">Type of config object to load. i.e.
  /// AnalyticsConfig</param>
  /// <returns>bool if there exists an asset file</returns>
  internal static bool HasConfigAsset(Type configType) {
    if (IsValidAssetType(configType) == false) {
      return false;
    }

    var assetPath = GetAssetPathFromAttribute(configType);

    if (String.IsNullOrEmpty(assetPath) == true) {
      return false;
    }

    var validPath =
        String.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) == false;

    return validPath == true && File.Exists(assetPath) == true;
  }

  /// <summary>
  /// Find and load values for a Firebase Editor config object
  /// </summary>
  /// <param name="configType">
  /// Type of config object to load. i.e. AnalyticsConfig
  /// </param>
  /// <returns>
  /// Null if configType is not a ScriptableObject and doesnt contain
  /// TabConfig attribute else config object
  /// </returns>
  internal static ScriptableObject LoadConfigAsset(Type configType) {
    if (IsValidAssetType(configType) == false) {
      return null;
    }

    var assetPath = GetAssetPathFromAttribute(configType);

    if (String.IsNullOrEmpty(assetPath) == true) {
      return null;
    }

    var raw = AssetDatabase.LoadAssetAtPath(assetPath, configType) as ScriptableObject;

    if (raw == null) {
      logger.LogDebug("Failed to load asset at path {0}. Creating new asset.", assetPath);
      CreateAllAssetFolders(Path.GetDirectoryName(assetPath));
      raw = ScriptableObject.CreateInstance(configType);
      AssetDatabase.CreateAsset(raw, assetPath);
    }

    return raw;
  }

  /// <summary>
  /// Saves an existing Firebase Config object
  /// </summary>
  /// <param name="config">Config object that has been loaded using LoadConfig</param>
  internal static void SaveConfigAsset(ScriptableObject config) {
    EditorUtility.SetDirty(config);
  }

  /// <summary>
  /// Apply defaults to config object if marked with the InitialValue attribute
  /// </summary>
  /// <param name="config">Firebase Editor config object</param>
  internal static void ApplyDefaults(ScriptableObject config) {
    var applyMethod = config.GetType().GetMethod("ApplyDefaults");

    if (applyMethod != null) {
      applyMethod.Invoke(config, null);
    }
  }

  // Is the type a valid type that can be used for config assets
  private static bool IsValidAssetType(Type configType) {
    // Check if it inheirts from ScriptableObject
    if (typeof(ScriptableObject).IsAssignableFrom(configType) == false) {
      logger.LogError("Object of type '{0}' is not a valid asset type as it does not inherit from ScriptableObject. " +
              "This type is not usable with Firebase Editor Config Api. " +
              "Please ensure '{0}' inheirts from ScriptableObject.",
              configType.FullName);
      return false;
    }

    return true;
  }

  // Get the asset path for a config object type based on the ConfigAsset attribute
  private static string GetAssetPathFromAttribute(Type configType) {
    var config = Utility.GetAttribute<ConfigAssetAttribute>(configType);

    if (config == null || String.IsNullOrEmpty(config.configAssetName) == true) {
      logger.LogError("Object of type '{0}' is missing ConfigAsset attribute with a valid value. " +
              "This type is not usable with Firebase Editor Config Api. " +
              "Please ensure '{0}' has a TabConfig attribute describing the name its config asset.",
              configType.FullName);
      return null;
    }

    var tokenPath = AssetDatabase.GUIDToAssetPath(tokenAssestGuid);

    if (String.IsNullOrEmpty(tokenPath) == false) {
      return Path.Combine(Path.GetDirectoryName(tokenPath), config.configAssetName + ".asset");
    }

    return Path.Combine(defaultAssetPath, config.configAssetName + ".asset");
  }

  // Create all the required folders for saving the asset at path
  private static void CreateAllAssetFolders(string relativeFolderPath) {
    string projectDir = Path.Combine(Application.dataPath, "..");
    string outputDir = Path.Combine(projectDir, relativeFolderPath);
    if (!Directory.Exists(outputDir)) {
      try {
        Directory.CreateDirectory(outputDir);
      } catch (Exception e) {
        Debug.LogError(
          String.Format("Unable to create folders for config asset at {0}", outputDir));
        Debug.LogException(e);
        return;
      }
    }
  }
}
}
