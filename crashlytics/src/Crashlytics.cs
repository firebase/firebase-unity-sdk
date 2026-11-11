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

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Firebase.Internal;
using Firebase.Platform;

// Always link the Crashlytics assembly, since it tends to be stripped,
// and a valid use case doesn't require the user to use the class directly.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]
[assembly: InternalsVisibleTo("Firebase.Crashlytics.Test"), InternalsVisibleTo("Firebase.Tests")]
[assembly: InternalsVisibleTo("Firebase.Benchmarks"), InternalsVisibleTo("Firebase.TestHarness")]
[assembly: InternalsVisibleTo("Assembly-CSharp"), InternalsVisibleTo("Assembly-CSharp-Editor"), InternalsVisibleTo("Tests")]

namespace Firebase.Crashlytics {
  /// <summary>Represents a single stack frame reported to Firebase Crashlytics.</summary>
  [UnityEngine.Scripting.Preserve]
  public class FirebaseCrashlyticsFrame : IDisposable {
    /// <summary>Initializes a new instance of the <see cref="FirebaseCrashlyticsFrame"/> class.</summary>
    public FirebaseCrashlyticsFrame() { }
    /// <summary>Gets or sets the library or class name associated with the stack frame.</summary>
    public string library { get; set; }
    /// <summary>Gets or sets the symbol or method name associated with the stack frame.</summary>
    public string symbol { get; set; }
    /// <summary>Gets or sets the source file name associated with the stack frame.</summary>
    public string fileName { get; set; }
    /// <summary>Gets or sets the line number associated with the stack frame.</summary>
    public string lineNumber { get; set; }
    /// <summary>Releases all resources used by the <see cref="FirebaseCrashlyticsFrame"/>.</summary>
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    /// <summary>Releases unmanaged and optionally managed resources.</summary>
    /// <param name="disposing">Whether managed resources should also be disposed.</param>
    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>
  /// Firebase Crashlytics API
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  [UnityEngine.Scripting.Preserve]
  public static class Crashlytics {
    private static int s_reportUncaughtAsFatal;

    /// <summary>
    /// Whether Crashlytics is set to report uncaught exceptions as fatal.
    /// Fatal exceptions count towards Crash Free Users and Velocity Alerts.
    /// It is recommended to enable this for new apps.
    /// <returns>
    /// true if Crashlytics is set to report uncaught exceptions as fatal, false otherwise.
    /// </returns>
    /// </summary>
    public static bool ReportUncaughtExceptionsAsFatal {
      get => Volatile.Read(ref s_reportUncaughtAsFatal) != 0;
      set => Volatile.Write(ref s_reportUncaughtAsFatal, value ? 1 : 0);
    }

    /// <summary>
    /// Checks whether the Crashlytics specific data collection flag has been enabled.
    /// <returns>
    /// true if the platform level data collection flag is enabled or unset, false otherwise
    /// </returns>
    /// </summary>
    public static bool IsCrashlyticsCollectionEnabled {
      get => PlatformAccessor.Impl.IsCrashlyticsCollectionEnabled();
      set => PlatformAccessor.Impl.SetCrashlyticsCollectionEnabled(value);
    }
    internal static bool DidCrashOnPreviousExecution => PlatformAccessor.Impl.DidCrashOnPreviousExecution();

    /// <summary>
    /// Initialize crash reporting for C# and Unity runtime.
    /// </summary>
    [UnityEngine.Scripting.Preserve] private static void Initialize() => PlatformAccessor.InitializeFromApp();

    /// <summary>
    /// Add text logs that will be sent with the next crash report.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public static void Log(string message) => PlatformAccessor.Impl.Log(message);

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
    public static void SetCustomKey(string key, string value) => PlatformAccessor.Impl.SetCustomKey(key, value);

    /// <summary>
    /// Optionally set an end-user's ID number, token, or other unique value to
    /// be associated with subsequent crash reports.
    /// </summary>
    /// <param name="identifier">
    /// The user identifier to associate with subsequent crashes.
    /// </param>
    public static void SetUserId(string identifier) => PlatformAccessor.Impl.SetUserId(identifier);

