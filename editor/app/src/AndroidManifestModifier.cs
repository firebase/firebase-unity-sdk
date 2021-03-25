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
using System.Xml;
using UnityEngine;
using UnityEditor;

namespace Firebase.Editor {

// This class is responisble for handling modifications to Android manifest files.
internal class AndroidManifestModifier {

 // Default location to save Android manifest files too.
  static readonly string manifestRootPath =
      "Assets/Plugins/Android/FirebaseApp.androidlib/EditorConfig/";

  static readonly string androidNamespace = "http://schemas.android.com/apk/res/android";

  // Default image extensions Android supports for drawables
  private static readonly string[] supportedDrawableExtensions =
      new string[]{ ".png", ".jpg", ".gif" };

  // Logging instance
  private static readonly CategoryLogger logger = new CategoryLogger("AndroidManifestModifier");

  // Path to the manifest file
  private readonly string manifestPath;

  // Mainfest file xml document
  private XmlDocument document = new XmlDocument();

  // Mainfest file root node
  private XmlNode root;

  /// <summary>
  /// Constructs an AndroidManifestModifier that handles loading an Android manifest file,
  /// allowing modifications on the internal configuration and saving it back to disk.
  /// </summary>
  /// <param name="manifestDirectory">Android manifest file directory</param>
  internal AndroidManifestModifier(string manifestDirectory) {
    this.manifestPath = Path.Combine(manifestDirectory, "AndroidManifest.xml");

    try {
      if (File.Exists(manifestPath) == true) {
        logger.LogDebug("Loading manifest file '{0}'.", manifestPath);
        document.Load(manifestPath);
      }
    }
    catch (Exception e) {
      logger.LogWarn("Failed to load manifest file '{0}': {1}", manifestPath, e);
    }

    root = document.GetElementsByTagName("manifest").Cast<XmlNode>().FirstOrDefault();
  }

  /// <summary>
  /// Return true if the manifest is empty
  /// </summary>
  internal bool IsEmpty() {
    return root == null;
  }

  /// <summary>
  /// Save changes to the Android manifest file to disk.
  /// </summary>
  internal void Save() {
    if (IsEmpty()) {
      logger.LogDebug("Manifest is empty, nothing will be saved");
      return;
    }
    logger.LogDebug("Saving mainfest {0}", manifestPath);
    using (var writer = new XmlTextWriter(manifestPath, System.Text.Encoding.UTF8)) {
      writer.Formatting = Formatting.Indented;
      document.WriteContentTo(writer);
    }
  }

  /// <summary>
  /// Set a meta data boolean value in the manifest. Will overide existing value if exists.
  /// </summary>
  /// <param name="key">Meta data key to save</param>
  /// <param name="value">Boolean value to save</param>
  internal void SetMetaDataValue(string key, bool value) {
    SetMetaDataValue(key, value.ToString());
  }

  /// <summary>
  /// Set a meta data string value in the manifest. Will overide existing value if exists.
  /// </summary>
  /// <param name="key">Meta data key to save</param>
  /// <param name="value">String value to save</param>
  internal void SetMetaDataValue(string key, string value) {
    logger.LogDebug("Setting key {0} to value {1} in {2}", key, value, manifestPath);
    FindOrCreateMeta(key).SetAttribute("android:value", value);
  }

  /// <summary>
  /// Set a meta resource color value in the manifest. Will overide existing value if exists.
  /// </summary>
  /// <param name="key">Meta data key to save</param>
  /// <param name="color">Color to save</param>
  internal void SetMetaDataResource(string key, Color color) {
    logger.LogDebug("Setting key {0} to value {1} in {2}", key, color, manifestPath);
    FindOrCreateMeta(key).SetAttribute("android:resource", "#" + ConvertToARGB(color));
  }

