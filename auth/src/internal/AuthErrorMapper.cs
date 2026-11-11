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
using System.Collections.Generic;
using Firebase.Internal;

namespace Firebase.Auth.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class AuthErrorMapper {
    private static readonly Dictionary<string, AuthError> BackendCodeMap =
        new Dictionary<string, AuthError>(StringComparer.Ordinal) {
          { "INVALID_CUSTOM_TOKEN", AuthError.InvalidCustomToken },
          { "CREDENTIAL_MISMATCH", AuthError.CustomTokenMismatch },
          { "INVALID_IDP_RESPONSE", AuthError.InvalidCredential },
          { "INVALID_PENDING_TOKEN", AuthError.InvalidCredential },
          { "INVALID_GRANT", AuthError.InvalidUserToken },
          { "INVALID_REFRESH_TOKEN", AuthError.InvalidUserToken },
          { "MISSING_REFRESH_TOKEN", AuthError.InvalidUserToken },
          { "USER_DISABLED", AuthError.UserDisabled },
          { "OPERATION_NOT_ALLOWED", AuthError.OperationNotAllowed },
          { "PASSWORD_LOGIN_DISABLED", AuthError.OperationNotAllowed },
          { "EMAIL_EXISTS", AuthError.EmailAlreadyInUse },
          { "CREDENTIAL_TOO_OLD_LOGIN_AGAIN", AuthError.RequiresRecentLogin },
          { "FEDERATED_USER_ID_ALREADY_LINKED", AuthError.CredentialAlreadyInUse },
          { "PHONE_NUMBER_EXISTS", AuthError.CredentialAlreadyInUse },
          { "INVALID_EMAIL", AuthError.InvalidEmail },
          { "INVALID_PASSWORD", AuthError.WrongPassword },
          { "USER_NOT_FOUND", AuthError.UserNotFound },
          { "EMAIL_NOT_FOUND", AuthError.UserNotFound },
          { "INVALID_PROVIDER_ID : Provider Id is not supported.", AuthError.NoSuchProvider },
          { "INVALID_PROVIDER_ID", AuthError.InvalidProviderId },
          { "INVALID_ID_TOKEN", AuthError.InvalidUserToken },
          { "TOKEN_EXPIRED", AuthError.UserTokenExpired },
          { "<<Network Error>>", AuthError.NetworkRequestFailed },
          { "EXPIRED_OOB_CODE", AuthError.ExpiredActionCode },
          { "INVALID_OOB_CODE", AuthError.InvalidActionCode },
          { "INVALID_MESSAGE_PAYLOAD", AuthError.InvalidMessagePayload },
          { "INVALID_PHONE_NUMBER", AuthError.InvalidPhoneNumber },
          { "MISSING_PHONE_NUMBER", AuthError.MissingPhoneNumber },
          { "INVALID_RECIPIENT_EMAIL", AuthError.InvalidRecipientEmail },
          { "INVALID_SENDER", AuthError.InvalidSender },
          { "INVALID_CODE", AuthError.InvalidVerificationCode },
          { "INVALID_SESSION_INFO", AuthError.InvalidVerificationId },
          { "INVALID_TEMPORARY_PROOF", AuthError.InvalidVerificationId },
          { "MISSING_CODE", AuthError.MissingVerificationCode },
          { "MISSING_SESSION_INFO", AuthError.MissingVerificationId },
          { "MISSING_EMAIL", AuthError.MissingEmail },
          { "MISSING_PASSWORD", AuthError.MissingPassword },
          { "QUOTA_EXCEEDED", AuthError.QuotaExceeded },
          { "SESSION_EXPIRED", AuthError.SessionExpired },
          { "APP_NOT_VERIFIED", AuthError.AppNotVerified },
          { "CAPTCHA_CHECK_FAILED", AuthError.CaptchaCheckFailed },
          { "INVALID_APP_CREDENTIAL", AuthError.AppNotAuthorized },
          { "MISSING_APP_CREDENTIAL", AuthError.MissingAppCredential },
          { "INVALID_CLIENT_ID", AuthError.InvalidClientId },
          { "INVALID_CONTINUE_URI", AuthError.InvalidContinueUri },
          { "MISSING_CONTINUE_URI", AuthError.MissingContinueUri },
          { "MISSING_IOS_BUNDLE_ID", AuthError.MissingIosBundleId },
          { "UNAUTHORIZED_DOMAIN", AuthError.UnauthorizedDomain },
          { "DYNAMIC_LINK_NOT_ACTIVATED", AuthError.DynamicLinkNotActivated },
          { "TENANT_ID_MISMATCH", AuthError.TenantIdMismatch },
          { "UNSUPPORTED_TENANT_OPERATION", AuthError.UnsupportedTenantOperation },
          { "INVALID_DYNAMIC_LINK_DOMAIN", AuthError.InvalidLinkDomain },
          { "REJECTED_CREDENTIAL", AuthError.RejectedCredential },
          { "PHONE_NUMBER_NOT_FOUND", AuthError.PhoneNumberNotFound },
          { "INVALID_TENANT_ID", AuthError.InvalidTenantId },
          { "MISSING_CLIENT_IDENTIFIER", AuthError.MissingClientIdentifier },
          { "MISSING_MFA_PENDING_CREDENTIAL", AuthError.MissingMultiFactorSession },
          { "MISSING_MFA_ENROLLMENT_ID", AuthError.MissingMultiFactorInfo },
          { "INVALID_MFA_PENDING_CREDENTIAL", AuthError.InvalidMultiFactorSession },
          { "MFA_ENROLLMENT_NOT_FOUND", AuthError.MultiFactorInfoNotFound },
          { "ADMIN_ONLY_OPERATION", AuthError.AdminRestrictedOperation },
          { "UNVERIFIED_EMAIL", AuthError.UnverifiedEmail },
          { "SECOND_FACTOR_EXISTS", AuthError.SecondFactorAlreadyEnrolled },
          { "SECOND_FACTOR_LIMIT_EXCEEDED", AuthError.MaximumSecondFactorCountExceeded },
          { "UNSUPPORTED_FIRST_FACTOR", AuthError.UnsupportedFirstFactor },
          { "EMAIL_CHANGE_NEEDS_VERIFICATION", AuthError.EmailChangeNeedsVerification },
          { "API_KEY_NOT_VALID", AuthError.InvalidApiKey },
          { "PROJECT_NOT_FOUND", AuthError.InvalidApiKey },
        };

