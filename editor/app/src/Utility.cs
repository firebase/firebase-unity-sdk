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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEngine;

namespace Firebase.Editor {
static internal class Utility {

  /// <summary>
  /// Return first (or null) attribute of type T on class type.
  /// </summary>
  /// <param name="type">C# class type to check for the attribute</param>
  static internal T GetAttribute<T>(Type type) where T : class {
    return type.GetCustomAttributes(typeof(T), true).FirstOrDefault() as T;
  }

  /// <summary>
  /// Return first (or null) attribute of type T on method info.
  /// </summary>
  /// <param name="method">C# method info to check for the attribute</param>
  static internal T GetAttribute<T>(MethodInfo method) where T : class {
    return method.GetCustomAttributes(typeof(T), true).FirstOrDefault() as T;
  }

  /// <summary>
  /// Return true if class type has an attribute of type T.
  /// </summary>
  /// <param name="type">C# class type to check for the attribute</param>
  static internal bool HasAttribute<T>(Type type) where T : class {
    return GetAttribute<T>(type) != null;
  }

  /// <summary>
  /// Return true if method info has an attribute of type T.
  /// </summary>
  /// <param name="method">C# method info to check for the attribute</param>
  static internal bool HasAttribute<T>(MethodInfo method) where T : class {
    return GetAttribute<T>(method) != null;
  }

  /// <summary>
  /// Finds any Editor Config fields on class instance and loads the saved config values into the
  /// instance. Will always set the field to a valid object instance.
  /// </summary>
  /// <param name="classInstance">Class instance to check for config objects</param>
  static internal void LoadConfig(System.Object classInstance) {
    // Will be implemented in next CL
    // throw new NotImplementedException();
  }

  /// <summary>
  /// Base directory for all generated Firebase Android plugin.
  /// </summary>
  internal static string FIREBASE_ANDROID_PLUGIN_BASE_DIRECTORY =
      Path.Combine(Path.Combine("Assets", "Plugins"), "Android");

  /// <summary>
  /// Directory to resource directory under Android plugin.
  /// </summary>
  internal static string ANDROID_RESOURCE_VALUE_DIRECTORY =
      Path.Combine("res", "values");

  /// <summary>
  /// AndroidManifest.xml filename for Android plugin directory.
  /// </summary>
  private static string ANDROID_MANIFEST_FILE = "AndroidManifest.xml";

  /// <summary>
  /// AndroidManifest.xml template for Android resources.
  /// </summary>
  private static string ANDROID_MANIFEST_RESOURCE_TEMPLATE =
      "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
      "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\"\n" +
      "          package=\"com.google.firebase.{0}.unity\"\n" +
      "          android:versionCode=\"1\"\n" +
      "          android:versionName=\"1.0\">\n" +
      "</manifest>";

  /// <summary>
  /// project.properties filename required for Android plugin directory.
  /// </summary>
  private static string PROJECT_PROPERTIES_FILE = "project.properties";

  /// <summary>
  /// project.properties template for Android resources.
  /// </summary>
  private static string PROJECT_PROPERTIES_FILE_CONTENT =
      "target=android-9\n" +
      "android.library=true";

  /// <summary>
  /// Android plugins path pattern that satisfy both older and newer version of Unity.
  /// Prior to 2020, Android plugin has to be under `Plugins/Android` with AndroidManifest.xml and
  /// project.properties.
  /// After 2020, Android plugin has to be under a directory with `.androidlib` extension.
  /// </summary>
  private static Regex ANDROID_PLUGINS_PATH_PATTERN =
      new Regex(@"^Assets[/\\]Plugins[/\\]Android[/\\][^\s]*\.androidlib[/\\]?$");

  /// <summary>
  /// Return absolute path to project folder.
  /// </summary>
  /// <return>The path as a string to project folder.</return>
  internal static string GetProjectDir() {
    return Path.Combine(Application.dataPath, "..");
  }

  /// <summary>
  /// Generate a file at given path from a given text.
  /// </summary>
  /// <return>True if the file is generated.</return>
  internal static bool GenerateFileFromText(string path, string content) {
    string projectDir = Utility.GetProjectDir();
    string filename = Path.Combine(projectDir, path);
    if (!File.Exists(filename)) {
      try {
        File.WriteAllText(filename, content);
        Debug.Log(String.Format("Generated Firebase Resources file {0}",
                  path));

      } catch (Exception e) {
        Debug.LogError(String.Format("Failed to generate Firebase Resources file {0}",
                  path));
        Debug.LogException(e);
        return false;
      }
    }
    return true;
  }

  /// <summary>
  /// Generate Android Plugin directory, for Android resources.
  /// This creates the base directory, `res/values` directory as well as AndroidManifest.xml and
  /// project.properties files.
  /// </summary>
  /// <param name="path">Path to Android plugin folder.</param>
  /// <param name="componentName">Component name such as app and crashlytics. This will be used
  /// to replace package name in AndroidManifest.xml</param>
  /// <return>True if everything is generated.</return>
  internal static bool GenerateAndroidPluginResourceDirectory(string path, string componentName) {
    if (!ANDROID_PLUGINS_PATH_PATTERN.IsMatch(path)) {
        Debug.LogError(
            String.Format("Firebase Android Plugins folder should match pattern {0} (path: {1})",
                ANDROID_PLUGINS_PATH_PATTERN.ToString(), path));
        return false;
    }

    if (!Directory.Exists(path)) {
      try {
        Directory.CreateDirectory(path);
      } catch (Exception e) {
        Debug.LogError(
            String.Format("Failed to generate Firebase Android Plugins folder {0}", path));
        Debug.LogException(e);
        return false;
      }
    }

    string valueDir = Path.Combine(path, ANDROID_RESOURCE_VALUE_DIRECTORY);
    if (!Directory.Exists(valueDir)) {
      try {
        Directory.CreateDirectory(valueDir);
      } catch (Exception e) {
        Debug.LogError(
            String.Format("Failed to generate Firebase Android Plugins folder {0}", valueDir));
        Debug.LogException(e);
        return false;
      }
    }

    bool result = GenerateFileFromText(
        Path.Combine(path, ANDROID_MANIFEST_FILE),
        String.Format(ANDROID_MANIFEST_RESOURCE_TEMPLATE, componentName));
    result &= GenerateFileFromText(
        Path.Combine(path, PROJECT_PROPERTIES_FILE), PROJECT_PROPERTIES_FILE_CONTENT);
    return result;
  }
}
}