  /// <summary>
  /// Set a meta data image in the manifest. Will overide existing value if exists.
  /// Will also copy image to mainfest resource directory.
  /// </summary>
  /// <param name="key">Meta data key to save</param>
  /// <param name="image">Image to save</param>
  internal void SetMetaDataResource(string key, Texture2D image) {
    if (image == null) {
      logger.LogWarn("SetMetaDataResource failed as image is null with key {0}", key);
      return;
    }

    var imagePath = AssetDatabase.GetAssetPath(image);
    var extension = Path.GetExtension(imagePath);

    if (supportedDrawableExtensions.Contains(extension) == false) {
      logger.LogWarn("SetMetaDataResource failed as image is an unsupported type {0} with key {1}.",
                     extension, key);
      return;
    }

    var resPath = Path.Combine(manifestRootPath, "res");
    var resName = Path.GetFileName(imagePath);
    var destPath = Path.Combine(resPath, resName);

    logger.LogDebug("Creating folder {0}", resPath);
    Directory.CreateDirectory(resPath);

    logger.LogDebug("Copying file {0} to {1}", imagePath, destPath);
    File.Copy(imagePath, destPath);

    logger.LogDebug("Setting key {0} to value {1} in {2}", key, resName, manifestPath);
    FindOrCreateMeta(key).SetAttribute("android:resource", "@drawable/" + resName);
  }

  /// <summary>
  /// Deletes any meta-data value with given key from Android manifest file.
  /// </summary>
  /// <param name="key">Meta data key to delete</param>
  internal void DeleteMetaData(string key) {
    var nodes = root.SelectNodes("meta-data")
                    .Cast<XmlElement>()
                    .Where(a => a != null)
                    .Where(a => a.Attributes.GetNamedItem("android:name") != null
                                && a.Attributes["android:name"].Value == key);

    foreach (var node in nodes) {
      root.RemoveChild(node);
    }
  }

  // Map of Editor UI config types to set of callbacks
  private static Dictionary<Type, List<Action<AndroidManifestModifier, object>>> delegatesByType =
    new Dictionary<Type, List<Action<AndroidManifestModifier, object>>>();

  /// <summary>
  /// Register a delegate to be called when the Android manifest is being generated. This should be
  /// called by a static class constructor with the class having an [InitializeOnLoad] attribute.
  /// </summary>
  /// <typeparam name="T">Type of Editor UI configuration class</typeparam>
  /// <param name="callback">
  /// Callback to invoke at time of manifest generation. First argument is instance of
  /// AndroidManifestModifier and second argument is instance of T with its fields loaded from
  /// saved configuration.
  /// </param>
  internal static void RegisterDelegate<T>(Action<AndroidManifestModifier, T> callback)
    where T : class {

      Action<AndroidManifestModifier, object> internalCallback =
        delegate(AndroidManifestModifier a, System.Object o) { callback(a, o as T); };

      List<Action<AndroidManifestModifier, object>> listOfCallbacks = null;

      if (delegatesByType.TryGetValue(typeof(T), out listOfCallbacks) == false) {
        listOfCallbacks = new List<Action<AndroidManifestModifier, object>>();
        delegatesByType.Add(typeof(T), listOfCallbacks);
      }

      listOfCallbacks.Add(internalCallback);
  }

  /// <summary>
  /// This method is should be called after Editor UI config objects are saved. It creates a class
  /// instance for each unique delegate type, loads Editor UI configuration into the instance and
  /// then invokes the callback methods.
  /// </summary>
  /// <param name="manifestDirectory">Android manifest file directory</param>
  internal static void PreProcessBuild(string manifestDirectory) {

    if (delegatesByType.Count() == 0) {
      logger.LogDebug("Skipping PreProcessBuild as no delegates are registered");
      return;
    }

    // Load Xcode project and plists
    var info = new AndroidManifestModifier(manifestDirectory);

    foreach (var t in delegatesByType) {
      // Create instance
      var classInstance = Activator.CreateInstance(t.Key);

      // Load any config fields in instance for easy access
      Utility.LoadConfig(classInstance);

      foreach (var callback in t.Value) {
        callback(info, classInstance);
      }
    }

    info.Save();
  }

