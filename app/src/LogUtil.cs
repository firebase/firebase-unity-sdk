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

// A utility class to help manage C# logging delegates.  LogUtil's static
// constructor creates a private singleton instance of LogUtil, which is an
// IDisposable that registers the proper C# delegates on creation, and
// unregisters them when it is disposed, to ensure that C++ doesn't try to call
// into a C# logging callback once the C# environment have been torn down.
//
// Classes that need access to logging should call LogUtil.InitializeLogging()
// both to ensure that LogUtil's static constructor is invoked, and perform any
// necessary logging initialization.
internal sealed class LogUtil : global::System.IDisposable {
  // Delegate which logs a message.
  internal delegate void LogMessageDelegate(Firebase.LogLevel log_level,
                                            string message);

  private static LogUtil _instance = null;

  static LogUtil() {
    _instance = new LogUtil();
  }

  // Implicitly calls the static constructor of LogUtil if it hasn't already
  // been called, which handles initialization.
  private static object InitializeLoggingLock = new object();
  public static void InitializeLogging() {
    lock(InitializeLoggingLock) {
      AppUtil.AppEnableLogCallback(true);
    }
  }

  // Convert from a Firebase LogLevel to the version used by the Platform layer.
  internal static Firebase.Platform.PlatformLogLevel ConvertLogLevel(
      Firebase.LogLevel logLevel) {
    switch (logLevel) {
      case Firebase.LogLevel.Verbose:
        return Firebase.Platform.PlatformLogLevel.Verbose;
      case Firebase.LogLevel.Info:
        return Firebase.Platform.PlatformLogLevel.Info;
      case Firebase.LogLevel.Warning:
        return Firebase.Platform.PlatformLogLevel.Warning;
      case Firebase.LogLevel.Error:
        return Firebase.Platform.PlatformLogLevel.Error;
      case Firebase.LogLevel.Debug:
      default:
        return Firebase.Platform.PlatformLogLevel.Debug;
    }
  }

  // Logs a message, based on the platform.
  internal static void LogMessage(Firebase.LogLevel logLevel, string message) {
    Firebase.Platform.FirebaseLogger.LogMessage(ConvertLogLevel(logLevel), message);
  }

  private  bool _disposed = false;

  // Log message via a callback from C/C++.
  [MonoPInvokeCallback(typeof(LogUtil.LogMessageDelegate))]
  internal static void LogMessageFromCallback(Firebase.LogLevel logLevel, string message) {
    if (Firebase.Platform.FirebaseLogger.CanRedirectNativeLogs) LogMessage(logLevel, message);
  }

  public LogUtil() {
    AppUtil.SetLogFunction(LogUtil.LogMessageFromCallback);
    AppDomain.CurrentDomain.DomainUnload += (object sender, EventArgs e) => {
      Dispose(false);
    };
  }

  ~LogUtil() {
    Dispose(false);
  }

  public void Dispose() {
    Dispose(true);
    System.GC.SuppressFinalize(this);
  }

  protected void Dispose(bool disposing) {
    if(!_disposed) {
      AppUtil.SetLogFunction(null);

      _disposed = true;
    }
  }
}

}  // namespace Firebase
