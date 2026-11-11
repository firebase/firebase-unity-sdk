// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.RemoteConfig.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RemoteConfigFetchClient {
    internal const string DefaultNamespace = "firebase";
    private const string FetchBaseUrl = "https://firebaseremoteconfig.googleapis.com/v1/projects";
    private const int MaxConnectionsPerServer = 16;

    private static readonly object ClientLock = new object();
    private static readonly HttpClient SharedClient = HttpHelpers.CreateHttpClient(Timeout.InfiniteTimeSpan, MaxConnectionsPerServer);
    private static readonly PropertyInfo PackageNameProp = typeof(AppOptions).GetProperty(
        "PackageName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static Func<HttpMessageHandler> _customFactory;
    private static HttpClient _customClient;
    private static volatile string _cachedPackageName = null, _cachedPlatformVersion = null;

    private readonly FirebaseApp _app;
    private readonly RemoteConfigStore _store;
    private readonly SemaphoreSlim _fetchGate = new SemaphoreSlim(1, 1);
    private readonly object _coalesceLock = new object();
    private Task _inFlightFetch;
    private bool _inFlightForceNetwork;
    private TimeSpan _inFlightMinExpiration = TimeSpan.MaxValue;

    internal RemoteConfigFetchClient(FirebaseApp app, RemoteConfigStore store) =>
        (_app, _store) = (app ?? throw new ArgumentNullException(nameof(app)), store ?? throw new ArgumentNullException(nameof(store)));

#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnRuntimeInit() => WarmEngineMetadata();
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void OnEditorInit() => WarmEngineMetadata();
#endif
#if UNITY_5_3_OR_NEWER
    private static void WarmEngineMetadata() {
      try { (_cachedPackageName, _cachedPlatformVersion) = (UnityEngine.Application.identifier, UnityEngine.SystemInfo.operatingSystem); }
      catch (Exception) {
        // Headless fallback.
      }
    }
#endif

    internal static HttpClient GetHttpClient() {
      Func<HttpMessageHandler> factory = HttpHelpers.CustomHandlerFactory;
      if (factory == null) return SharedClient;
      lock (ClientLock) {
        if (!ReferenceEquals(_customFactory, factory) || _customClient == null) {
          (_customFactory, _customClient) = (factory, HttpHelpers.CreateHttpClient(Timeout.InfiniteTimeSpan, MaxConnectionsPerServer));
        }
        return _customClient;
      }
    }

    internal static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    internal static string ResolveProjectIdentifier(FirebaseApp app) {
      AppOptions opts = app?.Options;
      if (opts == null) return null;
      if (!string.IsNullOrEmpty(opts.ProjectId)) return opts.ProjectId;
      if (!string.IsNullOrEmpty(opts.AppId) && opts.AppId.Split(':') is string[] p && p.Length >= 2 && !string.IsNullOrEmpty(p[1])) return p[1];
      return !string.IsNullOrEmpty(opts.MessageSenderId) ? opts.MessageSenderId : null;
    }

    internal Task FetchAsync(TimeSpan cacheExpiration, CancellationToken disposeToken) {
      disposeToken.ThrowIfCancellationRequested();
      if (_fetchGate.CurrentCount > 0 && _store.IsCacheFresh(cacheExpiration, NowMs())) return Task.CompletedTask;
      lock (_coalesceLock) {
        if (_inFlightFetch != null) {
          if (cacheExpiration <= TimeSpan.Zero) _inFlightForceNetwork = true;
          if (cacheExpiration < _inFlightMinExpiration) _inFlightMinExpiration = cacheExpiration;
          return _inFlightFetch;
        }
        if (_fetchGate.CurrentCount > 0 && _store.IsCacheFresh(cacheExpiration, NowMs())) return Task.CompletedTask;
        (_inFlightForceNetwork, _inFlightMinExpiration) = (cacheExpiration <= TimeSpan.Zero, cacheExpiration);
        Task task = RunCoalescedFetchAsync(disposeToken);
        if (!task.IsCompleted) _inFlightFetch = task;
        return task;
      }
    }

    internal async Task<long> FetchForRealtimeAsync(int attempt, CancellationToken cancelToken) {
      await _fetchGate.WaitAsync(cancelToken).ConfigureAwait(false);
      try { return await ExecuteNetworkFetchAsync("REALTIME", attempt, cancelToken).ConfigureAwait(false); }
      finally { _fetchGate.Release(); }
    }

    private async Task RunCoalescedFetchAsync(CancellationToken disposeToken) {
      await _fetchGate.WaitAsync(disposeToken).ConfigureAwait(false);
      try {
        bool force;
        TimeSpan minExp;
        lock (_coalesceLock) (force, minExp) = (_inFlightForceNetwork, _inFlightMinExpiration);
        if (force || !_store.IsCacheFresh(minExp, NowMs())) await ExecuteNetworkFetchAsync("BASE", 1, disposeToken).ConfigureAwait(false);
      } finally {
        lock (_coalesceLock) (_inFlightFetch, _inFlightForceNetwork, _inFlightMinExpiration) = (null, false, TimeSpan.MaxValue);
        _fetchGate.Release();
      }
    }

    private async Task<long> ExecuteNetworkFetchAsync(string fetchType, int attempt, CancellationToken cancelToken) {
      (bool allowed, long throttledUntilMs, string currentEtag, ulong timeoutMs) = await _store.TryBeginNetworkFetchAsync(NowMs()).ConfigureAwait(false);
      if (!allowed) throw CreateThrottledException(throttledUntilMs, 429);

      string project = ResolveProjectIdentifier(_app), apiKey = _app.Options?.ApiKey, appId = _app.Options?.AppId;
      if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(appId)) {
        await _store.RecordFetchFailureAsync(false, null, NowMs()).ConfigureAwait(false);
        throw new FirebaseException((int)RemoteConfigError.None, "Remote Config fetch requires ProjectId, ApiKey, and AppId.");
      }

      (string fid, string fisAuthToken) = await FirebaseInterops.GetInstallationIdentityAsync(_app).ConfigureAwait(false);
      if (string.IsNullOrEmpty(fid)) fid = await _store.GetOrCreateFallbackFidAsync().ConfigureAwait(false);
      string appCheckToken = await FirebaseInterops.GetAppCheckTokenAsync(_app).ConfigureAwait(false);

      string url = $"{FetchBaseUrl}/{Uri.EscapeDataString(project)}/namespaces/{DefaultNamespace}:fetch";
      using var request = new HttpRequestMessage(HttpMethod.Post, url) {
        Content = new StringContent(JsonHelper.Serialize(BuildRequestBody(appId, fid, fisAuthToken)), Encoding.UTF8, "application/json"),
      };
      request.Headers.TryAddWithoutValidation("Accept", "application/json");
      request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
      request.Headers.TryAddWithoutValidation("x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
      request.Headers.TryAddWithoutValidation("X-Google-GFE-Can-Retry", "yes");
      request.Headers.TryAddWithoutValidation("X-Firebase-RC-Fetch-Type", $"{fetchType}/{attempt}");
      if (!string.IsNullOrEmpty(currentEtag)) request.Headers.TryAddWithoutValidation("If-None-Match", currentEtag);
      if (!string.IsNullOrEmpty(fisAuthToken)) request.Headers.TryAddWithoutValidation("X-Goog-Firebase-Installations-Auth", fisAuthToken);
      if (!string.IsNullOrEmpty(appCheckToken)) request.Headers.TryAddWithoutValidation("X-Firebase-AppCheck", appCheckToken);
      HttpHelpers.SetDataCollectionHeaders(request, _app);

      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
      timeoutCts.CancelAfter((int)Math.Min(timeoutMs, (ulong)int.MaxValue));
      HttpResponseMessage response;
      try {
        response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false);
      } catch (OperationCanceledException) when (cancelToken.IsCancellationRequested) {
        throw;
      } catch (Exception ex) {
        await _store.RecordFetchFailureAsync(false, null, NowMs()).ConfigureAwait(false);
        string msg = ex is OperationCanceledException ? "Remote Config fetch timed out." : $"Remote Config network failure: {ex.Message}";
        throw new FirebaseException((int)RemoteConfigError.None, msg, ex);
      }

      using (response) {
        int code = (int)response.StatusCode;
        string responseEtag = ReadHeaderValue(response, "ETag");
        if (response.StatusCode == HttpStatusCode.NotModified) {
          await _store.RecordFetchSuccessAsync("NO_CHANGE", null, null, 0, responseEtag, NowMs()).ConfigureAwait(false);
          return -1L;
        }
        if (code == 429 || (code >= 500 && code < 600)) {
          throw CreateThrottledException(await _store.RecordFetchFailureAsync(true, ParseRetryAfterMs(response), NowMs()).ConfigureAwait(false), code);
        }
        if (code != 200) {
          string err = await HttpHelpers.ReadErrorBodySafeAsync(response).ConfigureAwait(false) ?? string.Empty;
          await _store.RecordFetchFailureAsync(false, null, NowMs()).ConfigureAwait(false);
          throw new FirebaseException((int)RemoteConfigError.None, $"Remote Config fetch failed with HTTP {code}: {(err.Length > 200 ? err.Substring(0, 200) : err)}");
        }
        string rawBody = response.Content != null ? await response.Content.ReadAsStringAsync().ConfigureAwait(false) : string.Empty;
        Dictionary<string, object> bodyMap = JsonHelper.ParseObject(rawBody);
        if (bodyMap == null) {
          await _store.RecordFetchFailureAsync(false, null, NowMs()).ConfigureAwait(false);
          throw new FirebaseException((int)RemoteConfigError.None, "Remote Config fetch response is not a valid JSON object.");
        }
        string state = JsonHelper.GetString(bodyMap, "state", "UPDATE");
        if (string.Equals(state, "INSTANCE_STATE_UNSPECIFIED", StringComparison.OrdinalIgnoreCase)) {
          throw CreateThrottledException(await _store.RecordFetchFailureAsync(true, null, NowMs()).ConfigureAwait(false), 500);
        }
        Dictionary<string, string> entries = await ParseEntriesAsync(bodyMap).ConfigureAwait(false), keyMeta = ParseKeyMetadata(bodyMap);
        long templateVersion = JsonHelper.GetLong(bodyMap, "templateVersion", 0);
        await _store.RecordFetchSuccessAsync(state, entries, keyMeta, templateVersion, responseEtag, NowMs()).ConfigureAwait(false);
        return string.Equals(state, "NO_CHANGE", StringComparison.OrdinalIgnoreCase) ? -1L : templateVersion;
      }
    }

    private Dictionary<string, object> BuildRequestBody(string appId, string fid, string fisAuthToken) {
      (string langCode, string countryCode) = GetLocaleCodes();
      var body = new Dictionary<string, object>(StringComparer.Ordinal) {
        ["appId"] = appId, ["appInstanceId"] = fid, ["sdkVersion"] = FirebaseInterops.GetVersionInfoSdkVersion(),
        ["languageCode"] = langCode, ["countryCode"] = countryCode, ["platformVersion"] = GetPlatformVersion(), ["timeZone"] = GetTimeZoneId(),
      };
      if (!string.IsNullOrEmpty(fisAuthToken)) body["appInstanceIdToken"] = fisAuthToken;
      if (GetPackageName(_app) is string pkg && pkg.Length > 0) body["packageName"] = pkg;
      if (FirebaseInterops.GetApplicationVersion() is string appVer && appVer.Length > 0) body["appVersion"] = appVer;
      if (_store.GetWireCustomSignalsSnapshot() is Dictionary<string, object> sig && sig.Count > 0) body["customSignals"] = sig;
      return body;
    }

    private async Task<Dictionary<string, string>> ParseEntriesAsync(Dictionary<string, object> bodyMap) {
      if (!bodyMap.TryGetValue("entries", out object rawEntries) || rawEntries == null) return new(StringComparer.Ordinal);
      if (rawEntries is not IDictionary<string, object> entriesMap) {
        await _store.RecordFetchFailureAsync(false, null, NowMs()).ConfigureAwait(false);
        throw new FirebaseException((int)RemoteConfigError.None, "Remote Config 'entries' field is malformed.");
      }
      var result = new Dictionary<string, string>(entriesMap.Count, StringComparer.Ordinal);
      foreach (KeyValuePair<string, object> kv in entriesMap) if (kv.Key != null) result[kv.Key] = JsonHelper.GetString(entriesMap, kv.Key, string.Empty) ?? string.Empty;
      return result;
    }

    private static Dictionary<string, string> ParseKeyMetadata(Dictionary<string, object> bodyMap) {
      var meta = new Dictionary<string, string>(StringComparer.Ordinal);
      AppendAffectedKeysMeta(meta, JsonHelper.GetList(bodyMap, "experimentDescriptions"), "exp", "experimentId", "variantId");
      AppendAffectedKeysMeta(meta, JsonHelper.GetList(bodyMap, "rolloutMetadata"), "ro", "rolloutId", "variantId");
      return meta;
    }

    private static void AppendAffectedKeysMeta(Dictionary<string, string> target, List<object> items, string tag, string idKey, string variantKey) {
      if (items == null) return;
      for (int i = 0; i < items.Count; i++) {
        if (items[i] is not IDictionary<string, object> item || JsonHelper.GetList(item, "affectedParameterKeys") is not List<object> keys) continue;
        string token = $"{tag}:{JsonHelper.GetString(item, idKey, string.Empty)}={JsonHelper.GetString(item, variantKey, string.Empty)};";
        for (int k = 0; k < keys.Count; k++) if (keys[k] is string p && p.Length > 0) target[p] = (target.TryGetValue(p, out string prev) ? prev : string.Empty) + token;
      }
    }

    private static string GetPackageName(FirebaseApp app) {
      if (app?.Options != null && PackageNameProp != null) {
        try { if (PackageNameProp.GetValue(app.Options) is string optPkg && optPkg.Length > 0) return optPkg; }
        catch (Exception) {
          // Fallback to cached identifier.
        }
      }
      return _cachedPackageName ?? string.Empty;
    }

    private static string GetPlatformVersion() {
      if (!string.IsNullOrEmpty(_cachedPlatformVersion)) return _cachedPlatformVersion;
      try { return Environment.OSVersion.VersionString; } catch (Exception) { return "unknown"; }
    }

    private static string GetTimeZoneId() { try { return TimeZoneInfo.Local.Id ?? "UTC"; } catch (Exception) { return "UTC"; } }

    private static (string LanguageCode, string CountryCode) GetLocaleCodes() {
      try {
        string name = CultureInfo.CurrentCulture?.Name;
        if (string.IsNullOrEmpty(name)) return ("en-US", "US");
        int dash = name.LastIndexOf('-');
        return (name, dash > 0 && dash + 1 < name.Length ? name.Substring(dash + 1).ToUpperInvariant() : "US");
      } catch (Exception) { return ("en-US", "US"); }
    }

    private static string ReadHeaderValue(HttpResponseMessage response, string headerName) {
      if (response.Headers.TryGetValues(headerName, out IEnumerable<string> vals)) foreach (string v in vals) if (!string.IsNullOrEmpty(v)) return v;
      return null;
    }

    private static long? ParseRetryAfterMs(HttpResponseMessage response) {
      if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta.TotalMilliseconds > 0) return (long)delta.TotalMilliseconds;
      if (response.Headers.RetryAfter?.Date is DateTimeOffset date && date.ToUnixTimeMilliseconds() - NowMs() is long diff && diff > 0) return diff;
      return null;
    }

    private static FirebaseException CreateThrottledException(long throttledUntilMs, int statusCode) =>
        new FirebaseException((int)RemoteConfigError.None, $"Fetch is throttled (HTTP {statusCode}) until {DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0L, throttledUntilMs)).UtcDateTime:O}.");
  }
}
