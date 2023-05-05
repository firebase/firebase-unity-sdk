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

using System.Collections.Generic;

namespace Firebase.Auth {

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
///
/// Note: Both @ref VerificationCompleted_DEPRECATED and
/// @ref VerificationCompleted will be triggered upon completion. Developer
/// should only use only one of them to prevent duplicated event handling.
public sealed class PhoneAuthProvider : global::System.IDisposable {
  /// @deprecated This is a deprecated delegate. Please use @ref
  /// VerificationCompleted instead.
  //
  /// Callback used when phone number auto-verification succeeded.
  [System.Obsolete("Please use `VerificationCompleted(PhoneAuthCredential)` instead", false)]
  public delegate void VerificationCompleted_DEPRECATED(Credential credential);
  /// Callback used when phone number auto-verification succeeded.
  public delegate void VerificationCompleted(PhoneAuthCredential credential);
  /// Callback used when phone number verification fails.
  public delegate void VerificationFailed(string error);
  /// Callback used when a verification code is sent to the given number.
  public delegate void CodeSent(string verificationId,
                                ForceResendingToken forceResendingToken);
  /// Callback used when a timeout occurs.
  public delegate void CodeAutoRetrievalTimeOut(string verificationId);

  private static int uidGenerator = 0;

  // Class to hold the delegates the user provides to the verification flow.
  private class PhoneAuthDelegates {
    public VerificationCompleted_DEPRECATED verificationCompleted_DEPRECATED;
    public VerificationCompleted verificationCompleted;
    public VerificationFailed verificationFailed;
    public CodeSent codeSent;
    public CodeAutoRetrievalTimeOut timeOut;
  };
  private static Dictionary<int, PhoneAuthDelegates> authCallbacks =
      new Dictionary<int, PhoneAuthDelegates>();

  // Hold onto the C++ Listeners that get created by the verification flow,
  // so that they can be deleted when necessary.
  private Dictionary<int, System.IntPtr> cppListeners =
      new Dictionary<int, System.IntPtr>();

  /// Caches the callbacks in a dictionary so that they can be called
  /// when the C++ library indicates a callback.
  ///
  /// @return The unique identifier for the cached callbacks.
  private static int SaveCallbacks(VerificationCompleted_DEPRECATED verificationCompleted_DEPRECATED,
                                   VerificationCompleted verificationCompleted,
                                   VerificationFailed verificationFailed,
                                   CodeSent codeSent,
                                   CodeAutoRetrievalTimeOut timeOut) {
    int uid = uidGenerator++;
    var delegates = new PhoneAuthDelegates {
      verificationCompleted_DEPRECATED = verificationCompleted_DEPRECATED,
      verificationCompleted = verificationCompleted,
      verificationFailed = verificationFailed,
      codeSent = codeSent,
      timeOut = timeOut
    };
    lock (authCallbacks) {
      authCallbacks[uid] = delegates;
    }
    return uid;
  }

  [MonoPInvokeCallback(typeof(PhoneAuthProviderInternal.VerificationCompletedDelegate_DEPRECATED))]
  private static void VerificationCompletedHandler_DEPRECATED(int callbackId,
                                                   System.IntPtr credential) {
    ExceptionAggregator.Wrap(() => {
        Credential c = new Credential(credential, true);
        lock (authCallbacks) {
          PhoneAuthDelegates callbacks;
          if (authCallbacks.TryGetValue(callbackId, out callbacks) &&
              callbacks.verificationCompleted_DEPRECATED != null) {
            callbacks.verificationCompleted_DEPRECATED(c);
          } else {
            c.Dispose();
          }
        }
      });
  }

  [MonoPInvokeCallback(typeof(PhoneAuthProviderInternal.VerificationCompletedDelegate))]
  private static void VerificationCompletedHandler(int callbackId,
                                                   System.IntPtr credential) {
    ExceptionAggregator.Wrap(() => {
        PhoneAuthCredential c = new PhoneAuthCredential(credential, true);
        lock (authCallbacks) {
          PhoneAuthDelegates callbacks;
          if (authCallbacks.TryGetValue(callbackId, out callbacks) &&
              callbacks.verificationCompleted != null) {
            callbacks.verificationCompleted(c);
          } else {
            c.Dispose();
          }
        }
      });
  }

  [MonoPInvokeCallback(typeof(PhoneAuthProviderInternal.VerificationFailedDelegate))]
  private static void VerificationFailedHandler(int callbackId, string error) {
    ExceptionAggregator.Wrap(() => {
        lock (authCallbacks) {
          PhoneAuthDelegates callbacks;
          if (authCallbacks.TryGetValue(callbackId, out callbacks) &&
              callbacks.verificationFailed != null) {
            callbacks.verificationFailed(error);
          }
        }
      });
  }

