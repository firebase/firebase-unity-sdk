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

namespace Firebase.Platform {

// Helper class that contains functions that handle platform specific behavior.
// This is done through the UnityEngine API.
internal static class PlatformInformation {
  private static string runtimeVersion;

  // Is the current platform Android?
  internal static bool IsAndroid {
    get {
      return UnityEngine.Application.platform == UnityEngine.RuntimePlatform.Android;
    }
  }

  // Is the current platform iOS or tvOS?
  internal static bool IsIOS {
    get {
      return UnityEngine.Application.platform == UnityEngine.RuntimePlatform.IPhonePlayer ||
         UnityEngine.Application.platform == UnityEngine.RuntimePlatform.tvOS;
    }
  }

  // Get the default location for Firebase configuration files.
  // This should be called from the main thread after FirebaseHandler is initialized.
  internal static string DefaultConfigLocation {
    get {
      return FirebaseHandler.RunOnMainThread(() => {
          return UnityEngine.Application.streamingAssetsPath;
        });
    }
  }

  // The synchronization context that should be used.
  internal static System.Threading.SynchronizationContext SynchronizationContext {
    get { return Firebase.Unity.UnitySynchronizationContext.Instance; }
  }

  // Get the time elapsed in seconds since the process started.
  // In Unity this must be called from the main thread.
  internal static float RealtimeSinceStartup {
    get { return UnityEngine.Time.realtimeSinceStartup; }
  }

  // Get the time elapsed in seconds since the process started.
  // In Unity this can be called from any thread but doesn't get updated until
  // FirebaseMonoBehaviour starts executing.
  internal static float RealtimeSinceStartupSafe { get; set; }

  // Get the runtime name.
  internal static string RuntimeName { get { return "unity"; } }

  // Get the runtime version.
  // This should be called from the main thread after FirebaseHandler is initialized.
  internal static string RuntimeVersion {
      get {
        if (runtimeVersion == null) {
          runtimeVersion = FirebaseHandler.RunOnMainThread(() => {
              return UnityEngine.Application.unityVersion;
          });
        }
        return runtimeVersion;
      }
  }
}

}  // namespace Firebase