    /// <summary>
    /// Record a non-fatal exception.
    /// </summary>
    /// <param name="exception">
    /// The exception to log.
    /// </param>
    public static void LogException(Exception exception) => PlatformAccessor.Impl.LogException(exception);

    /// <summary>
    /// Record a fatal exception.
    /// </summary>
    /// <param name="exception">
    /// The exception to log as fatal.
    /// </param>
    internal static void LogExceptionAsFatal(Exception exception) => PlatformAccessor.Impl.LogExceptionAsFatal(exception);

#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() { MetadataBuilder.CaptureMainThreadSnapshot(); PlatformAccessor.ResetForReload(); }
#endif

    /// <summary>
    /// This class holds a privately held instances that should be accessed via
    /// the internal getters. This allows us to lazily initialize the instances
    /// we need while retaining interdependence between Impl and FirebaseApp.
    /// </summary>
    [UnityEngine.Scripting.Preserve]
#if UNITY_6000_6_OR_NEWER
    [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class PlatformAccessor {
      private static readonly object s_lock = new object();
      [ThreadStatic] private static bool t_resolvingApp;
      private static ExceptionHandler _exceptionHandler;
      [UnityEngine.Scripting.Preserve] private static Impl _impl;
      private static FirebaseApp _app;
      private static bool _hooksRegistered;

      internal static void InitializeFromApp() { if (!t_resolvingApp) _ = EnsureInitialized(); }

      internal static (Impl Impl, ExceptionHandler Handler, FirebaseApp App) EnsureInitialized() {
        lock (s_lock) {
          if (!_hooksRegistered) {
            _hooksRegistered = true;
            FirebaseInstanceCache.RegisterResetHook(ResetForReload);
            AppDomain.CurrentDomain.DomainUnload += (_, __) => ResetForReload();
          }
          if (_impl != null && _exceptionHandler != null) return (_impl, _exceptionHandler, _app);
        }
        // Resolve DefaultInstance outside s_lock so RunOnMainThread(Crashlytics.Initialize) never AB-BA deadlocks.
        FirebaseApp resolvedApp = null;
        if (!t_resolvingApp) {
          t_resolvingApp = true;
          try { resolvedApp = TryResolveDefaultApp(); } finally { t_resolvingApp = false; }
        }
        lock (s_lock) {
          _exceptionHandler ??= new ExceptionHandler();
          if (_impl == null) {
            _app = resolvedApp;
            _impl = Impl.Make();
            if (_impl.IsSDKInitialized()) _exceptionHandler.Register();
            else Impl.SafeLog(PlatformLogLevel.Debug, "Did not register exception handlers: Crashlytics SDK was not initialized");
          } else if (_app == null && resolvedApp != null) _app = resolvedApp;
          return (_impl, _exceptionHandler, _app);
        }
      }

      internal static void ResetForReload() {
        Impl oldImpl;
        lock (s_lock) {
          Volatile.Write(ref s_reportUncaughtAsFatal, 0);
          _exceptionHandler?.Unregister();
          oldImpl = _impl; _impl = null; _app = null;
        }
        oldImpl?.Dispose();
      }

      private static FirebaseApp TryResolveDefaultApp() {
#if (UNITY_ANDROID || UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        return FirebaseApp.DefaultInstance;
#else
        try {
          return FirebaseApp.DefaultInstance;
        } catch (Exception) {
          // Headless .NET test hosts without desktop FirebaseApp config proceed with stub mode.
          return null;
        }
#endif
      }

      /// <summary>
      /// Return a singleton instance of an ExceptionHandler via lazy initialization
      /// </summary>
      [UnityEngine.Scripting.Preserve] internal static ExceptionHandler ExceptionHandler => EnsureInitialized().Handler;

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
      [UnityEngine.Scripting.Preserve] internal static Impl Impl =>
        // We leave the internal getters in for the Quickstart
        Volatile.Read(ref _impl) ?? EnsureInitialized().Impl;

      /// <summary>
      /// Return the singleton instance as defined in FirebaseApp. This will perform
      /// platform initialization on Crashlytics if it has not already happened.
      /// </summary>
      [UnityEngine.Scripting.Preserve] internal static FirebaseApp App => EnsureInitialized().App;
    }
  }
}
