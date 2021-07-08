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
using UnityEngine;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using GooglePlayServices;
using Firebase.Editor;

namespace Firebase.DynamicLinks.Editor {
  /// <summary>
  /// Unity asset that represents configuration required for Firebase Dynamic
  /// Links
  /// </summary>
  [ConfigAsset("dynamic_links")]
  internal class DynamicLinksConfig : ScriptableObject {
    /// <summary>
    /// Should Xcode projects be auto configured for dynamic links
    /// </summary>
    public bool autoConfigureXCode;

    /// <summary>
    /// List of keychain access groups for dynamic links
    /// </summary>
    public string[] keychainAccessGroups;

    // Disable the warning about unused fields as this is set via reflection.
    #pragma warning disable 649
    /// <summary>
    /// List of associated domains for dynamic links.
    /// </summary>
    public string[] associatedDomains;
    #pragma warning restore 649

    /// <summary>
    /// Setup config on first use in a Unity Editor Project
    /// </summary>
    public void Initialize() {
      autoConfigureXCode = true;
      keychainAccessGroups =
          new string[]{"$(AppIdentifierPrefix)" +
                       UnityCompat.GetApplicationId(BuildTarget.iOS)};
    }
  }

  /// <summary>
  /// Class handles all utility functions for config including rendering UI and
  /// modifing build projects.
  /// </summary>
  [InitializeOnLoad]
  internal class DynamicLinksConfigUtil {
    /// <summary>
    /// Gets called on first load and registers callbacks for UI and project
    /// modification.
    /// </summary>
    static DynamicLinksConfigUtil() {
      ConfigWindow.RegisterTab<DynamicLinksConfig>("Dynamic Links",
                                                   "fb_dynamic_links");

      ConfigWindow.RegisterSubTab<DynamicLinksConfig>(
          "Dynamic Links", "Dynamic Links - iOS",
          delegate(DynamicLinksConfig config) { OnGUIiOS(config); });

      XcodeProjectModifier.RegisterDelegate<DynamicLinksConfig>(
          delegate(XcodeProjectModifier info, DynamicLinksConfig config) {
            OnXCodePostGen(info, config);
          });
    }

    /// <summary>
    /// Callback to render iOS sub tab page
    /// </summary>
    /// <param name="config">Dynamic LInks config</param>
    private static void OnGUIiOS(DynamicLinksConfig config) {
      SerializedObject so = new SerializedObject(config);

      FirebaseGUILayout.PropertyField(so, "autoConfigureXCode",
                                      Strings.AutoConfigureIOS,
                                      Strings.AutoConfigureIOSDescription);

      EditorGUILayout.Space();

      FirebaseGUILayout.PropertyField(so, "keychainAccessGroups",
                                      Strings.KeychainAccessGroups,
                                      Strings.KeychainAccessGroupsDescription);

      EditorGUILayout.Space();

      FirebaseGUILayout.PropertyField(so, "associatedDomains",
                                      Strings.AssociatedDomains,
                                      Strings.AssociatedDomainsDescription);

      so.ApplyModifiedProperties();
    }

    static readonly string keychainKey = "keychain-access-groups";
    static readonly string associatedDomainsKey =
        "com.apple.developer.associated-domains";
    static readonly string associatedDomainsPrefix = "applinks:";

    /// <summary>
    /// Callback to setup Xcode project settings
    /// </summary>
    /// <param name="info">Xcode project info</param>
    /// <param name="config">Dynamic Links config</param>
    private static void OnXCodePostGen(XcodeProjectModifier info,
                                       DynamicLinksConfig config) {
      if (config.autoConfigureXCode == false) {
        return;
      }

      var capabilities = (PlistElementDict)info.Capabilities;
      var oldKeyChains = GetStringArray(capabilities, keychainKey);
      var oldAssociatedDomains = GetStringArray(capabilities, associatedDomainsKey);

      var keychainAccessGroups = capabilities.CreateArray(keychainKey);

      // Add existing keys chains
      foreach (var i in oldKeyChains) {
        keychainAccessGroups.AddString(i);
      }

      // Add ours that are not in the existing list
      foreach (var i in config.keychainAccessGroups) {
        if (oldKeyChains.Contains(i) == false) {
          keychainAccessGroups.AddString(i);
        }
      }

      var associatedDomains = capabilities.CreateArray(associatedDomainsKey);

      // Add in our custom values for associated domains
      foreach (var i in config.associatedDomains) {
        if (i.StartsWith(associatedDomainsPrefix) == false) {
          associatedDomains.AddString(associatedDomainsPrefix + i);
        } else {
          associatedDomains.AddString(i);
        }
      }

      // Add existing values that are not ours back to the plist
      foreach (var i in oldAssociatedDomains) {
        if (i.StartsWith(associatedDomainsPrefix) == true) {
          continue;
        }

        associatedDomains.AddString(i);
      }
    }

    private static List<string> GetStringArray(object plistElementDict,
                                               string key) {
      var dict = (PlistElementDict)plistElementDict;
      var ret = new List<string>();

      try {
        foreach (var v in dict [key]
                     .AsArray()
                     .values) {
          ret.Add(v.AsString());
        }
      } catch (Exception) {
      }

      return ret;
    }
  }

}
