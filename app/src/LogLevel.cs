/*
 * Copyright 2026 Google LLC
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

namespace Firebase {

/// <summary>
/// Levels used when logging messages.
/// </summary>
public enum LogLevel {
  /// <summary>Verbose Log Level.</summary>
  Verbose = 0,
  /// <summary>Debug Log Level.</summary>
  Debug = 1,
  /// <summary>Info Log Level.</summary>
  Info = 2,
  /// <summary>Warning Log Level.</summary>
  Warning = 3,
  /// <summary>Error Log Level.</summary>
  Error = 4,
  /// <summary>Assert Log Level.</summary>
  Assert = 5,
}

}  // namespace Firebase
