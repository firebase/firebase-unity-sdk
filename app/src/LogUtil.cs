/*
 * Copyright 2019 Google LLC
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

namespace Firebase {

#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
internal sealed class LogUtil : global::System.IDisposable {
  // Delegate which logs a message.
  internal delegate void LogMessageDelegate(LogLevel log_level, string message);

  public static void InitializeLogging() { }

  // Convert from a Firebase LogLevel to the version used by the Platform layer.
  internal static Platform.PlatformLogLevel ConvertLogLevel(LogLevel logLevel) => logLevel switch {
    LogLevel.Verbose => Platform.PlatformLogLevel.Verbose,
    LogLevel.Info => Platform.PlatformLogLevel.Info,
    LogLevel.Warning => Platform.PlatformLogLevel.Warning,
    LogLevel.Error => Platform.PlatformLogLevel.Error,
    LogLevel.Assert => Platform.PlatformLogLevel.Assert,
    _ => Platform.PlatformLogLevel.Debug,
  };

  // Logs a message, based on the platform.
  internal static void LogMessage(LogLevel logLevel, string message) {
    if (logLevel < FirebaseApp.LogLevel) return;
    try {
      Platform.FirebaseLogger.LogMessage(ConvertLogLevel(logLevel), message);
    } catch (Exception) {
      // Headless .NET test environment without UnityEngine.Debug ECall bound.
    }
  }

  public void Dispose() => GC.SuppressFinalize(this);
}

}  // namespace Firebase