  [MonoPInvokeCallback(typeof(PhoneAuthProviderInternal.CodeSentDelegate))]
  private static void CodeSentHandler(int callbackId, string verificationId, System.IntPtr token) {
    ExceptionAggregator.Wrap(() => {
        ForceResendingToken t = new ForceResendingToken(token, true);
        lock (authCallbacks) {
          PhoneAuthDelegates callbacks;
          if (authCallbacks.TryGetValue(callbackId, out callbacks) &&
              callbacks.codeSent != null) {
            callbacks.codeSent(verificationId, t);
          } else {
            t.Dispose();
          }
        }
      });
  }

  [MonoPInvokeCallback(typeof(PhoneAuthProviderInternal.TimeOutDelegate))]
  private static void TimeOutHandler(int callbackId, string verificationId) {
    ExceptionAggregator.Wrap(() => {
        lock (authCallbacks) {
          PhoneAuthDelegates callbacks;
          if (authCallbacks.TryGetValue(callbackId, out callbacks) &&
              callbacks.timeOut != null) {
            callbacks.timeOut(verificationId);
          }
        }
      });
  }

  private static PhoneAuthProviderInternal.VerificationCompletedDelegate_DEPRECATED
      verificationCompletedDelegate_DEPRECATED =
          new PhoneAuthProviderInternal.VerificationCompletedDelegate_DEPRECATED(
              VerificationCompletedHandler_DEPRECATED);
  private static PhoneAuthProviderInternal.VerificationCompletedDelegate
      verificationCompletedDelegate =
          new PhoneAuthProviderInternal.VerificationCompletedDelegate(
              VerificationCompletedHandler);
  private static PhoneAuthProviderInternal.VerificationFailedDelegate
      verificationFailedDelegate =
          new PhoneAuthProviderInternal.VerificationFailedDelegate(
              VerificationFailedHandler);
  private static PhoneAuthProviderInternal.CodeSentDelegate
      codeSentDelegate =
          new PhoneAuthProviderInternal.CodeSentDelegate(
              CodeSentHandler);
  private static PhoneAuthProviderInternal.TimeOutDelegate
      timeOutDelegate =
          new PhoneAuthProviderInternal.TimeOutDelegate(
              TimeOutHandler);

  private static bool callbacksInitialized = false;
  private static void InitializeCallbacks() {
    if (!callbacksInitialized) {
      callbacksInitialized = true;
      PhoneAuthProviderInternal.SetCallbacks(verificationCompletedDelegate_DEPRECATED,
                                             verificationCompletedDelegate,
                                             verificationFailedDelegate,
                                             codeSentDelegate,
                                             timeOutDelegate);
    }
  }

  /// @deprecated This is a deprecated method. Please use @ref
  /// VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent,
  /// CodeAutoRetrievalTimeOut) instead.
  ///
  /// Start the phone number authentication operation.
  ///
  /// @note  The verificationCompleted callback is never invoked on iOS since auto-validation is
  ///    not supported on that platform.
  ///
  /// @param[in] phoneNumber The phone number identifier supplied by the user.
  ///    Its format is normalized on the server, so it can be in any format
  ///    here.
  /// @param[in] autoVerifyTimeOutMs The time out for SMS auto retrieval, in
  ///    miliseconds. Currently SMS auto retrieval is only supported on Android.
  ///    If 0, do not do SMS auto retrieval.
  ///    If positive, try to auto-retrieve the SMS verification code.
  ///    When the time out is exceeded, `codeAutoRetrievalTimeOut`
  ///    is called.
  /// @param[in] forceResendingToken If NULL, assume this is a new phone
  ///    number to verify. If not-NULL, bypass the verification session deduping
  ///    and force resending a new SMS.
  ///    This token is received by the `CodeSent` callback.
  ///    This should only be used when the user presses a Resend SMS button.
  /// @param[in] verificationCompleted Phone number auto-verification succeeded.
  ///    Called when auto-sms-retrieval or instant validation succeeds.
  ///    Provided with the completed credential.
  /// @param[in] verificationFailed Phone number verification failed with an
  ///    error. For example, quota exceeded or unknown phone number format.
  ///    Provided with a description of the error.
  [System.Obsolete("Please use `VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent, CodeAutoRetrievalTimeOut)` instead", false)]
  public void VerifyPhoneNumber(string phoneNumber, uint autoVerifyTimeOutMs,
                                ForceResendingToken forceResendingToken,
                                VerificationCompleted_DEPRECATED verificationCompleted,
                                VerificationFailed verificationFailed) {
    VerifyPhoneNumber(phoneNumber, autoVerifyTimeOutMs, forceResendingToken,
                      verificationCompleted, verificationFailed,
                      null, null);
  }

