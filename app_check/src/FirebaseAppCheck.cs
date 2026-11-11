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
using System.Threading;
using System.Threading.Tasks;
using Firebase.AppCheck.Internal;
using Firebase.Internal;
using Firebase.Platform;

namespace Firebase.AppCheck {
  /// @brief Firebase App Check object.
  ///
  /// App Check helps protect your API resources from abuse by preventing
  /// unauthorized clients from accessing your backend resources.
  ///
  /// With App Check, devices running your app will use an AppCheckProvider that
  /// attests to one or both of the following:
  /// * Requests originate from your authentic app
  /// * Requests originate from an authentic, untampered device
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class FirebaseAppCheck {
    private const int MaxBackoffMs = 15 * 60 * 1000;
    private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5), TenMinutes = TimeSpan.FromMinutes(10);
    private static readonly Random s_jitterRandom = new();
    private static readonly HashSet<FirebaseAppCheck> s_live = new();
    private static readonly FirebaseInstanceCache<FirebaseAppCheck> s_instances = new(ac => ac.Dispose());
    // The user provided Factory.
    private static volatile IAppCheckProviderFactory s_factory;
    private static long s_factoryEpoch;
    internal static int InitialAutoRefreshBackoffMs { get; set; } = 30000;

    private readonly FirebaseApp _app;
    private readonly string _tokenStoreKey;
    private readonly object _stateLock = new(), _persistLock = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private CancellationTokenSource _refreshTimerCts;
    private bool? _autoRefreshOverride;
    private AppCheckToken _cachedToken;
    private string _cachedFactoryType;
    private bool _loadedUnboundFromDisk;
    private Task<AppCheckToken> _cachedTokenTask, _inFlightRefresh;
    private DateTime _cachedIssuedAtUtc;
    private long _cachedTokenEpoch = -1, _providerEpoch = -1, _nextSeq, _appliedSeq, _persistedSeq;
    private IAppCheckProvider _provider;
    private EventHandler<TokenChangedEventArgs> _tokenChanged;
    private int _disposed;
    static FirebaseAppCheck() { EnsureInteropRegistered(); FirebaseInstanceCache.RegisterResetHook(ResetStaticStateForReload); }
#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() => EnsureInteropRegistered();
#endif
    // Make the constructor private, since users aren't meant to make it.
    private FirebaseAppCheck(FirebaseApp app) {
      (_app, _disposeToken, _tokenStoreKey) = (app, _disposeCts.Token, $"appcheck/token_{DebugAppCheckProvider.BuildSafeStorageSlug(app)}.json");
      LoadPersistedTokenIfPresent(Interlocked.Read(ref s_factoryEpoch), s_factory);
      lock (s_live) s_live.Add(this);
      lock (_stateLock) EnsureAutoRefreshScheduledLocked();
    }
    /// Gets the instance of FirebaseAppCheck associated with the default
    /// FirebaseApp instance.
    public static FirebaseAppCheck DefaultInstance => GetInstance(FirebaseApp.DefaultInstance);
    /// Gets the instance of FirebaseAppCheck associated with the given
    /// FirebaseApp instance.
    public static FirebaseAppCheck GetInstance(FirebaseApp app) {
      if (app == null) throw new ArgumentNullException(nameof(app));
      EnsureInteropRegistered();
      return s_instances.GetOrCreate(app, () => new FirebaseAppCheck(app));
    }
    /// Installs the given IAppCheckProviderFactory, overwriting any that
    /// were previously associated with this FirebaseAppCheck instance.
    ///
    /// Automatic token refreshing will only occur if the global
    /// isDataCollectionDefaultEnabled flag is set to true. To allow automatic
    /// token refreshing for Firebase App Check without changing the
    /// isDataCollectionDefaultEnabled flag for other Firebase SDKs, call
    /// SetTokenAutoRefreshEnabled(bool) after installing the factory.
    ///
    /// This method should be called before initializing the Firebase App.
    public static void SetAppCheckProviderFactory(IAppCheckProviderFactory factory) {
      EnsureInteropRegistered();
      if (ReferenceEquals(s_factory, factory)) return;
      s_factory = factory;
      long newEpoch = Interlocked.Increment(ref s_factoryEpoch);
      MobileAppCheckBridge.SyncNativeFactory(factory);
      FirebaseAppCheck[] live;
      lock (s_live) { live = new FirebaseAppCheck[s_live.Count]; s_live.CopyTo(live); }
      foreach (FirebaseAppCheck ac in live) ac.OnFactoryChanged(factory, newEpoch);
    }
    /// Sets the {@code isTokenAutoRefreshEnabled} flag.
    public void SetTokenAutoRefreshEnabled(bool isTokenAutoRefreshEnabled) {
      ThrowIfDisposed();
      lock (_stateLock) {
        ThrowIfDisposed();
        _autoRefreshOverride = isTokenAutoRefreshEnabled;
        if (!isTokenAutoRefreshEnabled) CancelAutoRefreshTimerLocked();
        else EnsureAutoRefreshScheduledLocked(triggerImmediateIfMissing: s_factory != null);
      }
    }
    /// Requests a Firebase App Check token. This method should be used ONLY if you
    /// need to authorize requests to a non-Firebase backend. Requests to Firebase
    /// backends are authorized automatically if configured.
    public Task<AppCheckToken> GetAppCheckTokenAsync(bool forceRefresh) {
      ThrowIfDisposed();
      long epoch = Interlocked.Read(ref s_factoryEpoch), seq = 0;
      TaskCompletionSource<AppCheckToken> tcs = null;
      Task<AppCheckToken> existing = null;
      lock (_stateLock) {
        ThrowIfDisposed();
        if (!forceRefresh && HasValidCachedTokenLocked(DateTime.UtcNow, epoch)) {
          EnsureAutoRefreshScheduledLocked();
          return _cachedTokenTask ??= Task.FromResult(_cachedToken);
        }
        if (!forceRefresh && _inFlightRefresh != null) existing = _inFlightRefresh;
        else {
          (seq, tcs) = (++_nextSeq, new TaskCompletionSource<AppCheckToken>(TaskCreationOptions.RunContinuationsAsynchronously));
          if (!forceRefresh) _inFlightRefresh = tcs.Task;
        }
      }
      if (existing != null) return existing;
      _ = ExecuteRefreshIntoTcsAsync(tcs, seq, forceRefresh);
      return tcs.Task;
    }
    /// Requests a Firebase App Check token. This method should be used ONLY if you need to authorize
    /// requests to a non-Firebase backend.
    ///
    /// Returns limited-use tokens that are intended for use with your non-Firebase backend endpoints
    /// that are protected with Replay Protection. This method does
    /// not affect the token generation behavior of the GetAppCheckTokenAsync() method.
    public async Task<AppCheckToken> GetLimitedUseAppCheckTokenAsync() {
      ThrowIfDisposed();
      IAppCheckProvider provider = ResolveProvider(out _, out _);
      try { return await provider.GetLimitedUseTokenAsync().ConfigureAwait(false); }
      catch (FirebaseException) { throw; }
      catch (OperationCanceledException) when (_disposeToken.IsCancellationRequested) { throw; }
      catch (Exception ex) { throw new FirebaseException((int)AppCheckError.Unknown, ex.Message, ex); }
    }
    /// Called on the client when an AppCheckToken is created or changed.
    public event EventHandler<TokenChangedEventArgs> TokenChanged {
      add {
        ThrowIfDisposed();
        if (value == null) return;
        AppCheckToken? initial = null;
        long epoch = Interlocked.Read(ref s_factoryEpoch);
        lock (_stateLock) {
          ThrowIfDisposed();
          _tokenChanged += value;
          if (HasValidCachedTokenLocked(DateTime.UtcNow, epoch)) initial = _cachedToken;
          EnsureAutoRefreshScheduledLocked(triggerImmediateIfMissing: !initial.HasValue && s_factory != null);
        }
        if (initial.HasValue) DispatchToMainThread(() => InvokeSubscriberIfSubscribed(value, initial.Value));
      }
      remove {
        ThrowIfDisposed();
        if (value != null) lock (_stateLock) _tokenChanged -= value;
      }
    }

