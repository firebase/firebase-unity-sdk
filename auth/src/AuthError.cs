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

namespace Firebase.Auth {
  /// <summary>
  /// Error codes returned by Firebase Authentication operations in
  /// <see cref="Firebase.FirebaseException.ErrorCode" /> and
  /// <see cref="FirebaseAccountLinkException.ErrorCode" />.
  /// </summary>
  public enum AuthError : int {
    Unimplemented = -1,
    None = 0,
    Failure = 1,
    InvalidCustomToken = 2,
    CustomTokenMismatch = 3,
    InvalidCredential = 4,
    UserDisabled = 5,
    AccountExistsWithDifferentCredentials = 6,
    OperationNotAllowed = 7,
    EmailAlreadyInUse = 8,
    RequiresRecentLogin = 9,
    CredentialAlreadyInUse = 10,
    InvalidEmail = 11,
    WrongPassword = 12,
    TooManyRequests = 13,
    UserNotFound = 14,
    ProviderAlreadyLinked = 15,
    NoSuchProvider = 16,
    InvalidUserToken = 17,
    UserTokenExpired = 18,
    NetworkRequestFailed = 19,
    InvalidApiKey = 20,
    AppNotAuthorized = 21,
    UserMismatch = 22,
    WeakPassword = 23,
    NoSignedInUser = 24,
    ApiNotAvailable = 25,
    ExpiredActionCode = 26,
    InvalidActionCode = 27,
    InvalidMessagePayload = 28,
    InvalidPhoneNumber = 29,
    MissingPhoneNumber = 30,
    InvalidRecipientEmail = 31,
    InvalidSender = 32,
    InvalidVerificationCode = 33,
    InvalidVerificationId = 34,
    MissingVerificationCode = 35,
    MissingVerificationId = 36,
    MissingEmail = 37,
    MissingPassword = 38,
    QuotaExceeded = 39,
    RetryPhoneAuth = 40,
    SessionExpired = 41,
    AppNotVerified = 42,
    AppVerificationFailed = 43,
    CaptchaCheckFailed = 44,
    InvalidAppCredential = 45,
    MissingAppCredential = 46,
    InvalidClientId = 47,
    InvalidContinueUri = 48,
    MissingContinueUri = 49,
    KeychainError = 50,
    MissingAppToken = 51,
    MissingIosBundleId = 52,
    NotificationNotForwarded = 53,
    UnauthorizedDomain = 54,
    WebContextAlreadyPresented = 55,
    WebContextCancelled = 56,
    DynamicLinkNotActivated = 57,
    Cancelled = 58,
    InvalidProviderId = 59,
    WebInternalError = 60,
    WebStorageUnsupported = 61,
    TenantIdMismatch = 62,
    UnsupportedTenantOperation = 63,
    InvalidLinkDomain = 64,
    RejectedCredential = 65,
    PhoneNumberNotFound = 66,
    InvalidTenantId = 67,
    MissingClientIdentifier = 68,
    MissingMultiFactorSession = 69,
    MissingMultiFactorInfo = 70,
    InvalidMultiFactorSession = 71,
    MultiFactorInfoNotFound = 72,
    AdminRestrictedOperation = 73,
    UnverifiedEmail = 74,
    SecondFactorAlreadyEnrolled = 75,
    MaximumSecondFactorCountExceeded = 76,
    UnsupportedFirstFactor = 77,
    EmailChangeNeedsVerification = 78,
  }
}
