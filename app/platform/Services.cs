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

#region

using System;
using System.Collections.Specialized;
using System.IO;
using Firebase.Platform.Default;

#endregion

namespace Firebase.Platform {
  /// <summary>
  /// This is how consumers get access to concrete implementations of platform
  /// services such as unity versions of AuthTokenProvider and FirebaseHttpRequest.
  /// This is primarily to allow testing with mono (non-unity) versions of these
  /// services. Unity specifics are centralized within Firebase.App which allow
  /// firebase components themselves to remain dependent only on Mono.
  /// This allows us to more easily run unit tests in Mono, and do adhoc testing
  /// using mono command line apps.
  /// </summary>
  internal static class Services {

    static Services() {
      // Platform neutral versions until unity versions get installed.
      AppConfig = AppConfigExtensions.Instance;
      Clock = SystemClock.Instance;
      Logging = DebugLogger.Instance;
    }

    // This service stores additional config properties that are either
    // platform specific (writeable app path) or user specified, but not
    // part of the common API.
    public static IAppConfigExtensions AppConfig { get; internal set; }

    // Having the clock as a platform service allows test infrastructure to fast
    // forward time artificially which is useful for retry tests.
    public static IClockService Clock { get; internal set; }

    // This allows us to replace calls to unityengine.debug with regular
    // c# logging and capturing the logs for test purposes.
    public static ILoggingService Logging { get; internal set; }

  }
}
