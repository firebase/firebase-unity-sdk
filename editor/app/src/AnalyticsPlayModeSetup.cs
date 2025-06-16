using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Firebase.Editor {

[InitializeOnLoad]
internal static class AnalyticsPlayModeSetup {
    private const string DestinationDir = "./";

    static AnalyticsPlayModeSetup() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state) {
        if (state == PlayModeStateChange.EnteredPlayMode) {
            if (Application.platform == RuntimePlatform.WindowsEditor) {
                Type firebaseAnalyticsType = Type.GetType("Firebase.Analytics.FirebaseAnalytics, Firebase.Analytics");
                if (firebaseAnalyticsType != null) {
                    if (File.Exists("./analytics_win.dll")) {
                        string fullSourcePath = Path.GetFullPath("./analytics_win.dll");
                        // string destinationDirectory = "./"; // This is DestinationDir
                        string fullDestinationPath = Path.GetFullPath(Path.Combine(DestinationDir, "analytics_win.dll"));

                        if (fullSourcePath.Equals(fullDestinationPath, StringComparison.OrdinalIgnoreCase)) {
                            Debug.Log("Firebase Analytics: analytics_win.dll is already in the project root. No copy needed for Play Mode.");
                            return;
                        }

                        try {
                            Directory.CreateDirectory(DestinationDir); // Should be benign for "./"
                            string destinationDllPath = Path.Combine(DestinationDir, "analytics_win.dll");
                            File.Copy("./analytics_win.dll", destinationDllPath, true);
                            Debug.Log("Firebase Analytics: Copied analytics_win.dll to project root for Play Mode.");
                        } catch (Exception e) {
                            Debug.LogError("Firebase Analytics: Error copying analytics_win.dll for Play Mode: " + e.Message);
                        }
                    } else {
                        // Optional: Log if source DLL is not found, as it might be expected if Analytics is not used.
                        // Debug.LogWarning("Firebase Analytics: Source DLL ./analytics_win.dll not found. Skipping Play Mode setup.");
                    }
                }
            }
        }
    }
}

} // namespace Firebase.Editor
