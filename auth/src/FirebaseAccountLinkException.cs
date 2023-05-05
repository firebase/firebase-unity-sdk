/*
 * Copyright 2020 Google LLC
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

namespace Firebase.Auth {

/// Exception thrown for failed Account Link Attempts
///
/// Represents a Firebase Auth error when attempting to link an account. UserInfo contains
/// additional information about the account as returned by the Firebase Auth service, and may
/// include a valid UpdatedCredential which can be used to sign in to the service through
/// Firebase.Auth.SignInWithCredential.
public sealed class FirebaseAccountLinkException : System.Exception
{
  /// Initializes a new FirebaseAccountLinkException, with the given error code and
  /// message and the AdditionalUserInfo returned from the Firebase auth service.
  [System.Obsolete("Use `FirebaseAccountLinkException(int, string, AuthResult)` instead", false)]
  public FirebaseAccountLinkException(int errorCode, string message,
                                      SignInResult signInResult) : base(message)
  {
    ErrorCode = errorCode;
    result_DEPRECATED = signInResult;
  }

  /// Initializes a new FirebaseAccountLinkException, with the given error code and
  /// message and the AdditionalUserInfo returned from the Firebase auth service.
  public FirebaseAccountLinkException(int errorCode, string message,
                                      AuthResult authResult) : base(message)
  {
    ErrorCode = errorCode;
    result = authResult;
  }

  /// Returns the Auth defined non-zero error code.
  /// If the error code is 0, the error is with the Task itself, and not the
  /// API. See the exception message for more detail.
  public int ErrorCode { get; private set; }

  /// Returns a Firebase.Auth.UserInfo object that may include additional information about the
  /// account which failed to link. Additionally, if UserInfo.UpdatedCredential.IsValid() is true,
  /// the credential may be used to sign-in the user into Firebase with
  /// Firebase.Auth.SignInWithCredentialAsync.
  public AdditionalUserInfo UserInfo {
    get { return (result != null) ? result.AdditionalUserInfoInternal :
                 (result_DEPRECATED != null) ? result_DEPRECATED.Info : null; }
  }

  private SignInResult result_DEPRECATED = null;
  private AuthResult result = null;
}

}
