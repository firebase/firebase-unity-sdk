/*
 * Copyright 2017 Google LLC
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
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Firebase.Platform.Default {

  internal class UnityConfigExtensions : AppConfigExtensions {
    private static UnityConfigExtensions _instance = new UnityConfigExtensions();
    public static IAppConfigExtensions DefaultInstance { get { return _instance; } }

    /// <summary>
    /// Returns a valid writeable path for the app.
    /// </summary>
    public override string GetWriteablePath(IFirebaseAppPlatform app) {
      return FirebaseHandler.RunOnMainThread(() => {
        return Application.persistentDataPath;
      });
    }

    public override void SetEditorP12FileName(IFirebaseAppPlatform app, string p12Filename) {
      // Since UnityEngine.Application.dataPath can only be accessed from main thread, this would
      // access the property from the main thread and return the value back to current thread.
      string dataPath = FirebaseHandler.RunOnMainThread(() => {
        return UnityEngine.Application.dataPath;
      });

      string p12Path = p12Filename;
      if (!string.IsNullOrEmpty(p12Path) && !File.Exists(p12Path)) {
        // Try an auto lookup under Editor DefaultResources.
        p12Path = dataPath
          + System.IO.Path.DirectorySeparatorChar
          + "Editor Default Resources"
          + System.IO.Path.DirectorySeparatorChar
          + p12Path;
      }

      if (!string.IsNullOrEmpty(p12Path) && !File.Exists(p12Path)) {
        FirebaseLogger.LogMessage(
          PlatformLogLevel.Warning,
          p12Filename + " was not found.  Also looked in " + p12Path);
      }

      base.SetEditorP12FileName(app, p12Path);
    }

  }
}
