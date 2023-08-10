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

  public override void Dispose() {
    // Nothing to do, but kept for backwards compatibility
  }

  private void CompleteSignInResult(SignInResult signInResult) {
    if (signInResult != null) {
      // Cache the authProxy in the SignInResult
      signInResult.authProxy = authProxy;
    }
  }

  private void CompleteAuthResult(AuthResult authResult) {
    if (authResult != null) {
      // Cache the authProxy in the AuthResult
      authResult.authProxy = authProxy;
    }
  }

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref ReauthenticateWithProviderAsync(FederatedAuthProvider) instead.
  ///
  /// Reauthenticate a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS, tvOS and Android
  /// platforms. On other platforms this method will return a Future with a
  /// preset error code: kAuthErrorUnimplemented.
  [System.Obsolete("Please use `Task<AuthResult> ReauthenticateWithProviderAsync(FederatedAuthProvider)` instead", false)]
  public async Task<SignInResult> ReauthenticateWithProviderAsync_DEPRECATED(FederatedAuthProvider provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    SignInResult result = await userInternal.ReauthenticateWithProviderInternalAsync_DEPRECATED(provider);
    CompleteSignInResult(result);
    return result;
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

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref LinkWithProviderAsync(FederatedAuthProvider) instead.
  ///
  /// Link a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS, tvOS and Android
  /// platforms. On other platforms this method will return a Future with a
  /// preset error code: kAuthErrorUnimplemented.
  [System.Obsolete("Please use `Task<AuthResult> LinkWithProviderAsync(FederatedAuthProvider)` instead", false)]
  public async Task<SignInResult> LinkWithProviderAsync_DEPRECATED(FederatedAuthProvider provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    SignInResult result = await userInternal.LinkWithProviderInternalAsync_DEPRECATED(provider);
    CompleteSignInResult(result);
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

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref LinkWithCredentialAsync(Credential) instead.
  ///
  /// Links the user with the given 3rd party credentials.
  ///
  /// For example, a Facebook login access token, a Twitter token/token-secret
  /// pair.
  /// Status will be an error if the token is invalid, expired, or otherwise
  /// not accepted by the server as well as if the given 3rd party
  /// user id is already linked with another user account or if the current user
  /// is already linked with another id from the same provider.
  ///
  /// Data from the Identity Provider used to sign-in is returned in the
  /// @ref AdditionalUserInfo inside @ref SignInResult.
  [System.Obsolete("Please use `Task<AuthResult> LinkWithCredentialAsync(Credential)` instead", false)]
  public async Task<SignInResult> LinkAndRetrieveDataWithCredentialAsync(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    SignInResult result = await userInternal.LinkAndRetrieveDataWithCredentialInternalAsync(credential);
    CompleteSignInResult(result);
    return result;
  }

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref LinkWithCredentialAsync(Credential) instead.
  ///
  /// Associates a user account from a third-party identity provider.
  [System.Obsolete("Please use `Task<AuthResult> LinkWithCredentialAsync(Credential)` instead", false)]
  public async Task<FirebaseUser> LinkWithCredentialAsync_DEPRECATED(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    // We don't care about the returned user, since there is currently only meant to
    // be a single FirebaseUser under the hood.
    await userInternal.LinkWithCredentialInternalAsync_DEPRECATED(credential);
    return this;
  }

  /// Associates a user account from a third-party identity provider.
  public async Task<AuthResult> LinkWithCredentialAsync(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.LinkWithCredentialInternalAsync(credential);
    CompleteAuthResult(result);
    return result;
  }

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref ReauthenticateAndRetrieveDataAsync(Credential) instead.
  ///
  /// Reauthenticate using a credential.
  ///
  /// Data from the Identity Provider used to sign-in is returned in the
  /// AdditionalUserInfo inside the returned SignInResult.
  ///
  /// Returns an error if the existing credential is not for this user
  /// or if sign-in with that credential failed.
  ///
  /// @note: The current user may be signed out if this operation fails on
  /// Android and desktop platforms.
  [System.Obsolete("Please use `Task<AuthResult> ReauthenticateAndRetrieveDataAsync(Credential)` instead", false)]
  public async Task<SignInResult> ReauthenticateAndRetrieveDataAsync_DEPRECATED(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    SignInResult result = await userInternal.ReauthenticateAndRetrieveDataInternalAsync_DEPRECATED(credential);
    CompleteSignInResult(result);
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

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use @ref UnlinkAsync(string) instead.
  ///
  /// Unlinks the current user from the provider specified.
  /// Status will be an error if the user is not linked to the given provider.
  [System.Obsolete("Please use `Task<AuthResult> UnlinkAsync(string)` instead", false)]
  public async Task<FirebaseUser> UnlinkAsync_DEPRECATED(string provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    // We don't care about the returned user, since there is currently only meant to
    // be a single FirebaseUser under the hood.
    await userInternal.UnlinkInternalAsync_DEPRECATED(provider);
    return this;
  }

  /// Unlinks the current user from the provider specified.
  /// Status will be an error if the user is not linked to the given provider.
  public async Task<AuthResult> UnlinkAsync(string provider) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    AuthResult result = await userInternal.UnlinkInternalAsync(provider);
    CompleteAuthResult(result);
    return result;
  }

  /// @deprecated This method is deprecated in favor of methods that return
  /// `Task<AuthResult>`. Please use
  /// @ref UpdatePhoneNumberCredentialAsync(PhoneAuthCredential) instead.
  ///
  /// Updates the currently linked phone number on the user.
  /// This is useful when a user wants to change their phone number. It is a
  /// shortcut to calling `UnlinkAsync_DEPRECATED(phoneCredential.Provider)`
  /// and then `LinkWithCredentialAsync_DEPRECATED(phoneCredential)`.
  /// `phoneCredential` must have been created with @ref PhoneAuthProvider.
  [System.Obsolete("Please use `Task<AuthResult> UpdatePhoneNumberCredentialAsync(PhoneAuthCredential)` instead", false)]
  public async Task<FirebaseUser> UpdatePhoneNumberCredentialAsync_DEPRECATED(Credential credential) {
    FirebaseUserInternal userInternal = GetValidFirebaseUserInternal();
    // We don't care about the returned user, since there is currently only meant to
    // be a single FirebaseUser under the hood.
    await userInternal.UpdatePhoneNumberCredentialInternalAsync_DEPRECATED(credential);
    return this;
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
    await userInternal.UpdatePhoneNumberCredentialInternalAsync_DEPRECATED(credential);
    return this;
  }

  public bool IsValid() {
    if (authProxy != null) {
      FirebaseUserInternal userInternal = authProxy.CurrentUserInternal;
      if (userInternal != null) {
        return userInternal.IsValid();
      }
    }
    return false;
  }

  public Task<string> TokenAsync(bool forceRefresh) {
    return GetValidFirebaseUserInternal().TokenAsync(forceRefresh);
  }

  public Task UpdateEmailAsync(string email) {
    return GetValidFirebaseUserInternal().UpdateEmailAsync(email);
  }

  public Task UpdatePasswordAsync(string password) {
    return GetValidFirebaseUserInternal().UpdatePasswordAsync(password);
  }

  public Task ReauthenticateAsync(Credential credential) {
    return GetValidFirebaseUserInternal().ReauthenticateAsync(credential);
  }

  public Task SendEmailVerificationAsync() {
    return GetValidFirebaseUserInternal().SendEmailVerificationAsync();
  }

  public Task UpdateUserProfileAsync(UserProfile profile) {
    return GetValidFirebaseUserInternal().UpdateUserProfileAsync(profile);
  }

  public System.Threading.Tasks.Task ReloadAsync() {
    return GetValidFirebaseUserInternal().ReloadAsync();
  }

  public System.Threading.Tasks.Task DeleteAsync() {
    return GetValidFirebaseUserInternal().DeleteAsync();
  }

  public string DisplayName {
    get {
      return GetValidFirebaseUserInternal().DisplayName;
    } 
  }

  public string Email {
    get {
      return GetValidFirebaseUserInternal().Email;
    } 
  }

  public bool IsAnonymous {
    get {
      return GetValidFirebaseUserInternal().IsAnonymous;
    } 
  }

  public bool IsEmailVerified {
    get {
      return GetValidFirebaseUserInternal().IsEmailVerified;
    } 
  }

  public UserMetadata Metadata {
    get {
      return GetValidFirebaseUserInternal().Metadata;
    } 
  }

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

  public System.Collections.Generic.IEnumerable<IUserInfo> ProviderData {
    get {
      return GetValidFirebaseUserInternal().ProviderData;
    }
  }

  public string ProviderId {
    get {
      return GetValidFirebaseUserInternal().ProviderId;
    } 
  }

  public string UserId {
    get {
      return GetValidFirebaseUserInternal().UserId;
    } 
  }
}

}  // namespace Firebase.Auth
