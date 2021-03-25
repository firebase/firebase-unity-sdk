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
  using System.Collections.Generic;

  /// <summary>
  /// This class wraps every exception that is logged to Crashlytics, ensuring
  /// we have a consistent policy for handling empty stacktraces, and providing
  /// methods for parsing the stacktrace into a format that can be passed to the
  /// platform SDKs
  /// </summary>
  internal class LoggedException : Exception {

    public LoggedException(string name, string message, string stackTrace) : base(message) {
      Name = name;
      if (stackTrace == null) {
        CustomStackTrace = "";
      } else {
        CustomStackTrace = stackTrace;
      }
      ParsedStackTrace = StackTraceParser.ParseStackTraceString(CustomStackTrace);
    }

    public static LoggedException FromException(Exception exception) {
      if (exception is LoggedException) {
        return ((LoggedException) exception);
      }

      var name = exception.GetType().Name;
      var message = exception.Message;
      var customStackTrace = exception.StackTrace;
      return new LoggedException(name, message, customStackTrace);
    }

    public string Name { get; private set; }

    public string CustomStackTrace { get; private set; }

    public Dictionary<string, string>[] ParsedStackTrace { get; private set; }
  }
}
