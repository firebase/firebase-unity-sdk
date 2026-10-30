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
/// Controls how Firebase Analytics behaves in the macOS Unity Editor, where it
/// forwards to the Firebase Apple SDK embedded in the FirebaseCppApp bundle.
/// </summary>
/// <remarks>
/// Analytics is on by default in the Editor, in debug mode: events go through
/// the Apple SDK and appear in the Firebase console's DebugView, and Firebase
/// excludes them from production reports and the BigQuery export. Turn debug
/// mode off to send Editor events as regular traffic, or turn Analytics off
/// in the Editor entirely. Standalone macOS players are unaffected.
///
/// Editor caveats: the Editor process is the app as far as macOS is concerned,
/// so the native layer sets the bundle ID from the Firebase config, and the
/// Apple SDK's local storage (including the app instance ID) is shared by
/// every Unity project on the machine.
///
/// The settings are per machine (EditorPrefs) and are passed to the native
/// layer through the FIREBASE_ANALYTICS_APPLE_SDK environment variable when
/// entering play mode. Enabling / disabling applies the next time you enter
/// play mode; the Apple SDK reads debug mode once per Editor process, so that
/// change needs an Editor restart. A value set outside of Unity (e.g. by CI)
/// takes precedence.
/// </remarks>
[InitializeOnLoad]
internal static class MacOSEditorAnalytics {
  private const string EnabledMenuPath =
      "Window/Firebase/Analytics/macOS Editor: Analytics Enabled";
  private const string DebugMenuPath =
      "Window/Firebase/Analytics/macOS Editor: Debug Mode (DebugView)";
  private const string EnabledPrefKey = "Firebase.Analytics.MacOSEditor.Enabled";
  private const string DebugPrefKey = "Firebase.Analytics.MacOSEditor.DebugMode";
  // Must match kBackendModeEnvironmentVariable in the C++ SDK
  // (analytics/src/analytics_desktop_apple.h).
  internal const string EnvironmentVariable = "FIREBASE_ANALYTICS_APPLE_SDK";
  // Marks EnvironmentVariable as owned by this class, so that values set
  // outside of Unity are left alone across domain reloads.
  private const string OwnerVariable = "FIREBASE_ANALYTICS_APPLE_SDK_SET_BY_EDITOR";

  static MacOSEditorAnalytics() {
    Apply();
  }

  /// <summary>Whether Analytics is enabled in the macOS Editor.</summary>
  internal static bool Enabled {
    get { return EditorPrefs.GetBool(EnabledPrefKey, true); }
    set {
      EditorPrefs.SetBool(EnabledPrefKey, value);
      Apply();
    }
  }

  /// <summary>
  /// Whether Editor events are sent in debug mode (DebugView only, excluded
  /// from production reports).
  /// </summary>
  internal static bool DebugMode {
    get { return EditorPrefs.GetBool(DebugPrefKey, true); }
    set {
      EditorPrefs.SetBool(DebugPrefKey, value);
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
    string mode = !Enabled ? "disabled" : (DebugMode ? "debug" : "enabled");
    Environment.SetEnvironmentVariable(EnvironmentVariable, mode);
    Environment.SetEnvironmentVariable(OwnerVariable, "1");
  }

  private static bool WarnIfSetExternally() {
    if (!IsSetExternally) return false;
    Debug.LogWarning(String.Format(
        "Firebase Analytics: {0} is set outside of Unity ({1}); the menu " +
        "setting is ignored until it is unset.", EnvironmentVariable,
        Environment.GetEnvironmentVariable(EnvironmentVariable)));
    return true;
  }

  private const string PlayModeNote =
      " This applies the next time you enter play mode.";
  private const string RestartNote =
      " Restart the Editor for the change to take effect.";

  [MenuItem(EnabledMenuPath, false, 1100)]
  private static void ToggleEnabled() {
    if (WarnIfSetExternally()) return;
    Enabled = !Enabled;
    Debug.Log((Enabled ?
        "Firebase Analytics: enabled in the macOS Editor." :
        "Firebase Analytics: disabled in the macOS Editor.") + PlayModeNote);
  }

  [MenuItem(EnabledMenuPath, true)]
  private static bool ToggleEnabledValidate() {
    Menu.SetChecked(EnabledMenuPath, Enabled);
    return IsMacOSEditor;
  }

  [MenuItem(DebugMenuPath, false, 1101)]
  private static void ToggleDebugMode() {
    if (WarnIfSetExternally()) return;
    DebugMode = !DebugMode;
    Debug.Log((DebugMode ?
        "Firebase Analytics: macOS Editor events are sent in debug mode " +
        "(DebugView only)." :
        "Firebase Analytics: macOS Editor events are sent as regular " +
        "traffic and count toward production reports.") + RestartNote);
  }

  [MenuItem(DebugMenuPath, true)]
  private static bool ToggleDebugModeValidate() {
    Menu.SetChecked(DebugMenuPath, DebugMode);
    return IsMacOSEditor && Enabled;
  }
}

}  // namespace Firebase.Editor
