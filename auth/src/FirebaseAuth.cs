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
using System.Globalization;
using System.Threading.Tasks;
using Firebase.Auth.Internal;
using Firebase.Internal;

namespace Firebase.Auth {
  /// <summary>
  /// Firebase authentication object.
  /// </summary>
  /// <remarks>
  /// <see cref="FirebaseAuth" /> is the gateway to the Firebase authentication
  /// API. With it, you can reference <see cref="FirebaseUser" /> objects to
  /// manage user accounts and credentials.
  /// <para>
  /// Each <see cref="FirebaseApp" /> has up to one <see cref="FirebaseAuth" />
  /// class. You acquire the <see cref="FirebaseAuth" /> class through the static
  /// function <see cref="GetAuth(FirebaseApp)" />.
  /// </para>
  /// For example:
  /// <code>
  ///  // Get the Auth class for your App.
  ///  Firebase.Auth.FirebaseAuth auth = Firebase.Auth.FirebaseAuth.GetAuth(app);
  ///
  ///  // Request anonymous sign-in and wait until asynchronous call completes.
  ///  auth.SignInAnonymouslyAsync().ContinueWith((authTask) => {
  ///    // Print sign in results.
  ///    if (authTask.IsCanceled) {
  ///      DebugLog("Sign-in canceled.");
  ///    } else if (authTask.IsFaulted) {
  ///      DebugLog("Sign-in encountered an error.");
  ///      DebugLog(authTask.Exception.ToString());
  ///    } else if (authTask.IsCompleted) {
  ///      Firebase.Auth.AuthResult result = authTask.Result;
  ///      DebugLog(String.Format("Signed in as {0} user.",
  ///          result.User.IsAnonymous ? "an anonymous" : "a non-anonymous"));
  ///      DebugLog("Signing out.");
  ///      auth.SignOut();
  ///    }
  ///  });
  /// </code>
  /// </remarks>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class FirebaseAuth : IDisposable {
    private const long ProactiveRefreshBufferSeconds = 300L;
    private const long DefaultTokenLifetimeSeconds = 3600L;
    private const string DefaultRequestUri = "http://localhost";

    private sealed class AuthSlot {
      internal readonly object Sync = new object();
      internal FirebaseAuth Current;
      internal bool Evicted;
    }

    private static readonly FirebaseInstanceCache<AuthSlot> Instances =
        new FirebaseInstanceCache<AuthSlot>(slot => {
          FirebaseAuth toDispose;
          lock (slot.Sync) {
            slot.Evicted = true;
            toDispose = slot.Current;
            slot.Current = null;
          }
          toDispose?.Dispose();
        });

    internal static Action<Action> CustomMainThreadDispatcher { get; set; }
    internal static Func<long> UtcNowUnixSecondsOverride { get; set; }

    static FirebaseAuth() {
      RegisterInteropProvider();
    }

    internal static void RegisterInteropProvider() =>
        FirebaseInterops.RegisterAuthTokenProvider(GetTokenForInteropAsync);

    private static Task<string> GetTokenForInteropAsync(FirebaseApp app, bool forceRefresh) {
      if (app == null) return Task.FromResult<string>(null);
      FirebaseAuth auth;
      try {
        auth = GetAuth(app);
      } catch (ObjectDisposedException) {
        return Task.FromResult<string>(null);
      }
      FirebaseUser user = auth?.CurrentUser;
      return user != null ? user.TokenAsync(forceRefresh) : Task.FromResult<string>(null);
    }

    private readonly FirebaseApp _app;
    private readonly object _stateLock = new object();
    private readonly object _persistLock = new object();
    private AuthUserRecord _currentRecord;
    private FirebaseUser _currentUser;
    private PhoneAuthProvider _phoneAuthProvider;
    private Task<string> _inFlightRefreshTask;
    private Task<string> _inFlightForceRefreshTask;
    private long _sessionEpoch;
    private long _persistSeq;
    private long _lastPersistedSeq;
    private EventHandler _stateChanged;
    private EventHandler _idTokenChanged;
    private volatile string _languageCode = string.Empty;
    private volatile bool _disposed;

    internal IdentityToolkitClient Client { get; }
    internal bool IsDisposed => _disposed;
    internal AuthUserRecord ActiveRecord {
      get {
        lock (_stateLock) return _disposed ? null : _currentRecord;
      }
    }

    private FirebaseAuth(FirebaseApp app) {
      _app = app ?? throw new ArgumentNullException(nameof(app));
      Client = new IdentityToolkitClient(app);

      AuthUserSnapshot persisted = AuthPersistence.LoadUser(app.Name, app.Options?.ApiKey);
      if (persisted != null) {
        _currentRecord = new AuthUserRecord(persisted);
        _currentUser = new FirebaseUser(this, _currentRecord);
        if (!string.IsNullOrEmpty(persisted.RefreshToken) &&
            persisted.ExpiresAtUnixSeconds <= NowUnixSeconds() + ProactiveRefreshBufferSeconds) {
          _ = RefreshPersistedOnStartupAsync(_currentRecord);
        }
      }
    }

    private async Task RefreshPersistedOnStartupAsync(AuthUserRecord record) {
      try {
        await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      } catch (Exception) {
        // Offline or unconfigured startup refresh is non-fatal; terminal invalidation is handled inside GetOrRefreshTokenAsync.
      }
    }

    /// @brief Returns the FirebaseAuth associated with
    /// FirebaseApp.DefaultInstance.  FirebaseAuth will be created if required.
    ///
    /// @throw InitializationException Thrown with the invalid InitResult if
    /// default FirebaseApp initialization failed.
    public static FirebaseAuth DefaultInstance => GetAuth(FirebaseApp.DefaultInstance);

    /// @brief Returns the FirebaseAuth object for an App. Creates the
    /// FirebaseAuth if required.
    ///
    /// @param[in] app The FirebaseApp to use for the FirebaseAuth object.
    public static FirebaseAuth GetAuth(FirebaseApp app) {
      if (app == null) throw new ArgumentNullException(nameof(app));
      RegisterInteropProvider();
      AuthSlot slot = Instances.GetOrCreate(app, () => new AuthSlot());
      lock (slot.Sync) {
        if (slot.Evicted) throw new ObjectDisposedException(nameof(FirebaseApp));
        if (slot.Current == null || slot.Current.IsDisposed) {
          slot.Current = new FirebaseAuth(app);
        }
        return slot.Current;
      }
    }

    /// @brief Get the FirebaseApp associated with this object.
    ///
    /// @return FirebaseApp associated with this object.
    public FirebaseApp App => _app;

    /// @brief Synchronously gets the cached current user, or null if there is none.
    public FirebaseUser CurrentUser {
      get {
        lock (_stateLock) return _disposed ? null : _currentUser;
      }
    }

    /// The user-facing language code for auth operations that can be internationalized, such as
    /// FirebaseUser.SendEmailVerificationAsync(). This language code should follow the conventions
    /// defined by the IETF in BCP 47. Returns an empty string if the app default language code is
    /// being used.
    public string LanguageCode {
      get => _languageCode;
      set => _languageCode = value ?? string.Empty;
    }

    /// <summary>
    /// Sets the user-facing language code to be the default app language, which
    /// uses the Firebase service's default. You may subsequently customize the
    /// language code again by setting <see cref="LanguageCode" />.
    /// </summary>
    public void UseAppLanguage() => _languageCode = string.Empty;

