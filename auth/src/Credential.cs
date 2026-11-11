// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth.Internal;

namespace Firebase.Auth {
  internal enum CredentialKind {
    Invalid = 0,
    EmailPassword = 1,
    EmailLink = 2,
    OAuthAssertion = 3,
    Phone = 4,
    PendingToken = 5,
    GameCenter = 6,
  }

  /// <summary>
  /// Authentication credentials for an authentication provider.
  /// </summary>
  /// <remarks>
  /// An authentication provider is a service that allows you to authenticate
  /// a user. Firebase provides email/password authentication, but there are also
  /// external authentication providers such as Facebook.
  /// </remarks>
  public class Credential : IDisposable {
    internal CredentialKind Kind { get; private protected set; }
    internal string ProviderInternal { get; private protected set; } = string.Empty;
    internal string Email { get; private protected set; } = string.Empty;
    internal string Password { get; private protected set; } = string.Empty;
    internal string OobCode { get; private protected set; } = string.Empty;
    internal string IdToken { get; private protected set; } = string.Empty;
    internal string AccessToken { get; private protected set; } = string.Empty;
    internal string OAuthTokenSecret { get; private protected set; } = string.Empty;
    internal string ServerAuthCode { get; private protected set; } = string.Empty;
    internal string RawNonce { get; private protected set; } = string.Empty;
    internal string PendingToken { get; private protected set; } = string.Empty;
    internal string VerificationId { get; private protected set; } = string.Empty;
    internal string VerificationCode { get; private protected set; } = string.Empty;
    internal string TemporaryProof { get; private protected set; } = string.Empty;
    internal string PhoneNumber { get; private protected set; } = string.Empty;
    internal string GameCenterPlayerId { get; private protected set; } = string.Empty;
    internal string GameCenterPublicKeyUrl { get; private protected set; } = string.Empty;
    internal string GameCenterSignature { get; private protected set; } = string.Empty;
    internal string GameCenterSalt { get; private protected set; } = string.Empty;
    internal long GameCenterTimestamp { get; private protected set; }
    internal string GameCenterDisplayName { get; private protected set; } = string.Empty;
    internal string GameCenterTeamPlayerId { get; private protected set; } = string.Empty;
    internal string GameCenterGamePlayerId { get; private protected set; } = string.Empty;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes an empty (invalid) <see cref="Credential" />.
    /// </summary>
    /// <remarks>
    /// Valid credentials should be created by provider classes such as
    /// <see cref="EmailAuthProvider.GetCredential(string, string)" />,
    /// <see cref="FacebookAuthProvider.GetCredential(string)" />, and
    /// <see cref="GoogleAuthProvider.GetCredential(string, string)" />.
    /// </remarks>
    public Credential() {
      Kind = CredentialKind.Invalid;
    }

    internal static Credential CreateEmailPassword(string email, string password) =>
        email == null || password == null
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.EmailPassword,
              ProviderInternal = EmailAuthProvider.ProviderId,
              Email = email,
              Password = password,
            };

