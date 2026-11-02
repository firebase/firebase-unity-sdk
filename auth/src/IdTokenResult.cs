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

/// <summary>
/// Result of a <see cref="FirebaseUser.GetIdTokenResultAsync"/> call, containing
/// the token string, timestamps, and claims.
/// </summary>
public struct IdTokenResult {
  /// <summary>The JSON Web Token (JWT) string.</summary>
  public string Token { get; internal set; }

  /// <summary>
  /// Claims map decoded from the token payload, including custom and standard
  /// claims.
  /// </summary>
  public IDictionary<string, object> Claims { get; internal set; }

  /// <summary>Authentication timestamp.</summary>
  public DateTime AuthTimestamp { get; internal set; }

  /// <summary>Issued-at timestamp.</summary>
  public DateTime IssuedAtTimestamp { get; internal set; }

  /// <summary>Expiration timestamp.</summary>
  public DateTime ExpirationTimestamp { get; internal set; }

  /// <summary>The sign-in provider through which the token was obtained.</summary>
  public string SignInProvider { get; internal set; }

  /// <summary>The sign-in second factor through which the token was obtained.</summary>
  public string SignInSecondFactor { get; internal set; }

  internal static readonly DateTime UnixEpochUtc =
      new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  // Construct an IdTokenResult from the SWIG-generated internal wrapper.
  internal static IdTokenResult FromIdTokenResultInternal(
      IdTokenResultInternal resultInternal) {
    if (resultInternal == null) {
      return new IdTokenResult {
        Token = string.Empty,
        Claims = new Dictionary<string, object>(),
        AuthTimestamp = UnixEpochUtc,
        IssuedAtTimestamp = UnixEpochUtc,
        ExpirationTimestamp = UnixEpochUtc,
        SignInProvider = string.Empty,
        SignInSecondFactor = string.Empty,
      };
    }

    IDictionary<string, object> claims;
    using (VariantVariantMap claimsMap = resultInternal.ClaimsInternal) {
      claims = claimsMap != null
          ? claimsMap.ToStringVariantMap(VariantExtension.KeyOptions.UseStringKeys)
          : new Dictionary<string, object>();
    }

    IdTokenResult result = new IdTokenResult {
      Token = resultInternal.token ?? string.Empty,
      Claims = claims,
      AuthTimestamp =
          UnixEpochUtc.AddSeconds((double)resultInternal.auth_timestamp),
      IssuedAtTimestamp =
          UnixEpochUtc.AddSeconds((double)resultInternal.issued_at_timestamp),
      ExpirationTimestamp =
          UnixEpochUtc.AddSeconds((double)resultInternal.expiration_timestamp),
      SignInProvider = resultInternal.sign_in_provider ?? string.Empty,
      SignInSecondFactor = resultInternal.sign_in_second_factor ?? string.Empty,
    };
    GC.KeepAlive(resultInternal);
    return result;
  }
}

}  // namespace Firebase.Auth
