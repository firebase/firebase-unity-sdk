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

using Firebase;

namespace Firebase.Platform {
  // Provides functions that the Platform layer can use to interact with the
  // Firebase layer, since it cannot depend on it and call the functions directly.
  internal class FirebaseAppUtils : IFirebaseAppUtils {
    private static FirebaseAppUtils instance = new FirebaseAppUtils();
    public static FirebaseAppUtils Instance { get { return instance; } }

    // Runs the given function, handling any DllNotFoundExceptions that might
    // be thrown.
    public void TranslateDllNotFoundException(System.Action action) {
      FirebaseApp.TranslateDllNotFoundException(action);
    }

    // Called to poll any pending callbacks in the C++ -> C# path.
    public void PollCallbacks() { AppUtil.PollCallbacks(); }

    // Returns the default FirebaseApp, as a generic object.
    public IFirebaseAppPlatform GetDefaultInstance() {
      return FirebaseApp.DefaultInstance.AppPlatform;
    }

    // Returns the name used when creating the default FirebaseApp.
    public string GetDefaultInstanceName() {
      return FirebaseApp.DefaultName;
    }

    public PlatformLogLevel GetLogLevel() {
      Firebase.LogLevel currentLevel;
      try {
        currentLevel = FirebaseApp.LogLevel;
      } catch (Firebase.InitializationException) {
        // It isn't possible to get the log level if the native components are
        // missing.  Enable all log messages in the failure case.
        currentLevel = Firebase.LogLevel.Debug;
      }
      return LogUtil.ConvertLogLevel(currentLevel);
    }
  }
}
