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

// Always link the Crashlytics assembly, since it tends to be stripped,
// and a valid use case doesn't require the user to use the class directly.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Firebase.Crashlytics {
  using System;
  using System.Diagnostics;

  /// <summary>
  /// Firebase Crashlytics API
  /// </summary>
  [UnityEngine.Scripting.Preserve]
  public static class Crashlytics {

    /// <summary>
    /// Whether Crashlytics is set to report uncaught exceptions as fatal.
    /// Fatal exceptions count towards Crash Free Users and Velocity Alerts.
    /// It is recommended to enable this for new apps.
    /// <returns>
    /// true if Crashlytics is set to report uncaught exceptions as fatal, false otherwise.
    /// </returns>
    /// </summary>
    public static bool ReportUncaughtExceptionsAsFatal { get; set; } = false;

    /// <summary>
    /// Checks whether the Crashlytics specific data collection flag has been enabled.
    /// <returns>
    /// true if the platform level data collection flag is enabled or unset, false otherwise
    /// </returns>
    /// </summary>
    public static bool IsCrashlyticsCollectionEnabled {
      get {
        return PlatformAccessor.Impl.IsCrashlyticsCollectionEnabled();
      }
      set {
        PlatformAccessor.Impl.SetCrashlyticsCollectionEnabled(value);
      }
    }
    /// <summary>
    /// Initialize crash reporting for C# and Unity runtime.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    private static void Initialize() {
      // Initialization is tricky. There are 2 possibilities:
      //
      // 1) User calls Crashlytics.Method before calling CheckAndFixDependencies
      //    in which case we initialize PlatformAccessor, and Impl, which
      //    calls FirebaseApp.DefaultInstance, which ends up in this method. At
      //    this point Impl is null, because we are still inside
      //    PlatformAccessor's static constructor, which is ok because
      //    PlatformAccessor is the real initializer.
      // 2) User calls CheckAndFixDependencies first, which will call
      //    FirebaseApp.DefaultInstance, which will call this method. We grab a
      //    reference to impl to force PlatformAccessor's constructor to be
      //    called as early as possible
      var impl = PlatformAccessor.Impl;
    }

    /// <summary>
    /// Add text logs that will be sent with the next crash report.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message) {
      PlatformAccessor.Impl.Log(message);
    }

    /// <summary>
    /// Set a key/value pair to be sent with the next crash report.
    /// </summary>
    /// <param name="key">
    /// Key to associate with a given value. If the key already exists, the new
    /// value will overwrite the existing value. When crash reports are
    /// recorded, the current value associated with each key will be captured.
    /// </param>
    /// <param name="value">
    /// The value to associate with the given key.
    /// </param>
    public static void SetCustomKey(string key, string value) {
      PlatformAccessor.Impl.SetCustomKey(key, value);
    }

    /// <summary>
    /// Optionally set an end-user's ID number, token, or other unique value to
    /// be associated with subsequent crash reports.
    /// </summary>
    /// <param name="identifier">
    /// The user identifier to associate with subsequent crashes.
    /// </param>
    public static void SetUserId(string identifier) {
      PlatformAccessor.Impl.SetUserId(identifier);
    }

    /// <summary>
    /// Record a non-fatal exception.
    /// </summary>
    /// <param name="exception">
    /// The exception to log.
    /// </param>
    public static void LogException(Exception exception) {
      PlatformAccessor.Impl.LogException(exception);
    }

    /// <summary>
    /// Record a fatal exception.
    /// </summary>
    /// <param name="exception">
    /// The exception to log as fatal.
    /// </param>
    internal static void LogExceptionAsFatal(Exception exception) {
      PlatformAccessor.Impl.LogExceptionAsFatal(exception);
    }

    /// <summary>
    /// This class holds a privately held instances that should be accessed via
    /// the internal getters. This allows us to lazily initialize the instances
    /// we need while retaining interdependence between Impl and FirebaseApp.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    internal static class PlatformAccessor {
        private static ExceptionHandler _exceptionHandler;
        private static Impl _impl;
        private static FirebaseApp _app;

        static PlatformAccessor() {
            _exceptionHandler = new ExceptionHandler();
            _impl = Impl.Make();
            _app = FirebaseApp.DefaultInstance;

            if (!_impl.IsSDKInitialized()) {
              LogUtil.LogMessage(LogLevel.Debug, "Did not register exception handlers: Crashlytics SDK was not initialized");
              return;
            }

            _exceptionHandler.Register();
        }

        /// <summary>
        /// Return a singleton instance of an ExceptionHandler via lazy initialization
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        internal static ExceptionHandler ExceptionHandler {
            get {
                return _exceptionHandler;
            }
        }

        /// <summary>
        /// Return a singleton instance of an Impl via lazy initialization.
        /// A bit of history:
        /// We previously initialized Impl and FirebaseApp via static initialization
        /// in the Crashlytics class itself, but there is a circular dependency between
        /// the Crashlytics class which is acted upon via reflection from FirebaseApp.
        /// Because FirebaseApp was being accessed via the static constructor, the Crashlytics
        /// class does not exist at the time has not finished initialization because it
        /// was waiting for FirebaseApp to return DefaultInstance.
        /// </summary>
        internal static Impl Impl {
            // We leave the internal getters in for the Quickstart
            get {
                return _impl;
            }
        }

        /// <summary>
        /// Return the singleton instance as defined in FirebaseApp. This will perform
        /// platform initialization on Crashlytics if it has not already happened.
        /// </summary>
       [UnityEngine.Scripting.Preserve]
        internal static FirebaseApp App {
            get {
                return _app;
            }
        }
    }
  }
}