    internal static AuthError MapBackendError(string rawMessage, string reason = null) {
      if (string.IsNullOrEmpty(rawMessage)) {
        if (string.Equals(reason, "keyInvalid", StringComparison.Ordinal)) {
          return AuthError.InvalidApiKey;
        }
        if (string.Equals(reason, "ipRefererBlocked", StringComparison.Ordinal)) {
          return AuthError.AppNotAuthorized;
        }
        return AuthError.Failure;
      }

      string trimmed = rawMessage.Trim();
      if (BackendCodeMap.TryGetValue(trimmed, out AuthError exact)) {
        return exact;
      }

      // Check prefix before spaces or colons (e.g. "WEAK_PASSWORD : Password should be at least 6 characters").
      int sep = trimmed.IndexOfAny(new[] { ' ', ':' });
      if (sep > 0) {
        string prefix = trimmed.Substring(0, sep);
        if (BackendCodeMap.TryGetValue(prefix, out AuthError byPrefix)) {
          return byPrefix;
        }
      }

      if (trimmed.IndexOf("TOO_MANY_ATTEMPTS_TRY_LATER", StringComparison.Ordinal) >= 0) {
        return AuthError.TooManyRequests;
      }
      if (trimmed.IndexOf("WEAK_PASSWORD", StringComparison.Ordinal) >= 0) {
        return AuthError.WeakPassword;
      }
      if (trimmed.IndexOf("API_KEY_NOT_VALID", StringComparison.Ordinal) >= 0 ||
          (string.Equals(trimmed, "Bad Request", StringComparison.Ordinal) &&
           string.Equals(reason, "keyInvalid", StringComparison.Ordinal)) ||
          string.Equals(reason, "keyInvalid", StringComparison.Ordinal)) {
        return AuthError.InvalidApiKey;
      }
      if (string.Equals(reason, "ipRefererBlocked", StringComparison.Ordinal)) {
        return AuthError.AppNotAuthorized;
      }
      return AuthError.Failure;
    }

