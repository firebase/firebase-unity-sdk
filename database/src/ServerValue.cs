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

using System.Collections.Generic;

namespace Firebase.Database {
  /// <summary>
  ///   Contains placeholder values to use when writing data to the Firebase Database.
  /// </summary>
  public static class ServerValue {
    private const string NameSubkeyServervalue = ".sv";

    /// <summary>
    ///   A placeholder value for auto-populating the current timestamp (time since the Unix epoch,
    ///   in milliseconds) by the Firebase Database servers.
    /// </summary>
    public static readonly object Timestamp =
      CreateServerValuePlaceholder("timestamp");

    // Server values
    private static IDictionary<string, object> CreateServerValuePlaceholder(string key) {
      // The Firebase server defines a ServerValue for Timestamp as a map with the
      // key ".sv" and the value "timestamp".
      IDictionary<string, object> result = new Dictionary<string, object>();
      result[NameSubkeyServervalue] = key;
      return result;
    }
  }
}
