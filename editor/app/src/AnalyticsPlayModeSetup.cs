using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Firebase.Editor {

[InitializeOnLoad]
internal static class AnalyticsPlayModeSetup {
    private const string SourceDllPath = "./analytics_win.dll";
    private const string DestinationDir = "Assets/Plugins/";
    private const string DestinationDllName = "analytics_win.dll";

    static AnalyticsPlayModeSetup() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state) {
        if (state == PlayModeStateChange.EnteredPlayMode) {
            if (Application.platform == RuntimePlatform.WindowsEditor) {
                Type firebaseAnalyticsType = Type.GetType("Firebase.Analytics.FirebaseAnalytics, Firebase.Analytics");
                if (firebaseAnalyticsType != null) {
                    if (File.Exists(SourceDllPath)) {
                        try {
                            Directory.CreateDirectory(DestinationDir);
                            string destinationDllPath = Path.Combine(DestinationDir, DestinationDllName);
                            File.Copy(SourceDllPath, destinationDllPath, true);
                            Debug.Log("Firebase Analytics: Copied " + DestinationDllName + " to " + DestinationDir + " for Play Mode.");
                            AssetDatabase.Refresh();
                        } catch (Exception e) {
                            Debug.LogError("Firebase Analytics: Error copying " + DestinationDllName + " for Play Mode: " + e.Message);
                        }
                    } else {
                        // Optional: Log if source DLL is not found, as it might be expected if Analytics is not used.
                        // Debug.LogWarning("Firebase Analytics: Source DLL " + SourceDllPath + " not found. Skipping Play Mode setup.");
                    }
                }
            }
        }
    }
}

} // namespace Firebase.Editor
