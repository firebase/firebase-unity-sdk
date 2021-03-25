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

namespace Firebase.Editor {

using GooglePlayServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

[InitializeOnLoad]
internal class AndroidSettingsChecker : AssetPostprocessor {
    static bool checkedVersion = false;
    const int MinSupportedAndroidApiLevel = 14;

    static AndroidSettingsChecker() {
        if (EditorUserBuildSettings.activeBuildTarget ==
                    BuildTarget.Android) {
            CheckMinimumAndroidVersion();
        }
    }

    // Check the minimum android sdk and ensure its >=14.
    private static void CheckMinimumAndroidVersion() {
        if (!checkedVersion) {
            checkedVersion = true;
            var detectedApiLevel = UnityCompat.GetAndroidMinSDKVersion();
            bool isBelowMinSupported = detectedApiLevel < MinSupportedAndroidApiLevel;

            if (isBelowMinSupported) {
                Debug.LogError(DocRef.AndroidSdkVersionMismatch);
                var fix = EditorUtility.DisplayDialog(
                    DocRef.AndroidSdkVersionMismatchSummary,
                    DocRef.AndroidSdkVersionMismatch + "\n" +
                    DocRef.AndroidSdkVersionChange,
                    DocRef.Yes, cancel: DocRef.No);
                if (fix) {
                    bool setSdkVersion = UnityCompat.SetAndroidMinSDKVersion(
                            MinSupportedAndroidApiLevel);
                    if (!setSdkVersion) {
                        // Get the highest installed SDK version to see whether it's
                        // suitable.
                        if (UnityCompat.FindNewestInstalledAndroidSDKVersion() >=
                            MinSupportedAndroidApiLevel) {
                            // Set the mode to "auto" to use the latest installed
                            // version.
                            setSdkVersion = UnityCompat.SetAndroidMinSDKVersion(-1);
                        }
                    }

                    Measurement.analytics.Report(
                        "android/incompatibleapilevel/apply",
                        new KeyValuePair<string, string>[] {
                            new KeyValuePair<string, string>(
                                "androidApiLevel",
                                UnityCompat.GetAndroidMinSDKVersion().ToString())
                        },
                        "Incompatible Android API Level: Apply");

                    if (!setSdkVersion) {
                        Debug.LogError(String.Format(DocRef.AndroidMinSdkVersionSetFailure,
                            MinSupportedAndroidApiLevel));
                    }
                } else {
                    Measurement.analytics.Report(
                        "android/incompatibleapilevel/cancel",
                        new KeyValuePair<string, string>[] {
                            new KeyValuePair<string, string>("androidApiLevel",
                                                             detectedApiLevel.ToString())
                        },
                        "Incompatible Android API Level: Cancel");
                }
            }
        }
    }
}
}
