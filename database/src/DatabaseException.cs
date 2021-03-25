/*
 * Copyright 2016 Google LLC
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

namespace Firebase.Database {
  /// <summary>
  ///   This error is thrown when the Firebase Database library is unable to operate on the input it
  ///   has been given.
  /// </summary>
  [Serializable]
  public sealed class DatabaseException : Exception {
    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="message">A human readable description of the error</param>
    internal DatabaseException(string message) : base(message) {
    }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="message">A human readable description of the error</param>
    /// <param name="cause">The underlying cause for this error</param>
    internal DatabaseException(string message, Exception cause) : base(message, cause) {
    }
  }
}
