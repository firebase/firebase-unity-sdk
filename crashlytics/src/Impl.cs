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
using System.Threading;
using Firebase.Platform;

namespace Firebase.Crashlytics {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  [UnityEngine.Scripting.Preserve]
  internal sealed class Impl : IDisposable {
    internal static Action<LoggedException, bool> ExceptionRecordedForTesting;
    internal static Func<bool> IsInitializedOverrideForTesting;

    private Impl() { }
    internal static Impl Make() => new Impl();
    public void LogException(Exception exception) => RecordManaged(exception, isFatal: false);
    public void LogExceptionAsFatal(Exception exception) => RecordManaged(exception, isFatal: true);

#if (UNITY_ANDROID || UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
#if UNITY_ANDROID
    private readonly AndroidImpl _native = new AndroidImpl();
#else
    private readonly IOSImpl _native = new IOSImpl();
#endif
    [UnityEngine.Scripting.Preserve]
    public bool IsSDKInitialized() => IsInitializedOverrideForTesting?.Invoke() ?? (_native != null && _native.IsInitialized);
    public bool DidCrashOnPreviousExecution() => _native != null && _native.DidCrashOnPreviousExecution();
    public void Log(string message) => _native.Log(message);
    public void SetCustomKey(string key, string value) => _native.SetCustomKey(key, value);
    public void SetUserId(string identifier) => _native.SetUserId(identifier);
    public bool IsCrashlyticsCollectionEnabled() => _native.IsCrashlyticsCollectionEnabled();
    public void SetCrashlyticsCollectionEnabled(bool enabled) => _native.SetCrashlyticsCollectionEnabled(enabled);
    public void Dispose() => _native?.Dispose();

    private void RecordManaged(Exception exception, bool isFatal) {
      if (exception == null) return;
      LoggedException logged = LoggedException.FromException(exception);
      ExceptionRecordedForTesting?.Invoke(logged, isFatal);
      _native.RecordException(logged, isFatal);
    }
#else
    private int _desktopCollectionEnabled = 1;
    [UnityEngine.Scripting.Preserve] public bool IsSDKInitialized() => IsInitializedOverrideForTesting?.Invoke() ?? false;
    public bool DidCrashOnPreviousExecution() => false;
    public void Log(string message) { if (IsDebugLogEnabled()) SafeLog(PlatformLogLevel.Debug, $"Would log custom message if running on a physical device: {message}"); }
    public void SetCustomKey(string key, string value) { if (IsDebugLogEnabled()) SafeLog(PlatformLogLevel.Debug, $"Would set key-value if running on a physical device: {key}:{value}"); }
    public void SetUserId(string identifier) { if (IsDebugLogEnabled()) SafeLog(PlatformLogLevel.Debug, $"Would set user identifier if running on a physical device: {identifier}"); }
    public bool IsCrashlyticsCollectionEnabled() {
      if (IsDebugLogEnabled()) SafeLog(PlatformLogLevel.Debug, "Would get Crashlytics data collection if running on a physical device");
      return Volatile.Read(ref _desktopCollectionEnabled) != 0;
    }
    public void SetCrashlyticsCollectionEnabled(bool enabled) {
      Volatile.Write(ref _desktopCollectionEnabled, enabled ? 1 : 0);
      if (IsDebugLogEnabled()) SafeLog(PlatformLogLevel.Debug, $"Would set Crashlytics data collection if running on a physical device: {enabled}");
    }
    public void Dispose() { }

    private void RecordManaged(Exception exception, bool isFatal) {
      if (exception == null) return;
      LoggedException logged = LoggedException.FromException(exception);
      ExceptionRecordedForTesting?.Invoke(logged, isFatal);
      if (IsDebugLogEnabled()) {
        SafeLog(PlatformLogLevel.Debug, isFatal ? $"Would log exception as fatal if running on a physical device: {exception}" : $"Would log exception if running on a physical device: {exception}");
      }
    }
#endif

    internal static bool IsDebugLogEnabled() => FirebaseHandler.AppUtils is var u && u != null && u.GetLogLevel() <= PlatformLogLevel.Debug;
    internal static void SafeLog(PlatformLogLevel level, string message) {
      if (FirebaseHandler.AppUtils == null) return;
      try {
        FirebaseLogger.LogMessage(level, message);
      } catch (Exception) {
        // Ignore logger ECall errors when invoked from standalone .NET test runners.
      }
    }
  }
}
