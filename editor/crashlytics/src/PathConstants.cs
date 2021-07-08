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


namespace Firebase.Crashlytics.Editor {
  using System.IO;
  using UnityEngine;
  using Firebase.Editor;

  /// <summary>
  ///  Define paths constants for collateral under the Unity project folder
  /// </summary>
  public static class PathConstants {

    /// <summary>
    ///  The relative path to the Android plugin directory from project directory.
    /// </summary>
    public static string ANDROID_PLUGIN_RELATIVE_DIRECTORY =
      Path.Combine(Utility.FIREBASE_ANDROID_PLUGIN_BASE_DIRECTORY, "FirebaseCrashlytics.androidlib");

    /// <summary>
    ///  The full path to the Android plugin directory
    /// </summary>
    public static string ANDROID_PLUGIN_DIRECTORY =
      Path.Combine(Path.Combine(Application.dataPath, ".."), ANDROID_PLUGIN_RELATIVE_DIRECTORY);

    /// <summary>
    ///  The full path to the the Android resource file
    /// </summary>
    public static string ANDROID_RESOURCE_FILE_PATH =
        Path.Combine(ANDROID_PLUGIN_DIRECTORY, Utility.ANDROID_RESOURCE_VALUE_DIRECTORY);
  }
}