    internal static Credential CreateEmailLink(string email, string oobCode) =>
        string.IsNullOrEmpty(email) || string.IsNullOrEmpty(oobCode)
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.EmailLink,
              ProviderInternal = EmailAuthProvider.ProviderId,
              Email = email,
              OobCode = oobCode,
            };

    internal static Credential CreateIdTokenOrAccessToken(
        string providerId, string idToken, string rawNonce, string accessToken) =>
        string.IsNullOrEmpty(providerId)
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.OAuthAssertion,
              ProviderInternal = providerId,
              IdToken = idToken ?? string.Empty,
              RawNonce = rawNonce ?? string.Empty,
              AccessToken = accessToken ?? string.Empty,
            };

    internal static Credential CreateTwitter(string token, string secret) =>
        token == null || secret == null
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.OAuthAssertion,
              ProviderInternal = TwitterAuthProvider.ProviderId,
              AccessToken = token,
              OAuthTokenSecret = secret,
            };

    internal static Credential CreateAuthCode(string providerId, string serverAuthCode) =>
        string.IsNullOrEmpty(providerId) || serverAuthCode == null
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.OAuthAssertion,
              ProviderInternal = providerId,
              ServerAuthCode = serverAuthCode,
            };

    internal static Credential CreatePendingToken(string providerId, string pendingToken) =>
        string.IsNullOrEmpty(providerId) || string.IsNullOrEmpty(pendingToken)
            ? new Credential()
            : new Credential {
              Kind = CredentialKind.PendingToken,
              ProviderInternal = providerId,
              PendingToken = pendingToken,
            };

    internal static Credential CreateGameCenter(
        string playerId,
        string publicKeyUrl,
        string signatureBase64,
        string saltBase64,
        long timestamp,
        string displayName = "",
        string teamPlayerId = "",
        string gamePlayerId = "") {
      if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(publicKeyUrl) ||
          string.IsNullOrEmpty(signatureBase64) || string.IsNullOrEmpty(saltBase64)) {
        return new Credential();
      }
      return new Credential {
        Kind = CredentialKind.GameCenter,
        ProviderInternal = GameCenterAuthProvider.ProviderId,
        GameCenterPlayerId = playerId,
        GameCenterPublicKeyUrl = publicKeyUrl,
        GameCenterSignature = ToUnpaddedBase64Url(signatureBase64),
        GameCenterSalt = ToUnpaddedBase64Url(saltBase64),
        GameCenterTimestamp = timestamp,
        GameCenterDisplayName = displayName ?? string.Empty,
        GameCenterTeamPlayerId = teamPlayerId ?? string.Empty,
        GameCenterGamePlayerId = gamePlayerId ?? string.Empty,
      };
    }

    internal static string ToUnpaddedBase64Url(string base64OrUrl) =>
        string.IsNullOrEmpty(base64OrUrl)
            ? string.Empty
            : base64OrUrl.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal virtual Credential Clone() {
      if (!IsValid()) return new Credential();
      return new Credential {
        Kind = Kind,
        ProviderInternal = ProviderInternal,
        Email = Email,
        Password = Password,
        OobCode = OobCode,
        IdToken = IdToken,
        AccessToken = AccessToken,
        OAuthTokenSecret = OAuthTokenSecret,
        ServerAuthCode = ServerAuthCode,
        RawNonce = RawNonce,
        PendingToken = PendingToken,
        VerificationId = VerificationId,
        VerificationCode = VerificationCode,
        TemporaryProof = TemporaryProof,
        PhoneNumber = PhoneNumber,
        GameCenterPlayerId = GameCenterPlayerId,
        GameCenterPublicKeyUrl = GameCenterPublicKeyUrl,
        GameCenterSignature = GameCenterSignature,
        GameCenterSalt = GameCenterSalt,
        GameCenterTimestamp = GameCenterTimestamp,
        GameCenterDisplayName = GameCenterDisplayName,
        GameCenterTeamPlayerId = GameCenterTeamPlayerId,
        GameCenterGamePlayerId = GameCenterGamePlayerId,
      };
    }

    internal string BuildIdpPostBody() {
      var sb = new StringBuilder();
      sb.Append("providerId=").Append(Uri.EscapeDataString(ProviderInternal));
      if (!string.IsNullOrEmpty(IdToken)) {
        sb.Append("&id_token=").Append(Uri.EscapeDataString(IdToken));
      } else if (!string.IsNullOrEmpty(ServerAuthCode)) {
        sb.Append("&code=").Append(Uri.EscapeDataString(ServerAuthCode));
      } else if (!string.IsNullOrEmpty(AccessToken)) {
        sb.Append("&access_token=").Append(Uri.EscapeDataString(AccessToken));
        if (!string.IsNullOrEmpty(OAuthTokenSecret)) {
          sb.Append("&oauth_token_secret=").Append(Uri.EscapeDataString(OAuthTokenSecret));
        }
      }
      if (!string.IsNullOrEmpty(RawNonce)) {
        sb.Append("&nonce=").Append(Uri.EscapeDataString(RawNonce));
      }
      return sb.ToString();
    }

    /// <summary>Gets the name of the Identification Provider (IDP) for the credential.</summary>
    public string Provider => !_disposed && Kind != CredentialKind.Invalid ? ProviderInternal : string.Empty;

    /// <summary>
    /// Get whether this credential is valid. A credential can be
    /// invalid in an error condition, e.g. empty username/password.
    /// </summary>
    /// <returns>True if the credential is valid, false otherwise.</returns>
    public bool IsValid() => !_disposed && Kind != CredentialKind.Invalid;

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public virtual void Dispose(bool disposing) {
      if (disposing) _disposed = true;
    }
  }

  /// <summary>Wraps phone number and verification information for authentication purposes.</summary>
  public sealed class PhoneAuthCredential : Credential {
    private readonly string _smsCode;

    /// <summary>Initializes an empty (invalid) <see cref="PhoneAuthCredential" />.</summary>
    public PhoneAuthCredential() {
      _smsCode = string.Empty;
    }

    /// <summary>Copy constructor. Initializes a copy of an existing <see cref="PhoneAuthCredential" />.</summary>
    /// <param name="rhs">The <see cref="PhoneAuthCredential" /> to copy.</param>
    public PhoneAuthCredential(PhoneAuthCredential rhs) {
      if (rhs != null && rhs.IsValid()) {
        Kind = rhs.Kind;
        ProviderInternal = rhs.ProviderInternal;
        VerificationId = rhs.VerificationId;
        VerificationCode = rhs.VerificationCode;
        TemporaryProof = rhs.TemporaryProof;
        PhoneNumber = rhs.PhoneNumber;
        _smsCode = rhs._smsCode;
      } else {
        _smsCode = string.Empty;
      }
    }

    internal PhoneAuthCredential(string verificationId, string verificationCode, string smsCode = "") {
      if (verificationId != null && verificationCode != null) {
        Kind = CredentialKind.Phone;
        ProviderInternal = "phone";
        VerificationId = verificationId;
        VerificationCode = verificationCode;
        _smsCode = smsCode ?? string.Empty;
      } else {
        _smsCode = string.Empty;
      }
    }

    internal override Credential Clone() => IsValid() ? new PhoneAuthCredential(this) : new PhoneAuthCredential();

    /// <summary>
    /// Gets the auto-retrieved SMS verification code if applicable.
    /// </summary>
    /// <remarks>
    /// This property is supported on Android devices only. It will return empty strings on
    /// other platforms.
    ///
    /// When SMS verification is used, you will be called back first via
    /// <see cref="PhoneAuthProvider.CodeSent" />, and later
    /// <see cref="PhoneAuthProvider.VerificationCompleted" /> with a <see cref="PhoneAuthCredential" /> containing
    /// a non-empty SMS code if auto-retrieval succeeded. If Firebase used another approach
    /// to verify the phone number and triggers a callback via
    /// <see cref="PhoneAuthProvider.VerificationCompleted" />, then the SMS code can be an empty string.
    /// </remarks>
    public string SmsCode => _smsCode ?? string.Empty;

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Credential.Dispose()" />.</param>
    public override void Dispose(bool disposing) => base.Dispose(disposing);
  }

  /// <summary>
  /// Use email and password to authenticate.
  /// </summary>
  /// <remarks>
  /// Allows developers to use the email and password credentials as they could
  /// other auth providers. For example, this can be used to change passwords,
  /// log in, etc.
  /// </remarks>
  public static class EmailAuthProvider {
    /// <summary>The string used to identify this provider (<c>password</c>).</summary>
    public static string ProviderId => "password";

    /// <summary>
    /// Generate a credential from the given email and password.
    /// </summary>
    /// <param name="email">E-mail to generate the credential from.</param>
    /// <param name="password">Password to use for the new credential.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string email, string password) =>
        Credential.CreateEmailPassword(email, password);
  }

  /// <summary>Use an access token provided by Facebook to authenticate.</summary>
  public static class FacebookAuthProvider {
    /// <summary>The string used to identify this provider (<c>facebook.com</c>).</summary>
    public static string ProviderId => "facebook.com";

    /// <summary>
    /// Generate a credential from the given Facebook token.
    /// </summary>
    /// <param name="accessToken">Facebook token to generate the credential from.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string accessToken) =>
        accessToken == null
            ? new Credential()
            : Credential.CreateIdTokenOrAccessToken(ProviderId, string.Empty, string.Empty, accessToken);
  }

  /// <summary>GameCenter (Apple) auth provider.</summary>
  public static class GameCenterAuthProvider {
    /// <summary>The string used to identify this provider (<c>gc.apple.com</c>).</summary>
    public static string ProviderId => "gc.apple.com";

    /// <summary>
    /// Generate a credential from GameCenter for the current user.
    /// </summary>
    /// <returns>A Task that will be fulfilled with the resulting <see cref="Credential" />.</returns>
    public static Task<Credential> GetCredentialAsync() => MobileAuthPlatform.GetGameCenterCredentialAsync();

    /// <summary>
    /// Tests to see if the current user is signed in to GameCenter.
    /// </summary>
    /// <returns>True if the user is signed in, false otherwise.</returns>
    public static bool IsPlayerAuthenticated() => MobileAuthPlatform.IsGameCenterPlayerAuthenticated();
  }

  /// <summary>Use an access token provided by GitHub to authenticate.</summary>
  public static class GitHubAuthProvider {
    /// <summary>The string used to identify this provider (<c>github.com</c>).</summary>
    public static string ProviderId => "github.com";

    /// <summary>
    /// Generate a credential from the given GitHub token.
    /// </summary>
    /// <param name="token">The GitHub OAuth access token.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string token) =>
        token == null
            ? new Credential()
            : Credential.CreateIdTokenOrAccessToken(ProviderId, string.Empty, string.Empty, token);
  }

  /// <summary>Use an ID token and access token provided by Google to authenticate.</summary>
  public static class GoogleAuthProvider {
    /// <summary>The string used to identify this provider (<c>google.com</c>).</summary>
    public static string ProviderId => "google.com";

    /// <summary>
    /// Generate a credential from the given Google ID token and/or access token.
    /// </summary>
    /// <param name="idToken">Google Sign-In ID token.</param>
    /// <param name="accessToken">Google Sign-In access token.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string idToken, string accessToken) {
      if (!string.IsNullOrEmpty(idToken)) {
        return Credential.CreateIdTokenOrAccessToken(ProviderId, idToken, string.Empty, string.Empty);
      }
      return accessToken != null
          ? Credential.CreateIdTokenOrAccessToken(ProviderId, string.Empty, string.Empty, accessToken)
          : Credential.CreateIdTokenOrAccessToken(ProviderId, string.Empty, string.Empty, string.Empty);
    }
  }

  /// <summary>Use an access token provided by Microsoft to authenticate.</summary>
  public class MicrosoftAuthProvider : IDisposable {
    /// <summary>Initializes a new <see cref="MicrosoftAuthProvider" />.</summary>
    public MicrosoftAuthProvider() { }

    /// <summary>The string used to identify this provider (<c>microsoft.com</c>).</summary>
    public static string ProviderId => "microsoft.com";

    /// <summary>Releases resources held by this instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by this instance.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()" />.</param>
    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>OAuth2.0+UserInfo auth provider (OIDC compliant and non-compliant).</summary>
  public static class OAuthProvider {
    /// <summary>
    /// Generate a credential for an OAuth2 provider.
    /// </summary>
    /// <param name="ProviderId">Name of the OAuth2 provider.</param>
    /// <param name="idToken">The authentication token (OIDC only).</param>
    /// <param name="accessToken">The access token associated with the Auth credential to be created.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string ProviderId, string idToken, string accessToken) =>
        ProviderId == null || idToken == null || accessToken == null
            ? new Credential()
            : Credential.CreateIdTokenOrAccessToken(ProviderId, idToken, string.Empty, accessToken);

    /// <summary>
    /// Generate a credential for an OAuth2 provider.
    /// </summary>
    /// <param name="ProviderId">Name of the OAuth2 provider.</param>
    /// <param name="idToken">The authentication token (OIDC only).</param>
    /// <param name="rawNonce">The raw nonce associated with the Auth credential being created.</param>
    /// <param name="accessToken">
    /// The access token associated with the Auth credential to be created, if available. This value may be null.
    /// </param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string ProviderId, string idToken, string rawNonce, string accessToken) =>
        ProviderId == null || idToken == null || rawNonce == null
            ? new Credential()
            : Credential.CreateIdTokenOrAccessToken(ProviderId, idToken, rawNonce, accessToken ?? string.Empty);
  }

  /// <summary>Use a server auth code provided by Google Play Games to authenticate.</summary>
  public static class PlayGamesAuthProvider {
    /// <summary>The string used to identify this provider (<c>playgames.google.com</c>).</summary>
    public static string ProviderId => "playgames.google.com";

    /// <summary>
    /// Generate a credential from the given Server Auth Code.
    /// </summary>
    /// <param name="serverAuthCode">Play Games Sign in Server Auth Code.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string serverAuthCode) =>
        Credential.CreateAuthCode(ProviderId, serverAuthCode);
  }

  /// <summary>Use a token and secret provided by Twitter to authenticate.</summary>
  public static class TwitterAuthProvider {
    /// <summary>The string used to identify this provider (<c>twitter.com</c>).</summary>
    public static string ProviderId => "twitter.com";

    /// <summary>
    /// Generate a credential from the given Twitter token and secret.
    /// </summary>
    /// <param name="token">The Twitter OAuth token.</param>
    /// <param name="secret">The Twitter OAuth secret.</param>
    /// <returns>New <see cref="Credential" />.</returns>
    public static Credential GetCredential(string token, string secret) =>
        Credential.CreateTwitter(token, secret);
  }
}