    /// <summary>
    ///   Configures the Auth SDK to use an emulated backend instead of
    ///   the default remote backend.
    /// </summary>
    /// <param name="host">
    ///   The host that the auth emulator is running on.
    /// </param>
    /// <param name="port">
    ///   The port that the auth emulator is running on.
    /// </param>
    public void UseEmulator(string host, int port) {
      ThrowIfDisposed();
      Client.SetEmulator(host, port);
    }

    /// @brief Event raised on changes in the authentication state.
    ///
    /// Authentication state changes are:
    ///   - Right after the listener has been registered
    ///   - When a user signs in
    ///   - When the current user signs out
    ///   - When the current user changes
    ///
    /// It is a recommended practice to always listen to sign-out events, as you
    /// may want to prompt the user to sign in again and maybe restrict the
    /// information or actions they have access to.
    public event EventHandler StateChanged {
      add {
        if (value == null) return;
        bool shouldNotify;
        lock (_stateLock) {
          _stateChanged += value;
          shouldNotify = !_disposed;
        }
        if (shouldNotify) DispatchSingleSubscriber(value, isStateEvent: true);
      }
      remove {
        lock (_stateLock) _stateChanged -= value;
      }
    }

    /// @brief Event raised on ID token changes.
    ///
    /// Authentication ID token changes are:
    ///   - Right after the listener has been registered
    ///   - When a user signs in
    ///   - When the current user signs out
    ///   - When the current user changes
    ///   - When there is a change in the current user's token
    public event EventHandler IdTokenChanged {
      add {
        if (value == null) return;
        bool shouldNotify;
        lock (_stateLock) {
          _idTokenChanged += value;
          shouldNotify = !_disposed;
        }
        if (shouldNotify) DispatchSingleSubscriber(value, isStateEvent: false);
      }
      remove {
        lock (_stateLock) _idTokenChanged -= value;
      }
    }

    /// <summary>
    /// Removes any existing authentication credentials from this client.
    /// This function always succeeds.
    /// </summary>
    public void SignOut() {
      if (_disposed) return;
      bool hadUser = false;
      long persistSeq;

      lock (_stateLock) {
        if (_disposed) return;
        persistSeq = ++_persistSeq;
        if (_currentRecord != null) {
          hadUser = true;
          _currentRecord = null;
          _currentUser = null;
          _inFlightRefreshTask = null;
          _inFlightForceRefreshTask = null;
          _sessionEpoch++;
        }
      }

      PersistSnapshotOrdered(persistSeq, null);
      if (hadUser) {
        DispatchRegisteredHandlers(isStateEvent: true);
        DispatchRegisteredHandlers(isStateEvent: false);
      }
    }

