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
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.AppCheck.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class DebugAppCheckProvider : IAppCheckProvider, IDisposable {
    private const string ExchangeUrlPrefix = "https://firebaseappcheck.googleapis.com/v1/projects/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly object SecretLock = new();
    private static readonly HashSet<string> LoggedSecrets = new(StringComparer.Ordinal);

    internal static TimeSpan MaxExchangeRetryTime { get; set; } = TimeSpan.FromSeconds(30);
    internal static int InitialRetryBackoffMs { get; set; } = RetryBackoff.InitialBackoffMs;
    private readonly FirebaseApp _app;
    private readonly DebugAppCheckProviderFactory _factory;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly CancellationToken _disposeToken;
    private HttpClient _defaultClient;
    private volatile string _generatedSecret;
    private int _disposed;

    internal DebugAppCheckProvider(FirebaseApp app, DebugAppCheckProviderFactory factory) {
      (_app, _factory, _disposeToken) = (app ?? throw new ArgumentNullException(nameof(app)), factory ?? throw new ArgumentNullException(nameof(factory)), _disposeCts.Token);
    }
    public Task<AppCheckToken> GetTokenAsync() => ExchangeDebugTokenAsync(false);
    public Task<AppCheckToken> GetLimitedUseTokenAsync() => ExchangeDebugTokenAsync(true);
    public void Dispose() {
      if (Interlocked.Exchange(ref _disposed, 1) == 0) { _disposeCts.Cancel(); Interlocked.Exchange(ref _defaultClient, null)?.Dispose(); }
    }
    internal static string BuildSafeStorageSlug(FirebaseApp app) {
      using var sha = SHA256.Create();
      byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{app?.Name ?? string.Empty}|{app?.Options?.AppId ?? string.Empty}"));
      var sb = new StringBuilder(24);
      for (int i = 0; i < 12; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
      return sb.ToString();
    }
    private HttpClient GetDefaultHttpClient() => LazyInitializer.EnsureInitialized(ref _defaultClient, () => HttpHelpers.CreateHttpClient(RequestTimeout));

    private Task<string> ResolveDebugSecretAsync() {
      if (_factory.DebugToken is string explicitTok && explicitTok.Length > 0) return Task.FromResult(explicitTok);
      string env = Environment.GetEnvironmentVariable("FIREBASE_APP_CHECK_DEBUG_TOKEN");
      if (string.IsNullOrEmpty(env)) env = Environment.GetEnvironmentVariable("APP_CHECK_DEBUG_TOKEN");
      if (!string.IsNullOrEmpty(env)) return Task.FromResult(env);
      if (_generatedSecret is string cached && cached.Length > 0) return Task.FromResult(cached);
      return Task.Run(LoadOrCreateDebugSecret, _disposeToken);
    }

    private string LoadOrCreateDebugSecret() {
      string secretKey = $"appcheck/debug_secret_{BuildSafeStorageSlug(_app)}.txt", toLog = null, resolved;
      lock (SecretLock) {
        if (string.IsNullOrEmpty(_generatedSecret)) {
          string persisted = AtomicFileStore.Default.ReadAllText(secretKey)?.Trim();
          if (!string.IsNullOrEmpty(persisted)) _generatedSecret = persisted;
          else {
            _generatedSecret = Guid.NewGuid().ToString("D");
            try { AtomicFileStore.Default.WriteAllText(secretKey, _generatedSecret); }
            catch (Exception ex) { LogUtil.LogMessage(LogLevel.Debug, $"Unable to persist App Check debug secret: {ex.Message}"); }
          }
        }
        resolved = _generatedSecret;
        if (LoggedSecrets.Add($"{_app.Name}|{resolved}")) {
          string proj = _app.Options?.ProjectId ?? "<project-id>", appId = _app.Options?.AppId ?? "<app-id>";
          toLog = $"\nWARNING: Firebase App Check local debug token: {resolved}\n" +
              $"Register it at: https://console.firebase.google.com/project/{proj}/appcheck/apps?selectedAppId={appId}\n" +
              $"Or via Firebase CLI: firebase appcheck:debugtokens:create {resolved} --app {appId}\n";
        }
      }
      if (toLog != null) LogUtil.LogMessage(LogLevel.Warning, toLog);
      return resolved;
    }

    private async Task<AppCheckToken> ExchangeDebugTokenAsync(bool limitedUse) {
      if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(DebugAppCheckProvider));
      MobileAppCheckBridge.ValidateAppOptions(_app);
      AppOptions opts = _app.Options;
      string url = $"{ExchangeUrlPrefix}{opts.ProjectId}/apps/{opts.AppId}:exchangeDebugToken";
      string secret = await ResolveDebugSecretAsync().ConfigureAwait(false);
      string payloadJson = JsonHelper.Serialize(new Dictionary<string, object> { ["debug_token"] = secret, ["limited_use"] = limitedUse });
      var backoff = new RetryBackoff(MaxExchangeRetryTime,
          err => err as FirebaseException ?? new FirebaseException((int)AppCheckError.ServerUnreachable, err?.Message ?? "App Check server unreachable.", err),
          InitialRetryBackoffMs);

      while (true) {
        _disposeToken.ThrowIfCancellationRequested();
        bool useCustom = HttpHelpers.CustomHandlerFactory != null;
        HttpClient client = useCustom ? HttpHelpers.CreateHttpClient(RequestTimeout) : GetDefaultHttpClient();
        try {
          using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(payloadJson, Encoding.UTF8, "application/json") };
          request.Headers.TryAddWithoutValidation("x-goog-api-key", opts.ApiKey);
          request.Headers.TryAddWithoutValidation("x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
          HttpHelpers.SetDataCollectionHeaders(request, _app);
          string responseJson;
          try {
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _disposeToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
              int status = (int)response.StatusCode;
              FirebaseException httpErr = MapHttpFailure(status, await HttpHelpers.ReadErrorBodySafeAsync(response).ConfigureAwait(false));
              if (HttpHelpers.IsRetryableStatusCode(status)) { await backoff.DelayOrThrowAsync(false, httpErr, _disposeToken).ConfigureAwait(false); continue; }
              throw httpErr;
            }
            using var ms = new MemoryStream();
            if (response.Content != null) {
              using Stream bodyStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
              await HttpHelpers.CopyToAsync(bodyStream, ms, _disposeToken).ConfigureAwait(false);
            }
            responseJson = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
          } catch (Exception ex) when (!_disposeToken.IsCancellationRequested && !(ex is FirebaseException) && HttpHelpers.IsTransportError(ex)) {
            var netErr = new FirebaseException((int)AppCheckError.ServerUnreachable, $"App Check transport failure: {ex.Message}", ex);
            await backoff.DelayOrThrowAsync(true, netErr, _disposeToken).ConfigureAwait(false);
            continue;
          }
          Dictionary<string, object> parsed = JsonHelper.ParseObject(responseJson);
          string minted = JsonHelper.GetString(parsed, "token");
          if (string.IsNullOrEmpty(minted)) throw new FirebaseException((int)AppCheckError.Unknown, "App Check exchangeDebugToken response missing token.");
          return new AppCheckToken { Token = minted, ExpireTime = DateTime.UtcNow.AddSeconds(ParseTtlSeconds(JsonHelper.GetString(parsed, "ttl", "3600s"))) };
        } finally { if (useCustom) client.Dispose(); }
      }
    }

    private static double ParseTtlSeconds(string rawTtl) =>
        !string.IsNullOrWhiteSpace(rawTtl) && double.TryParse(rawTtl.Trim().TrimEnd('s', 'S'), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s > 0 ? s : 3600.0;

    private static FirebaseException MapHttpFailure(int status, string body) {
      AppCheckError code = status switch { 400 or 404 => AppCheckError.InvalidConfiguration, 408 or 429 or >= 500 => AppCheckError.ServerUnreachable, _ => AppCheckError.Unknown };
      return new FirebaseException((int)code, $"App Check exchangeDebugToken failed with HTTP {status}: {body}");
    }
  }
}
