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

using System;
using UnityEditor;
using UnityEngine;

namespace Firebase.Editor {

/// <summary>
/// Controls whether Firebase Analytics in the macOS Unity Editor forwards to
/// the Firebase Apple SDK embedded in the FirebaseCppApp bundle.
/// </summary>
/// <remarks>
/// The native layer disables the Apple SDK inside the Editor by default:
/// the Editor process reports the Editor's bundle ID and shares one
/// NSUserDefaults / measurement store across every Unity project on the
/// machine. When enabled here, events are sent in debug mode so they only
/// show up in the Firebase console's DebugView. Standalone macOS players are
/// unaffected and always use the Apple SDK.
///
/// The setting is per machine (EditorPrefs) and is passed to the native layer
/// through the FIREBASE_ANALYTICS_APPLE_SDK environment variable, which it
/// reads when Analytics initializes (i.e. when entering play mode). A value
/// set outside of Unity (e.g. by CI) takes precedence.
/// </remarks>
[InitializeOnLoad]
internal static class MacOSEditorAnalytics {
  private const string MenuPath =
      "Window/Firebase/Analytics/Send macOS Editor Events to DebugView";
  private const string PrefKey = "Firebase.Analytics.MacOSEditorAppleSdk";
  // Must match kBackendModeEnvironmentVariable in the C++ SDK
  // (analytics/src/analytics_desktop_apple.h).
  internal const string EnvironmentVariable = "FIREBASE_ANALYTICS_APPLE_SDK";
  // Marks EnvironmentVariable as owned by this class, so that values set
  // outside of Unity are left alone across domain reloads.
  private const string OwnerVariable = "FIREBASE_ANALYTICS_APPLE_SDK_SET_BY_EDITOR";

  static MacOSEditorAnalytics() {
    Apply();
  }

  /// <summary>
  /// Whether Analytics in the macOS Editor sends events to DebugView.
  /// </summary>
  internal static bool Enabled {
    get { return EditorPrefs.GetBool(PrefKey, false); }
    set {
      EditorPrefs.SetBool(PrefKey, value);
      Apply();
    }
  }

  private static bool IsMacOSEditor {
    get { return Application.platform == RuntimePlatform.OSXEditor; }
  }

  private static bool IsSetExternally {
    get {
      return !String.IsNullOrEmpty(
                 Environment.GetEnvironmentVariable(EnvironmentVariable)) &&
             String.IsNullOrEmpty(Environment.GetEnvironmentVariable(OwnerVariable));
    }
  }

  private static void Apply() {
    if (!IsMacOSEditor || IsSetExternally) return;
    if (Enabled) {
      Environment.SetEnvironmentVariable(EnvironmentVariable, "debug");
      Environment.SetEnvironmentVariable(OwnerVariable, "1");
    } else {
      Environment.SetEnvironmentVariable(EnvironmentVariable, null);
      Environment.SetEnvironmentVariable(OwnerVariable, null);
    }
  }

  [MenuItem(MenuPath, false, 1100)]
  private static void Toggle() {
    if (IsSetExternally) {
      Debug.LogWarning(String.Format(
          "Firebase Analytics: {0} is set outside of Unity ({1}); the menu " +
          "setting is ignored until it is unset.", EnvironmentVariable,
          Environment.GetEnvironmentVariable(EnvironmentVariable)));
      return;
    }
    Enabled = !Enabled;
    Debug.Log(Enabled ?
        "Firebase Analytics: macOS Editor events will be sent to DebugView " +
        "from the next time you enter play mode. Switching Firebase projects " +
        "requires restarting the Editor." :
        "Firebase Analytics: macOS Editor events are disabled from the next " +
        "time you enter play mode.");
  }

  [MenuItem(MenuPath, true)]
  private static bool ToggleValidate() {
    Menu.SetChecked(MenuPath, Enabled);
    return IsMacOSEditor;
  }
}

}  // namespace Firebase.Editor
