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
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using UnityEngine;
using Firebase.Editor;

namespace Firebase.Auth.Editor {
  /// <summary>
  /// Unity asset that represents configuration required for Firebase
  /// Authentication
  /// </summary>
  [ConfigAsset("authentication")]
  internal class AuthConfig : ScriptableObject {
    /// <summary>
    /// Should Xcode projects be auto configured for dynamic links
    /// </summary>
    public bool autoConfigureXCode;

    /// <summary>
    /// Setup config on first use in a Unity Editor Project
    /// </summary>
    public void Initialize() { autoConfigureXCode = true; }
  }

  /// <summary>
  /// Class handles all utility functions for config including rendering UI and
  /// modifing build projects.
  /// </summary>
  [InitializeOnLoad]
  internal class AuthConfigUtil {
    /// <summary>
    /// Gets called on first load and registers callbacks for UI and project
    /// modification.
    /// </summary>
    static AuthConfigUtil() {
      // Common settings tab ui
      ConfigWindow.RegisterTab<AuthConfig>("Authentication", "fb_auth");

      ConfigWindow.RegisterSubTab<AuthConfig>(
          "Authentication", "Authentication - iOS",
          delegate(AuthConfig config) { OnGUIiOS(config); });

      XcodeProjectModifier.RegisterDelegate<AuthConfig>(
          delegate(XcodeProjectModifier info, AuthConfig config) {
            OnXCodePostGen(info, config);
          });
    }

    /// <summary>
    /// Callback to render iOS sub tab page
    /// </summary>
    /// <param name="config">Authentication config</param>
    private static void OnGUIiOS(AuthConfig config) {
      SerializedObject so = new SerializedObject(config);

      FirebaseGUILayout.PropertyField(so, "autoConfigureXCode",
                                      Strings.AutoConfigureIOS,
                                      Strings.AutoConfigureIOSDescription);

      so.ApplyModifiedProperties();
    }

    static readonly string pushNotificationKey = "aps-environment";
    static readonly string pushNotificationValue = "production";

    /// <summary>
    /// Callback to setup Xcode project settings
    /// </summary>
    /// <param name="info">Xcode project info</param>
    /// <param name="config">Authentication config</param>
    private static void OnXCodePostGen(XcodeProjectModifier info,
                                       AuthConfig config) {
      if (config.autoConfigureXCode == false) {
        return;
      }

      ((PBXProject)info.Project).AddFrameworkToProject(info.TargetGUID,
                                                       "UserNotifications.framework", false);

      try {
        ((PlistElementDict)info.Capabilities).SetString(pushNotificationKey, pushNotificationValue);
      } catch (Exception e) {
        Debug.Log("Failed to set push notifications for XCode [" +
                  e.ToString() + "]");
      }
    }
  }
}
