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

using System;

internal class FirebaseLogger {
  // Determine whether Unity's stack unwinder is enabled when logging.
  private static MainThreadProperty<bool> incompatibleStackUnwindingEnabled =
      new MainThreadProperty<bool>(CurrentStackTraceLogTypeIsIncompatibleWithNativeLogs);

  // Determine whether the specified log type is incompatible with call stacks that come from the
  // Firebase C++ library callbacks.
  private static bool IsStackTraceLogTypeIncompatibleWithNativeLogs(
      UnityEngine.StackTraceLogType logType) {
    return !(logType == UnityEngine.StackTraceLogType.None ||
             logType == UnityEngine.StackTraceLogType.ScriptOnly);
  }

  // Try to determine whether the *current* log type is incompatible with call stacks that come from
  // Firebase C++ library callbacks.
  // Since GetStackTraceLogType() can only be called from the main thread, this method *must* be
  // called from the main thread.
  private static bool CurrentStackTraceLogTypeIsIncompatibleWithNativeLogs() {
    // Unity 5.6+ uses GetStackTraceLogType() to retrieve the stack trace logging mode for
    // each type of log level.
    var getStackTraceLogTypeMethod =
      typeof(UnityEngine.Application).GetMethod("GetStackTraceLogType");
    if (getStackTraceLogTypeMethod != null) {
      foreach (var logType in
               new[] {
                 UnityEngine.LogType.Log,
                 UnityEngine.LogType.Warning,
                 UnityEngine.LogType.Error,
                 UnityEngine.LogType.Assert,
                 UnityEngine.LogType.Exception
               }) {
        if (IsStackTraceLogTypeIncompatibleWithNativeLogs(
                (UnityEngine.StackTraceLogType)getStackTraceLogTypeMethod.Invoke(
                    null, new object[] { (object)logType }))) {
          return true;
        }
      }
    }
    return false;
  }

  // Determine whether it's possible to redirect C++ logs via the C# logger.
  // Unity's full stack unroller crashes when walking stacks of the Firebase C/C++ library,
  // so we disable redirection of C/C++ logs in this case.
  internal static bool CanRedirectNativeLogs {
    get {
      if (incompatibleStackUnwindingEnabled.Value) return false;
      // Unity 5.x uses the stackTraceLogType field to expose the stack trace logging mode.
      var stackTraceLogTypeField = typeof(UnityEngine.Application).GetField("stackTraceLogType");
      if (stackTraceLogTypeField != null) {
        if (IsStackTraceLogTypeIncompatibleWithNativeLogs(
                (UnityEngine.StackTraceLogType)stackTraceLogTypeField.GetValue(null))) {
          return false;
        }
      }
      return true;
    }
  }

  // Log a message via Unity's debug logger.
  internal static void LogMessage(PlatformLogLevel logLevel,
                                  string message) {
    PlatformLogLevel currentLevel = FirebaseHandler.AppUtils.GetLogLevel();
    if (logLevel < currentLevel) return;
    switch (logLevel) {
      case PlatformLogLevel.Verbose:
      case PlatformLogLevel.Debug:
      case PlatformLogLevel.Info:
        UnityEngine.Debug.Log(message);
        break;
      case PlatformLogLevel.Warning:
        UnityEngine.Debug.LogWarning(message);
        break;
      case PlatformLogLevel.Error:
        UnityEngine.Debug.LogError(message);
        break;
      case PlatformLogLevel.Assert:
        UnityEngine.Debug.LogAssertion(message);
        break;
    }
  }
}

}  // namespace Firebase
