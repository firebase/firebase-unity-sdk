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

using System;

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

  /// @brief Token used by the Firebase App Check service.
  ///
  /// Struct to hold tokens emitted by the Firebase App Check service which are
  /// minted upon a successful application verification. These tokens are the
  /// federated output of a verification flow, the structure of which is
  /// independent of the mechanism by which the application was verified.
  public struct AppCheckToken {
    internal static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// A Firebase App Check token.
    public string Token { get; set; }

    /// The time at which the token will expire.
    public DateTime ExpireTime { get; set; }

    internal long ExpireTimeMs => (long)(ExpireTime.ToUniversalTime() - UnixEpochUtc).TotalMilliseconds;
    internal static AppCheckToken FromUnixTimeMilliseconds(string token, long expireTimeMs) =>
        new AppCheckToken { Token = token ?? string.Empty, ExpireTime = UnixEpochUtc.AddMilliseconds(expireTimeMs) };
  }

  /// @brief Passed to the FirebaseAppCheck.TokenChanged event.
  ///
  /// The AppCheck Token is passed via these arguments to the
  /// FirebaseAppCheck.TokenChanged event.
  public sealed class TokenChangedEventArgs : EventArgs {
    /// A Firebase App Check token.
    public AppCheckToken Token { get; set; }
  }
}
