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
using System.Reflection;

namespace Firebase.Platform {

// Helper class that contains functions that handle platform specific behavior.
internal static class PlatformInformation {
  // Is the current platform Android?
  internal static bool IsAndroid {
    get {
      return false;
    }
  }

  // Is the current platform iOS?
  internal static bool IsIOS {
    get {
      return false;
    }
  }

  internal static string DefaultConfigLocation {
    get {
      return ".";
    }
  }

  // The synchronization context that should be used.
  internal static System.Threading.SynchronizationContext SynchronizationContext {
    get { return null; }
  }

  // Get the time elapsed in seconds since the process started.
  // In Unity this must be called from the main thread.
  internal static float RealtimeSinceStartup {
    get {
      var startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
      return (float)((DateTime.UtcNow - startTime).TotalSeconds);
    }
  }

  // Get the time elapsed in seconds since the process started.
  // In Unity this can be called from any thread but doesn't get updated until
  // FirebaseMonoBehaviour starts executing.
  internal static float RealtimeSinceStartupSafe {
    get { return RealtimeSinceStartup; }
  }

  // Get the runtime name.
  internal static string RuntimeName { get { return "mono"; } }

  // Get the runtime version.
  internal static string RuntimeVersion {
    get {
      var monoRuntimeType = Type.GetType("Mono.Runtime");
      if (monoRuntimeType != null) {
        var displayNameMethod = monoRuntimeType.GetMethod(
          "GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
        if (displayNameMethod != null) {
          var version = displayNameMethod.Invoke(null, null) as string;
          if (version != null) return version;
        }
      }
      return "unknown";
    }
  }
}

}  // namespace Firebase.Platform
