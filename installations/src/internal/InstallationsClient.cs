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
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Installations.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class InstallationsClient {
    private const string BaseUrl = "https://firebaseinstallations.googleapis.com/v1", AuthVersion = "FIS_v2";
    private const long ExpiryMarginSeconds = 60;
    private static readonly object _httpLock = new();
    private static readonly HttpClient _defaultHttpClient = HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(30));
    private static Func<HttpMessageHandler> _customFactory;
    private static HttpClient _customHttpClient;

    internal static TimeSpan DefaultMaxRetryTime { get; set; } = TimeSpan.FromSeconds(30);
    internal static int DefaultInitialBackoffMs { get; set; } = RetryBackoff.InitialBackoffMs;
    internal static AtomicFileStore CustomStoreForTesting { get; set; }

    private sealed class PersistedState {
      internal readonly string Fid, RefreshToken, AuthToken;
      internal readonly long ExpiresAt, CreatedStamp;
      internal readonly Task<string> IdTask, TokenTask;
      internal PersistedState(string fid, string refreshToken, string authToken, long expiresAt, long createdStamp = 0) {
        (Fid, RefreshToken, AuthToken, ExpiresAt, CreatedStamp) = (fid ?? string.Empty, refreshToken ?? string.Empty, authToken ?? string.Empty, expiresAt, createdStamp);
        (IdTask, TokenTask) = (Fid.Length > 0 ? Task.FromResult(Fid) : null, AuthToken.Length > 0 ? Task.FromResult(AuthToken) : null);
      }
      internal bool IsRegistered => Fid.Length > 0 && RefreshToken.Length > 0;
      internal bool HasValidToken(long nowSec) => IsRegistered && AuthToken.Length > 0 && nowSec + ExpiryMarginSeconds < ExpiresAt;
    }

    private readonly FirebaseApp _app;
    private readonly AtomicFileStore _store;
    private readonly string _storageKey;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private readonly SemaphoreSlim _opGate = new(1, 1);
    private readonly object _flightLock = new();
    private readonly Task<PersistedState>[] _inFlight = new Task<PersistedState>[3];
    private volatile PersistedState _state;
    private int _pendingDeletes;
    private bool _diskLoaded;

    internal InstallationsClient(FirebaseApp app) => (_app, _store, _storageKey, _disposeToken) = (app, CustomStoreForTesting ?? AtomicFileStore.Default, ComputeStorageKey(app.Name, app.Options?.AppId), _disposeCts.Token);
    internal void Dispose() => _disposeCts.Cancel();

    private static HttpClient GetHttpClient() {
      if (HttpHelpers.CustomHandlerFactory is not Func<HttpMessageHandler> factory) return _defaultHttpClient;
      lock (_httpLock) {
        if (!ReferenceEquals(_customFactory, factory) || _customHttpClient == null) (_customFactory, _customHttpClient) = (factory, HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(30)));
        return _customHttpClient;
      }
    }

    internal Task<string> GetIdAsync() => ValidateOptions() is Exception err ? Task.FromException<string>(err) : (MobileInstallationsBridge.TryGetIdAsync(_app, this) ?? PureGetIdAsync());
    internal Task<string> GetTokenAsync(bool forceRefresh) => ValidateOptions() is Exception err ? Task.FromException<string>(err) : (MobileInstallationsBridge.TryGetTokenAsync(_app, forceRefresh, this) ?? PureGetTokenAsync(forceRefresh));
    internal Task DeleteAsync() => ValidateOptions() is Exception err ? Task.FromException(err) : (MobileInstallationsBridge.TryDeleteAsync(_app, this) ?? EnqueueDeleteAsync());
    internal Task<string> PureGetIdAsync() => Volatile.Read(ref _pendingDeletes) == 0 && _state is PersistedState snap && snap.IsRegistered ? snap.IdTask : GetIdSlowAsync();
    internal Task<string> PureGetTokenAsync(bool forceRefresh) => !forceRefresh && Volatile.Read(ref _pendingDeletes) == 0 && _state is PersistedState snap && snap.HasValidToken(NowUnixSeconds()) ? snap.TokenTask : GetTokenSlowAsync(forceRefresh);
    internal Task EnqueueDeleteAsync() { lock (_flightLock) { Interlocked.Increment(ref _pendingDeletes); Array.Clear(_inFlight, 0, _inFlight.Length); return DeleteCoreAsync(); } }

    private async Task<string> GetIdSlowAsync() => (await CoalesceRegisterAsync().ConfigureAwait(false)).Fid;
    private async Task<string> GetTokenSlowAsync(bool forceRefresh) {
      long callStamp = Stopwatch.GetTimestamp();
      PersistedState state = await CoalesceRegisterAsync().ConfigureAwait(false);
      bool canReuse = Volatile.Read(ref _pendingDeletes) == 0 && (!forceRefresh || state.CreatedStamp >= callStamp) && state.HasValidToken(NowUnixSeconds());
      return canReuse ? state.AuthToken : (await CoalesceRefreshAsync(forceRefresh).ConfigureAwait(false)).AuthToken;
    }

    private Task<PersistedState> CoalesceRegisterAsync() {
      lock (_flightLock) {
        if (Volatile.Read(ref _pendingDeletes) == 0 && _state is PersistedState s && s.IsRegistered) return Task.FromResult(s);
        return (_inFlight[0] == null || _inFlight[0].IsCompleted) ? (_inFlight[0] = StartSlot(0, RegisterCoreAsync)) : _inFlight[0];
      }
    }
    private Task<PersistedState> CoalesceRefreshAsync(bool forceRefresh) {
      int slot = forceRefresh ? 2 : 1;
      lock (_flightLock) {
        if (!forceRefresh && Volatile.Read(ref _pendingDeletes) == 0 && _state is PersistedState s && s.HasValidToken(NowUnixSeconds())) return Task.FromResult(s);
        return (_inFlight[slot] == null || _inFlight[slot].IsCompleted) ? (_inFlight[slot] = StartSlot(slot, () => RefreshCoreAsync(forceRefresh))) : _inFlight[slot];
      }
    }
    private Task<PersistedState> StartSlot(int slot, Func<Task<PersistedState>> work) { Task<PersistedState> t = null; t = RunGatedAsync(slot, () => t, work); return t; }
    private void ClearSlot(int slot, Task<PersistedState> self) { lock (_flightLock) { if (ReferenceEquals(_inFlight[slot], self)) _inFlight[slot] = null; } }
    private async Task<PersistedState> RunGatedAsync(int slot, Func<Task<PersistedState>> self, Func<Task<PersistedState>> work) {
      try { await _opGate.WaitAsync(_disposeToken).ConfigureAwait(false); try { return await work().ConfigureAwait(false); } finally { ClearSlot(slot, self()); _opGate.Release(); } }
      catch { ClearSlot(slot, self()); throw; }
    }

    private async Task<PersistedState> RegisterCoreAsync() {
      EnsureDiskLoaded();
      if (_state != null && _state.IsRegistered) return _state;
      string candidateFid = (_state != null && _state.Fid.Length > 0) ? _state.Fid : GenerateFid();
      SaveState(new PersistedState(candidateFid, string.Empty, string.Empty, 0));
      string url = $"{BaseUrl}/projects/{Uri.EscapeDataString(_app.Options.ProjectId)}/installations";
      string bodyJson = JsonHelper.Serialize(new Dictionary<string, object> {
        ["fid"] = candidateFid, ["appId"] = _app.Options.AppId, ["authVersion"] = AuthVersion, ["sdkVersion"] = $"o:u:unity:{FirebaseInterops.GetVersionInfoSdkVersion()}",
      });
      (int code, string respBody) = await SendWithRetryAsync(HttpMethod.Post, url, bodyJson, null).ConfigureAwait(false);
      if (code == 409) { ClearState(); throw MakeHttpError(code, respBody, "Create installation"); }
      if (code < 200 || code >= 300) throw MakeHttpError(code, respBody, "Create installation");
      Dictionary<string, object> root = JsonHelper.ParseObject(respBody) ?? throw new FirebaseException((int)Error.Unknown, "Malformed installation response.");
      Dictionary<string, object> authMap = JsonHelper.GetMap(root, "authToken");
      string serverFid = JsonHelper.GetString(root, "fid"), refreshToken = JsonHelper.GetString(root, "refreshToken"), authToken = JsonHelper.GetString(authMap, "token");
      if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(authToken)) throw new FirebaseException((int)Error.Unknown, "Incomplete installation response.");
      var registered = new PersistedState(!string.IsNullOrEmpty(serverFid) ? serverFid : candidateFid,
          refreshToken, authToken, NowUnixSeconds() + ParseExpiresInSeconds(JsonHelper.GetString(authMap, "expiresIn")), Stopwatch.GetTimestamp());
      SaveState(registered);
      return registered;
    }

    private async Task<PersistedState> RefreshCoreAsync(bool forceRefresh) {
      EnsureDiskLoaded();
      if (_state is not PersistedState cur || !cur.IsRegistered) return await RegisterCoreAsync().ConfigureAwait(false);
      if (!forceRefresh && cur.HasValidToken(NowUnixSeconds())) return cur;
      string url = $"{BaseUrl}/projects/{Uri.EscapeDataString(_app.Options.ProjectId)}/installations/{Uri.EscapeDataString(cur.Fid)}/authTokens:generate";
      string bodyJson = JsonHelper.Serialize(new Dictionary<string, object> {
        ["installation"] = new Dictionary<string, object> { ["sdkVersion"] = $"o:u:unity:{FirebaseInterops.GetVersionInfoSdkVersion()}", ["appId"] = _app.Options.AppId },
      });
      (int code, string respBody) = await SendWithRetryAsync(HttpMethod.Post, url, bodyJson, cur.RefreshToken).ConfigureAwait(false);
      if (code == 401 || code == 404) { ClearState(); throw MakeHttpError(code, respBody, "Generate auth token"); }
      if (code < 200 || code >= 300) throw MakeHttpError(code, respBody, "Generate auth token");
      Dictionary<string, object> root = JsonHelper.ParseObject(respBody) ?? throw new FirebaseException((int)Error.Unknown, "Malformed authToken response.");
      Dictionary<string, object> tokMap = JsonHelper.GetMap(root, "authToken") ?? root;
      string token = JsonHelper.GetString(tokMap, "token");
      if (string.IsNullOrEmpty(token)) throw new FirebaseException((int)Error.Unknown, "Missing auth token.");
      var updated = new PersistedState(cur.Fid, cur.RefreshToken, token,
          NowUnixSeconds() + ParseExpiresInSeconds(JsonHelper.GetString(tokMap, "expiresIn")), Stopwatch.GetTimestamp());
      SaveState(updated);
      return updated;
    }

    private async Task DeleteCoreAsync() {
      bool decremented = false;
      try {
        await _opGate.WaitAsync(_disposeToken).ConfigureAwait(false);
        try {
          EnsureDiskLoaded();
          PersistedState prev = _state; _state = null;
          if (prev != null && prev.IsRegistered) {
            string url = $"{BaseUrl}/projects/{Uri.EscapeDataString(_app.Options.ProjectId)}/installations/{Uri.EscapeDataString(prev.Fid)}";
            try {
              (int code, string respBody) = await SendWithRetryAsync(HttpMethod.Delete, url, null, prev.RefreshToken).ConfigureAwait(false);
              if ((code < 200 || code >= 300) && code != 401 && code != 404) throw MakeHttpError(code, respBody, "Delete installation");
            } catch { _state = prev; throw; }
          }
          ClearState();
        } finally { Interlocked.Decrement(ref _pendingDeletes); decremented = true; _opGate.Release(); }
      } finally { if (!decremented) Interlocked.Decrement(ref _pendingDeletes); }
    }

    private async Task<(int StatusCode, string Body)> SendWithRetryAsync(HttpMethod method, string url, string jsonBody, string refreshToken) {
      var backoff = new RetryBackoff(DefaultMaxRetryTime,
          last => last as FirebaseException ?? new FirebaseException((int)Error.NoAccess, $"Installations request failed after retries: {last?.Message}", last), DefaultInitialBackoffMs);
      string appCheck = await FirebaseInterops.GetAppCheckTokenAsync(_app).ConfigureAwait(false);
      while (true) {
        _disposeToken.ThrowIfCancellationRequested();
        try {
          using var req = new HttpRequestMessage(method, url);
          req.Headers.TryAddWithoutValidation("x-goog-api-key", _app.Options.ApiKey);
          req.Headers.TryAddWithoutValidation("x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
          req.Headers.TryAddWithoutValidation("Accept", "application/json");
          HttpHelpers.SetDataCollectionHeaders(req, _app);
          if (!string.IsNullOrEmpty(appCheck)) req.Headers.TryAddWithoutValidation("X-Firebase-AppCheck", appCheck);
          if (!string.IsNullOrEmpty(refreshToken)) req.Headers.TryAddWithoutValidation("Authorization", $"{AuthVersion} {refreshToken}");
          if (jsonBody != null) req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
          using HttpResponseMessage resp = await GetHttpClient().SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _disposeToken).ConfigureAwait(false);
          int code = (int)resp.StatusCode;
          if (HttpHelpers.IsRetryableStatusCode(code)) {
            Exception statusEx = null;
            try { await HttpHelpers.ValidateHttpResponse(resp).ConfigureAwait(false); } catch (HttpRequestException hre) { statusEx = hre; }
            await backoff.DelayOrThrowAsync(false, statusEx, _disposeToken).ConfigureAwait(false);
            continue;
          }
          return (code, resp.Content != null ? await resp.Content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty : string.Empty);
        } catch (Exception ex) {
          _disposeToken.ThrowIfCancellationRequested();
          if (ex is FirebaseException) throw;
          if (HttpHelpers.IsTransportError(ex)) { await backoff.DelayOrThrowAsync(true, ex, _disposeToken).ConfigureAwait(false); continue; }
          throw new FirebaseException((int)Error.Unknown, ex.Message, ex);
        }
      }
    }

    private void EnsureDiskLoaded() {
      if (_diskLoaded) return;
      _diskLoaded = true;
      Dictionary<string, object> map = JsonHelper.ParseObject(_store.ReadAllText(_storageKey));
      if (JsonHelper.GetString(map, "fid") is string fid && fid.Length > 0)
        _state = new PersistedState(fid, JsonHelper.GetString(map, "refreshToken", string.Empty), JsonHelper.GetString(map, "authToken", string.Empty), JsonHelper.GetLong(map, "expiresAt", 0));
    }
    private void SaveState(PersistedState n) { (_state, _diskLoaded) = (n, true); _store.WriteAllText(_storageKey, JsonHelper.Serialize(new Dictionary<string, object> { ["fid"] = n.Fid, ["refreshToken"] = n.RefreshToken, ["authToken"] = n.AuthToken, ["expiresAt"] = n.ExpiresAt })); }
    private void ClearState() { (_state, _diskLoaded) = (null, true); _store.Delete(_storageKey); }
    private Exception ValidateOptions() => (_app?.Options is not AppOptions o || string.IsNullOrWhiteSpace(o.ProjectId) || string.IsNullOrWhiteSpace(o.AppId) || string.IsNullOrWhiteSpace(o.ApiKey))
        ? new FirebaseException((int)Error.InvalidConfiguration, "FirebaseApp options must specify non-empty ProjectId, AppId, and ApiKey.") : null;
    private static FirebaseException MakeHttpError(int code, string body, string op) =>
        new((int)(code == 400 ? Error.InvalidConfiguration : (code == 401 || code == 403 || code == 404 || code == 429 || code >= 500 ? Error.NoAccess : Error.Unknown)),
            $"{op} failed ({code}): {JsonHelper.GetString(JsonHelper.GetMap(JsonHelper.ParseObject(body), "error"), "message", "HTTP " + code)}");
    internal static string GenerateFid() { byte[] b = new byte[17]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); b[0] = (byte)(0x70 | (b[0] & 0x0F)); return Convert.ToBase64String(b).Substring(0, 22).Replace('+', '-').Replace('/', '_'); }
    internal static long ParseExpiresInSeconds(string raw) { if (string.IsNullOrWhiteSpace(raw)) return 0; string s = raw.Trim(); if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 1); return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? (long)v : 0; }
    internal static string ComputeStorageKey(string appName, string appId) { using var sha = SHA256.Create(); return "installations/fis_" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes($"{appName ?? string.Empty}\0{appId ?? string.Empty}")), 0, 16).Replace("-", string.Empty).ToLowerInvariant() + ".json"; }
    private static long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  }
}
