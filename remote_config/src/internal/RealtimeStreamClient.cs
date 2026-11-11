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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;
using Firebase.Platform;

namespace Firebase.RemoteConfig.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RealtimeStreamClient {
    private const string RealtimeBaseUrl = "https://firebaseremoteconfigrealtime.googleapis.com/v1/projects";
    private const int MaxAutoFetchAttempts = 3, MaxObjectChars = 64 * 1024;
    private static readonly HashSet<RealtimeStreamClient> ActiveClients = new HashSet<RealtimeStreamClient>();
    private static volatile bool _appHasFocus = true;

    internal static int ReconnectDelayMs { get; set; } = 150;
    internal static int AutoFetchRetryDelayMs { get; set; } = 150;
    internal static TimeSpan StreamRetryBudget { get; set; } = TimeSpan.FromSeconds(30);

    private readonly object _gate = new object();
    private readonly FirebaseRemoteConfig _owner;
    private readonly FirebaseApp _app;
    private readonly RemoteConfigStore _store;
    private readonly RemoteConfigFetchClient _fetchClient;
    private readonly CancellationToken _disposeToken;
    private event EventHandler<ConfigUpdateEventArgs> _listeners;
    private CancellationTokenSource _streamCts;
    private bool _isBackgrounded, _isFeatureDisabled, _reconnectRequested;
    private int _disposed;

    internal RealtimeStreamClient(
        FirebaseRemoteConfig owner, FirebaseApp app, RemoteConfigStore store,
        RemoteConfigFetchClient fetchClient, CancellationToken disposeToken) =>
        (_owner, _app, _store, _fetchClient, _disposeToken, _isBackgrounded) = (owner, app, store, fetchClient, disposeToken, !_appHasFocus);

#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnRuntimeInitFocusHook() => RegisterGlobalFocusHook();
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void OnEditorInitFocusHook() => RegisterGlobalFocusHook();
#endif
#if UNITY_5_3_OR_NEWER
    private static void RegisterGlobalFocusHook() {
      try { UnityEngine.Application.focusChanged -= OnGlobalUnityFocusChanged; UnityEngine.Application.focusChanged += OnGlobalUnityFocusChanged; }
      catch (Exception) {
        // Ignore in headless batchmode contexts.
      }
    }

    private static void OnGlobalUnityFocusChanged(bool hasFocus) {
      _appHasFocus = hasFocus;
      RealtimeStreamClient[] snapshot;
      lock (ActiveClients) {
        if (ActiveClients.Count == 0) return;
        snapshot = new RealtimeStreamClient[ActiveClients.Count];
        ActiveClients.CopyTo(snapshot);
      }
      for (int i = 0; i < snapshot.Length; i++) snapshot[i].SetBackgrounded(!hasFocus);
    }
#endif

