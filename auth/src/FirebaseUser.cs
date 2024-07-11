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

 using System.Threading.Tasks;

namespace Firebase.Auth {

/// @brief Firebase user account object.
///
/// This class allows you to manipulate the profile of a user, link to and
/// unlink from authentication providers, and refresh authentication tokens.
public sealed class FirebaseUser : UserInfoInterface {
  private FirebaseAuth authProxy;

  internal FirebaseUser(FirebaseAuth auth) {
    authProxy = auth;
  }

  private FirebaseUserInternal GetValidFirebaseUserInternal() {
    if (authProxy == null) {
      throw new System.NullReferenceException();
    } else {
      FirebaseUserInternal userInternal = authProxy.CurrentUserInternal;
      if (userInternal == null) {
        throw new System.NullReferenceException();
      } else {
        return userInternal;
      }
    }
  }

  private void CompleteAuthResult(AuthResult authResult) {
    if (authResult != null) {
      // Cache the authProxy in the AuthResult
      authResult.authProxy = authProxy;
    }
  }

  /// Reauthenticate a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS, tvOS and Android
  /// platforms. On other platforms this method will return a Future with a
  /// preset error code: kAuthErrorUnimplemented.
  public async Task<AuthResult> ReauthenticateWithProviderAsync(FederatedAuthProvider provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.ReauthenticateWithProviderInternalAsync(provider);
    CompleteAuthResult(result);
    return result;
  }

  /// Link a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS, tvOS and Android
  /// platforms. On other platforms this method will return a Future with a
  /// preset error code: kAuthErrorUnimplemented.
  public async Task<AuthResult> LinkWithProviderAsync(FederatedAuthProvider provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.LinkWithProviderInternalAsync(provider);
    CompleteAuthResult(result);
    return result;
  }

  /// Associates a user account from a third-party identity provider.
  public async Task<AuthResult> LinkWithCredentialAsync(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.LinkWithCredentialInternalAsync(credential);
    CompleteAuthResult(result);
    return result;
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
  public async Task<AuthResult> ReauthenticateAndRetrieveDataAsync(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.ReauthenticateAndRetrieveDataInternalAsync(credential);
    CompleteAuthResult(result);
    return result;
  }

  /// Unlinks the current user from the provider specified.
  /// Status will be an error if the user is not linked to the given provider.
  public async Task<AuthResult> UnlinkAsync(string provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.UnlinkInternalAsync(provider);
    CompleteAuthResult(result);
    return result;
  }

  /// Updates the currently linked phone number on the user.
  /// This is useful when a user wants to change their phone number. It is a
  /// shortcut to calling `UnlinkAsync(phoneCredential.Provider)`
  /// and then `LinkWithCredentialAsync(phoneCredential)`.
  /// `phoneCredential` must have been created with @ref PhoneAuthProvider.
  public async Task<FirebaseUser> UpdatePhoneNumberCredentialAsync(PhoneAuthCredential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    // We don't care about the returned user, since there is currently only meant to
    // be a single FirebaseUser under the hood.
    await userInternal.UpdatePhoneNumberCredentialInternalAsync(credential);
    return this;
  }

  /// Returns whether this FirebaseUser object represents a valid user. Could be
  /// false on FirebaseUsers contained with AuthResult structures from failed Auth
  /// operations.
  public bool IsValid() {
    if (authProxy != null) {
      FirebaseUserInternal userInternal = authProxy.CurrentUserInternal;
      if (userInternal != null) {
        return userInternal.IsValid();
      }
    }
    return false;
  }

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
    return GetValidFirebaseUserInternal().TokenAsync(forceRefresh);
  }

  /// @deprecated This method is deprecated. Please use
  /// @ref SendEmailVerificationBeforeUpdatingEmailAsync(string) instead.
  /// 
  /// Sets the email address for the user.
  ///
  /// May fail if there is already an email/password-based account for the same
  /// email address.
  [System.Obsolete("Please use `Task SendEmailVerificationBeforeUpdatingEmailAsync(string)` instead", false)]
  public Task UpdateEmailAsync(string email) {
    return GetValidFirebaseUserInternal().UpdateEmailAsync(email);
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
    return GetValidFirebaseUserInternal().UpdatePasswordAsync(password);
  }

  /// Convenience function for ReauthenticateAndRetrieveData that discards
  /// the returned AdditionalUserInfo data.
  public Task ReauthenticateAsync(Credential credential) {
    return GetValidFirebaseUserInternal().ReauthenticateAsync(credential);
  }

  /// Initiates email verification for the user.
  public Task SendEmailVerificationAsync() {
    return GetValidFirebaseUserInternal().SendEmailVerificationAsync();
  }

  /// Send an email to verify the ownership of the account, then update
  /// to the new email.
  public Task SendEmailVerificationBeforeUpdatingEmailAsync(string email) {
    return GetValidFirebaseUserInternal().SendEmailVerificationBeforeUpdatingEmailAsync(email);
  }

  /// Updates a subset of user profile information.
  public Task UpdateUserProfileAsync(UserProfile profile) {
    return GetValidFirebaseUserInternal().UpdateUserProfileAsync(profile);
  }

  /// Refreshes the data for this user.
  ///
  /// For example, the attached providers, email address, display name, etc.
  public System.Threading.Tasks.Task ReloadAsync() {
    return GetValidFirebaseUserInternal().ReloadAsync();
  }

  /// Deletes the user account.
  public System.Threading.Tasks.Task DeleteAsync() {
    return GetValidFirebaseUserInternal().DeleteAsync();
  }

  /// Gets the display name associated with the user, if any.
  public string DisplayName {
    get {
      return GetValidFirebaseUserInternal().DisplayName;
    } 
  }

  /// Gets email associated with the user, if any.
  public string Email {
    get {
      return GetValidFirebaseUserInternal().Email;
    } 
  }

  /// Returns true if user signed in anonymously.
  public bool IsAnonymous {
    get {
      return GetValidFirebaseUserInternal().IsAnonymous;
    } 
  }

  /// Returns true if the email address associated with this user has been
  /// verified.
  public bool IsEmailVerified {
    get {
      return GetValidFirebaseUserInternal().IsEmailVerified;
    } 
  }

  /// Gets the metadata for this user account.
  public UserMetadata Metadata {
    get {
      return GetValidFirebaseUserInternal().Metadata;
    } 
  }

  /// Gets the phone number for the user, in E.164 format.
  public string PhoneNumber {
    get {
      return GetValidFirebaseUserInternal().PhoneNumber;
    } 
  }

  /// The photo url associated with the user, if any.
  public System.Uri PhotoUrl {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(GetValidFirebaseUserInternal().PhotoUrlInternal);
    }
  }

  /// Gets the third party profile data associated with this user returned by
  /// the authentication server, if any.
  public System.Collections.Generic.IEnumerable<IUserInfo> ProviderData {
    get {
      return GetValidFirebaseUserInternal().ProviderData;
    }
  }

  /// Gets the provider ID for the user (For example, "Facebook").
  public string ProviderId {
    get {
      return GetValidFirebaseUserInternal().ProviderId;
    } 
  }

  /// Gets the unique Firebase user ID for the user.
  ///
  /// @note The user's ID, unique to the Firebase project.
  /// Do NOT use this value to authenticate with your backend server, if you
  /// have one.
  /// Use FirebaseUser.TokenAsync instead.
  public string UserId {
    get {
      return GetValidFirebaseUserInternal().UserId;
    } 
  }
}

}  // namespace Firebase.Auth
