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

//[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Firebase.Crashlytics.Editor.Test")]
namespace Firebase.Crashlytics.Editor {

  using UnityEngine;
  using UnityEditor;

  /// <summary>
  /// Utility to get the version of the Crashlytics Editor DLL.
  /// </summary>
  internal static class VersionInfo {

    private static char[] VERSION_DELIMITER = new char[] { '.' };

    /// <summary>
    /// Return the major version of the Unity environment as an integer. Pass
    /// Application.unityVersion as the argument.
    /// </summary>
    /// <param name="unityVersion">Application.unityVersion</param>
    internal static int GetUnityMajorVersion(string unityVersion) {
      var unityVersionParts = unityVersion.Split(VERSION_DELIMITER);
      if (unityVersionParts.Length == 0) {
        return -1;
      }
      var versionMajorPart = unityVersionParts[0];
      var versionMajorNumber = 0;
      if ( ! System.Int32.TryParse(versionMajorPart, out versionMajorNumber)) {
        return -1;
      }
      return versionMajorNumber;
    }
  }
}