    internal static AuthError ParseErrorBody(string responseBody, int httpStatusCode) {
      Dictionary<string, object> root = JsonHelper.ParseObject(responseBody);
      if (root != null) {
        Dictionary<string, object> errObj = JsonHelper.GetMap(root, "error");
        if (errObj != null) {
          string msg = JsonHelper.GetString(errObj, "message", string.Empty);
          string reason = string.Empty;
          List<object> errors = JsonHelper.GetList(errObj, "errors");
          if (errors != null && errors.Count > 0 && errors[0] is IDictionary<string, object> first) {
            reason = JsonHelper.GetString(first, "reason", string.Empty);
          }
          return MapBackendError(msg, reason);
        }
        // SecureToken OAuth2 style error {"error": "invalid_grant", "error_description": "..."}
        string flatErr = JsonHelper.GetString(root, "error");
        if (!string.IsNullOrEmpty(flatErr)) {
          string upper = flatErr.ToUpperInvariant();
          string desc = JsonHelper.GetString(root, "error_description", string.Empty);
          if (!string.IsNullOrEmpty(desc)) {
            AuthError descMapped = MapBackendError(desc.ToUpperInvariant());
            if (descMapped != AuthError.Failure) return descMapped;
          }
          return MapBackendError(upper);
        }
      }

      if (httpStatusCode == 429) {
        return AuthError.TooManyRequests;
      }
      return AuthError.Failure;
    }

    /// <summary>
    /// Returns true ONLY when the server definitively revoked or deleted the user session.
    /// Transient network, 429 rate-limit, 5xx, or generic failures MUST NEVER sign out the user.
    /// </summary>
    internal static bool IsTerminalSessionInvalidation(AuthError error) =>
        error == AuthError.UserDisabled ||
        error == AuthError.UserNotFound ||
        error == AuthError.UserTokenExpired ||
        error == AuthError.InvalidUserToken;

    internal static FirebaseException CreateException(AuthError code, Exception inner = null) {
      string msg = GetMessage(code);
      return inner != null
          ? new FirebaseException((int)code, msg, inner)
          : new FirebaseException((int)code, msg);
    }

