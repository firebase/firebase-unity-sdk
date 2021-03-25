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

namespace Firebase.Editor {

  // Wrapper around Play Services Resolver logger to make internal logging easier
  // by prefixing the log line with a category name (i.e. "[Localization] ...")
  // and having first class functions for log level.
  internal class CategoryLogger {
    Google.Logger logger = new Google.Logger();

    private readonly string category;

    internal CategoryLogger(string category) {
      this.category = String.Format("[{0}] ", category);
    }

    // Log as debug level given a string format and arguments
    public void LogDebug(string format, params object[] args) {
      logger.Log(Format(format, args), Google.LogLevel.Debug);
    }

    // Log as warning level given a string format and arguments
    public void LogWarn(string format, params object[] args) {
      logger.Log(Format(format, args), Google.LogLevel.Warning);
    }

    // Log as error level given a string format and arguments
    public void LogError(string format, params object[] args) {
      logger.Log(Format(format, args), Google.LogLevel.Error);
    }

    // Helper function to format input and prepend category
    private string Format(string format, params object[] args) {
      return category + String.Format(format, args);
    }
  }
}
