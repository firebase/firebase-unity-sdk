/*
 * Copyright 2018 Google LLC
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

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

[InitializeOnLoad]
internal class DllLocationPatcher : AssetPostprocessor {
    // When in the build process the project should be patched.
    private const int BUILD_ORDER_PATCH_PROJECT = 1;

    // Delimiter for version numbers.
    private static char[] VERSION_DELIMITER = new char[] { '.' };

    /// <summary>
    /// Copy App.so/App.bundle from BuildDataFolder/Plugins/x86_64/ to proper location.
    /// This is to fix the specific DllNotFoundException happening in
    ///   OSX Standalone build using Unity 2017.x and 2018.x with Firebase Plugin 4.3.0+
    ///   Linux Standalone build using Unity 5.x with Firebase Plugin 4.5.0+
    /// </summary>
    [PostProcessBuildAttribute(BUILD_ORDER_PATCH_PROJECT)]
    internal static void OnPostProcessDllLocation(
            BuildTarget buildTarget, string pathToBuiltProject) {
        // BuildTarget enum can changes in different version of Unity, therefore use string
        // comparison instead.  Ex. Unity 2017 only has StandaloneOSX while Unity 5 has
        // StandaloneOSXUniversal, StandaloneOSXIntel and StandaloneOSXIntel64 :/
        string buildTargetString = buildTarget.ToString();

        // Get the first part of version number from unityVersion, ex. 2017 from "2017.4.1f1"
        long version = 0;
        string[] versionComponents = Application.unityVersion.Split(VERSION_DELIMITER);
        if (versionComponents.Length == 0 || !long.TryParse(versionComponents[0], out version) ||
            version == 0) {
            Debug.LogWarning("Cannot apply patch: unable to parse unityVersion: " +
                             Application.unityVersion);
            return;
        }

        bool patched = false;
        if (buildTargetString.StartsWith("StandaloneOSX") && version >= 2017) {
            // pathToBuiltProject is both the executable and data folder.
            // Ex.
            // pathToBuiltProject = path/to/build.app
            // srcFolder          = path/to/build.app/Contents/Plugins/x86_64/
            // dstFolder          = path/to/build.app/Contents/Frameworks/MonoEmbedRuntime/osx/
            string srcFolder = Path.Combine(pathToBuiltProject,
                                            "Contents/Plugins/x86_64/");
            string dstFolder = Path.Combine(pathToBuiltProject,
                                            "Contents/Frameworks/MonoEmbedRuntime/osx/");
            // Later versions of Unity 2018 correctly place 64-bit libraries in the correct
            // location.  This can be detected by checking for the existance of srcFolder.
            if (Directory.Exists(srcFolder)) {
              CopyLibrary(srcFolder, dstFolder, "lib", "bundle");
            }
            patched = true;
        } else if (buildTargetString.StartsWith("StandaloneLinux") && version == 5) {
            // pathToBuiltProject is executable binaray.
            // Ex.
            // pathToBuiltProject = path/to/build.x86_64
            // srcFolder          = path/to/build_Data/Plugins/x86_64/
            // dstFolder          = path/to/build_Data/Mono/x86_64/
            string binaryFileName = Path.GetFileNameWithoutExtension(pathToBuiltProject);
            string dataFolder = Path.Combine(Path.GetDirectoryName(pathToBuiltProject),
                                             binaryFileName + "_Data");
            string srcFolder = Path.Combine(dataFolder, "Plugins/x86_64/");
            string dstFolder = Path.Combine(dataFolder, "Mono/x86_64/");
            CopyLibrary(srcFolder, dstFolder, "lib", "so");
            patched = true;
        }
        if (patched) {
            Measurement.ReportWithBuildTarget("desktop/dlllocationpatched",
                                              null, "Shared Library Location Patched");
        }
    }

    internal static void CopyLibrary(
            string srcFolder, string dstFolder, string prefix, string extension) {
        Debug.Log("Post process to patch App." + extension + "'s location");
        Directory.CreateDirectory(dstFolder);
        // Only search for App since this is the only library causing problem for now.
        string[] srcFilePaths = Directory.GetFiles(srcFolder, "*App*." + extension);
        foreach (string srcFilePath in srcFilePaths) {
            string srcFileName = Path.GetFileName(srcFilePath);
            string dstFilePath = Path.Combine(dstFolder, prefix + srcFileName);
            File.Copy(srcFilePath, dstFilePath);
            Debug.Log("Copied " + srcFilePath + " to " + dstFilePath);
        }
    }
}

}  // namespace Firebase.Editor