  /// <summary>
  /// Add or retrieve a new intent filter to the activity that already houses the main launcher
  /// intent filter if one with the same action does not already exist.
  /// </summary>
  /// <param name="action">The action of the intent to add</param>
  /// <param name="category">The category of the intent to add</param>
  /// <param name="data">The type of data in the intent to add</param>
  internal void AddIntentFilter(string action, string category, string data) {
    var activity = GetMainActivity();
    if (activity == null) {
      logger.LogWarn("Failed to find the main activity. Additional intent filter will not be added");
      return;
    }
    if (ContainsIntentFilter(activity, action)) {
      logger.LogDebug("Intent filter already in the main activity");
      return;
    }
    var filterElement = CreateIntentFilter(action, category, data);
    activity.AppendChild(filterElement);
  }

  // Helper method to determine if an activity already contains an intent filter
  // with the same action.
  private bool ContainsIntentFilter(XmlElement activity, string action) {
    foreach (XmlNode filter in activity.SelectNodes("intent-filter")) {
      foreach (XmlNode actionElement in filter.SelectNodes("action")) {
        if (actionElement.Attributes.GetNamedItem("android:name") != null
            && actionElement.Attributes["android:name"].Value == action) {
          return true;
        }
      }
    }
    return false;
  }

  // Helper method to locate the activity declared in the manifest with the main intent
  // filter (i.e. the entry point of the app).
  private XmlElement GetMainActivity() {
    var activities = root.SelectNodes("application/activity")
      .Cast<XmlElement>()
      .Where(a => a != null);

    foreach (XmlElement activity in activities) {
      var filters = activity.SelectNodes("intent-filter")
        .Cast<XmlElement>()
        .Where(a => a != null)
        .Where(a => a.HasChildNodes);
      foreach (XmlElement filter in filters) {
        var mainFilter = filter.SelectNodes("action")
          .Cast<XmlElement>()
          .Where(a => a != null)
          .Where(a => a.Attributes.GetNamedItem("android:name") != null
                      && a.Attributes["android:name"].Value == "android.intent.action.MAIN")
          .FirstOrDefault();
        if (mainFilter != null) {
          return activity;
        }
      }
    }
    return null;
  }

  // Helper function to build an intent filter element.
  private XmlElement CreateIntentFilter(string action, string category, string data) {
    var filterElement = document.CreateElement("intent-filter");

    var actionElement = document.CreateElement("action");
    actionElement.SetAttribute("name", androidNamespace, action);
    filterElement.AppendChild(actionElement);

    var categoryElement = document.CreateElement("category");
    categoryElement.SetAttribute("name", androidNamespace, category);
    filterElement.AppendChild(categoryElement);

    var dataElement = document.CreateElement("data");
    dataElement.SetAttribute("mimeType", androidNamespace, data);
    filterElement.AppendChild(dataElement);

    return filterElement;
  }

  // Helper function to find an existing xml node or create one in place.
  private XmlElement FindOrCreateMeta(string key) {
    var node = document.DocumentElement.SelectNodes("meta-data")
      .Cast<XmlElement>()
      .Where(a => a != null)
      .Where(a => a.Attributes.GetNamedItem("android:name") != null
                  && a.Attributes["android:name"].Value == key)
      .FirstOrDefault();

    if (node == null) {
      node = document.CreateElement("meta-data");
      node.SetAttribute("name", androidNamespace, key);
      root.AppendChild(node);
    }

    return node;
  }

  // Converts a unity color into string format ARGB for Android
  private string ConvertToARGB(Color color) {
    var rgba = ColorUtility.ToHtmlStringRGBA(color);
    return (rgba + rgba).Substring(6, 8);
  }
}
}
