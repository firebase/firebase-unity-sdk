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
using System.Collections.Generic;
using System.Threading;
using Firebase.Platform;
using UnityEngine;

namespace Firebase.Crashlytics {
  internal sealed class LoggedException : Exception {
    private Dictionary<string, string>[] _parsedDictionaries;
    internal LoggedException(string name, string message, string stackTrace, int skipFrames = 0) : base(message ?? string.Empty) {
      Name = string.IsNullOrEmpty(name) ? "Exception" : name;
      CustomStackTrace = stackTrace ?? string.Empty;
      Frames = StackTraceParser.ParseFrames(CustomStackTrace, skipFrames);
    }
    internal static LoggedException FromException(Exception exception) =>
        exception == null ? null : exception is LoggedException existing ? existing
        : new LoggedException(exception.GetType().Name, exception.Message, exception.StackTrace);
    internal string Name { get; }
    internal string CustomStackTrace { get; }
    internal StackFrameData[] Frames { get; }
    internal Dictionary<string, string>[] ParsedStackTrace => _parsedDictionaries ??= StackTraceParser.ToDictionaries(Frames);
  }

  /// <summary>
  /// This class registers exception handlers in the Unity runtime.
  ///
  /// Exceptions caught by this class are serialized and logged to the
  /// platform-specific Crashlytics SDK as non-fatal exceptions.
  /// </summary>
  internal sealed class ExceptionHandler {
    private readonly object _lock = new object();
    private bool _isRegistered;
    internal bool IsRegistered { get { lock (_lock) return _isRegistered; } }

    /// <summary>
    /// Register both Mono and Unity-specific exception handlers
    /// </summary>
    internal void Register() {
      lock (_lock) {
        if (_isRegistered) return;
        Impl.SafeLog(PlatformLogLevel.Debug, "Registering Crashlytics exception handlers");
        AppDomain.CurrentDomain.UnhandledException -= HandleException;
        AppDomain.CurrentDomain.UnhandledException += HandleException;
        _isRegistered = true;
        UpdateUnityLogCallback(subscribe: true);
      }
    }

    internal void Unregister() {
      lock (_lock) {
        AppDomain.CurrentDomain.UnhandledException -= HandleException;
        _isRegistered = false;
        UpdateUnityLogCallback(subscribe: false);
      }
    }

    private void UpdateUnityLogCallback(bool subscribe) {
      int mainTid = MetadataBuilder.MainThreadId;
      if (mainTid != 0 && Thread.CurrentThread.ManagedThreadId != mainTid) {
        Action applyOnMain = () => { lock (_lock) ApplyUnityLogCallback(_isRegistered); };
        if (FirebaseHandler.DefaultInstance != null) _ = FirebaseHandler.RunOnMainThreadAsync(() => { applyOnMain(); return true; });
        else MetadataBuilder.MainSyncContext?.Post(_ => applyOnMain(), null);
        return;
      }
      ApplyUnityLogCallback(subscribe);
    }

    private void ApplyUnityLogCallback(bool subscribe) {
      try {
        Application.logMessageReceived -= HandleLog;
        if (subscribe) Application.logMessageReceived += HandleLog;
      } catch (Exception) {
        // Headless .NET test hosts do not load native UnityEngine runtime bindings.
      }
    }

    internal void HandleException(object sender, UnhandledExceptionEventArgs eArgs) {
      if (eArgs?.ExceptionObject == null) return;
      LogException(eArgs.ExceptionObject is Exception ex ? LoggedException.FromException(ex)
          : new LoggedException("UnhandledException", eArgs.ExceptionObject.ToString(), string.Empty));
    }

    internal void HandleLog(string message, string stackTraceString, LogType type) {
      if (type != LogType.Exception || message == null) return;
      SplitExceptionCondition(message, out string name, out string reason);
      LogException(new LoggedException(name, reason, stackTraceString));
    }

    internal static void SplitExceptionCondition(string message, out string name, out string reason) {
      if (!string.IsNullOrEmpty(message)) {
        // Split into two parts so we only split on the first delimiter
        int colon = message.IndexOf(':');
        if (colon > 0) {
          ReadOnlySpan<char> first = message.AsSpan(0, colon).Trim();
          // We want to make sure the first part of the message does not contain a
          // space because it should just be the name of an exception class
          if (first.Length > 0 && first.IndexOf(' ') < 0) {
            name = first.ToString(); reason = message.AsSpan(colon + 1).Trim().ToString(); return;
          }
        }
      }
      name = "Exception"; reason = message ?? string.Empty;
    }

    /// <summary>
    /// Forwards the exception to the Crashlytics platform SDK.
    /// </summary>
    internal void LogException(LoggedException e) {
      if (e == null) return;
      if (Impl.IsDebugLogEnabled()) Impl.SafeLog(PlatformLogLevel.Debug, $"Crashlytics recording exception: {e.Message}\nException stack trace:\n{e.CustomStackTrace}");
      if (Crashlytics.ReportUncaughtExceptionsAsFatal) Crashlytics.LogExceptionAsFatal(e);
      else Crashlytics.LogException(e);
    }
  }
}
