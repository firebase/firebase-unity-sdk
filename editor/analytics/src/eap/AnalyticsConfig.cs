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
 * See the License for the specific language governing permanentlyissions and
 * limitations under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using Firebase.Editor;

namespace Firebase.Analytics.Editor {

  internal class AndroidAnalyticsConfig : ScriptableObject {
    // Should you want to manually configure this plugin settings and not
    // generate an AndroidManifest.xml set this to false.
    public bool autoConfigure;

    // Should you wish to disable collection of the Advertising ID in your
    // Android app set this to true
    public bool disableAndroidADID;

    // Should you wish to disable collection of Android Secure ID set this to
    // true
    public bool disableAndroidSSAID;

    internal static readonly string temporaryDisableKey =
        "firebase_analytics_collection_enabled";
    internal static readonly string permanentlyDisableKey =
        "firebase_analytics_collection_deactivated";
    internal static readonly string personalAdsKey =
        "google_analytics_default_allow_ad_personalization_signals";
    internal static readonly string adidCollectionKey =
        "google_analytics_adid_collection_enabled";
    internal static readonly string ssaidCollectionKey =
        "google_analytics_ssaid_collection_enabled";
  }

  internal class IOSAnalyticsConfig : ScriptableObject {
    // Should you want to manually configure this plugin settings and not
    // configure XCode project automatically set this to false.
    public bool autoConfigure;

    // Should you wish to disable collection of the Advertising ID in your iOS
    // app set this to true
    public bool permanentlyDisableAppleIDFA;

    // Should you wish to disable collection of the Vendor ID in your iOS app
    // set this to true
    public bool permanentlyDisableAppleIDFV;

    internal static readonly string temporaryDisableKey =
        "FIREBASE_ANALYTICS_COLLECTION_ENABLED";
    internal static readonly string permanentlyDisableKey =
        "FIREBASE_ANALYTICS_COLLECTION_DEACTIVATED";
    internal static readonly string personalAdsKey =
        "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_PERSONALIZATION_SIGNALS";
    internal static readonly string idfvCollectionKey =
        "GOOGLE_ANALYTICS_IDFV_COLLECTION_ENABLED";
  }

  /// <summary>
  /// Unity asset that represents configuration required for Firebase Analytics
  /// </summary>
  [ConfigAsset("analytics")]
  internal class AnalyticsConfig : ScriptableObject {
    // Should you want to get end user consent before collectiong data you can
    // set this value to true. If you want to re-enable call
    // FirebaseAnalytics.SetAnalyticsCollectionEnabled(true);
    public bool temporaryDisableCollection;

    // Should you want to never enable analytics in this application, set this
    // to true
    public bool permanentlyDisableCollection;

    // Should you want to indicate that a users analytics should not be used for
    // personalized advertising set this to true. If you want to re-enable call
    // FirebaseAnalytics.SetUserProperty("allow_personalized_ads", "true");
    public bool temporaryDisablePersonalizedAdvert;

    // Android configuration
    public AndroidAnalyticsConfig android = new AndroidAnalyticsConfig();

    // iOS configuration
    public IOSAnalyticsConfig iOS = new IOSAnalyticsConfig();

    /// <summary>
    /// Setup config on first use in a Unity Editor Project
    /// </summary>
    public void Initialize() {
      android.autoConfigure = true;
      iOS.autoConfigure = true;
    }
  }

  /// <summary>
  /// Class handles all utility functions for config including rendering UI and
  /// modifing build projects.
  /// </summary>
  [InitializeOnLoad]
  internal class AnalyticsConfigUtil {
    /// <summary>
    /// Gets called on first load and registers callbacks for UI and project
    /// modification.
    /// </summary>
    static AnalyticsConfigUtil() {
      // Common settings tab ui
      ConfigWindow.RegisterTab<AnalyticsConfig>(
          "Analytics", "fb_analytics",
          delegate(AnalyticsConfig config) { OnGUI(config); });

      ConfigWindow.RegisterSubTab<AnalyticsConfig>(
          "Analytics", "Analytics - iOS",
          delegate(AnalyticsConfig config) { OnGUIiOS(config); });

      ConfigWindow.RegisterSubTab<AnalyticsConfig>(
          "Analytics", "Analytics - Android",
          delegate(AnalyticsConfig config) { OnGUIAndroid(config); });

      AndroidManifestModifier.RegisterDelegate<AnalyticsConfig>(
          delegate(AndroidManifestModifier info, AnalyticsConfig config) {
            OnAndroidPreGen(info, config);
          });

      XcodeProjectModifier.RegisterDelegate<AnalyticsConfig>(
          delegate(XcodeProjectModifier info, AnalyticsConfig config) {
            OnXCodePostGen(info, config);
          });
    }

    /// <summary>
    /// Options to show on UI about the three states analytics can be configured
    /// to run in.
    /// </summary>
    static readonly string[] options = new string[]{
        Strings.OptionEnabled,
        Strings.OptionTemporaryDisabled,
        Strings.OptionPermanetlyDisabled,
    };

