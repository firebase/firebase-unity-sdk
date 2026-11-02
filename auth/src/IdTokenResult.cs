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

using System;
using System.Collections.Generic;

namespace Firebase.Auth {

/// @brief Result of a @ref FirebaseUser.GetIdTokenResultAsync call, containing
/// the ID token string, UTC timestamps, sign-in metadata, and decoded claims.
public struct IdTokenResult {
  private static readonly DateTime UnixEpochUtc =
      new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  /// The Firebase Auth ID token JSON Web Token (JWT) string.
  public string Token { get; internal set; }

  /// The entire payload claims of the ID token, including standard reserved
  /// claims and custom claims.
  public IDictionary<string, object> Claims { get; internal set; }

  /// The UTC timestamp at which the user most recently authenticated.
  public DateTime AuthTimestamp { get; internal set; }

  /// The UTC timestamp at which this ID token was issued by the server.
  public DateTime IssuedAtTimestamp { get; internal set; }

  /// The UTC timestamp at which this ID token expires.
  public DateTime ExpirationTimestamp { get; internal set; }

  /// The sign-in provider through which the ID token was obtained (for example,
  /// "anonymous", "password", or "custom"), or an empty string if unavailable.
  public string SignInProvider { get; internal set; }

  /// The second factor identifier used to sign in (for example, "phone"), or
  /// an empty string if multi-factor authentication was not used.
  public string SignInSecondFactor { get; internal set; }

  // Construct an IdTokenResult from the SWIG-generated internal wrapper.
  internal static IdTokenResult FromIdTokenResultInternal(
      IdTokenResultInternal resultInternal) {
    IDictionary<string, object> convertedClaims;
    using (VariantVariantMap claimsMap = resultInternal.claims) {
      convertedClaims = claimsMap != null
          ? claimsMap.ToStringVariantMap(VariantExtension.KeyOptions.UseStringKeys)
          : new Dictionary<string, object>();
      GC.KeepAlive(claimsMap);
    }

    return new IdTokenResult {
      Token = resultInternal.token,
      Claims = convertedClaims,
      AuthTimestamp = UnixEpochUtc.AddSeconds(resultInternal.auth_timestamp),
      IssuedAtTimestamp =
          UnixEpochUtc.AddSeconds(resultInternal.issued_at_timestamp),
      ExpirationTimestamp =
          UnixEpochUtc.AddSeconds(resultInternal.expiration_timestamp),
      SignInProvider = resultInternal.sign_in_provider,
      SignInSecondFactor = resultInternal.sign_in_second_factor,
    };
  }
}

}  // namespace Firebase.Auth
