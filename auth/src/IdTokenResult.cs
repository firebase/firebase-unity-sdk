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
using System.Collections.ObjectModel;

namespace Firebase.Auth {

/// <summary>
/// Result of a <see cref="FirebaseUser.GetIdTokenResultAsync"/> call, containing
/// the ID token string, UTC timestamps, sign-in metadata, and decoded claims.
/// </summary>
public struct IdTokenResult {
  internal static readonly DateTime UnixEpochUtc =
      new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  private static readonly ulong MaxUnixSeconds =
      (ulong)((DateTime.MaxValue.Ticks - UnixEpochUtc.Ticks) / TimeSpan.TicksPerSecond);

  private static readonly IDictionary<string, object> EmptyClaims =
      new ReadOnlyDictionary<string, object>(new Dictionary<string, object>());

  private string token;
  private IDictionary<string, object> claims;
  private DateTime authTimestamp;
  private DateTime issuedAtTimestamp;
  private DateTime expirationTimestamp;
  private string signInProvider;
  private string signInSecondFactor;

  /// <summary>The Firebase Auth ID token JSON Web Token (JWT) string.</summary>
  public string Token {
    get { return token ?? string.Empty; }
    internal set { token = value; }
  }

  /// <summary>
  /// The entire payload claims of the ID token, including standard reserved
  /// claims and custom claims. Nested JSON objects are represented as
  /// <see cref="IDictionary{TKey, TValue}"/> with <see cref="string"/> keys.
  /// </summary>
  public IDictionary<string, object> Claims {
    get { return claims ?? EmptyClaims; }
    internal set { claims = value; }
  }

  /// <summary>
  /// The UTC time (<see cref="DateTimeKind.Utc"/>) at which the user most
  /// recently authenticated.
  /// </summary>
  public DateTime AuthTimestamp {
    get {
      return authTimestamp.Ticks == 0 && authTimestamp.Kind == DateTimeKind.Unspecified
          ? UnixEpochUtc
          : authTimestamp;
    }
    internal set { authTimestamp = value; }
  }

  /// <summary>
  /// The UTC time (<see cref="DateTimeKind.Utc"/>) at which this ID token was
  /// issued by the server.
  /// </summary>
  public DateTime IssuedAtTimestamp {
    get {
      return issuedAtTimestamp.Ticks == 0 && issuedAtTimestamp.Kind == DateTimeKind.Unspecified
          ? UnixEpochUtc
          : issuedAtTimestamp;
    }
    internal set { issuedAtTimestamp = value; }
  }

  /// <summary>
  /// The UTC time (<see cref="DateTimeKind.Utc"/>) at which this ID token
  /// expires.
  /// </summary>
  public DateTime ExpirationTimestamp {
    get {
      return expirationTimestamp.Ticks == 0 && expirationTimestamp.Kind == DateTimeKind.Unspecified
          ? UnixEpochUtc
          : expirationTimestamp;
    }
    internal set { expirationTimestamp = value; }
  }

  /// <summary>
  /// The sign-in provider through which the ID token was obtained (for example,
  /// <c>"anonymous"</c>, <c>"password"</c>, or <c>"custom"</c>), or
  /// <see cref="string.Empty"/> if unavailable.
  /// </summary>
  public string SignInProvider {
    get { return signInProvider ?? string.Empty; }
    internal set { signInProvider = value; }
  }

  /// <summary>
  /// The second factor identifier used to sign in (for example,
  /// <c>"phone"</c>), or <see cref="string.Empty"/> if multi-factor
  /// authentication was not used.
  /// </summary>
  public string SignInSecondFactor {
    get { return signInSecondFactor ?? string.Empty; }
    internal set { signInSecondFactor = value; }
  }

  private static DateTime UnixSecondsToUtcDateTime(ulong seconds) {
    if (seconds > MaxUnixSeconds) {
      return UnixEpochUtc;
    }
    return UnixEpochUtc.AddTicks((long)seconds * TimeSpan.TicksPerSecond);
  }

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

    IDictionary<string, object> convertedClaims;
    using (VariantVariantMap claimsMap = resultInternal.claims) {
      convertedClaims = claimsMap != null
          ? claimsMap.ToStringVariantMap(VariantExtension.KeyOptions.UseStringKeys)
          : new Dictionary<string, object>();
    }

    IdTokenResult result = new IdTokenResult {
      Token = resultInternal.token ?? string.Empty,
      Claims = convertedClaims,
      AuthTimestamp = UnixSecondsToUtcDateTime(resultInternal.auth_timestamp),
      IssuedAtTimestamp =
          UnixSecondsToUtcDateTime(resultInternal.issued_at_timestamp),
      ExpirationTimestamp =
          UnixSecondsToUtcDateTime(resultInternal.expiration_timestamp),
      SignInProvider = resultInternal.sign_in_provider ?? string.Empty,
      SignInSecondFactor =
          resultInternal.sign_in_second_factor ?? string.Empty,
    };
    GC.KeepAlive(resultInternal);
    return result;
  }
}

}  // namespace Firebase.Auth