    /// Creates, and on success, logs in a user with the given email address
    /// and password.
    ///
    /// An error is returned when account creation is unsuccessful
    /// (due to another existing account, invalid password, etc.).
    public async Task<AuthResult> CreateUserWithEmailAndPasswordAsync(string email, string password) {
      ThrowIfDisposed();
      ValidateEmailAndPassword(email, password);
      var req = new Dictionary<string, object> { ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("signUp", req).ConfigureAwait(false);
      return await CompleteSignInAsync(resp, isAnonymousHint: false, EmailAuthProvider.ProviderId, null).ConfigureAwait(false);
    }

    /// Signs in using provided email address and password.
    /// An error is returned if the password is wrong or otherwise not accepted
    /// by the server.
    public async Task<AuthResult> SignInWithEmailAndPasswordAsync(string email, string password) {
      ThrowIfDisposed();
      ValidateEmailAndPassword(email, password);
      var req = new Dictionary<string, object> { ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("signInWithPassword", req).ConfigureAwait(false);
      return await CompleteSignInAsync(resp, isAnonymousHint: false, EmailAuthProvider.ProviderId, null).ConfigureAwait(false);
    }

    /// Asynchronously creates and becomes an anonymous user.
    /// If there is already an anonymous user signed in, that user will be
    /// returned instead.
    /// If there is any other existing user, that user will be signed out.
    ///
    /// @code{.cs}
    ///  void SignIn(Firebase.Auth.FirebaseAuth auth) {
    ///    auth.SignInAnonymouslyAsync().ContinueWith((authTask) => {
    ///      if (authTask.IsCanceled) {
    ///        DebugLog("Anonymous sign in canceled.");
    ///      } else if (authTask.IsFaulted) {
    ///        DebugLog("Anonymous sign in encountered an error.");
    ///        DebugLog(authTask.Exception.ToString());
    ///      } else if (authTask.IsCompleted) {
    ///        DebugLog("Anonymous sign in successful!");
    ///      }
    ///    });
    ///  }
    /// @endcode
    public async Task<AuthResult> SignInAnonymouslyAsync() {
      ThrowIfDisposed();
      FirebaseUser existing = CurrentUser;
      if (existing != null && existing.IsAnonymous && existing.IsValid()) {
        return new AuthResult(this, existing, new AdditionalUserInfo(), credential: null);
      }
      var req = new Dictionary<string, object> { ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("signUp", req).ConfigureAwait(false);
      return await CompleteSignInAsync(resp, isAnonymousHint: true, providerId: null, credential: null).ConfigureAwait(false);
    }

    /// Asynchronously logs into Firebase with the given Auth token.
    ///
    /// An error is returned, if the token is invalid, expired or otherwise
    /// not accepted by the server.
    public async Task<AuthResult> SignInWithCustomTokenAsync(string token) {
      ThrowIfDisposed();
      if (string.IsNullOrEmpty(token)) throw AuthErrorMapper.CreateException(AuthError.InvalidCustomToken);
      var req = new Dictionary<string, object> { ["token"] = token, ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("signInWithCustomToken", req).ConfigureAwait(false);
      return await CompleteSignInAsync(resp, isAnonymousHint: false, providerId: null, credential: null).ConfigureAwait(false);
    }

    /// @brief Asynchronously logs into Firebase with the given credentials.
    ///
    /// Convenience method for <see cref="SignInAndRetrieveDataWithCredentialAsync" />
    /// that doesn't return additional identity provider data.
    public async Task<FirebaseUser> SignInWithCredentialAsync(Credential credential) =>
        (await SignInAndRetrieveDataWithCredentialAsync(credential).ConfigureAwait(false)).User;

    /// Asynchronously logs into Firebase with the given credentials.
    ///
    /// For example, the credential could wrap a Facebook login access token or
    /// a Twitter token/token-secret pair.
    ///
    /// @ref AuthResult contains both a reference to the @ref FirebaseUser and
    /// @ref AdditionalUserInfo, which holds details specific to the Identity
    /// Provider used to sign in.
    ///
    /// An error is returned if the credential is invalid, expired, or otherwise
    /// not accepted by the server.
    public async Task<AuthResult> SignInAndRetrieveDataWithCredentialAsync(Credential credential) {
      ThrowIfDisposed();
      ValidateCredentialOrThrow(credential);
      (Dictionary<string, object> resp, AdditionalUserInfo addInfo, Credential outCred) =
          await ExecuteCredentialSignInOrLinkAsync(credential, linkIdToken: null, isReauth: false, linkRecord: null).ConfigureAwait(false);
      AuthUserSnapshot snapshot = await BuildSignedInSnapshotAsync(resp, isAnonymousHint: false).ConfigureAwait(false);
      FirebaseUser user = ApplyAuthenticatedSnapshot(snapshot);
      return new AuthResult(this, user, addInfo, outCred);
    }

    /// Sign-in a user authenticated via a federated auth provider.
    ///
    /// @param[in] provider Contains information on the provider to authenticate
    /// with.
    ///
    /// @return A Task with the result of the sign-in request.
    ///
    /// @note: This operation is supported only on Android platforms (when the
    /// native Firebase Auth SDK is linked and not using the emulator). On other
    /// platforms this method will fail with AuthError.Unimplemented.
    public async Task<AuthResult> SignInWithProviderAsync(FederatedAuthProvider provider) {
      ThrowIfDisposed();
      Credential credential = await MobileAuthPlatform.TryInteractiveFederatedAuthAsync(this, provider).ConfigureAwait(false);
      return await SignInAndRetrieveDataWithCredentialAsync(credential).ConfigureAwait(false);
    }

    /// Asynchronously requests the IDPs (identity providers) that can be used
    /// for the given email address.
    ///
    /// Useful for an "identifier-first" login flow.
    ///
    /// @code{.cs}
    ///  // Print out all available providers for a given email.
    ///  void DisplayIdentityProviders(Firebase.Auth.FirebaseAuth auth,
    ///                                String email) {
    ///    auth.FetchProvidersForEmailAsync(email).ContinueWith((authTask) => {
    ///      if (authTask.IsCanceled) {
    ///        DebugLog("Provider fetch canceled.");
    ///      } else if (authTask.IsFaulted) {
    ///        DebugLog("Provider fetch encountered an error.");
    ///        DebugLog(authTask.Exception.ToString());
    ///      } else if (authTask.IsCompleted) {
    ///        DebugLog("Email Providers:");
    ///        foreach (string provider in authTask.Result) {
    ///          DebugLog(provider);
    ///        }
    ///      }
    ///    });
    ///  }
    /// @endcode
    public async Task<IEnumerable<string>> FetchProvidersForEmailAsync(string email) {
      ThrowIfDisposed();
      if (string.IsNullOrEmpty(email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      var req = new Dictionary<string, object> { ["identifier"] = email, ["continueUri"] = DefaultRequestUri };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("createAuthUri", req).ConfigureAwait(false);
      var result = new List<string>();
      List<object> rawList = JsonHelper.GetList(resp, "allProviders") ?? JsonHelper.GetList(resp, "signinMethods");
      if (rawList != null) {
        foreach (object item in rawList) {
          if (item is string s && s.Length > 0) result.Add(s);
        }
      }
      return result;
    }

    /// <summary>
    /// Initiates a password reset for the given email address.
    /// </summary>
    /// <remarks>
    /// If the email address is not registered, then the returned task has a
    /// status of IsFaulted.
    ///
    /// <code>
    ///  void ResetPassword(string email) {
    ///    auth.SendPasswordResetEmailAsync(email).ContinueWith((authTask) => {
    ///      if (authTask.IsCanceled) {
    ///        DebugLog("Password reset was canceled.");
    ///      } else if (authTask.IsFaulted) {
    ///        DebugLog("Password reset encountered an error.");
    ///        DebugLog(authTask.Exception.ToString());
    ///      } else if (authTask.IsCompleted) {
    ///        DebugLog("Password reset successful!");
    ///      }
    ///    });
    ///  }
    /// </code>
    /// </remarks>
    public async Task SendPasswordResetEmailAsync(string email) {
      ThrowIfDisposed();
      if (string.IsNullOrEmpty(email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      var req = new Dictionary<string, object> { ["requestType"] = "PASSWORD_RESET", ["email"] = email };
      await ExecuteAccountsRpcAsync("sendOobCode", req).ConfigureAwait(false);
    }

    internal async Task<AuthResult> SignInWithEmailLinkInternalAsync(string email, string oobCode) {
      ThrowIfDisposed();
      if (string.IsNullOrEmpty(email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      if (string.IsNullOrEmpty(oobCode)) throw AuthErrorMapper.CreateException(AuthError.InvalidActionCode);
      var req = new Dictionary<string, object> { ["email"] = email, ["oobCode"] = oobCode };
      Dictionary<string, object> resp = await ExecuteAccountsRpcAsync("signInWithEmailLink", req).ConfigureAwait(false);
      return await CompleteSignInAsync(resp, isAnonymousHint: false, EmailAuthProvider.ProviderId, null).ConfigureAwait(false);
    }

    internal PhoneAuthProvider GetOrCreatePhoneAuthProvider() {
      lock (_stateLock) {
        ThrowIfDisposed();
        return _phoneAuthProvider ??= new PhoneAuthProvider(this);
      }
    }

    internal async Task<string> GetOrRefreshTokenAsync(AuthUserRecord record, bool forceRefresh) {
      EnsureActiveRecord(record);
      AuthUserSnapshot current = record.Snapshot;
      if (!forceRefresh && !string.IsNullOrEmpty(current.IdToken) &&
          current.ExpiresAtUnixSeconds > NowUnixSeconds() + ProactiveRefreshBufferSeconds) {
        return current.IdToken;
      }

      Task<string> taskToAwait;
      lock (_stateLock) {
        EnsureActiveRecordLocked(record);
        current = record.Snapshot;
        if (!forceRefresh && !string.IsNullOrEmpty(current.IdToken) &&
            current.ExpiresAtUnixSeconds > NowUnixSeconds() + ProactiveRefreshBufferSeconds) {
          return current.IdToken;
        }

        Task<string> existingFlight = forceRefresh
            ? _inFlightForceRefreshTask
            : (_inFlightRefreshTask ?? _inFlightForceRefreshTask);
        if (existingFlight != null) {
          taskToAwait = existingFlight;
        } else {
          long epochAtStart = _sessionEpoch;
          var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
          taskToAwait = tcs.Task;
          if (forceRefresh) _inFlightForceRefreshTask = taskToAwait;
          else _inFlightRefreshTask = taskToAwait;
          _ = RunRefreshFlightAsync(record, current.RefreshToken, forceRefresh, epochAtStart, tcs);
        }
      }
      return await taskToAwait.ConfigureAwait(false);
    }

    private async Task RunRefreshFlightAsync(
        AuthUserRecord record, string refreshToken, bool forceRefresh, long epochAtStart, TaskCompletionSource<string> tcs) {
      try {
        Dictionary<string, object> resp = await Client.RefreshTokenAsync(refreshToken).ConfigureAwait(false);
        string newIdToken = FirstNonEmpty(JsonHelper.GetString(resp, "id_token"), JsonHelper.GetString(resp, "idToken"));
        string newRefreshToken = FirstNonEmpty(
            JsonHelper.GetString(resp, "refresh_token"), JsonHelper.GetString(resp, "refreshToken"), refreshToken);
        long expiresIn = ParseLongFlexible(resp, "expires_in", ParseLongFlexible(resp, "expiresIn", DefaultTokenLifetimeSeconds));
        long expiresAt = ComputeExpiresAtUnixSeconds(newIdToken, expiresIn);
        if (string.IsNullOrEmpty(newIdToken)) throw AuthErrorMapper.CreateException(AuthError.Failure);

        bool tokenChanged;
        long persistSeq;
        AuthUserSnapshot updated;
        lock (_stateLock) {
          ClearFlightLocked(forceRefresh, tcs.Task);
          if (_disposed || _sessionEpoch != epochAtStart || !ReferenceEquals(_currentRecord, record)) {
            tcs.TrySetException(AuthErrorMapper.CreateException(AuthError.NoSignedInUser));
            return;
          }
          string prevToken = record.Snapshot.IdToken;
          updated = record.Snapshot.WithTokens(newIdToken, newRefreshToken, expiresAt);
          record.Snapshot = updated;
          persistSeq = ++_persistSeq;
          tokenChanged = !string.Equals(prevToken, newIdToken, StringComparison.Ordinal);
        }

        PersistSnapshotOrdered(persistSeq, updated);
        if (tokenChanged) DispatchRegisteredHandlers(isStateEvent: false);
        tcs.TrySetResult(newIdToken);
      } catch (AuthBackendErrorException backendEx) {
        ClearFlightSafe(forceRefresh, tcs.Task);
        if (AuthErrorMapper.IsTerminalSessionInvalidation(backendEx.ErrorCode)) SignOutIfMatchingRecord(record);
        tcs.TrySetException(AuthErrorMapper.CreateException(backendEx.ErrorCode));
      } catch (FirebaseException fbEx) {
        ClearFlightSafe(forceRefresh, tcs.Task);
        if (AuthErrorMapper.IsTerminalSessionInvalidation((AuthError)fbEx.ErrorCode)) SignOutIfMatchingRecord(record);
        tcs.TrySetException(fbEx);
      } catch (Exception ex) {
        ClearFlightSafe(forceRefresh, tcs.Task);
        tcs.TrySetException(AuthErrorMapper.CreateException(AuthError.Failure, ex));
      }
    }

    private void ClearFlightSafe(bool forceRefresh, Task<string> flightTask) {
      lock (_stateLock) ClearFlightLocked(forceRefresh, flightTask);
    }

    private void ClearFlightLocked(bool forceRefresh, Task<string> flightTask) {
      if (forceRefresh && ReferenceEquals(_inFlightForceRefreshTask, flightTask)) _inFlightForceRefreshTask = null;
      else if (!forceRefresh && ReferenceEquals(_inFlightRefreshTask, flightTask)) _inFlightRefreshTask = null;
    }

    internal async Task<AuthResult> LinkUserWithCredentialAsync(AuthUserRecord record, Credential credential) {
      EnsureActiveRecord(record);
      ValidateCredentialOrThrow(credential);
      if (HasProviderLinked(record.Snapshot, credential.ProviderInternal)) {
        throw AuthErrorMapper.CreateException(AuthError.ProviderAlreadyLinked);
      }
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      (Dictionary<string, object> resp, AdditionalUserInfo addInfo, Credential outCred) =
          await ExecuteCredentialSignInOrLinkAsync(credential, linkIdToken: idToken, isReauth: false, linkRecord: record)
              .ConfigureAwait(false);
      try {
        FirebaseUser user = await CompleteUserMutationAsync(record, idToken, resp, isAnon: false).ConfigureAwait(false);
        return new AuthResult(this, user, addInfo, outCred);
      } catch (AuthBackendErrorException backendEx) {
        if (AuthErrorMapper.IsTerminalSessionInvalidation(backendEx.ErrorCode)) SignOutIfMatchingRecord(record);
        throw AuthErrorMapper.CreateException(backendEx.ErrorCode);
      }
    }

    internal async Task<AuthResult> ReauthenticateUserAsync(AuthUserRecord record, Credential credential) {
      EnsureActiveRecord(record);
      ValidateCredentialOrThrow(credential);
      string expectedUid = record.Snapshot.Uid;
      (Dictionary<string, object> resp, AdditionalUserInfo addInfo, Credential outCred) =
          await ExecuteCredentialSignInOrLinkAsync(credential, linkIdToken: null, isReauth: true, linkRecord: null)
              .ConfigureAwait(false);

      string returnedUid = JsonHelper.GetString(resp, "localId", string.Empty);
      if (!string.IsNullOrEmpty(returnedUid) && !string.Equals(returnedUid, expectedUid, StringComparison.Ordinal)) {
        throw AuthErrorMapper.CreateException(AuthError.UserMismatch);
      }
      FirebaseUser user = await CompleteUserMutationAsync(
          record, record.Snapshot.IdToken, resp, record.Snapshot.IsAnonymous).ConfigureAwait(false);
      return new AuthResult(this, user, addInfo, outCred);
    }

    internal async Task<AuthResult> UnlinkUserProviderAsync(AuthUserRecord record, string provider) {
      EnsureActiveRecord(record);
      if (string.IsNullOrEmpty(provider) || !HasProviderLinked(record.Snapshot, provider)) {
        throw AuthErrorMapper.CreateException(AuthError.NoSuchProvider);
      }
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> { ["idToken"] = idToken, ["deleteProvider"] = new List<object> { provider } };
      await ExecuteAccountsRpcForRecordAsync(record, "update", req).ConfigureAwait(false);
      AuthUserSnapshot reloaded = await LookupSnapshotAsync(
          idToken, record.Snapshot.RefreshToken, record.Snapshot.ExpiresAtUnixSeconds,
          isAnonymousHint: false, fallbackAuthResp: null, requireNonEmptyUsers: true).ConfigureAwait(false);
      FirebaseUser user = UpdateExistingRecordSnapshot(record, reloaded);
      return new AuthResult(this, user, new AdditionalUserInfo(provider), credential: null);
    }

    internal async Task<FirebaseUser> UpdateUserPhoneCredentialAsync(AuthUserRecord record, PhoneAuthCredential credential) {
      EnsureActiveRecord(record);
      if (credential == null || !credential.IsValid()) throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      Dictionary<string, object> resp = await ExecuteAccountsRpcForRecordAsync(
          record, "signInWithPhoneNumber", BuildPhonePayload(credential, idToken)).ConfigureAwait(false);
      return await CompleteUserMutationAsync(record, idToken, resp, isAnon: false).ConfigureAwait(false);
    }

    internal async Task UpdateUserProfileInternalAsync(AuthUserRecord record, UserProfile profile) {
      EnsureActiveRecord(record);
      if (profile == null) throw new ArgumentNullException(nameof(profile));
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> { ["idToken"] = idToken, ["returnSecureToken"] = true };
      var deleteAttrs = new List<object>();
      if (profile.HasDisplayName) {
        if (string.IsNullOrEmpty(profile.DisplayName)) deleteAttrs.Add("DISPLAY_NAME");
        else req["displayName"] = profile.DisplayName;
      }
      if (profile.HasPhotoUrl) {
        if (string.IsNullOrEmpty(profile.PhotoUrlString)) deleteAttrs.Add("PHOTO_URL");
        else req["photoUrl"] = profile.PhotoUrlString;
      }
      if (deleteAttrs.Count > 0) req["deleteAttribute"] = deleteAttrs;
      Dictionary<string, object> resp = await ExecuteAccountsRpcForRecordAsync(record, "update", req).ConfigureAwait(false);
      await CompleteUserMutationAsync(record, idToken, resp, record.Snapshot.IsAnonymous).ConfigureAwait(false);
    }

    internal async Task UpdateUserPasswordInternalAsync(AuthUserRecord record, string password) {
      EnsureActiveRecord(record);
      if (string.IsNullOrEmpty(password)) throw AuthErrorMapper.CreateException(AuthError.MissingPassword);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> { ["idToken"] = idToken, ["password"] = password, ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcForRecordAsync(record, "update", req).ConfigureAwait(false);
      await CompleteUserMutationAsync(record, idToken, resp, isAnon: false).ConfigureAwait(false);
    }

    internal async Task UpdateUserEmailInternalAsync(AuthUserRecord record, string email) {
      EnsureActiveRecord(record);
      if (string.IsNullOrEmpty(email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> { ["idToken"] = idToken, ["email"] = email, ["returnSecureToken"] = true };
      Dictionary<string, object> resp = await ExecuteAccountsRpcForRecordAsync(record, "update", req).ConfigureAwait(false);
      await CompleteUserMutationAsync(record, idToken, resp, isAnon: false).ConfigureAwait(false);
    }

    internal async Task SendEmailVerificationInternalAsync(AuthUserRecord record) {
      EnsureActiveRecord(record);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> { ["requestType"] = "VERIFY_EMAIL", ["idToken"] = idToken };
      await ExecuteAccountsRpcForRecordAsync(record, "sendOobCode", req).ConfigureAwait(false);
    }

    internal async Task SendVerifyBeforeUpdateEmailInternalAsync(AuthUserRecord record, string newEmail) {
      EnsureActiveRecord(record);
      if (string.IsNullOrEmpty(newEmail)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      var req = new Dictionary<string, object> {
        ["requestType"] = "VERIFY_AND_CHANGE_EMAIL", ["idToken"] = idToken, ["newEmail"] = newEmail,
      };
      await ExecuteAccountsRpcForRecordAsync(record, "sendOobCode", req).ConfigureAwait(false);
    }

    internal async Task ReloadUserInternalAsync(AuthUserRecord record) {
      EnsureActiveRecord(record);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      try {
        AuthUserSnapshot reloaded = await LookupSnapshotAsync(
            idToken, record.Snapshot.RefreshToken, record.Snapshot.ExpiresAtUnixSeconds,
            record.Snapshot.IsAnonymous, fallbackAuthResp: null, requireNonEmptyUsers: true).ConfigureAwait(false);
        UpdateExistingRecordSnapshot(record, reloaded);
      } catch (AuthBackendErrorException backendEx) {
        if (AuthErrorMapper.IsTerminalSessionInvalidation(backendEx.ErrorCode)) SignOutIfMatchingRecord(record);
        throw AuthErrorMapper.CreateException(backendEx.ErrorCode);
      } catch (FirebaseException fbEx) {
        if (AuthErrorMapper.IsTerminalSessionInvalidation((AuthError)fbEx.ErrorCode)) SignOutIfMatchingRecord(record);
        throw;
      }
    }

    internal async Task DeleteUserInternalAsync(AuthUserRecord record) {
      EnsureActiveRecord(record);
      string idToken = await GetOrRefreshTokenAsync(record, forceRefresh: false).ConfigureAwait(false);
      await ExecuteAccountsRpcForRecordAsync(
          record, "delete", new Dictionary<string, object> { ["idToken"] = idToken }).ConfigureAwait(false);
      SignOutIfMatchingRecord(record);
    }

    private async Task<AuthResult> CompleteSignInAsync(
        Dictionary<string, object> resp, bool isAnonymousHint, string providerId, Credential credential) {
      AuthUserSnapshot snapshot = await BuildSignedInSnapshotAsync(resp, isAnonymousHint).ConfigureAwait(false);
      FirebaseUser user = ApplyAuthenticatedSnapshot(snapshot);
      return new AuthResult(this, user, new AdditionalUserInfo(providerId), credential);
    }

    private async Task<FirebaseUser> CompleteUserMutationAsync(
        AuthUserRecord record, string fallbackIdToken, Dictionary<string, object> resp, bool isAnon) {
      AuthUserSnapshot updated = await BuildSignedInSnapshotAsync(
          resp, isAnon, record.Snapshot.RefreshToken, fallbackIdToken).ConfigureAwait(false);
      return UpdateExistingRecordSnapshot(record, updated);
    }

    private async Task<Dictionary<string, object>> ExecuteCredentialRpcAsync(
        string endpointMethod,
        Dictionary<string, object> req,
        bool isLink,
        AuthUserRecord linkRecord,
        Credential credential) {
      try {
        return await Client.PostAccountsAsync(endpointMethod, req, _languageCode).ConfigureAwait(false);
      } catch (AuthBackendErrorException backendEx) {
        if (isLink && linkRecord != null && AuthErrorMapper.IsTerminalSessionInvalidation(backendEx.ErrorCode)) {
          SignOutIfMatchingRecord(linkRecord);
        }
        if (isLink && IsAccountLinkConflict(backendEx.ErrorCode)) {
          throw CreateAccountLinkException(backendEx.ErrorCode, backendEx.ResponseMap, credential);
        }
        throw AuthErrorMapper.CreateException(backendEx.ErrorCode);
      }
    }

    private async Task<(Dictionary<string, object> Resp, AdditionalUserInfo Info, Credential OutCred)>
        ExecuteCredentialSignInOrLinkAsync(
            Credential credential, string linkIdToken, bool isReauth, AuthUserRecord linkRecord) {
      bool isLink = !string.IsNullOrEmpty(linkIdToken);
      switch (credential.Kind) {
        case CredentialKind.EmailPassword: {
          ValidateEmailAndPassword(credential.Email, credential.Password);
          var req = new Dictionary<string, object> {
            ["email"] = credential.Email, ["password"] = credential.Password, ["returnSecureToken"] = true,
          };
          if (isLink) req["idToken"] = linkIdToken;
          Dictionary<string, object> resp = await ExecuteCredentialRpcAsync(
              isLink ? "update" : "signInWithPassword", req, isLink, linkRecord, credential).ConfigureAwait(false);
          return (resp, new AdditionalUserInfo(EmailAuthProvider.ProviderId), credential.Clone());
        }

        case CredentialKind.EmailLink: {
          if (string.IsNullOrEmpty(credential.Email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
          if (string.IsNullOrEmpty(credential.OobCode)) throw AuthErrorMapper.CreateException(AuthError.InvalidActionCode);
          var req = new Dictionary<string, object> { ["email"] = credential.Email, ["oobCode"] = credential.OobCode };
          if (isLink) req["idToken"] = linkIdToken;
          Dictionary<string, object> resp = await ExecuteCredentialRpcAsync(
              "signInWithEmailLink", req, isLink, linkRecord, credential).ConfigureAwait(false);
          return (resp, new AdditionalUserInfo(EmailAuthProvider.ProviderId), credential.Clone());
        }

        case CredentialKind.Phone: {
          Dictionary<string, object> resp = await ExecuteCredentialRpcAsync(
              "signInWithPhoneNumber", BuildPhonePayload(credential, linkIdToken), isLink, linkRecord, credential)
              .ConfigureAwait(false);
          return (resp, new AdditionalUserInfo("phone"), credential.Clone());
        }

        case CredentialKind.GameCenter: {
          if (string.IsNullOrEmpty(credential.GameCenterPlayerId) ||
              string.IsNullOrEmpty(credential.GameCenterPublicKeyUrl) ||
              string.IsNullOrEmpty(credential.GameCenterSignature) ||
              string.IsNullOrEmpty(credential.GameCenterSalt)) {
            throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
          }
          var req = new Dictionary<string, object> {
            ["playerId"] = credential.GameCenterPlayerId,
            ["publicKeyUrl"] = credential.GameCenterPublicKeyUrl,
            ["signature"] = credential.GameCenterSignature,
            ["salt"] = credential.GameCenterSalt,
            ["timestamp"] = credential.GameCenterTimestamp,
          };
          if (!string.IsNullOrEmpty(credential.GameCenterDisplayName)) req["displayName"] = credential.GameCenterDisplayName;
          if (!string.IsNullOrEmpty(credential.GameCenterTeamPlayerId)) req["teamPlayerId"] = credential.GameCenterTeamPlayerId;
          if (!string.IsNullOrEmpty(credential.GameCenterGamePlayerId)) req["gamePlayerId"] = credential.GameCenterGamePlayerId;
          if (isLink) req["idToken"] = linkIdToken;
          Dictionary<string, object> resp = await ExecuteCredentialRpcAsync(
              "signInWithGameCenter", req, isLink, linkRecord, credential).ConfigureAwait(false);
          return (resp, new AdditionalUserInfo(GameCenterAuthProvider.ProviderId), credential.Clone());
        }

        case CredentialKind.OAuthAssertion:
        case CredentialKind.PendingToken: {
          ValidateOAuthCredentialOrThrow(credential);
          var req = new Dictionary<string, object> {
            ["requestUri"] = DefaultRequestUri, ["returnSecureToken"] = true, ["returnIdpCredential"] = true,
          };
          if (isLink) req["idToken"] = linkIdToken;
          if (isReauth) req["autoCreate"] = false;
          if (credential.Kind == CredentialKind.PendingToken) req["pendingToken"] = credential.PendingToken;
          else req["postBody"] = credential.BuildIdpPostBody();

          Dictionary<string, object> resp = await ExecuteCredentialRpcAsync(
              "signInWithIdp", req, isLink, linkRecord, credential).ConfigureAwait(false);

          string inlineError = JsonHelper.GetString(resp, "errorMessage");
          bool needConfirmation = JsonHelper.GetBool(resp, "needConfirmation", false);
          if (!string.IsNullOrEmpty(inlineError) || needConfirmation) {
            AuthError conflictCode = !string.IsNullOrEmpty(inlineError)
                ? AuthErrorMapper.MapBackendError(inlineError)
                : AuthError.AccountExistsWithDifferentCredentials;
            if (conflictCode == AuthError.Failure && needConfirmation) {
              conflictCode = AuthError.AccountExistsWithDifferentCredentials;
            }
            if (isLink && IsAccountLinkConflict(conflictCode)) {
              throw CreateAccountLinkException(conflictCode, resp, credential);
            }
            throw AuthErrorMapper.CreateException(conflictCode);
          }

          Credential returnedCred = ExtractIdpCredentialFromResponse(resp, credential) ?? credential.Clone();
          AdditionalUserInfo info = ParseAdditionalUserInfo(resp, credential.ProviderInternal, updatedCredential: null);
          return (resp, info, returnedCred);
        }

        default:
          throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
      }
    }

    private static bool IsAccountLinkConflict(AuthError code) =>
        code == AuthError.CredentialAlreadyInUse ||
        code == AuthError.EmailAlreadyInUse ||
        code == AuthError.AccountExistsWithDifferentCredentials;

    private static FirebaseAccountLinkException CreateAccountLinkException(
        AuthError code, Dictionary<string, object> respMap, Credential originalCredential) {
      Dictionary<string, object> dataMap = respMap;
      if (respMap != null && respMap.TryGetValue("error", out object errObj) &&
          errObj is Dictionary<string, object> nestedErr &&
          !respMap.ContainsKey("oauthIdToken") && !respMap.ContainsKey("oauthAccessToken") &&
          !respMap.ContainsKey("pendingToken")) {
        dataMap = nestedErr;
      }
      Credential updatedCred = ExtractIdpCredentialFromResponse(dataMap, originalCredential) ?? originalCredential.Clone();
      AdditionalUserInfo info = ParseAdditionalUserInfo(dataMap, originalCredential.ProviderInternal, updatedCred);
      var partialResult = new AuthResult(auth: null, user: null, additionalUserInfo: info, credential: updatedCred);
      return new FirebaseAccountLinkException((int)code, AuthErrorMapper.GetMessage(code), partialResult);
    }

    private static Credential ExtractIdpCredentialFromResponse(Dictionary<string, object> map, Credential fallback) {
      if (map == null) return null;
      string providerId = FirstNonEmpty(JsonHelper.GetString(map, "providerId"), fallback?.ProviderInternal);
      string pendingToken = JsonHelper.GetString(map, "pendingToken");
      if (!string.IsNullOrEmpty(pendingToken) && !string.IsNullOrEmpty(providerId)) {
        return Credential.CreatePendingToken(providerId, pendingToken);
      }
      string oauthIdToken = JsonHelper.GetString(map, "oauthIdToken");
      string oauthAccessToken = JsonHelper.GetString(map, "oauthAccessToken");
      string oauthSecret = JsonHelper.GetString(map, "oauthTokenSecret");
      if (!string.IsNullOrEmpty(oauthSecret) && !string.IsNullOrEmpty(oauthAccessToken)) {
        return Credential.CreateTwitter(oauthAccessToken, oauthSecret);
      }
      if (!string.IsNullOrEmpty(oauthIdToken) || !string.IsNullOrEmpty(oauthAccessToken)) {
        return Credential.CreateIdTokenOrAccessToken(
            providerId, oauthIdToken ?? string.Empty, string.Empty, oauthAccessToken ?? string.Empty);
      }
      return null;
    }

    private static AdditionalUserInfo ParseAdditionalUserInfo(
        Dictionary<string, object> map, string fallbackProviderId, Credential updatedCredential) {
      string providerId = FirstNonEmpty(map != null ? JsonHelper.GetString(map, "providerId") : null, fallbackProviderId);
      string rawUserInfo = map != null ? JsonHelper.GetString(map, "rawUserInfo") : null;
      IDictionary<string, object> profile = !string.IsNullOrEmpty(rawUserInfo) ? JsonHelper.ParseObject(rawUserInfo) : null;
      profile ??= new Dictionary<string, object>();

      string userName = map != null ? JsonHelper.GetString(map, "screenName", string.Empty) : string.Empty;
      if (string.IsNullOrEmpty(userName)) {
        if (profile.TryGetValue("login", out object login) && login is string loginStr) userName = loginStr;
        else if (profile.TryGetValue("screen_name", out object sn) && sn is string snStr) userName = snStr;
        else if (profile.TryGetValue("username", out object un) && un is string unStr) userName = unStr;
      }
      return new AdditionalUserInfo(providerId, userName, profile, updatedCredential);
    }

    private static Dictionary<string, object> BuildPhonePayload(Credential credential, string linkIdToken) {
      var req = new Dictionary<string, object>();
      if (!string.IsNullOrEmpty(linkIdToken)) req["idToken"] = linkIdToken;
      if (!string.IsNullOrEmpty(credential.TemporaryProof) && !string.IsNullOrEmpty(credential.PhoneNumber)) {
        req["temporaryProof"] = credential.TemporaryProof;
        req["phoneNumber"] = credential.PhoneNumber;
        return req;
      }
      if (string.IsNullOrEmpty(credential.VerificationId)) throw AuthErrorMapper.CreateException(AuthError.MissingVerificationId);
      if (string.IsNullOrEmpty(credential.VerificationCode)) throw AuthErrorMapper.CreateException(AuthError.MissingVerificationCode);
      req["sessionInfo"] = credential.VerificationId;
      req["code"] = credential.VerificationCode;
      return req;
    }

    private static void ValidateEmailAndPassword(string email, string password) {
      if (string.IsNullOrEmpty(email)) throw AuthErrorMapper.CreateException(AuthError.MissingEmail);
      if (string.IsNullOrEmpty(password)) throw AuthErrorMapper.CreateException(AuthError.MissingPassword);
    }

    private static void ValidateCredentialOrThrow(Credential credential) {
      if (credential == null || !credential.IsValid()) throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
    }

    private static void ValidateOAuthCredentialOrThrow(Credential credential) {
      if (credential.Kind == CredentialKind.PendingToken) {
        if (string.IsNullOrEmpty(credential.PendingToken)) throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
        return;
      }
      if (string.IsNullOrEmpty(credential.ProviderInternal) ||
          (string.IsNullOrEmpty(credential.IdToken) &&
           string.IsNullOrEmpty(credential.AccessToken) &&
           string.IsNullOrEmpty(credential.ServerAuthCode))) {
        throw AuthErrorMapper.CreateException(AuthError.InvalidCredential);
      }
    }

    private static bool HasProviderLinked(AuthUserSnapshot snapshot, string providerId) {
      if (snapshot == null || string.IsNullOrEmpty(providerId)) return false;
      if (string.Equals(providerId, EmailAuthProvider.ProviderId, StringComparison.Ordinal) && snapshot.HasPasswordCredential) {
        return true;
      }
      for (int i = 0; i < snapshot.ProviderInfos.Count; i++) {
        if (string.Equals(snapshot.ProviderInfos[i].ProviderId, providerId, StringComparison.Ordinal)) return true;
      }
      return false;
    }

    private async Task<Dictionary<string, object>> ExecuteAccountsRpcAsync(string endpointMethod, Dictionary<string, object> req) {
      try {
        return await Client.PostAccountsAsync(endpointMethod, req, _languageCode).ConfigureAwait(false);
      } catch (AuthBackendErrorException backendEx) {
        throw AuthErrorMapper.CreateException(backendEx.ErrorCode);
      }
    }

    private async Task<Dictionary<string, object>> ExecuteAccountsRpcForRecordAsync(
        AuthUserRecord record, string endpointMethod, Dictionary<string, object> req) {
      try {
        return await Client.PostAccountsAsync(endpointMethod, req, _languageCode).ConfigureAwait(false);
      } catch (AuthBackendErrorException backendEx) {
        if (AuthErrorMapper.IsTerminalSessionInvalidation(backendEx.ErrorCode)) SignOutIfMatchingRecord(record);
        throw AuthErrorMapper.CreateException(backendEx.ErrorCode);
      }
    }

    private async Task<AuthUserSnapshot> BuildSignedInSnapshotAsync(
        Dictionary<string, object> authResp, bool isAnonymousHint, string fallbackRefreshToken = null, string fallbackIdToken = null) {
      string idToken = FirstNonEmpty(JsonHelper.GetString(authResp, "idToken"), JsonHelper.GetString(authResp, "id_token"), fallbackIdToken);
      string refreshToken = FirstNonEmpty(
          JsonHelper.GetString(authResp, "refreshToken"), JsonHelper.GetString(authResp, "refresh_token"), fallbackRefreshToken);
      long expiresIn = ParseLongFlexible(authResp, "expiresIn", DefaultTokenLifetimeSeconds);
      long expiresAt = ComputeExpiresAtUnixSeconds(idToken, expiresIn);
      if (string.IsNullOrEmpty(idToken)) throw AuthErrorMapper.CreateException(AuthError.Failure);

      return await LookupSnapshotAsync(
          idToken, refreshToken, expiresAt, isAnonymousHint, fallbackAuthResp: authResp, requireNonEmptyUsers: false).ConfigureAwait(false);
    }

    private async Task<AuthUserSnapshot> LookupSnapshotAsync(
        string idToken, string refreshToken, long expiresAtUnixSeconds, bool isAnonymousHint,
        Dictionary<string, object> fallbackAuthResp, bool requireNonEmptyUsers) {
      Dictionary<string, object> lookupResp = await Client.PostAccountsAsync(
          "lookup", new Dictionary<string, object> { ["idToken"] = idToken }, _languageCode).ConfigureAwait(false);
      List<object> users = JsonHelper.GetList(lookupResp, "users");
      Dictionary<string, object> userObj = users != null && users.Count > 0 ? users[0] as Dictionary<string, object> : null;
      if (userObj == null && requireNonEmptyUsers) {
        throw AuthErrorMapper.CreateException(AuthError.UserNotFound);
      }

      string uid = FirstNonEmpty(
          userObj != null ? JsonHelper.GetString(userObj, "localId") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "localId") : null,
          ExtractSubFromJwt(idToken));
      if (string.IsNullOrEmpty(uid)) throw AuthErrorMapper.CreateException(AuthError.Failure);

      string email = FirstNonEmpty(
          userObj != null ? JsonHelper.GetString(userObj, "email") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "email") : null);
      string displayName = FirstNonEmpty(
          userObj != null ? JsonHelper.GetString(userObj, "displayName") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "displayName") : null);
      string photoUrl = FirstNonEmpty(
          userObj != null ? JsonHelper.GetString(userObj, "photoUrl") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "profilePicture") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "photoUrl") : null);
      string phoneNumber = FirstNonEmpty(
          userObj != null ? JsonHelper.GetString(userObj, "phoneNumber") : null,
          fallbackAuthResp != null ? JsonHelper.GetString(fallbackAuthResp, "phoneNumber") : null);

      bool emailVerified = userObj != null && JsonHelper.GetBool(userObj, "emailVerified", false);
      bool hasPassword = userObj != null && !string.IsNullOrEmpty(JsonHelper.GetString(userObj, "passwordHash"));

      var providerInfos = new List<AuthProviderInfoSnapshot>();
      List<object> rawProviders = userObj != null ? JsonHelper.GetList(userObj, "providerUserInfo") : null;
      if (rawProviders != null) {
        foreach (object item in rawProviders) {
          if (item is IDictionary<string, object> pMap) {
            string pId = JsonHelper.GetString(pMap, "providerId", string.Empty);
            if (string.Equals(pId, EmailAuthProvider.ProviderId, StringComparison.Ordinal)) hasPassword = true;
            providerInfos.Add(new AuthProviderInfoSnapshot {
              ProviderId = pId,
              UserId = FirstNonEmpty(
                  JsonHelper.GetString(pMap, "rawId"), JsonHelper.GetString(pMap, "federatedId"), JsonHelper.GetString(pMap, "email"), uid),
              Email = JsonHelper.GetString(pMap, "email", string.Empty),
              DisplayName = JsonHelper.GetString(pMap, "displayName", string.Empty),
              PhotoUrl = JsonHelper.GetString(pMap, "photoUrl", string.Empty),
              PhoneNumber = JsonHelper.GetString(pMap, "phoneNumber", string.Empty),
            });
          }
        }
      } else if (!string.IsNullOrEmpty(email) && !isAnonymousHint) {
        hasPassword = true;
        providerInfos.Add(new AuthProviderInfoSnapshot {
          ProviderId = EmailAuthProvider.ProviderId, UserId = email, Email = email,
          DisplayName = displayName ?? string.Empty, PhotoUrl = photoUrl ?? string.Empty,
        });
      }

      long fallbackNowMs = NowUnixSeconds() * 1000L;
      long createdAtMs = userObj != null ? ParseLongFlexible(userObj, "createdAt", fallbackNowMs) : fallbackNowMs;
      long lastLoginMs = Math.Max(createdAtMs, userObj != null ? ParseLongFlexible(userObj, "lastLoginAt", createdAtMs) : createdAtMs);
      bool isAnonymous = providerInfos.Count == 0 && string.IsNullOrEmpty(email) &&
                         string.IsNullOrEmpty(phoneNumber) && !hasPassword &&
                         (isAnonymousHint || IsJwtAnonymous(idToken));

      return new AuthUserSnapshot(
          uid, email, displayName, photoUrl, phoneNumber, "Firebase", isAnonymous, emailVerified,
          hasPassword, createdAtMs, lastLoginMs, idToken, refreshToken, expiresAtUnixSeconds, providerInfos);
    }

    private FirebaseUser ApplyAuthenticatedSnapshot(AuthUserSnapshot snapshot) {
      bool fireState = false, fireToken = false;
      long persistSeq;
      FirebaseUser resultUser;

      lock (_stateLock) {
        ThrowIfDisposed();
        persistSeq = ++_persistSeq;
        bool sameUid = _currentRecord != null &&
                       string.Equals(_currentRecord.Snapshot.Uid, snapshot.Uid, StringComparison.Ordinal);
        if (sameUid) {
          string prevToken = _currentRecord.Snapshot.IdToken;
          _currentRecord.Snapshot = snapshot;
          resultUser = _currentUser;
          fireToken = !string.Equals(prevToken, snapshot.IdToken, StringComparison.Ordinal);
        } else {
          _currentRecord = new AuthUserRecord(snapshot);
          _currentUser = new FirebaseUser(this, _currentRecord);
          _inFlightRefreshTask = null;
          _inFlightForceRefreshTask = null;
          _sessionEpoch++;
          resultUser = _currentUser;
          fireState = true;
          fireToken = true;
        }
      }

      PersistSnapshotOrdered(persistSeq, snapshot);
      if (fireState) DispatchRegisteredHandlers(isStateEvent: true);
      if (fireToken) DispatchRegisteredHandlers(isStateEvent: false);
      return resultUser;
    }

    private FirebaseUser UpdateExistingRecordSnapshot(AuthUserRecord record, AuthUserSnapshot updated) {
      bool fireToken;
      long persistSeq;
      FirebaseUser user;
      lock (_stateLock) {
        EnsureActiveRecordLocked(record);
        persistSeq = ++_persistSeq;
        string prevToken = record.Snapshot.IdToken;
        record.Snapshot = updated;
        user = _currentUser;
        fireToken = !string.Equals(prevToken, updated.IdToken, StringComparison.Ordinal);
      }
      PersistSnapshotOrdered(persistSeq, updated);
      if (fireToken) DispatchRegisteredHandlers(isStateEvent: false);
      return user;
    }

    private void SignOutIfMatchingRecord(AuthUserRecord expectedRecord) {
      bool cleared = false;
      long persistSeq = 0;
      lock (_stateLock) {
        if (!_disposed && ReferenceEquals(_currentRecord, expectedRecord)) {
          _currentRecord = null;
          _currentUser = null;
          _inFlightRefreshTask = null;
          _inFlightForceRefreshTask = null;
          _sessionEpoch++;
          persistSeq = ++_persistSeq;
          cleared = true;
        }
      }
      if (cleared) {
        PersistSnapshotOrdered(persistSeq, null);
        DispatchRegisteredHandlers(isStateEvent: true);
        DispatchRegisteredHandlers(isStateEvent: false);
      }
    }

    private void PersistSnapshotOrdered(long seq, AuthUserSnapshot snapshot) {
      lock (_persistLock) {
        if (seq < _lastPersistedSeq) return;
        _lastPersistedSeq = seq;
        if (snapshot == null) {
          AuthPersistence.DeleteUser(_app.Name, _app.Options?.ApiKey);
        } else {
          AuthPersistence.SaveUser(_app.Name, _app.Options?.ApiKey, snapshot);
        }
      }
    }

    internal void EnsureActiveRecord(AuthUserRecord record) {
      lock (_stateLock) EnsureActiveRecordLocked(record);
    }

    private void EnsureActiveRecordLocked(AuthUserRecord record) {
      if (_disposed || record == null || !ReferenceEquals(_currentRecord, record)) {
        throw AuthErrorMapper.CreateException(AuthError.NoSignedInUser);
      }
    }

    private void ThrowIfDisposed() {
      if (_disposed) throw new NullReferenceException("FirebaseAuth instance has been disposed.");
    }

    private void DispatchSingleSubscriber(EventHandler handler, bool isStateEvent) {
      if (handler == null || _disposed) return;
      DispatchToMainThread(() => {
        bool active;
        lock (_stateLock) {
          EventHandler current = isStateEvent ? _stateChanged : _idTokenChanged;
          active = !_disposed && IsHandlerSubscribed(current, handler);
        }
        if (active) ExceptionAggregator.Wrap(() => handler(this, EventArgs.Empty));
      });
    }

    private void DispatchRegisteredHandlers(bool isStateEvent) {
      if (_disposed) return;
      DispatchToMainThread(() => {
        Delegate[] list;
        lock (_stateLock) {
          EventHandler current = isStateEvent ? _stateChanged : _idTokenChanged;
          if (_disposed || current == null) return;
          list = current.GetInvocationList();
        }
        for (int i = 0; i < list.Length; i++) {
          EventHandler single = (EventHandler)list[i];
          bool active;
          lock (_stateLock) {
            EventHandler current = isStateEvent ? _stateChanged : _idTokenChanged;
            active = !_disposed && IsHandlerSubscribed(current, single);
          }
          if (active) ExceptionAggregator.Wrap(() => single(this, EventArgs.Empty));
        }
      });
    }

    private static bool IsHandlerSubscribed(EventHandler multicast, EventHandler target) {
      if (multicast == null || target == null) return false;
      Delegate[] current = multicast.GetInvocationList();
      for (int i = 0; i < current.Length; i++) {
        if (current[i].Equals(target)) return true;
      }
      return false;
    }

    internal static void DispatchToMainThread(Action callback) {
      if (callback == null) return;
      Action<Action> custom = CustomMainThreadDispatcher;
      if (custom != null) {
        custom(callback);
        return;
      }
      _ = Firebase.Platform.FirebaseHandler.RunOnMainThreadAsync(() => {
        ExceptionAggregator.Wrap(callback);
        return true;
      });
    }

    internal static long NowUnixSeconds() =>
        UtcNowUnixSecondsOverride != null ? UtcNowUnixSecondsOverride() : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static long ComputeExpiresAtUnixSeconds(string jwt, long expiresInSeconds) {
      long jwtExp = IdTokenResult.TryExtractExpUnixSeconds(jwt);
      if (jwtExp > 0L) return jwtExp;
      return NowUnixSeconds() + (expiresInSeconds > 0L ? expiresInSeconds : DefaultTokenLifetimeSeconds);
    }

    private static string ExtractSubFromJwt(string jwt) {
      if (IdTokenResult.TryParseJwt(jwt, out IdTokenResult parsed) && parsed.Claims != null) {
        if (parsed.Claims.TryGetValue("user_id", out object uid) && uid is string uidStr && uidStr.Length > 0) return uidStr;
        if (parsed.Claims.TryGetValue("sub", out object sub) && sub is string subStr && subStr.Length > 0) return subStr;
      }
      return string.Empty;
    }

    private static bool IsJwtAnonymous(string jwt) =>
        IdTokenResult.TryParseJwt(jwt, out IdTokenResult parsed) &&
        string.Equals(parsed.SignInProvider, "anonymous", StringComparison.Ordinal);

    private static long ParseLongFlexible(Dictionary<string, object> map, string key, long fallback) {
      if (map == null || !map.TryGetValue(key, out object val) || val == null) return fallback;
      if (val is long l) return l;
      if (val is int i) return i;
      if (val is double d) return (long)d;
      if (val is string s && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)) return parsed;
      return fallback;
    }

    private static string FirstNonEmpty(params string[] candidates) {
      for (int i = 0; i < candidates.Length; i++) {
        if (!string.IsNullOrEmpty(candidates[i])) return candidates[i];
      }
      return string.Empty;
    }

    /// <summary>Releases resources held by this <see cref="FirebaseAuth" /> instance.</summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed and unmanaged resources held by this <see cref="FirebaseAuth" /> instance.</summary>
    public void Dispose(bool disposing) {
      if (!disposing) return;
      lock (_stateLock) {
        if (_disposed) return;
        _disposed = true;
        _stateChanged = null;
        _idTokenChanged = null;
        _inFlightRefreshTask = null;
        _inFlightForceRefreshTask = null;
        _currentRecord = null;
        _currentUser = null;
        _sessionEpoch++;
      }
      Client.Dispose();
    }
  }
}