  /// @deprecated This is a deprecated method. Please use @ref
  /// VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent,
  /// CodeAutoRetrievalTimeOut) instead.
  ///
  /// Start the phone number authentication operation.
  ///
  /// @note  The verificationCompleted callback is never invoked on iOS since auto-validation is
  ///    not supported on that platform.
  ///
  /// @param[in] phoneNumber The phone number identifier supplied by the user.
  ///    Its format is normalized on the server, so it can be in any format
  ///    here.
  /// @param[in] autoVerifyTimeOutMs The time out for SMS auto retrieval, in
  ///    miliseconds. Currently SMS auto retrieval is only supported on Android.
  ///    If 0, do not do SMS auto retrieval.
  ///    If positive, try to auto-retrieve the SMS verification code.
  ///    When the time out is exceeded, `codeAutoRetrievalTimeOut`
  ///    is called.
  /// @param[in] forceResendingToken If NULL, assume this is a new phone
  ///    number to verify. If not-NULL, bypass the verification session deduping
  ///    and force resending a new SMS.
  ///    This token is received by the `CodeSent` callback.
  ///    This should only be used when the user presses a Resend SMS button.
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
  [System.Obsolete("Please use `VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent, CodeAutoRetrievalTimeOut)` instead", false)]
  public void VerifyPhoneNumber(string phoneNumber, uint autoVerifyTimeOutMs,
                                ForceResendingToken forceResendingToken,
                                VerificationCompleted_DEPRECATED verificationCompleted,
                                VerificationFailed verificationFailed,
                                CodeSent codeSent) {
    VerifyPhoneNumber(phoneNumber, autoVerifyTimeOutMs, forceResendingToken,
                      verificationCompleted, verificationFailed,
                      codeSent, null);
  }

  /// @deprecated This is a deprecated method. Please use @ref
  /// VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent,
  /// CodeAutoRetrievalTimeOut) instead.
  ///
  /// Start the phone number authentication operation.
  ///
  /// @note  The verificationCompleted callback is never invoked on iOS since auto-validation is
  ///    not supported on that platform.
  ///
  /// @param[in] phoneNumber The phone number identifier supplied by the user.
  ///    Its format is normalized on the server, so it can be in any format
  ///    here.
  /// @param[in] autoVerifyTimeOutMs The time out for SMS auto retrieval, in
  ///    miliseconds. Currently SMS auto retrieval is only supported on Android.
  ///    If 0, do not do SMS auto retrieval.
  ///    If positive, try to auto-retrieve the SMS verification code.
  ///    When the time out is exceeded, `codeAutoRetrievalTimeOut`
  ///    is called.
  /// @param[in] forceResendingToken If NULL, assume this is a new phone
  ///    number to verify. If not-NULL, bypass the verification session deduping
  ///    and force resending a new SMS.
  ///    This token is received by the `CodeSent` callback.
  ///    This should only be used when the user presses a Resend SMS button.
  /// @param[in] verificationCompleted Phone number auto-verification succeeded.
  ///    Called when auto-sms-retrieval or instant validation succeeds.
  ///    Provided with the completed credential.
  /// @param[in] verificationFailed Phone number verification failed with an
  ///    error. For example, quota exceeded or unknown phone number format.
  ///    Provided with a description of the error.
  [System.Obsolete("Please use `VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent, CodeAutoRetrievalTimeOut)` instead", false)]
  public void VerifyPhoneNumber(string phoneNumber, uint autoVerifyTimeOutMs,
                                ForceResendingToken forceResendingToken,
                                VerificationCompleted_DEPRECATED verificationCompleted,
                                VerificationFailed verificationFailed,
                                CodeAutoRetrievalTimeOut codeAutoRetrievalTimeOut) {
    VerifyPhoneNumber(phoneNumber, autoVerifyTimeOutMs, forceResendingToken,
                      verificationCompleted, verificationFailed,
                      null, codeAutoRetrievalTimeOut);
  }