    internal static string GetMessage(AuthError code) => code switch {
      AuthError.InvalidCustomToken => "The custom token format is incorrect. Please check the documentation.",
      AuthError.CustomTokenMismatch => "The custom token corresponds to a different audience.",
      AuthError.InvalidCredential => "The supplied auth credential is malformed or has expired.",
      AuthError.UserDisabled => "The user account has been disabled by an administrator.",
      AuthError.OperationNotAllowed => "This operation is not allowed. You must enable this service in the console.",
      AuthError.EmailAlreadyInUse => "The email address is already in use by another account.",
      AuthError.RequiresRecentLogin => "This operation is sensitive and requires recent authentication. Log in again before retrying this request.",
      AuthError.CredentialAlreadyInUse => "This credential is already associated with a different user account.",
      AuthError.InvalidEmail => "The email address is badly formatted.",
      AuthError.WrongPassword => "The password is invalid or the user does not have a password.",
      AuthError.UserNotFound => "There is no user record corresponding to this identifier. The user may have been deleted.",
      AuthError.NoSuchProvider => "User was not linked to an account with the given provider.",
      AuthError.InvalidUserToken => "The user's credential is no longer valid. The user must sign in again.",
      AuthError.UserTokenExpired => "The user's credential is no longer valid. The user must sign in again.",
      AuthError.NetworkRequestFailed => "A network error (such as timeout, interrupted connection or unreachable host) has occurred.",
      AuthError.ExpiredActionCode => "The out of band code has expired.",
      AuthError.InvalidActionCode => "The out of band code is invalid. This can happen if the code is malformed, expired, or has already been used.",
      AuthError.InvalidMessagePayload => "The email template corresponding to this action contains invalid characters in its message.",
      AuthError.InvalidPhoneNumber => "The format of the phone number provided is incorrect. Please enter the phone number in E.164 format.",
      AuthError.MissingPhoneNumber => "To send verification codes, provide a phone number for the recipient.",
      AuthError.InvalidRecipientEmail => "The email corresponding to this action failed to send as the provided recipient email address is invalid.",
      AuthError.InvalidSender => "The email template corresponding to this action contains an invalid sender email or name.",
      AuthError.InvalidVerificationCode => "The sms verification code used to create the phone auth credential is invalid.",
      AuthError.InvalidVerificationId => "The verification ID used to create the phone auth credential is invalid.",
      AuthError.MissingVerificationCode => "The Phone Auth Credential was created with an empty sms verification code.",
      AuthError.MissingVerificationId => "The Phone Auth Credential was created with an empty verification ID.",
      AuthError.MissingEmail => "An email address must be provided.",
      AuthError.MissingPassword => "A password must be provided.",
      AuthError.QuotaExceeded => "The project's quota for this operation has been exceeded.",
      AuthError.SessionExpired => "The sms code has expired. Please re-send the verification code to try again.",
      AuthError.AppNotAuthorized => "This app is not authorized to use Firebase Authentication.",
      AuthError.TooManyRequests => "We have blocked all requests from this device due to unusual activity. Try again later.",
      AuthError.InvalidApiKey => "Your API key is invalid, please check you have copied it correctly.",
      AuthError.WeakPassword => "The given password is invalid.",
      AuthError.UserMismatch => "The supplied credentials do not correspond to the previously signed in user.",
      AuthError.AccountExistsWithDifferentCredentials => "An account already exists with the same email address but different sign-in credentials.",
      AuthError.ApiNotAvailable => "The API that you are calling is not available on this platform.",
      AuthError.ProviderAlreadyLinked => "User has already been linked to the given provider.",
      AuthError.NoSignedInUser => "Please sign in before trying to perform this operation.",
      AuthError.InvalidProviderId => "The requested provider ID is invalid. You must enable this provider in the console.",
      AuthError.MissingClientIdentifier => "This request is missing a valid client identifier or reCAPTCHA token.",
      AuthError.MissingMultiFactorSession => "The request is missing proof of first factor successful sign-in.",
      AuthError.MissingMultiFactorInfo => "No second factor identifier is provided.",
      AuthError.InvalidMultiFactorSession => "The request does not contain a valid proof of first factor successful sign-in.",
      AuthError.MultiFactorInfoNotFound => "The user does not have a second factor matching the identifier provided.",
      AuthError.AdminRestrictedOperation => "This operation is restricted to administrators only.",
      AuthError.UnverifiedEmail => "This operation requires a verified email.",
      AuthError.SecondFactorAlreadyEnrolled => "The second factor is already enrolled on this account.",
      AuthError.MaximumSecondFactorCountExceeded => "The maximum allowed number of second factors on a user has been exceeded.",
      AuthError.UnsupportedFirstFactor => "Enrolling a second factor or signing in with a multi-factor account requires sign-in with a supported first factor.",
      AuthError.EmailChangeNeedsVerification => "Multi-factor users must always have a verified email.",
      AuthError.Unimplemented => "Operation is not supported on this platform.",
      AuthError.Cancelled => "The authentication operation was cancelled.",
      AuthError.WebContextCancelled => "The web authentication operation was cancelled by the user.",
      AuthError.WebContextAlreadyPresented => "A web authentication operation is already in progress.",
      _ => "An internal error has occurred.",
    };
  }
}
