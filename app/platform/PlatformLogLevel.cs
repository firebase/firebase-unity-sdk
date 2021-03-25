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

/// @brief Levels used when logging messages.
internal enum PlatformLogLevel {
  /// Verbose Log Level
  Verbose = 0,
  /// Debug Log Level
  Debug,
  /// Info Log Level
  Info,
  /// Warning Log Level
  Warning,
  /// Error Log Level
  Error,
  /// Assert Log Level
  Assert
}

}  // namespace Firebase