  /// @deprecated This is a deprecated method. Please use @ref
  /// VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent,
  /// CodeAutoRetrievalTimeOut) instead.
  ///
  /// Start the phone number authentication operation.
  ///
  /// @note  On iOS the verificationCompleted callback is never invoked and the
  ///    codeAutoRetrievalTimeOut callback is invoked immediately since auto-validation is not
  ///    supported on that platform.
  ///
  /// @param[in] phoneNumber The phone number identifier supplied by the user.
  ///    Its format is normalized on the server, so it can be in any format
  ///    here.
  /// @param[in] autoVerifyTimeOutMs The time out for SMS auto retrieval, in
  ///    miliseconds. Currently SMS auto retrieval is only supported on Android.
  ///    If 0, do not do SMS auto retrieval.
  ///    If positive, try to auto-retrieve the SMS verification code.
  ///    When the time out is exceeded, `codeAutoRetrievalTimeOut`
  ///    is called.
  /// @param[in] forceResendingToken If NULL, assume this is a new phone
  ///    number to verify. If not-NULL, bypass the verification session deduping
  ///    and force resending a new SMS.
  ///    This token is received by the `CodeSent` callback.
  ///    This should only be used when the user presses a Resend SMS button.
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
  [System.Obsolete("Please use `VerifyPhoneNumber(PhoneAuthOptions, VerificationCompleted, VerificationFailed, CodeSent, CodeAutoRetrievalTimeOut)` instead", false)]
  public void VerifyPhoneNumber(string phoneNumber, uint autoVerifyTimeOutMs,
                                ForceResendingToken forceResendingToken,
                                VerificationCompleted_DEPRECATED verificationCompleted,
                                VerificationFailed verificationFailed,
                                CodeSent codeSent,
                                CodeAutoRetrievalTimeOut codeAutoRetrievalTimeOut) {
    int callbackId = SaveCallbacks(
        verificationCompleted_DEPRECATED: verificationCompleted,
        verificationCompleted: null,
        verificationFailed: verificationFailed,
        codeSent: codeSent,
        timeOut: codeAutoRetrievalTimeOut);
    System.IntPtr listener = InternalProvider.VerifyPhoneNumberInternal(
        phoneNumber, autoVerifyTimeOutMs, forceResendingToken, callbackId);
    lock (cppListeners) {
      cppListeners.Add(callbackId, listener);
    }
  }


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
    int callbackId = SaveCallbacks(
        verificationCompleted_DEPRECATED: null,
        verificationCompleted: verificationCompleted,
        verificationFailed: verificationFailed,
        codeSent: codeSent,
        timeOut: codeAutoRetrievalTimeOut);
    System.IntPtr listener = InternalProvider.VerifyPhoneNumberInternal(
        options, callbackId);
    lock (cppListeners) {
      cppListeners.Add(callbackId, listener);
    }
  }

  // The SWIG generated PhoneAuthProvider which contains the C++ object.
  private PhoneAuthProviderInternal InternalProvider;
  internal PhoneAuthProvider(FirebaseAuth auth) {
    InitializeCallbacks();
    InternalProvider = PhoneAuthProviderInternal.GetInstance(auth);
  }

  // Clear all native resources used by this Provider.
  public void Dispose() {
    lock (cppListeners) {
      foreach (System.IntPtr cPtr in cppListeners.Values) {
        InternalProvider.DestroyListenerImplInternal(cPtr);
      }
      cppListeners.Clear();
      InternalProvider.Dispose();
      global::System.GC.SuppressFinalize(this);
    }
  }

  // Contains the mapping of FirebaseAuths to PhoneAuthProviders, so that
  // calls to GetInstance can return the same ones.
  private static Dictionary<FirebaseAuth, PhoneAuthProvider> CachedProviders =
      new Dictionary<FirebaseAuth, PhoneAuthProvider>();

  /// Return the PhoneAuthProvider for the specified `auth`.
  ///
  /// @param[in] auth The Auth session for which we want to get a
  ///    PhoneAuthProvider.
  ///
  /// @returns a PhoneAuthProvider for the given auth object.
  public static PhoneAuthProvider GetInstance(FirebaseAuth auth) {
    lock (CachedProviders) {
      PhoneAuthProvider provider = null;
      if (!CachedProviders.TryGetValue(auth, out provider)) {
        provider = new PhoneAuthProvider(auth);
        CachedProviders.Add(auth, provider);
      }
      return provider;
    }
  }

  /// @deprecated This is a deprecated method. Please use @ref GetCredential instead.
  ///
  /// Generate a credential for the given phone number.
  ///
  /// @param[in] verification_id The id returned when sending the verification
  ///    code. Sent to the caller via @ref Listener::OnCodeSent.
  /// @param[in] verification_code The verification code supplied by the user,
  ///    most likely by a GUI where the user manually enters the code
  ///    received in the SMS sent by @ref VerifyPhoneNumber.
  ///
  /// @returns New Credential.
  [System.Obsolete("Please use `PhoneAuthCredential GetCredential(string, string)` instead", false)]
  public Credential GetCredential_DEPRECATED(string verificationId,
                                  string verificationCode) {
    return InternalProvider.GetCredential_DEPRECATED(verificationId, verificationCode);
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
  public PhoneAuthCredential GetCredential(string verificationId,
                                           string verificationCode) {
    return InternalProvider.GetCredential(verificationId, verificationCode);
  }
}

}  // namespace Firebase.Auth