    private static void EnsureInteropRegistered() => FirebaseInterops.RegisterAppCheckTokenProvider(GetTokenForInteropAsync);
    private static void ResetStaticStateForReload() { s_factory = null; Interlocked.Increment(ref s_factoryEpoch); lock (s_live) s_live.Clear(); }
    private static async Task<string> GetTokenForInteropAsync(FirebaseApp app, bool limitedUse, bool forceRefresh) =>
        app == null || s_factory == null ? null : (limitedUse
            ? await GetInstance(app).GetLimitedUseAppCheckTokenAsync().ConfigureAwait(false)
            : await GetInstance(app).GetAppCheckTokenAsync(forceRefresh).ConfigureAwait(false)).Token;
    private void OnFactoryChanged(IAppCheckProviderFactory factory, long newEpoch) {
      if (Volatile.Read(ref _disposed) != 0) return;
      lock (_stateLock) {
        if (_disposed != 0) return;
        if (_loadedUnboundFromDisk && factory != null) {
          _loadedUnboundFromDisk = false;
          if (string.Equals(factory.GetType().FullName, _cachedFactoryType, StringComparison.Ordinal)) _cachedTokenEpoch = newEpoch;
        }
        EnsureAutoRefreshScheduledLocked(triggerImmediateIfMissing: factory != null && (_autoRefreshOverride == true || _tokenChanged != null), reschedule: true);
      }
    }
    private void Dispose() {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      lock (s_live) s_live.Remove(this);
      lock (_stateLock) { CancelAutoRefreshTimerLocked(); (_tokenChanged, _provider, _inFlightRefresh, _cachedTokenTask) = (null, null, null, null); }
      _disposeCts.Cancel();
    }
    private void ThrowIfDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FirebaseAppCheck)); }
    private bool IsAutoRefreshEffectiveLocked() => _autoRefreshOverride ?? FirebaseInterops.GetIsDataCollectionDefaultEnabled(_app);
    private bool HasValidCachedTokenLocked(DateTime utcNow, long currentEpoch) {
      IAppCheckProviderFactory f = s_factory;
      if (f == null || _cachedTokenEpoch != currentEpoch || string.IsNullOrEmpty(_cachedToken.Token)) return false;
      if (!string.Equals(f.GetType().FullName, _cachedFactoryType, StringComparison.Ordinal)) return false;
      DateTime exp = _cachedToken.ExpireTime.ToUniversalTime();
      if (exp <= utcNow) return false;
      TimeSpan ttl = exp > _cachedIssuedAtUtc ? exp - _cachedIssuedAtUtc : TimeSpan.Zero;
      return exp - utcNow > TimeSpan.FromTicks(Math.Min(FiveMinutes.Ticks, Math.Max(0L, ttl.Ticks / 10)));
    }
    private static TimeSpan ComputeProactiveRefreshDelay(DateTime utcNow, DateTime issuedAtUtc, DateTime expireUtc) {
      TimeSpan ttl = expireUtc > issuedAtUtc ? expireUtc - issuedAtUtc : TimeSpan.Zero;
      TimeSpan offset = ttl > TenMinutes ? TimeSpan.FromTicks(ttl.Ticks / 2) + FiveMinutes : TimeSpan.FromTicks(Math.Max(ttl.Ticks / 2, TimeSpan.FromMilliseconds(25).Ticks));
      DateTime fireAt = issuedAtUtc + offset;
      return fireAt > utcNow ? fireAt - utcNow : TimeSpan.FromMilliseconds(25);
    }
    private IAppCheckProvider ResolveProvider(out long resolvedEpoch, out string factoryType) {
      while (true) {
        IAppCheckProviderFactory factory = s_factory ?? throw new FirebaseException((int)AppCheckError.InvalidConfiguration, "No AppCheckProvider installed.");
        long epoch = Interlocked.Read(ref s_factoryEpoch);
        string ft = factory.GetType().FullName;
        lock (_stateLock) { ThrowIfDisposed(); if (_provider != null && _providerEpoch == epoch) { (resolvedEpoch, factoryType) = (epoch, ft); return _provider; } }
        IAppCheckProvider created = factory.CreateProvider(_app)
            ?? throw new FirebaseException((int)AppCheckError.InvalidConfiguration, $"Failed to create IAppCheckProvider for App: {_app.Name}");
        lock (_stateLock) {
          ThrowIfDisposed();
          if (Interlocked.Read(ref s_factoryEpoch) != epoch) continue;
          if (_provider == null || _providerEpoch != epoch) { _provider = created; _providerEpoch = epoch; }
          (resolvedEpoch, factoryType) = (epoch, ft);
          return _provider;
        }
      }
    }
    private async Task ExecuteRefreshIntoTcsAsync(TaskCompletionSource<AppCheckToken> tcs, long seq, bool forceRefresh) {
      try {
        IAppCheckProvider provider = ResolveProvider(out long resolvedEpoch, out string factoryType);
        AppCheckToken fresh = await provider.GetTokenAsync().ConfigureAwait(false);
        AppCheckToken winner = await UpdateCachedTokenAsync(fresh, resolvedEpoch, factoryType, seq).ConfigureAwait(false);
        tcs.TrySetResult(winner);
      } catch (OperationCanceledException oce) when (_disposeToken.IsCancellationRequested) { tcs.TrySetCanceled(oce.CancellationToken); }
      catch (FirebaseException fex) { tcs.TrySetException(fex); }
      catch (Exception ex) { tcs.TrySetException(new FirebaseException((int)AppCheckError.Unknown, ex.Message, ex)); }
      finally { if (!forceRefresh) lock (_stateLock) if (ReferenceEquals(_inFlightRefresh, tcs.Task)) _inFlightRefresh = null; }
    }
    private async Task<AppCheckToken> UpdateCachedTokenAsync(AppCheckToken token, long epoch, string factoryType, long seq) {
      DateTime now = DateTime.UtcNow, expUtc = token.ExpireTime.ToUniversalTime();
      var normalized = new AppCheckToken { Token = token.Token ?? string.Empty, ExpireTime = expUtc };
      AppCheckToken winner = normalized;
      bool changed = false, accepted = false;
      lock (_stateLock) {
        if (_disposed == 0 && epoch == Interlocked.Read(ref s_factoryEpoch)) {
          if (seq >= _appliedSeq) {
            (accepted, _appliedSeq, _loadedUnboundFromDisk) = (true, seq, false);
            changed = !string.IsNullOrEmpty(normalized.Token) && !string.Equals(_cachedToken.Token, normalized.Token, StringComparison.Ordinal);
            (_cachedToken, _cachedFactoryType, _cachedTokenTask, _cachedIssuedAtUtc, _cachedTokenEpoch) = (normalized, factoryType, Task.FromResult(normalized), now, epoch);
            EnsureAutoRefreshScheduledLocked(reschedule: true);
          } else if (_cachedTokenEpoch == epoch && !string.IsNullOrEmpty(_cachedToken.Token)) winner = _cachedToken;
        }
      }
      if (!accepted) return winner;
      await Task.Run(() => PersistTokenSafe(normalized, now, factoryType, seq)).ConfigureAwait(false);
      if (changed) DispatchToMainThread(() => DeliverTokenChanged(normalized));
      return winner;
    }
    private void LoadPersistedTokenIfPresent(long currentEpoch, IAppCheckProviderFactory currentFactory) {
      Dictionary<string, object> map = JsonHelper.ParseObject(AtomicFileStore.Default.ReadAllText(_tokenStoreKey));
      string tok = JsonHelper.GetString(map, "token"), factoryType = JsonHelper.GetString(map, "factory");
      long expMs = JsonHelper.GetLong(map, "expireTimeMs", 0), issuedMs = JsonHelper.GetLong(map, "issuedAtMs", 0);
      if (string.IsNullOrEmpty(tok) || string.IsNullOrEmpty(factoryType) || expMs <= 0) return;
      AppCheckToken loaded = AppCheckToken.FromUnixTimeMilliseconds(tok, expMs);
      if (loaded.ExpireTime <= DateTime.UtcNow) return;
      lock (_stateLock) {
        _cachedFactoryType = factoryType;
        _loadedUnboundFromDisk = currentFactory == null;
        long stampedEpoch = currentFactory != null && string.Equals(currentFactory.GetType().FullName, factoryType, StringComparison.Ordinal) ? currentEpoch : -1;
        (_cachedToken, _cachedTokenTask, _cachedTokenEpoch) = (loaded, Task.FromResult(loaded), stampedEpoch);
        _cachedIssuedAtUtc = issuedMs > 0 ? AppCheckToken.UnixEpochUtc.AddMilliseconds(issuedMs) : DateTime.UtcNow;
      }
    }
    private void PersistTokenSafe(AppCheckToken token, DateTime issuedAtUtc, string factoryType, long seq) {
      if (string.IsNullOrEmpty(token.Token) || Volatile.Read(ref _disposed) != 0) return;
      lock (_persistLock) {
        if (seq < _persistedSeq || Volatile.Read(ref _disposed) != 0) return;
        _persistedSeq = seq;
        try {
          long issuedMs = (long)(issuedAtUtc.ToUniversalTime() - AppCheckToken.UnixEpochUtc).TotalMilliseconds;
          AtomicFileStore.Default.WriteAllText(_tokenStoreKey, JsonHelper.Serialize(new Dictionary<string, object> {
            ["token"] = token.Token, ["expireTimeMs"] = token.ExpireTimeMs, ["issuedAtMs"] = issuedMs, ["factory"] = factoryType ?? string.Empty,
          }));
        } catch (Exception ex) { LogUtil.LogMessage(LogLevel.Debug, $"Failed to persist App Check token cache: {ex.Message}"); }
      }
    }
    private void CancelAutoRefreshTimerLocked() { _refreshTimerCts?.Cancel(); _refreshTimerCts = null; }
    private void EnsureAutoRefreshScheduledLocked(bool triggerImmediateIfMissing = false, bool reschedule = false) {
      if (_disposed != 0 || !IsAutoRefreshEffectiveLocked() || s_factory == null) { CancelAutoRefreshTimerLocked(); return; }
      if (!reschedule && _refreshTimerCts != null && !_refreshTimerCts.IsCancellationRequested) return;
      DateTime now = DateTime.UtcNow;
      bool hasValid = HasValidCachedTokenLocked(now, Interlocked.Read(ref s_factoryEpoch));
      if (!hasValid && !triggerImmediateIfMissing) return;
      TimeSpan delay = hasValid ? ComputeProactiveRefreshDelay(now, _cachedIssuedAtUtc, _cachedToken.ExpireTime) : TimeSpan.FromMilliseconds(15);
      _refreshTimerCts?.Cancel();
      _refreshTimerCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeToken);
      _ = RunAutoRefreshTimerAsync(_refreshTimerCts, delay);
    }
    private async Task RunAutoRefreshTimerAsync(CancellationTokenSource timerCts, TimeSpan initialDelay) {
      try {
        int backoffMs = Math.Max(10, InitialAutoRefreshBackoffMs);
        TimeSpan nextDelay = initialDelay;
        while (!timerCts.IsCancellationRequested) {
          await Task.Delay(nextDelay, timerCts.Token).ConfigureAwait(false);
          lock (_stateLock) {
            if (_disposed != 0 || !ReferenceEquals(_refreshTimerCts, timerCts) || !IsAutoRefreshEffectiveLocked() || s_factory == null) return;
          }
          try { await GetAppCheckTokenAsync(forceRefresh: true).ConfigureAwait(false); return; }
          catch (OperationCanceledException) when (timerCts.IsCancellationRequested || _disposeToken.IsCancellationRequested) { return; }
          catch (Exception ex) {
            LogUtil.LogMessage(LogLevel.Warning, $"App Check background auto-refresh failed; retrying in ~{backoffMs}ms: {ex.Message}");
            int jitter;
            lock (s_jitterRandom) jitter = s_jitterRandom.Next(Math.Max(1, backoffMs / 4));
            nextDelay = TimeSpan.FromMilliseconds(backoffMs + jitter);
            backoffMs = Math.Min(backoffMs * 2, MaxBackoffMs);
          }
        }
      } catch (OperationCanceledException) {
        // Expected cancellation on reschedule, SetTokenAutoRefreshEnabled(false), AppDisposed, or Domain Reload.
      } finally { timerCts.Dispose(); }
    }
    private static void DispatchToMainThread(Action callback) {
      if (FirebaseHandler.DefaultInstance != null) _ = FirebaseHandler.RunOnMainThreadAsync(() => { callback(); return true; });
      else callback();
    }
    private bool IsSubscribedLocked(EventHandler<TokenChangedEventArgs> sub) =>
        _disposed == 0 && _tokenChanged != null && Array.IndexOf(_tokenChanged.GetInvocationList(), sub) >= 0;
    private void DeliverTokenChanged(AppCheckToken token) {
      Delegate[] subs;
      lock (_stateLock) subs = _disposed == 0 && _tokenChanged != null ? _tokenChanged.GetInvocationList() : null;
      if (subs != null) foreach (Delegate d in subs) InvokeSubscriberIfSubscribed((EventHandler<TokenChangedEventArgs>)d, token);
    }
    private void InvokeSubscriberIfSubscribed(EventHandler<TokenChangedEventArgs> sub, AppCheckToken token) {
      lock (_stateLock) if (!IsSubscribedLocked(sub)) return;
      try { sub(this, new TokenChangedEventArgs { Token = token }); }
      catch (Exception ex) { ExceptionAggregator.LogException(ex); }
    }
  }
}
