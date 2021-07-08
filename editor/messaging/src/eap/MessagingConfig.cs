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
using UnityEngine;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using Firebase.Editor;

namespace Firebase.Messaging.Editor {
  /// <summary>
  /// Unity asset that represents configuration required for Firebase Messaging
  /// </summary>
  [ConfigAsset("messaging")]
  internal class MessagingConfig : ScriptableObject {
    /// <summary>
    /// Should Xcode projects be auto configured for dynamic links
    /// </summary>
    public bool autoConfigureXCode;

    /// <summary>
    /// Should XCode projects be auto configured for dynamic links
    /// </summary>
    public bool autoConfigureAndroid;

    /// <summary>
    /// Disable auto init messaging on android application start
    /// </summary>
    public bool disableAutoInitAndroid;

    /// <summary>
    /// Display icon for notifications
    /// </summary>
    public Texture2D notificationIcon;

    /// <summary>
    /// Display color for notifications
    /// </summary>
    public Color notificationColor;

    /// <summary>
    /// Custom channel id for notifications
    /// </summary>
    public string notificationChannelId;

    /// <summary>
    /// Setup config on first use in a Unity Editor Project
    /// </summary>
    public void Initialize() {
      autoConfigureXCode = true;
      autoConfigureAndroid = true;
    }
  }

  /// <summary>
  /// Class handles all utility functions for config including rendering UI and
  /// modifing build projects.
  /// </summary>
  [InitializeOnLoad]
  internal class MessagingConfigUtil {
    /// <summary>
    /// Gets called on first load and registers callbacks for UI and project
    /// modification.
    /// </summary>
    static MessagingConfigUtil() {
      ConfigWindow.RegisterTab<MessagingConfig>("Messaging",
                                                "fb_cloud_messaging");

      ConfigWindow.RegisterSubTab<MessagingConfig>(
          "Messaging", "Messaging - iOS",
          delegate(MessagingConfig config) { OnGUIiOS(config); });

      ConfigWindow.RegisterSubTab<MessagingConfig>(
          "Messaging", "Messaging - Android",
          delegate(MessagingConfig config) { OnGUIAndroid(config); });

      AndroidManifestModifier.RegisterDelegate<MessagingConfig>(
          delegate(AndroidManifestModifier info, MessagingConfig config) {
            OnAndroidPreGen(info, config);
          });

      XcodeProjectModifier.RegisterDelegate<MessagingConfig>(
          delegate(XcodeProjectModifier info, MessagingConfig config) {
            OnXCodePostGen(info, config);
          });
    }

    /// <summary>
    /// Callback to render iOS sub tab page
    /// </summary>
    /// <param name="config">Messaging config</param>
    private static void OnGUIiOS(MessagingConfig config) {
      SerializedObject so = new SerializedObject(config);

      FirebaseGUILayout.PropertyField(so, "autoConfigureXCode",
                                      Strings.AutoConfigureIOS,
                                      Strings.AutoConfigureIOSDescription);

      so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Callback to render Android sub tab page
    /// </summary>
    /// <param name="config">Messaging config</param>
    private static void OnGUIAndroid(MessagingConfig config) {
      SerializedObject so = new SerializedObject(config);

      FirebaseGUILayout.PropertyField(so, "autoConfigureAndroid",
                                      Strings.AutoConfigureAndroid,
                                      Strings.AutoConfigureAndroidDescription);

      FirebaseGUILayout.PropertyField(so, "disableAutoInitAndroid",
                                      Strings.DisableAutoId,
                                      Strings.DisableAutoIdDescription);

      FirebaseGUILayout.PropertyField(so, "notificationIcon",
                                      Strings.NotificationIcon,
                                      Strings.NotificationIconDescription);

      FirebaseGUILayout.PropertyField(so, "notificationColor",
                                      Strings.NotificationColor,
                                      Strings.NotificationColorDescription);

      FirebaseGUILayout.PropertyField(so, "notificationChannelId",
                                      Strings.NotificationChannelId,
                                      Strings.NotificationChannelIdDescription);

      so.ApplyModifiedProperties();
    }

    static readonly string autoInitEnabledKey =
        "firebase_messaging_auto_init_enabled";
    static readonly string notificationIconKey =
        "com.google.firebase.messaging.default_notification_icon";
    static readonly string notificationColorKey =
        "com.google.firebase.messaging.default_notification_color";
    static readonly string notificationChannelIdKey =
        "com.google.firebase.messaging.default_notification_channel_id";

    /// <summary>
    /// Callback to setup android manifest file
    /// </summary>
    /// <param name="info">Android manifest info</param>
    /// <param name="config">Messaging config</param>
    private static void OnAndroidPreGen(AndroidManifestModifier info,
                                        MessagingConfig config) {

      if (config.autoConfigureAndroid == false) {
        return;
      }

      info.SetMetaDataValue(autoInitEnabledKey,
                            config.disableAutoInitAndroid == true);

      if (config.notificationIcon == null) {
        info.DeleteMetaData(notificationIconKey);
      } else {
        info.SetMetaDataResource(notificationIconKey, config.notificationIcon);
      }

      if (config.notificationColor == null) {
        info.DeleteMetaData(notificationColorKey);
      } else {
        info.SetMetaDataResource(notificationColorKey,
                                 config.notificationColor);
      }

      if (String.IsNullOrEmpty(config.notificationChannelId)) {
        info.DeleteMetaData(notificationChannelIdKey);
      } else {
        info.SetMetaDataValue(notificationChannelIdKey,
                              config.notificationChannelId);
      }
    }

    static readonly string backgroundModesKey = "UIBackgroundModes";
    static readonly string remoteNotificationKey = "remote-notification";

    static readonly string pushNotificationKey = "aps-environment";
    static readonly string pushNotificationValue = "production";

    /// <summary>
    /// Callback to setup Xcode project settings
    /// </summary>
    /// <param name="info">Xcode project info</param>
    /// <param name="config">Messaging config</param>
    private static void OnXCodePostGen(XcodeProjectModifier info,
                                       MessagingConfig config) {
      if (config.autoConfigureXCode == false) {
        return;
      }

      ((PBXProject)info.Project).AddFrameworkToProject(
        info.TargetGUID, "UserNotifications.framework", false);

      try {
        var array = ((PlistElementDict)info.ProjectInfo)[backgroundModesKey].AsArray();

        var hasRemoteNotification =
            array.values.Where(a => a.ToString() == remoteNotificationKey)
                .Count();

        if (hasRemoteNotification == 0) {
          array.AddString(remoteNotificationKey);
        }
      } catch (Exception e) {
        Debug.Log("Failed to set background modes for XCode [" + e.ToString() +
                  "]");

        var list = ((PlistElementDict)info.ProjectInfo).CreateArray(backgroundModesKey);
        list.AddString(remoteNotificationKey);
      }

      try {
        ((PlistElementDict)info.Capabilities).SetString(pushNotificationKey, pushNotificationValue);
      } catch (Exception e) {
        Debug.Log("Failed to set push notifications for XCode [" +
                  e.ToString() + "]");
      }
    }
  }

}
