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
using System.Text;
using Firebase.Internal;

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

    internal static bool TryParseJwt(string token, out IdTokenResult result) {
      result = new IdTokenResult {
        Token = token ?? string.Empty,
        Claims = new Dictionary<string, object>(),
        AuthTimestamp = UnixEpochUtc,
        IssuedAtTimestamp = UnixEpochUtc,
        ExpirationTimestamp = UnixEpochUtc,
        SignInProvider = string.Empty,
        SignInSecondFactor = string.Empty,
      };

      if (string.IsNullOrEmpty(token)) {
        return false;
      }

      int firstDot = token.IndexOf('.');
      if (firstDot <= 0) {
        return false;
      }
      int secondDot = token.IndexOf('.', firstDot + 1);
      if (secondDot <= firstDot + 1) {
        return false;
      }
      if (token.IndexOf('.', secondDot + 1) >= 0) {
        return false;
      }

      string payloadSegment = token.Substring(firstDot + 1, secondDot - firstDot - 1);
      if (!TryBase64UrlDecodeUtf8(payloadSegment, out string payloadJson)) {
        return false;
      }

      Dictionary<string, object> claims = JsonHelper.ParseObject(payloadJson);
      if (claims == null) {
        return false;
      }

      long authSec = Math.Max(0L, JsonHelper.GetLong(claims, "auth_time", 0L));
      long iatSec = Math.Max(0L, JsonHelper.GetLong(claims, "iat", 0L));
      long expSec = Math.Max(0L, JsonHelper.GetLong(claims, "exp", 0L));

      string signInProvider = string.Empty;
      string signInSecondFactor = string.Empty;
      if (claims.TryGetValue("firebase", out object fbObj) &&
          fbObj is IDictionary<string, object> fbMap) {
        if (fbMap.TryGetValue("sign_in_provider", out object prov) && prov is string provStr) {
          signInProvider = provStr;
        }
        if (fbMap.TryGetValue("sign_in_second_factor", out object sf) && sf is string sfStr) {
          signInSecondFactor = sfStr;
        }
      }

      result = new IdTokenResult {
        Token = token,
        Claims = claims,
        AuthTimestamp = UnixEpochUtc.AddSeconds(authSec),
        IssuedAtTimestamp = UnixEpochUtc.AddSeconds(iatSec),
        ExpirationTimestamp = UnixEpochUtc.AddSeconds(expSec),
        SignInProvider = signInProvider,
        SignInSecondFactor = signInSecondFactor,
      };
      return true;
    }

    internal static long TryExtractExpUnixSeconds(string token) {
      if (TryParseJwt(token, out IdTokenResult parsed) && parsed.ExpirationTimestamp > UnixEpochUtc) {
        return (long)(parsed.ExpirationTimestamp - UnixEpochUtc).TotalSeconds;
      }
      return 0L;
    }

    private static bool TryBase64UrlDecodeUtf8(string segment, out string decoded) {
      decoded = null;
      if (string.IsNullOrEmpty(segment)) {
        return false;
      }

      int padLen = (4 - (segment.Length % 4)) % 4;
      char[] chars = new char[segment.Length + padLen];
      for (int i = 0; i < segment.Length; i++) {
        char c = segment[i];
        chars[i] = c == '-' ? '+' : (c == '_' ? '/' : c);
      }
      for (int i = 0; i < padLen; i++) {
        chars[segment.Length + i] = '=';
      }

      try {
        byte[] bytes = Convert.FromBase64String(new string(chars));
        decoded = Encoding.UTF8.GetString(bytes);
        return true;
      } catch (FormatException) {
        return false;
      }
    }
  }
}  // namespace Firebase.Auth
