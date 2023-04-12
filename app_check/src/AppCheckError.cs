/*
 * Copyright 2023 Google LLC
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

namespace Firebase.AppCheck {

/// @brief Error codes used by App Check.
public enum AppCheckError {
  /// The operation was a success, no error occurred.
  None = 0,
  /// A network connection error.
  ServerUnreachable = 1,
  /// Invalid configuration error. Currently, an exception is thrown but this
  /// error is reserved for future implementations of invalid configuration
  /// detection.
  InvalidConfiguration = 2,
  /// System keychain access error. Ensure that the app has proper keychain
  /// access.
  SystemKeychain = 3,
  /// Selected AppCheckProvider is not supported on the current platform
  /// or OS version.
  UnsupportedProvider = 4,
  /// An unknown error occurred.
  Unknown = 5,
}

}
