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
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth.Internal;

namespace Firebase.Auth {
  /// @brief Firebase user account object.
  ///
  /// This class allows you to manipulate the profile of a user, link to and
  /// unlink from authentication providers, and refresh authentication tokens.
  public sealed class FirebaseUser : UserInfoInterface {
    private readonly FirebaseAuth _auth;
    private readonly AuthUserRecord _record;

    internal FirebaseUser(FirebaseAuth auth, AuthUserRecord record) {
      _auth = auth ?? throw new ArgumentNullException(nameof(auth));
      _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    internal override string GetDisplayNameCore() => _record.Snapshot.DisplayName;
    internal override string GetEmailCore() => _record.Snapshot.Email;
    internal override string GetPhoneNumberCore() => _record.Snapshot.PhoneNumber;
    internal override Uri GetPhotoUrlCore() => _record.Snapshot.ParsedPhotoUrl;
    internal override string GetProviderIdCore() => _record.Snapshot.ProviderId;
    internal override string GetUserIdCore() => _record.Snapshot.Uid;

    /// Gets the display name associated with the user, if any.
    public new string DisplayName => _record.Snapshot.DisplayName;

    /// Gets email associated with the user, if any.
    public new string Email => _record.Snapshot.Email;

    /// Returns true if user signed in anonymously.
    public bool IsAnonymous => _record.Snapshot.IsAnonymous;

    /// Returns true if the email address associated with this user has been
    /// verified.
    public bool IsEmailVerified => _record.Snapshot.IsEmailVerified;

    /// Gets the metadata for this user account.
    public UserMetadata Metadata => _record.Snapshot.CachedMetadata;

    /// Gets the phone number for the user, in E.164 format.
    public new string PhoneNumber => _record.Snapshot.PhoneNumber;

    /// The photo url associated with the user, if any.
    public new Uri PhotoUrl => _record.Snapshot.ParsedPhotoUrl;

    /// Gets the third party profile data associated with this user returned by
    /// the authentication server, if any.
    public IEnumerable<IUserInfo> ProviderData => _record.Snapshot.CachedProviderData;

    /// Gets the provider ID for the user (For example, "Facebook").
    public new string ProviderId => _record.Snapshot.ProviderId;

    /// Gets the unique Firebase user ID for the user.
    ///
    /// @note The user's ID, unique to the Firebase project.
    /// Do NOT use this value to authenticate with your backend server, if you
    /// have one.
    /// Use FirebaseUser.TokenAsync instead.
    public new string UserId => _record.Snapshot.Uid;

    /// Returns whether this FirebaseUser object represents a valid user. Could be
    /// false on FirebaseUsers contained with AuthResult structures from failed Auth
    /// operations.
    public bool IsValid() =>
        _auth != null && !_auth.IsDisposed && ReferenceEquals(_auth.ActiveRecord, _record);

    /// The Java Web Token (JWT) that can be used to identify the user to
    /// the backend.
    ///
    /// If a current ID token is still believed to be valid (i.e. it has not yet
    /// expired), that token will be returned immediately.
    /// A developer may set the optional force_refresh flag to get a new ID token,
    /// whether or not the existing token has expired. For example, a developer
    /// may use this when they have discovered that the token is invalid for some
    /// other reason.
    public Task<string> TokenAsync(bool forceRefresh) {
      if (!IsValid()) {
        return Task.FromException<string>(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      AuthUserSnapshot snap = _record.Snapshot;
      if (!forceRefresh &&
          snap.CachedTokenTask != null &&
          snap.ExpiresAtUnixSeconds > FirebaseAuth.NowUnixSeconds() + 300L) {
        return snap.CachedTokenTask;
      }
      return _auth.GetOrRefreshTokenAsync(_record, forceRefresh);
    }

    /// Retrieves the Firebase authentication token result, including decoded
    /// claims and timestamps.
    ///
    /// If a current ID token is still believed to be valid (i.e. it has not yet
    /// expired), that token will be returned immediately. A developer may set the
    /// `forceRefresh` flag to get a new ID token, whether or not the existing
    /// token has expired.
    ///
    /// @param forceRefresh Force refreshes the token if true.
    /// @returns A task that completes with the @ref IdTokenResult for the current
    /// user.
    public async Task<IdTokenResult> GetIdTokenResultAsync(bool forceRefresh) {
      string token = await TokenAsync(forceRefresh).ConfigureAwait(false);
      if (!IdTokenResult.TryParseJwt(token, out IdTokenResult parsed)) {
        throw AuthErrorMapper.CreateException(AuthError.InvalidUserToken);
      }
      return parsed;
    }

    /// Associates a user account from a third-party identity provider.
    public Task<AuthResult> LinkWithCredentialAsync(Credential credential) {
      if (!IsValid()) {
        return Task.FromException<AuthResult>(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.LinkUserWithCredentialAsync(_record, credential);
    }

    /// Link a user via a federated auth provider.
    ///
    /// @note: This operation is supported only on Android platforms (when the
    /// native Firebase Auth SDK is linked and not using the emulator). On other
    /// platforms this method will fail with AuthError.Unimplemented.
    public async Task<AuthResult> LinkWithProviderAsync(FederatedAuthProvider provider) {
      _auth.EnsureActiveRecord(_record);
      Credential credential = await MobileAuthPlatform.TryInteractiveFederatedAuthAsync(_auth, provider)
          .ConfigureAwait(false);
      return await _auth.LinkUserWithCredentialAsync(_record, credential).ConfigureAwait(false);
    }

    /// Reauthenticate using a credential.
    ///
    /// Data from the Identity Provider used to sign-in is returned in the
    /// AdditionalUserInfo inside the returned @ref AuthResult.
    ///
    /// Returns an error if the existing credential is not for this user
    /// or if sign-in with that credential failed.
    ///
    /// @note: The current user may be signed out if this operation fails on
    /// Android and desktop platforms.
    public Task<AuthResult> ReauthenticateAndRetrieveDataAsync(Credential credential) {
      if (!IsValid()) {
        return Task.FromException<AuthResult>(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.ReauthenticateUserAsync(_record, credential);
    }

    /// Convenience function for ReauthenticateAndRetrieveData that discards
    /// the returned AdditionalUserInfo data.
    public Task ReauthenticateAsync(Credential credential) =>
        ReauthenticateAndRetrieveDataAsync(credential);

    /// Reauthenticate a user via a federated auth provider.
    ///
    /// @note: This operation is supported only on Android platforms (when the
    /// native Firebase Auth SDK is linked and not using the emulator). On other
    /// platforms this method will fail with AuthError.Unimplemented.
    public async Task<AuthResult> ReauthenticateWithProviderAsync(FederatedAuthProvider provider) {
      _auth.EnsureActiveRecord(_record);
      Credential credential = await MobileAuthPlatform.TryInteractiveFederatedAuthAsync(_auth, provider)
          .ConfigureAwait(false);
      return await _auth.ReauthenticateUserAsync(_record, credential).ConfigureAwait(false);
    }

    /// Unlinks the current user from the provider specified.
    /// Status will be an error if the user is not linked to the given provider.
    public Task<AuthResult> UnlinkAsync(string provider) {
      if (!IsValid()) {
        return Task.FromException<AuthResult>(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.UnlinkUserProviderAsync(_record, provider);
    }

    /// Updates the currently linked phone number on the user.
    /// This is useful when a user wants to change their phone number. It is a
    /// shortcut to calling `UnlinkAsync(phoneCredential.Provider)`
    /// and then `LinkWithCredentialAsync(phoneCredential)`.
    /// `phoneCredential` must have been created with @ref PhoneAuthProvider.
    public Task<FirebaseUser> UpdatePhoneNumberCredentialAsync(PhoneAuthCredential credential) {
      if (!IsValid()) {
        return Task.FromException<FirebaseUser>(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.UpdateUserPhoneCredentialAsync(_record, credential);
    }

    /// Attempts to change the password for the current user.
    ///
    /// For an account linked to an Identity Provider (IDP) with no password,
    /// this will result in the account becoming an email/password-based account
    /// while maintaining the IDP link. May fail if the password is invalid,
    /// if there is a conflicting email/password-based account, or if the token
    /// has expired.
    /// To retrieve fresh tokens, call ReauthenticateAsync.
    public Task UpdatePasswordAsync(string password) {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.UpdateUserPasswordInternalAsync(_record, password);
    }

    /// Updates a subset of user profile information.
    public Task UpdateUserProfileAsync(UserProfile profile) {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.UpdateUserProfileInternalAsync(_record, profile);
    }

    /// Initiates email verification for the user.
    public Task SendEmailVerificationAsync() {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.SendEmailVerificationInternalAsync(_record);
    }

    /// Send an email to verify the ownership of the account, then update
    /// to the new email.
    public Task SendEmailVerificationBeforeUpdatingEmailAsync(string email) {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.SendVerifyBeforeUpdateEmailInternalAsync(_record, email);
    }

    /// Refreshes the data for this user.
    ///
    /// For example, the attached providers, email address, display name, etc.
    public Task ReloadAsync() {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.ReloadUserInternalAsync(_record);
    }

    /// Deletes the user account.
    public Task DeleteAsync() {
      if (!IsValid()) {
        return Task.FromException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
      }
      return _auth.DeleteUserInternalAsync(_record);
    }
  }
}  // namespace Firebase.Auth
