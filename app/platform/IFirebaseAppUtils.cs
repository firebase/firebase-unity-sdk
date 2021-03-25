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

  // Provides functions that the Platform layer can use to interact with the
  // Firebase layer, since it cannot depend on it and call the functions directly.
  internal interface IFirebaseAppUtils {
    // Runs the given function, handling any DllNotFoundExceptions that might
    // be thrown.
    void TranslateDllNotFoundException(System.Action action);

    // Called to poll any pending callbacks in the C++ -> C# path.
    void PollCallbacks();

    // Returns the default FirebaseApp, as a generic object.
    IFirebaseAppPlatform GetDefaultInstance();

    // Returns the name used when creating the default FirebaseApp.
    string GetDefaultInstanceName();

    // Gets the current log level of FirebaseApp.
    PlatformLogLevel GetLogLevel();
  }

  // Stub implementation of the above interface, used by FirebaseHandler until
  // FirebaseApp provided the real implementation.
  internal class FirebaseAppUtilsStub : IFirebaseAppUtils {
    private static FirebaseAppUtilsStub _instance = new FirebaseAppUtilsStub();
    public static FirebaseAppUtilsStub Instance { get { return _instance; } }

    public void TranslateDllNotFoundException(System.Action action) {
      action();
    }

    public void PollCallbacks() { }

    public IFirebaseAppPlatform GetDefaultInstance() { return null; }

    public string GetDefaultInstanceName() { return "__FIRAPP_DEFAULT"; }

    public PlatformLogLevel GetLogLevel() {
      return PlatformLogLevel.Debug;
    }
  }
}
