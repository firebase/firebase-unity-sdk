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

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Firebase.Crashlytics.Editor {
  /// <summary>
  /// ScriptableSettingsStorage wraps an in memory
  /// DictionaryConfigurationStorage object and writes
  /// the object to a Unity Asset
  /// </summary>
  public class ScriptableSettingsStorage : ScriptableObject, IFirebaseConfigurationStorage {

    /// <summary>
    /// A helper class that contains all of the
    /// necessary fields for creating an instance
    /// of an Asset in a particular location
    /// </summary>
    public class AssetContext {

      private static readonly string _settingsDirectory = "Editor Default Resources";

      /// <summary>
      /// The path under the assets directory
      /// where we intend to put this asset
      /// </summary>
      public string SettingsDirectory {
        get { return _settingsDirectory; }
      }

      private static readonly string _assetsDirectory = "Assets";

      /// <summary>
      /// The top level assets directory
      /// </summary>
      public string AssetsDirectory {
        get { return _assetsDirectory; }
      }
      /// <summary>
      /// The name of the file without an extension
      /// </summary>
      public string Name { get; private set; }

      /// <summary>
      /// The extension of the file
      /// </summary>
      public string Extension { get; private set; }

      private string _fullName;

      /// <summary>
      /// The name of the file with extension
      /// </summary>
      /// <exception cref="InvalidDataException"></exception>
      public string FullName {
        get {
          if (Name == null || Extension == null) {
            throw new InvalidDataException("Malformed AssetContext must have both a Name and an Extension.");
          }

          if (_fullName == null) {
            _fullName = string.Join(".", new string[] {
              Name,
              Extension
            });
          }

          return _fullName;
        }
      }

      private string _pathName;

      /// <summary>
      /// The full path to the file with the full file name
      /// </summary>
      /// <exception cref="InvalidDataException">
      /// thrown if any of the set properties on the class are null
      /// </exception>
      public string FullyQualifiedFileName {
        get {
          if (AssetsDirectory == null || SettingsDirectory == null || FullName == null) {
            throw new InvalidDataException("Malformed AssetContext must have a " +
                                           "AssetDirectory, SettingsDirectory, and FullName" +
                                           " to retrieve the PathName.");
          }

          if (_pathName == null) {
            _pathName = Path.Combine(
              Path.Combine(AssetsDirectory, SettingsDirectory),
              FullName
            );
          }

          return _pathName;
        }
      }

      /// <summary>
      /// Constructor for AssetContext to ensure that Name and Extension are
      /// read only.
      /// </summary>
      public AssetContext(string name, string extension) {
        Name = name;
        Extension = extension;
      }
    }

    /// <summary>
    /// Create or Load the appropriate asset from file.
    /// </summary>
    /// <param name="assetContext">An instance of an AssetContext object</param>
    /// <returns>A valid instance of ScriptableSettings</returns>
    public static ScriptableSettingsStorage CreateOrLoadInstance(AssetContext assetContext) {

      // Attempt to load the settings out of the stored asset file
      ScriptableSettingsStorage instance = EditorGUIUtility.Load(assetContext.FullyQualifiedFileName) as ScriptableSettingsStorage;

      if (instance == null) {
        instance = CreateInstance<ScriptableSettingsStorage>();

        if (!Directory.Exists(Path.Combine(assetContext.AssetsDirectory, assetContext.SettingsDirectory))) {
          AssetDatabase.CreateFolder(assetContext.AssetsDirectory, assetContext.SettingsDirectory);
        }

        AssetDatabase.CreateAsset(instance, assetContext.FullyQualifiedFileName);
      }

      return instance;
    }

    /// <summary>
    /// This field is reserved for manipulation by Unity. The
    /// intent is that the class interacts with the property
    /// "Configuration", which will ensure lazy initialization
    /// giving Unity a chance to set these rather than overwriting
    /// the value during construction.
    /// </summary>
    [SerializeField] private AssociationConfigurationStorage configuration;

    /// <summary>
    /// The null-safe property wrapper of "configuration"
    /// </summary>
    private AssociationConfigurationStorage Configuration {
      get {
        if (configuration == null) {
          configuration = new AssociationConfigurationStorage();
        }

        return configuration;
      }
      set { configuration = value; }
    }

    /// <summary>
    /// Pass through implementation of fetching a string from Asset storage.
    /// </summary>
    /// <param name="key">The key we are searching for.</param>
    /// <returns>The value if found</returns>
    /// <throws>KeyNotFoundException when the key does not exist</throws>
    public string FetchString(string key) {
      return Configuration.FetchString(key);
    }

    /// <summary>
    /// Pass through implementation of fetching an int from Asset storage.
    /// </summary>
    /// <param name="key">The key we are searching for.</param>
    /// <returns>The value if found</returns>
    /// <throws>KeyNotFoundException when the key does not exist</throws>
    public int FetchInt(string key) {
      return Configuration.FetchInt(key);
    }

    /// <summary>
    /// Pass through implementation of fetching an float from Asset storage.
    /// </summary>
    /// <param name="key">The key we are searching for.</param>
    /// <returns>The value if found</returns>
    /// <throws>KeyNotFoundException when the key does not exist</throws>
    public float FetchFloat(string key) {
      return Configuration.FetchFloat(key);
    }

    /// <summary>
    /// Pass through implementation of fetching a boolean from Asset storage.
    /// </summary>
    /// <param name="key">The key we are searching for.</param>
    /// <returns>The value if found</returns>
    /// <throws>KeyNotFoundException when the key does not exist</throws>
    public bool FetchBool(string key) {
      return Configuration.FetchBool(key);
    }

    /// <summary>
    /// Check whether a key exists in the storage layer
    /// </summary>
    /// <param name="key"></param>
    /// <returns>true if the Key exists, false otherwise</returns>
    public bool HasKey(string key) {
      return Configuration.HasKey(key);
    }

    /// <summary>
    /// Store the string value associated with the passed key.
    /// This is accomplished by notifying the EditorUtility that this object
    /// is dirty.
    /// </summary>
    /// <param name="key">The key that the value should be associated with</param>
    /// <param name="value">The value we intend to store</param>
    public void Store(string key, string value) {
      Configuration.Store(key, value);
      SaveAsset();
    }

    /// <summary>
    /// Store the int value associated with the passed key.
    /// This is accomplished by notifying the EditorUtility that this object
    /// is dirty.
    /// </summary>
    /// <param name="key">The key that the value should be associated with</param>
    /// <param name="value">The value we intend to store</param>
    public void Store(string key, int value) {
      Configuration.Store(key, value);
      SaveAsset();
    }

    /// <summary>
    /// Store the float value associated with the passed key.
    /// This is accomplished by notifying the EditorUtility that this object
    /// is dirty.
    /// </summary>
    /// <param name="key">The key that the value should be associated with</param>
    /// <param name="value">The value we intend to store</param>
    public void Store(string key, float value) {
      Configuration.Store(key, value);
      SaveAsset();
    }

    /// <summary>
    /// Store the bool value associated with the passed key.
    /// This is accomplished by notifying the EditorUtility that this object
    /// is dirty.
    /// </summary>
    /// <param name="key">The key that the value should be associated with</param>
    /// <param name="value">The value we intend to store</param>
    public void Store(string key, bool value) {
      Configuration.Store(key, value);
      SaveAsset();
    }

    /// <summary>
    /// Delete the value associated with the passed key.
    /// This is accomplished by notifying the EditorUtility that this object
    /// is dirty.
    /// </summary>
    /// <param name="key">The key that should be deleted</param>
    public void Delete(string key) {
      Configuration.Delete(key);
      SaveAsset();
    }

    /// <summary>
    /// Set this class as dirty and ask the asset database to save.
    /// </summary>
    public void SaveAsset() {
      EditorUtility.SetDirty(this);
      AssetDatabase.SaveAssets();
    }
  }
}