    /// <summary>
    /// Callback to render common tab page
    /// </summary>
    /// <param name="config">Analytics config</param>
    internal static void OnGUI(AnalyticsConfig config) {
      string selected = Strings.OptionEnabled;

      if (config.permanentlyDisableCollection == true) {
        selected = Strings.OptionPermanetlyDisabled;
      } else if (config.temporaryDisableCollection == true) {
        selected = Strings.OptionTemporaryDisabled;
      }

      selected = FirebaseGUILayout.Popup(Strings.AnalyticsCollection, selected,
                                         options);

      if (selected == Strings.OptionTemporaryDisabled) {
        config.permanentlyDisableCollection = false;
        config.temporaryDisableCollection = true;
      } else if (selected == Strings.OptionPermanetlyDisabled) {
        config.permanentlyDisableCollection = true;
        config.temporaryDisableCollection = false;
      } else  // (selected == Strings.OptionEnabled)
      {
        config.permanentlyDisableCollection = false;
        config.temporaryDisableCollection = false;
      }

      SerializedObject so = new SerializedObject(config);

      FirebaseGUILayout.PropertyField(so, "temporaryDisablePersonalizedAdvert",
                                      Strings.DisablePersonalAds,
                                      Strings.DisablePersonalAdsDescription);

      so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Callback to render iOS sub tab page
    /// </summary>
    /// <param name="config">Messaging config</param>
    internal static void OnGUIiOS(AnalyticsConfig config) {
      SerializedObject so = new SerializedObject(config.iOS);

      FirebaseGUILayout.PropertyField(so, "autoConfigure",
                                      Strings.AutoConfigureIOS,
                                      Strings.AutoConfigureIOSDescription);

      FirebaseGUILayout.PropertyField(
          so, "permanentlyDisableAppleIDFA",
          Strings.PermanentlyDisableAppleIDFA,
          Strings.PermanentlyDisableAppleIDFADescription);

      FirebaseGUILayout.PropertyField(
          so, "permanentlyDisableAppleIDFV",
          Strings.PermanentlyDisableAppleIDFV,
          Strings.PermanentlyDisableAppleIDFVDescription);

      so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Callback to render Android sub tab page
    /// </summary>
    /// <param name="config">Messaging config</param>
    internal static void OnGUIAndroid(AnalyticsConfig config) {
      SerializedObject so = new SerializedObject(config.android);

      FirebaseGUILayout.PropertyField(so, "autoConfigure",
                                      Strings.AutoConfigureAndroid,
                                      Strings.AutoConfigureAndroidDescription);

      FirebaseGUILayout.PropertyField(
          so, "disableAndroidADID", Strings.PermanentlyDisableAndroidADID,
          Strings.PermanentlyDisableAndroidADIDDescription);

      FirebaseGUILayout.PropertyField(
          so, "disableAndroidSSAID", Strings.PermanentlyDisableAndroidSSAID,
          Strings.PermanentlyDisableAndroidSSAIDDescription);

      so.ApplyModifiedProperties();
    }

    /// <summary>
    /// Callback to setup android manifest file
    /// </summary>
    /// <param name="info">Android manifest info</param>
    /// <param name="config">Analytics config</param>
    internal static void OnAndroidPreGen(AndroidManifestModifier info,
                                         AnalyticsConfig config) {
      if (config.android.autoConfigure == false) {
        return;
      }

      info.SetMetaDataValue(AndroidAnalyticsConfig.temporaryDisableKey,
                            config.temporaryDisableCollection == false);
      info.SetMetaDataValue(AndroidAnalyticsConfig.permanentlyDisableKey,
                            config.permanentlyDisableCollection == false);
      info.SetMetaDataValue(AndroidAnalyticsConfig.personalAdsKey,
                            config.temporaryDisablePersonalizedAdvert == false);

      info.SetMetaDataValue(AndroidAnalyticsConfig.adidCollectionKey,
                            config.android.disableAndroidADID == false);
      info.SetMetaDataValue(AndroidAnalyticsConfig.ssaidCollectionKey,
                            config.android.disableAndroidSSAID == false);
    }

    /// <summary>
    /// Callback to setup Xcode project settings
    /// </summary>
    /// <param name="info">Xcode project info</param>
    /// <param name="config">Analytics config</param>
    internal static void OnXCodePostGen(XcodeProjectModifier info,
                                        AnalyticsConfig config) {
      if (config.iOS.autoConfigure == false) {
        return;
      }

      var projectInfo = (PlistElementDict)info.ProjectInfo;
      projectInfo.SetBoolean(IOSAnalyticsConfig.temporaryDisableKey,
                                  config.temporaryDisableCollection == false);
      projectInfo.SetBoolean(IOSAnalyticsConfig.permanentlyDisableKey,
                                  config.permanentlyDisableCollection);
      projectInfo.SetBoolean(
          IOSAnalyticsConfig.personalAdsKey,
          config.temporaryDisablePersonalizedAdvert == false);

      projectInfo.SetBoolean(
          IOSAnalyticsConfig.idfvCollectionKey,
          config.iOS.permanentlyDisableAppleIDFV == false);

      if (config.iOS.permanentlyDisableAppleIDFA == true) {
        ((PBXProject)info.Project).RemoveFrameworkFromProject(info.TargetGUID,
                                                              "AdSupport.framework");
      }
    }
  }
}
