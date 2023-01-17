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
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Firebase.Crashlytics.Test")]

namespace Firebase.Crashlytics {

  using System;
  using System.Runtime.InteropServices;
  using UnityEngine;

  /// <summary>
  /// This class registers exception handlers in the Unity runtime.
  ///
  /// Exceptions caught by this class are serialized and logged to the
  /// platform-specific Crashlytics SDK as non-fatal exceptions.
  /// </summary>
  internal class ExceptionHandler {

    private bool isRegistered = false;

    /// <summary>
    /// Register both Mono and Unity-specific exception handlers
    /// </summary>
    internal void Register() {
      if (isRegistered) {
        return;
      }

      LogUtil.LogMessage(LogLevel.Debug, "Registering Crashlytics exception handlers");
      AppDomain.CurrentDomain.UnhandledException += HandleException;

      if (Application.unityVersion.StartsWith("4.", System.StringComparison.OrdinalIgnoreCase)) {
        Application.RegisterLogCallback(HandleLog);
      } else {
        Application.logMessageReceived += HandleLog;
      }
      isRegistered = true;
    }

    internal void HandleException(object sender, UnhandledExceptionEventArgs eArgs) {
      Exception e = (Exception)eArgs.ExceptionObject;
      var loggedException = LoggedException.FromException(e);
      LogException(loggedException);
    }

    internal void HandleLog(string message, string stackTraceString, LogType type) {
      if (type == LogType.Exception) {
        string[] messageParts = getMessageParts(message);
        var exception = new LoggedException(messageParts[0], messageParts[1], stackTraceString);
        LogException(exception);
      }
    }

    private string[] getMessageParts(string message) {
      // Split into two parts so we only split on the first delimiter
      char[] delim = { ':' };
      string[] messageParts = message.Split(delim, 2, StringSplitOptions.None);

      if (messageParts.Length == 2) {
        string[] trimmedParts = new string[2];
        for (int i = 0; i < 2; i++) {
          trimmedParts[i] = messageParts[i].Trim();
        }
        // We want to make sure the first part of the message does not contain a
        // space because it should just be the name of an exception class
        if (!trimmedParts[0].Contains(" ")) {
          return trimmedParts;
        }
      }

      return new string[] {"Exception", message};
    }

    /// <summary>
    /// Forwards the exception to the Crashlytics platform SDK.
    /// This is virtual for testing.
    /// </summary>
    internal virtual void LogException(LoggedException e) {
      LogUtil.LogMessage (LogLevel.Debug,
                              String.Format("Crashlytics recording exception: {0}\n" +
                                            "Exception stack trace:\n" +
                                            "{1}", e.Message, e.StackTrace)
                             );
      if (Crashlytics.ReportUncaughtExceptionsAsFatal) {
        Crashlytics.LogExceptionAsFatal(e);
      } else {
        Crashlytics.LogException(e);
      }
    }
  }
}
