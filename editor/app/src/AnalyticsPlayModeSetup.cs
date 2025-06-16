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
                Type firebaseAnalyticsType = Type.GetType(FirebaseAnalyticsEditorConstants.AnalyticsTypeFullName);
                if (firebaseAnalyticsType != null) {
                    if (File.Exists(FirebaseAnalyticsEditorConstants.DllSourcePath)) {
                        try {
                            Directory.CreateDirectory(DestinationDir); // Should be benign for "./"
                            string destinationDllPath = Path.Combine(DestinationDir, FirebaseAnalyticsEditorConstants.DllName);
                            File.Copy(FirebaseAnalyticsEditorConstants.DllSourcePath, destinationDllPath, true);
                            Debug.Log("Firebase Analytics: Copied " + FirebaseAnalyticsEditorConstants.DllName + " to project root for Play Mode.");
                        } catch (Exception e) {
                            Debug.LogError("Firebase Analytics: Error copying " + FirebaseAnalyticsEditorConstants.DllName + " for Play Mode: " + e.Message);
                        }
                    } else {
                        // Optional: Log if source DLL is not found, as it might be expected if Analytics is not used.
                        // Debug.LogWarning("Firebase Analytics: Source DLL " + FirebaseAnalyticsEditorConstants.DllSourcePath + " not found. Skipping Play Mode setup.");
                    }
                }
            }
        }
    }
}

} // namespace Firebase.Editor
