/*
 * Copyright 2017 Google LLC
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
using Firebase.Auth.Internal;

namespace Firebase.Auth {
  /// <summary>
  /// Token to maintain current phone number verification session.
  /// Acquired via <see cref="PhoneAuthProvider.CodeSent" />. Used in
  /// <see cref="PhoneAuthProvider.VerifyPhoneNumber" />.
  /// </summary>
  public sealed class ForceResendingToken : IDisposable {
    internal string SessionInfo { get; }
    internal ForceResendingToken(string sessionInfo = null) => SessionInfo = sessionInfo ?? string.Empty;

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public void Dispose(bool disposing) { }
  }

  /// <summary>Configuration options for <see cref="PhoneAuthProvider.VerifyPhoneNumber" />.</summary>
  public sealed class PhoneAuthOptions : IDisposable {
    private string _phoneNumber = string.Empty;

    public PhoneAuthOptions() { }

    /// <summary>
    /// Gets or sets the <see cref="Firebase.Auth.ForceResendingToken" /> to force
    /// another verification SMS to be sent before the auto-retrieval timeout.
    /// </summary>
    /// <remarks>
    /// If <c>null</c>, assume this is a new phone number to verify. If not <c>null</c>,
    /// bypass the verification session deduping and force resending a new SMS.
    /// This token is received in <see cref="PhoneAuthProvider.CodeSent" />.
    /// This should only be used when the user presses a Resend SMS button.
    /// </remarks>
    public ForceResendingToken ForceResendingToken { get; set; }

    /// <summary>The phone number for sign-in, sign-up, or second factor enrollment.</summary>
    public string PhoneNumber {
      get => _phoneNumber;
      set => _phoneNumber = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the maximum amount of time, in milliseconds, to wait for SMS
    /// auto-retrieval before invoking <see cref="PhoneAuthProvider.CodeAutoRetrievalTimeOut" />.
    /// </summary>
    /// <remarks>
    /// Positive values wait up to a maximum of 120000 milliseconds (values greater
    /// than 120000 are clamped to 120000). Use 0 to disable waiting and invoke
    /// <see cref="PhoneAuthProvider.CodeAutoRetrievalTimeOut" /> immediately.
    /// </remarks>
    public uint TimeoutInMilliseconds { get; set; }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public void Dispose(bool disposing) { }
  }

  /// @brief Use phone number text messages to authenticate.
  ///
  /// Allows developers to use the phone number and SMS verification codes
  /// to authenticate a user.
  ///
  /// This class is not supported on tvOS and Desktop platforms.
  ///
  /// The verification flow results in a @ref PhoneAuthCredential that can be
  /// used to,
  /// * Sign in to an existing phone number account/sign up with a new
  ///   phone number
  /// * Link a phone number to a current user. This provider will be added to
  ///   the user.
  /// * Update a phone number on an existing user.
  /// * Re-authenticate an existing user. This may be needed when a sensitive
  ///   operation requires the user to be recently logged in.
  ///
  /// Possible verification flows:
  /// (1) User manually enters verification code.
  ///     - App calls @ref VerifyPhoneNumber.
  ///     - Web verification page is displayed to user where they may need to
  ///       solve a CAPTCHA. [iOS only].
  ///     - Auth server sends the verification code via SMS to the provided
  ///       phone number. App receives verification id via @ref CodeSent.
  ///     - User receives SMS and enters verification code in app's GUI.
  ///     - App uses user's verification code to call
  ///       @ref PhoneAuthProvider.GetCredential.
  ///
  /// (2) SMS is automatically retrieved (Android only).
  ///     - App calls @ref VerifyPhoneNumber with `autoVerifyTimeOutMs` > 0.
  ///     - Auth server sends the verification code via SMS to the provided
  ///       phone number.
  ///     - SMS arrives and is automatically retrieved by the operating system.
  ///       @ref PhoneAuthCredential is automatically created and passed to the
  ///       app via @ref VerificationCompleted.
  ///
  /// (3) Phone number is instantly verified (Android only).
  ///     - App calls @ref VerifyPhoneNumber.
  ///     - The operating system validates the phone number without having to
  ///       send an SMS. @ref PhoneAuthCredential is automatically created and
  ///       passed to the app via @ref VerificationCompleted.
  public sealed class PhoneAuthProvider : IDisposable {
    /// Callback used when phone number auto-verification succeeded.
    public delegate void VerificationCompleted(PhoneAuthCredential credential);
    /// Callback used when phone number verification fails.
    public delegate void VerificationFailed(string error);
    /// Callback used when a verification code is sent to the given number.
    public delegate void CodeSent(string verificationId, ForceResendingToken forceResendingToken);
    /// Callback used when a timeout occurs.
    public delegate void CodeAutoRetrievalTimeOut(string verificationId);

    private readonly FirebaseAuth _auth;

    internal PhoneAuthProvider(FirebaseAuth auth) =>
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));

    /// Return the PhoneAuthProvider for the specified `auth`.
    ///
    /// @param[in] auth The Auth session for which we want to get a
    ///    PhoneAuthProvider.
    ///
    /// @returns a PhoneAuthProvider for the given auth object.
    public static PhoneAuthProvider GetInstance(FirebaseAuth auth) {
      if (auth == null) throw new ArgumentNullException(nameof(auth));
      return auth.GetOrCreatePhoneAuthProvider();
    }

    /// Generate a credential for the given phone number.
    ///
    /// @param[in] verification_id The id returned when sending the verification
    ///    code. Sent to the caller via @ref Listener::OnCodeSent.
    /// @param[in] verification_code The verification code supplied by the user,
    ///    most likely by a GUI where the user manually enters the code
    ///    received in the SMS sent by @ref VerifyPhoneNumber.
    ///
    /// @returns New PhoneAuthCredential.
    public PhoneAuthCredential GetCredential(string verificationId, string verificationCode) =>
        new PhoneAuthCredential(verificationId, verificationCode);

    /// Start the phone number authentication operation.
    ///
    /// @note  On iOS the verificationCompleted callback is never invoked and the
    ///    codeAutoRetrievalTimeOut callback is invoked immediately since auto-validation is not
    ///    supported on that platform.
    ///
    /// @param[in] options The PhoneAuthOptions struct with a verification
    ///    configuration.
    /// @param[in] verificationCompleted Phone number auto-verification succeeded.
    ///    Called when auto-sms-retrieval or instant validation succeeds.
    ///    Provided with the completed credential.
    /// @param[in] verificationFailed Phone number verification failed with an
    ///    error. For example, quota exceeded or unknown phone number format.
    ///    Provided with a description of the error.
    /// @param[in] codeSent SMS message with verification code sent to phone
    ///    number. Provided with the verification id to pass along to
    ///    `GetCredential` along with the sent code, and a token to use if
    ///    the user requests another SMS message be sent.
    /// @param[in] codeAutoRetrievalTimeOut The timeout specified has expired.
    ///    Provided with the verification id for the transaction that timed out.
    public void VerifyPhoneNumber(
        PhoneAuthOptions options,
        VerificationCompleted verificationCompleted,
        VerificationFailed verificationFailed,
        CodeSent codeSent,
        CodeAutoRetrievalTimeOut codeAutoRetrievalTimeOut) {
      _ = MobileAuthPlatform.VerifyPhoneNumberAsync(
          _auth, options, verificationCompleted, verificationFailed, codeSent, codeAutoRetrievalTimeOut);
    }

    public void Dispose() => GC.SuppressFinalize(this);
  }
}  // namespace Firebase.Auth
