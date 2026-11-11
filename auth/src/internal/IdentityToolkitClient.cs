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
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Auth.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class IdentityToolkitClient : IDisposable {
    private const string ProdIdentityToolkitBase = "https://identitytoolkit.googleapis.com/v1/accounts:";
    private const string ProdSecureTokenUrl = "https://securetoken.googleapis.com/v1/token";
    private const string EmulatorIdentityToolkitPath = "/identitytoolkit.googleapis.com/v1/accounts:";
    private const string EmulatorSecureTokenPath = "/securetoken.googleapis.com/v1/token";
    private const string HeaderFirebaseLocale = "X-Firebase-Locale";
    private const string HeaderAppCheck = "X-Firebase-AppCheck";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AppCheckFetchTimeout = TimeSpan.FromSeconds(5);

    internal static TimeSpan DefaultMaxRetryTime { get; set; } = TimeSpan.FromSeconds(15);
    internal static int RetryInitialBackoffMs { get; set; } = RetryBackoff.InitialBackoffMs;
    internal static int RetryMaxJitterMs { get; set; } = RetryBackoff.MaxJitterMs;

    private readonly FirebaseApp _app;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private HttpClient _httpClient;
    private Func<HttpMessageHandler> _cachedHandlerFactory;
    private readonly object _clientLock = new object();
    private volatile string _emulatorHostPort;
    private volatile bool _disposed;

    internal IdentityToolkitClient(FirebaseApp app) {
      _app = app ?? throw new ArgumentNullException(nameof(app));
      _emulatorHostPort = ResolveEnvEmulatorHostPort();
    }

    internal bool HasEmulator => !string.IsNullOrEmpty(_emulatorHostPort);
    internal string EmulatorHostPort => _emulatorHostPort;

    internal void SetEmulator(string host, int port) {
      if (string.IsNullOrWhiteSpace(host)) {
        throw new ArgumentException("Emulator host cannot be null or empty.", nameof(host));
      }
      if (port <= 0 || port > 65535) {
        throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535.");
      }
      string cleanHost = StripSchemeAndTrailingSlash(host.Trim());
      _emulatorHostPort = $"{cleanHost}:{port}";
    }

    private static string ResolveEnvEmulatorHostPort() {
      string envHost = Environment.GetEnvironmentVariable("FIREBASE_AUTH_EMULATOR_HOST");
      if (!string.IsNullOrWhiteSpace(envHost)) {
        return StripSchemeAndTrailingSlash(envHost.Trim());
      }
      if (Environment.GetEnvironmentVariable("USE_AUTH_EMULATOR") != null) {
        string port = Environment.GetEnvironmentVariable("AUTH_EMULATOR_PORT");
        if (string.IsNullOrWhiteSpace(port)) port = "9099";
        return $"localhost:{port.Trim()}";
      }
      return null;
    }

    private static string StripSchemeAndTrailingSlash(string raw) {
      if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) {
        raw = raw.Substring("http://".Length);
      } else if (raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
        raw = raw.Substring("https://".Length);
      }
      return raw.TrimEnd('/');
    }

    private HttpClient GetOrCreateClient() {
      lock (_clientLock) {
        if (_disposed) {
          throw new ObjectDisposedException(nameof(IdentityToolkitClient));
        }
        Func<HttpMessageHandler> currentFactory = HttpHelpers.CustomHandlerFactory;
        if (_httpClient == null || !ReferenceEquals(_cachedHandlerFactory, currentFactory)) {
          _httpClient = HttpHelpers.CreateHttpClient(DefaultRequestTimeout);
          _cachedHandlerFactory = currentFactory;
        }
        return _httpClient;
      }
    }

    private string GetValidatedApiKey() {
      string apiKey = _app.Options?.ApiKey;
      if (string.IsNullOrEmpty(apiKey)) {
        throw AuthErrorMapper.CreateException(AuthError.InvalidApiKey);
      }
      return apiKey;
    }

    internal string BuildAccountsUrl(string endpointMethod) {
      string apiKey = Uri.EscapeDataString(GetValidatedApiKey());
      string emu = _emulatorHostPort;
      return string.IsNullOrEmpty(emu)
          ? $"{ProdIdentityToolkitBase}{endpointMethod}?key={apiKey}"
          : $"http://{emu}{EmulatorIdentityToolkitPath}{endpointMethod}?key={apiKey}";
    }

    internal string BuildSecureTokenUrl() {
      string apiKey = Uri.EscapeDataString(GetValidatedApiKey());
      string emu = _emulatorHostPort;
      return string.IsNullOrEmpty(emu)
          ? $"{ProdSecureTokenUrl}?key={apiKey}"
          : $"http://{emu}{EmulatorSecureTokenPath}?key={apiKey}";
    }

    internal async Task<Dictionary<string, object>> PostAccountsAsync(
        string endpointMethod,
        Dictionary<string, object> body,
        string languageCode = null,
        CancellationToken cancellationToken = default) {
      string url = BuildAccountsUrl(endpointMethod);
      return await SendJsonWithRetryAsync(url, body, languageCode, includeAppCheck: true, cancellationToken)
          .ConfigureAwait(false);
    }

    internal async Task<Dictionary<string, object>> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default) {
      if (string.IsNullOrEmpty(refreshToken)) {
        throw AuthErrorMapper.CreateException(AuthError.InvalidUserToken);
      }
      var body = new Dictionary<string, object> {
        { "grantType", "refresh_token" },
        { "grant_type", "refresh_token" },
        { "refreshToken", refreshToken },
        { "refresh_token", refreshToken },
      };
      string url = BuildSecureTokenUrl();
      return await SendJsonWithRetryAsync(url, body, languageCode: null, includeAppCheck: true, cancellationToken)
          .ConfigureAwait(false);
    }

    private async Task<Dictionary<string, object>> SendJsonWithRetryAsync(
        string url,
        Dictionary<string, object> payload,
        string languageCode,
        bool includeAppCheck,
        CancellationToken callerToken) {
      string jsonBody = JsonHelper.Serialize(payload ?? new Dictionary<string, object>());
      string appCheckToken = includeAppCheck
          ? await TryGetAppCheckTokenBoundedAsync().ConfigureAwait(false)
          : null;

      var backoff = new RetryBackoff(
          DefaultMaxRetryTime,
          wrapExhaustedError: WrapExhaustedRetryException,
          initialBackoffMs: RetryInitialBackoffMs,
          maxBackoffMs: RetryBackoff.MaxBackoffMs,
          maxJitterMs: RetryMaxJitterMs);

      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, callerToken);
      CancellationToken token = linkedCts.Token;

      while (true) {
        token.ThrowIfCancellationRequested();
        HttpClient client = GetOrCreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, url) {
          Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation(
            "X-Goog-Api-Client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
        HttpHelpers.SetDataCollectionHeaders(request, _app);
        if (!string.IsNullOrEmpty(languageCode)) {
          request.Headers.TryAddWithoutValidation(HeaderFirebaseLocale, languageCode);
        }
        if (!string.IsNullOrEmpty(appCheckToken)) {
          request.Headers.TryAddWithoutValidation(HeaderAppCheck, appCheckToken);
        }

        HttpResponseMessage response = null;
        try {
          response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token)
              .ConfigureAwait(false);
          int statusCode = (int)response.StatusCode;

          if (response.IsSuccessStatusCode) {
            string text = response.Content != null
                ? await response.Content.ReadAsStringAsync().ConfigureAwait(false)
                : "{}";
            return JsonHelper.ParseObject(text) ?? new Dictionary<string, object>();
          }

          string errorBody = await HttpHelpers.ReadErrorBodySafeAsync(response).ConfigureAwait(false);
          if (HttpHelpers.IsRetryableStatusCode(statusCode)) {
            var retryableEx = new RetryableAuthHttpException(statusCode, errorBody);
            await backoff.DelayOrThrowAsync(
                transportFailure: false,
                lastError: retryableEx,
                token: token).ConfigureAwait(false);
            continue;
          }

          // Non-retryable HTTP error (e.g. 400 INVALID_PASSWORD, FEDERATED_USER_ID_ALREADY_LINKED):
          // surface immediately with full parsed response map for conflict handling.
          AuthError mappedCode = AuthErrorMapper.ParseErrorBody(errorBody, statusCode);
          Dictionary<string, object> errorMap = JsonHelper.ParseObject(errorBody);
          throw new AuthBackendErrorException(mappedCode, errorMap);
        } catch (OperationCanceledException) when (callerToken.IsCancellationRequested || _disposeCts.IsCancellationRequested) {
          throw AuthErrorMapper.CreateException(AuthError.Cancelled);
        } catch (Exception ex) when (HttpHelpers.IsTransportError(ex)) {
          await backoff.DelayOrThrowAsync(
              transportFailure: true,
              lastError: ex,
              token: token).ConfigureAwait(false);
        } finally {
          response?.Dispose();
        }
      }
    }

    private async Task<string> TryGetAppCheckTokenBoundedAsync() {
      try {
        Task<string> acTask = FirebaseInterops.GetAppCheckTokenAsync(_app);
        if (acTask == null) return null;
        if (acTask.IsCompleted) return await acTask.ConfigureAwait(false);
        Task completed = await Task.WhenAny(acTask, Task.Delay(AppCheckFetchTimeout, _disposeCts.Token))
            .ConfigureAwait(false);
        return ReferenceEquals(completed, acTask) ? await acTask.ConfigureAwait(false) : null;
      } catch (Exception) {
        // Never fail or block an Auth operation when AppCheck is unavailable or times out.
        return null;
      }
    }

    private static Exception WrapExhaustedRetryException(Exception lastError) {
      if (lastError is RetryableAuthHttpException httpEx) {
        AuthError code = AuthErrorMapper.ParseErrorBody(httpEx.ErrorBody, httpEx.StatusCode);
        return AuthErrorMapper.CreateException(code);
      }
      return AuthErrorMapper.CreateException(AuthError.NetworkRequestFailed, lastError);
    }

    public void Dispose() {
      HttpClient toDispose;
      lock (_clientLock) {
        if (_disposed) return;
        _disposed = true;
        toDispose = _httpClient;
        _httpClient = null;
      }
      try {
        _disposeCts.Cancel();
      } catch (ObjectDisposedException) {
        // Already disposed.
      }
      _disposeCts.Dispose();
      toDispose?.Dispose();
    }
  }

  internal sealed class RetryableAuthHttpException : Exception {
    internal int StatusCode { get; }
    internal string ErrorBody { get; }

    internal RetryableAuthHttpException(int statusCode, string errorBody)
        : base($"Retryable HTTP {statusCode}") {
      StatusCode = statusCode;
      ErrorBody = errorBody ?? string.Empty;
    }
  }

  internal sealed class AuthBackendErrorException : Exception {
    internal AuthError ErrorCode { get; }
    internal Dictionary<string, object> ResponseMap { get; }

    internal AuthBackendErrorException(AuthError errorCode, Dictionary<string, object> responseMap)
        : base(AuthErrorMapper.GetMessage(errorCode)) {
      ErrorCode = errorCode;
      ResponseMap = responseMap;
    }
  }
}
