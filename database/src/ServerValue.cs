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
using System.Collections.Generic;

namespace Firebase.Database {
  /// <summary>
  ///   Contains placeholder values to use when writing data to the Firebase Database.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public static class ServerValue {
    private const string NameSubkeyServervalue = ".sv";
    private const string NameSubkeyIncrement = "increment";

    /// <summary>
    ///   A placeholder value for auto-populating the current timestamp (time since the Unix epoch,
    ///   in milliseconds) by the Firebase Database servers.
    /// </summary>
    public static readonly object Timestamp = CreateServerValuePlaceholder("timestamp");

    /// <summary>
    ///   Returns a placeholder value that can be used to atomically increment the current database
    ///   value by the provided 64-bit integer delta.
    /// </summary>
    /// <param name="delta">The amount to modify the current value atomically.</param>
    /// <returns>A placeholder value for modifying data atomically server-side.</returns>
    public static object Increment(long delta) {
      return new Dictionary<string, object>(1) {
        [NameSubkeyServervalue] = new Dictionary<string, object>(1) {
          [NameSubkeyIncrement] = delta,
        },
      };
    }

    /// <summary>
    ///   Returns a placeholder value that can be used to atomically increment the current database
    ///   value by the provided double-precision floating-point delta.
    /// </summary>
    /// <param name="delta">The amount to modify the current value atomically.</param>
    /// <returns>A placeholder value for modifying data atomically server-side.</returns>
    public static object Increment(double delta) {
      if (double.IsNaN(delta) || double.IsInfinity(delta)) {
        throw new ArgumentException("ServerValue.Increment delta must be a finite number.", nameof(delta));
      }
      return new Dictionary<string, object>(1) {
        [NameSubkeyServervalue] = new Dictionary<string, object>(1) {
          [NameSubkeyIncrement] = delta,
        },
      };
    }

    // Server values
    private static IDictionary<string, object> CreateServerValuePlaceholder(string key) {
      // The Firebase server defines a ServerValue for Timestamp as a map with the
      // key ".sv" and the value "timestamp".
      return new Dictionary<string, object>(1) {
        [NameSubkeyServervalue] = key,
      };
    }
  }
}