    internal void AddListener(EventHandler<ConfigUpdateEventArgs> handler) {
      if (handler == null) return;
      if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FirebaseRemoteConfig));
      bool becameActive = false;
      lock (_gate) {
        if (_disposed != 0) throw new ObjectDisposedException(nameof(FirebaseRemoteConfig));
        if (_listeners == null) { becameActive = true; _isFeatureDisabled = false; }
        _listeners += handler;
        if (becameActive && !_isBackgrounded) StartStreamLocked();
      }
      if (becameActive && Volatile.Read(ref _disposed) == 0) lock (ActiveClients) ActiveClients.Add(this);
    }

    internal void RemoveListener(EventHandler<ConfigUpdateEventArgs> handler) {
      if (handler == null) return;
      CancellationTokenSource toCancel = null;
      bool becameEmpty = false;
      lock (_gate) { _listeners -= handler; if (_listeners == null) (becameEmpty, toCancel) = (true, DetachStreamLocked()); }
      if (becameEmpty) lock (ActiveClients) ActiveClients.Remove(this);
      CancelSafe(toCancel);
    }

    internal void SetBackgrounded(bool backgrounded) {
      CancellationTokenSource toCancel = null;
      lock (_gate) {
        if (_disposed != 0 || _isBackgrounded == backgrounded) return;
        _isBackgrounded = backgrounded;
        if (backgrounded) toCancel = DetachStreamLocked();
        else if (_listeners != null && !_isFeatureDisabled) StartStreamLocked();
      }
      CancelSafe(toCancel);
    }

    internal void Dispose() {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      lock (ActiveClients) ActiveClients.Remove(this);
      CancellationTokenSource toCancel;
      lock (_gate) { _listeners = null; toCancel = DetachStreamLocked(); }
      CancelSafe(toCancel);
    }

    private void StartStreamLocked() {
      if (_streamCts != null || _disposed != 0) return;
      _streamCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeToken);
      CancellationTokenSource cts = _streamCts;
      _ = Task.Run(() => RunStreamLoopAsync(cts));
    }

    private CancellationTokenSource DetachStreamLocked() { CancellationTokenSource cts = _streamCts; _streamCts = null; return cts; }

    private static void CancelSafe(CancellationTokenSource cts) {
      if (cts == null) return;
      try { cts.Cancel(); }
      catch (Exception) {
        // Already cancelled/disposed.
      }
    }

    private bool HasActiveListeners { get { lock (_gate) return _disposed == 0 && !_isBackgrounded && !_isFeatureDisabled && _listeners != null; } }

    private async Task RunStreamLoopAsync(CancellationTokenSource loopCts) {
      CancellationToken token = loopCts.Token;
      int initMs = Math.Max(50, ReconnectDelayMs);
      var backoff = new RetryBackoff(StreamRetryBudget, initialBackoffMs: initMs, maxBackoffMs: 10_000, maxJitterMs: 50);
      try {
        while (!token.IsCancellationRequested && HasActiveListeners) {
          long waitMs = _store.GetRealtimeBackoff().BackoffEndMs - RemoteConfigFetchClient.NowMs();
          if (waitMs > 0) { await Task.Delay((int)Math.Min(waitMs, 60_000L), token).ConfigureAwait(false); continue; }

          bool connectedOk = false, reconnectingAfterDirective = false;
          HttpResponseMessage response = null;
          try {
            response = await OpenStreamAsync(token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) {
              connectedOk = true;
              backoff.Reset();
              await _store.UpdateRealtimeBackoffAsync(0, 0).ConfigureAwait(false);
              await ReadInvalidationStreamAsync(response, token).ConfigureAwait(false);
              reconnectingAfterDirective = _reconnectRequested;
            } else if (HttpHelpers.IsRetryableStatusCode((int)response.StatusCode)) {
              int failed = _store.GetRealtimeBackoff().FailedStreams + 1;
              await _store.UpdateRealtimeBackoffAsync(RemoteConfigFetchClient.NowMs() + Math.Min(10_000L, (long)initMs << Math.Min(failed - 1, 8)), failed).ConfigureAwait(false);
              await backoff.DelayOrThrowAsync(false, null, token).ConfigureAwait(false);
              continue;
            } else { await DispatchErrorAsync(RemoteConfigError.ConfigUpdateStreamError, token).ConfigureAwait(false); break; }
          } catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
          catch (Exception ex) {
            if (token.IsCancellationRequested || !HasActiveListeners) break;
            if (connectedOk) { await Task.Delay(Math.Max(20, ReconnectDelayMs), token).ConfigureAwait(false); continue; }
            try { await backoff.DelayOrThrowAsync(HttpHelpers.IsTransportError(ex), ex, token).ConfigureAwait(false); continue; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception) { await DispatchErrorAsync(RemoteConfigError.ConfigUpdateStreamError, token).ConfigureAwait(false); break; }
          } finally { response?.Dispose(); }

          if (connectedOk && !reconnectingAfterDirective && !token.IsCancellationRequested && HasActiveListeners) {
            await Task.Delay(Math.Max(20, ReconnectDelayMs), token).ConfigureAwait(false);
          }
        }
      } catch (OperationCanceledException) {
        // Expected on unsubscribe, backgrounding, App.Dispose(), or Domain Reload.
      } finally {
        lock (_gate) if (ReferenceEquals(_streamCts, loopCts)) _streamCts = null;
        loopCts.Dispose();
      }
    }

    private async Task<HttpResponseMessage> OpenStreamAsync(CancellationToken token) {
      string project = RemoteConfigFetchClient.ResolveProjectIdentifier(_app), apiKey = _app.Options?.ApiKey, appId = _app.Options?.AppId;
      if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(appId)) {
        throw new FirebaseException((int)RemoteConfigError.ConfigUpdateStreamError, "Realtime Remote Config requires ProjectId, ApiKey, and AppId.");
      }
      (string fid, string fisAuthToken) = await FirebaseInterops.GetInstallationIdentityAsync(_app).ConfigureAwait(false);
      if (string.IsNullOrEmpty(fid)) fid = await _store.GetOrCreateFallbackFidAsync().ConfigureAwait(false);
      string appCheckToken = await FirebaseInterops.GetAppCheckTokenAsync(_app).ConfigureAwait(false);
      string etag = _store.GetETag();
      string url = $"{RealtimeBaseUrl}/{Uri.EscapeDataString(project)}/namespaces/" +
                   $"{RemoteConfigFetchClient.DefaultNamespace}:streamFetchInvalidations?key={Uri.EscapeDataString(apiKey)}";
      var payload = new Dictionary<string, object>(StringComparer.Ordinal) {
        ["project"] = project, ["namespace"] = RemoteConfigFetchClient.DefaultNamespace,
        ["lastKnownVersionNumber"] = _store.LatestTemplateVersion.ToString(CultureInfo.InvariantCulture),
        ["appId"] = appId, ["sdkVersion"] = FirebaseInterops.GetVersionInfoSdkVersion(), ["appInstanceId"] = fid,
      };
      using var request = new HttpRequestMessage(HttpMethod.Post, url) {
        Content = new StringContent(JsonHelper.Serialize(payload), Encoding.UTF8, "application/json"),
      };
      request.Headers.TryAddWithoutValidation("Accept", "application/json");
      request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", apiKey);
      request.Headers.TryAddWithoutValidation("x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
      request.Headers.TryAddWithoutValidation("If-None-Match", string.IsNullOrEmpty(etag) ? "*" : etag);
      if (!string.IsNullOrEmpty(fisAuthToken)) request.Headers.TryAddWithoutValidation("X-Goog-Firebase-Installations-Auth", fisAuthToken);
      if (!string.IsNullOrEmpty(appCheckToken)) request.Headers.TryAddWithoutValidation("X-Firebase-AppCheck", appCheckToken);
      HttpHelpers.SetDataCollectionHeaders(request, _app);
      return await RemoteConfigFetchClient.GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }

    private async Task ReadInvalidationStreamAsync(HttpResponseMessage response, CancellationToken token) {
      if (response.Content == null) return;
      _reconnectRequested = false;
      using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
      var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      using CancellationTokenRegistration reg = token.Register(() => {
        cancelTcs.TrySetResult(true);
        try { response.Dispose(); }
        catch (Exception) {
          // Best-effort socket abort on Mono/IL2CPP.
        }
      });

      byte[] byteBuf = ArrayPool<byte>.Shared.Rent(4096);
      char[] charBuf = ArrayPool<char>.Shared.Rent(4096);
      try {
        Decoder decoder = Encoding.UTF8.GetDecoder();
        var objBuilder = new StringBuilder(256);
        int braceDepth = 0;
        bool inString = false, escaped = false;
        while (!token.IsCancellationRequested && HasActiveListeners) {
          Task<int> readTask = stream.ReadAsync(byteBuf, 0, byteBuf.Length, token);
          if (!readTask.IsCompleted && await Task.WhenAny(readTask, cancelTcs.Task).ConfigureAwait(false) == cancelTcs.Task) token.ThrowIfCancellationRequested();
          int bytesRead = await readTask.ConfigureAwait(false);
          if (bytesRead <= 0) break;
          int charCount = decoder.GetChars(byteBuf, 0, bytesRead, charBuf, 0, flush: false);
          for (int i = 0; i < charCount; i++) {
            char c = charBuf[i];
            if (braceDepth > 0) {
              if (objBuilder.Length < MaxObjectChars) objBuilder.Append(c);
              if (inString) {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
              } else if (c == '"') inString = true;
              else if (c == '{') braceDepth++;
              else if (c == '}' && --braceDepth == 0) {
                string rawJson = objBuilder.ToString();
                objBuilder.Clear();
                await ProcessInvalidationObjectAsync(rawJson, token).ConfigureAwait(false);
                if (_isFeatureDisabled || _reconnectRequested || !HasActiveListeners) return;
              }
            } else if (c == '{') {
              braceDepth = 1; inString = escaped = false; objBuilder.Clear(); objBuilder.Append('{');
            }
          }
        }
      } finally {
        ArrayPool<byte>.Shared.Return(byteBuf);
        ArrayPool<char>.Shared.Return(charBuf);
      }
    }

    private async Task ProcessInvalidationObjectAsync(string rawJson, CancellationToken token) {
      Dictionary<string, object> msg = JsonHelper.ParseObject(rawJson);
      if (msg == null) { await DispatchErrorAsync(RemoteConfigError.ConfigUpdateMessageInvalid, token).ConfigureAwait(false); return; }
      if (JsonHelper.GetBool(msg, "featureDisabled", false)) {
        _isFeatureDisabled = true;
        await DispatchErrorAsync(RemoteConfigError.ConfigUpdateUnavailable, token).ConfigureAwait(false);
        return;
      }
      if (msg.ContainsKey("retryIntervalSeconds")) {
        long sec = Math.Max(0L, JsonHelper.GetLong(msg, "retryIntervalSeconds", 0));
        await _store.UpdateRealtimeBackoffAsync(RemoteConfigFetchClient.NowMs() + sec * 1000L, _store.GetRealtimeBackoff().FailedStreams).ConfigureAwait(false);
        _reconnectRequested = true;
      }
      if (msg.ContainsKey("latestTemplateVersionNumber")) {
        long targetVersion = JsonHelper.GetLong(msg, "latestTemplateVersionNumber", 0);
        if (targetVersion > _store.LatestTemplateVersion) await AutoFetchLatestAsync(targetVersion, token).ConfigureAwait(false);
      }
    }

    private async Task AutoFetchLatestAsync(long targetVersion, CancellationToken token) {
      for (int attempt = 1; attempt <= MaxAutoFetchAttempts; attempt++) {
        token.ThrowIfCancellationRequested();
        if (attempt > 1 && AutoFetchRetryDelayMs > 0) await Task.Delay(AutoFetchRetryDelayMs, token).ConfigureAwait(false);
        long reportedVersion;
        try { reportedVersion = await _fetchClient.FetchForRealtimeAsync(attempt, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { await DispatchErrorAsync(RemoteConfigError.ConfigUpdateNotFetched, token).ConfigureAwait(false); return; }
        if (reportedVersion < 0) {
          await _store.AdvanceTemplateVersionFloorAsync(targetVersion).ConfigureAwait(false);
          return;
        }
        if (reportedVersion == 0) await _store.AdvanceTemplateVersionFloorAsync(targetVersion).ConfigureAwait(false);
        if (_store.LatestTemplateVersion >= targetVersion) {
          IReadOnlyList<string> updatedKeys = _store.ComputeFetchedVsActivatedDiff();
          if (updatedKeys.Count > 0) {
            await DispatchEventAsync(new ConfigUpdateEventArgs { Error = RemoteConfigError.None, UpdatedKeys = updatedKeys }, token).ConfigureAwait(false);
          }
          return;
        }
      }
      await DispatchErrorAsync(RemoteConfigError.ConfigUpdateNotFetched, token).ConfigureAwait(false);
    }

    private Task DispatchErrorAsync(RemoteConfigError error, CancellationToken token) =>
        DispatchEventAsync(new ConfigUpdateEventArgs { Error = error, UpdatedKeys = Array.Empty<string>() }, token);

    private async Task DispatchEventAsync(ConfigUpdateEventArgs args, CancellationToken token) {
      if (token.IsCancellationRequested || Volatile.Read(ref _disposed) != 0) return;
      await FirebaseHandler.RunOnMainThreadAsync(() => {
        if (token.IsCancellationRequested || Volatile.Read(ref _disposed) != 0) return false;
        EventHandler<ConfigUpdateEventArgs> handler;
        lock (_gate) handler = _listeners;
        if (handler != null) Firebase.ExceptionAggregator.Wrap(() => handler(_owner, args));
        return true;
      }).ConfigureAwait(false);
    }
  }
}
