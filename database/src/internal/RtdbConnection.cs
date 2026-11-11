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
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;
using Firebase.Platform;

namespace Firebase.Database.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RtdbConnection : IDisposable {
    internal const int MaxFrameChars = 16384;
    private const int ReceiveChunkBytes = 16 * 1024; // Strictly < 85,000-byte LOH threshold.
    private const int KeepAliveIntervalMs = 45 * 1000;
    private const int ConnectTimeoutMs = 30 * 1000;

    internal static Func<Uri, CancellationToken, Task<WebSocket>> CustomWebSocketConnector { get; set; }

    private sealed class PendingRpc {
      internal long RequestId { get; }
      internal string Action { get; }
      internal Action<string, object> Callback { get; }

      internal PendingRpc(long requestId, string action, Action<string, object> callback) {
        RequestId = requestId;
        Action = action;
        Callback = callback;
      }
    }

    private sealed class OutstandingListen {
      internal QuerySpec Spec { get; }
      internal long? Tag { get; }
      internal Action<string, string, bool> OnComplete { get; }

      internal OutstandingListen(QuerySpec spec, long? tag, Action<string, string, bool> onComplete) {
        Spec = spec;
        Tag = tag;
        OnComplete = onComplete;
      }
    }

    private sealed class OutstandingWrite {
      internal long SeqId { get; }
      internal string Action { get; } // "p" or "m"
      internal RtdbPath Path { get; }
      internal object WireData { get; }
      internal string CompareHash { get; } // Non-null only for compare-and-put transactions
      internal Action<string, string> OnComplete { get; }
      internal bool SentOnCurrentSocket { get; set; }

      internal OutstandingWrite(
          long seqId,
          string action,
          RtdbPath path,
          object wireData,
          string compareHash,
          Action<string, string> onComplete) {
        SeqId = seqId;
        Action = action;
        Path = path;
        WireData = wireData;
        CompareHash = compareHash;
        OnComplete = onComplete;
      }
    }

    private sealed class OutstandingOnDisconnect {
      internal string Action { get; } // "o", "om", "oc"
      internal RtdbPath Path { get; }
      internal object WireData { get; }
      internal Action<string, string> OnComplete { get; }

      internal OutstandingOnDisconnect(
          string action,
          RtdbPath path,
          object wireData,
          Action<string, string> onComplete) {
        Action = action;
        Path = path;
        WireData = wireData;
        OnComplete = onComplete;
      }
    }

    private readonly FirebaseApp _app;
    private readonly ParsedDatabaseUrl _parsedUrl;
    private readonly DatabaseRepo _repo;
    private readonly object _connLock = new object();
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private readonly RetryBackoff _backoff;

    private readonly Dictionary<QuerySpec, OutstandingListen> _listens =
        new Dictionary<QuerySpec, OutstandingListen>();
    private readonly SortedDictionary<long, OutstandingWrite> _writes =
        new SortedDictionary<long, OutstandingWrite>();
    private readonly Queue<OutstandingOnDisconnect> _pendingOnDisconnects =
        new Queue<OutstandingOnDisconnect>();
    private readonly Dictionary<long, PendingRpc> _inFlightRpcs =
        new Dictionary<long, PendingRpc>();

    private WebSocket _activeSocket;
    private SemaphoreSlim _sendLock;
    private CancellationTokenSource _activeSessionCts;
    private TaskCompletionSource<bool> _activeSessionStopTcs;
    private TaskCompletionSource<bool> _onlineSignal =
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _desiredOnline = true;
    private bool _isReady;
    private bool _disposed;
    private bool _forceAuthRefresh;
    private bool _immediateReconnect;
    private string _redirectHost;
    private string _lastSessionId;
    private string _lastAuthToken = string.Empty;
    private string _lastAppCheckToken = string.Empty;
    private long _nextRequestId;
    private long _nextWriteSeq;
    private bool _sentFirstStats;

    internal RtdbConnection(FirebaseApp app, ParsedDatabaseUrl parsedUrl, DatabaseRepo repo) {
      _app = app;
      _parsedUrl = parsedUrl;
      _repo = repo;
      _backoff = new RetryBackoff(
          maxRetryTime: TimeSpan.MaxValue,
          initialBackoffMs: 500,
          maxBackoffMs: 30000,
          maxJitterMs: 200);
    }

    internal bool IsDesiredOnline {
      get {
        lock (_connLock) return _desiredOnline && !_disposed;
      }
    }

    internal void Start() {
      _ = Task.Run(ConnectionLoopAsync);
    }

    internal void GoOffline() {
      CancellationTokenSource sessionToCancel = null;
      TaskCompletionSource<bool> stopSignal = null;
      bool cancelImmediately = false;
      lock (_connLock) {
        if (_disposed || !_desiredOnline) return;
        _desiredOnline = false;
        cancelImmediately = !_isReady;
        _isReady = false;
        _onlineSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionToCancel = _activeSessionCts;
        stopSignal = _activeSessionStopTcs;
      }
      stopSignal?.TrySetResult(true);
      if (cancelImmediately) {
        SafeCancel(sessionToCancel);
      }
    }

    internal void GoOnline() {
      TaskCompletionSource<bool> signalToSet = null;
      lock (_connLock) {
        if (_disposed || _desiredOnline) return;
        _desiredOnline = true;
        _backoff.Reset();
        signalToSet = _onlineSignal;
      }
      signalToSet?.TrySetResult(true);
    }

    internal void Listen(QuerySpec spec, long? tag, Action<string, string, bool> onComplete) {
      bool sendNow;
      var item = new OutstandingListen(spec, tag, onComplete);
      lock (_connLock) {
        if (_disposed) return;
        _listens[spec] = item;
        sendNow = _isReady;
      }
      if (sendNow) {
        _ = SendListenAsync(item);
      }
    }

    internal void Unlisten(QuerySpec spec) {
      OutstandingListen removed = null;
      bool sendNow = false;
      lock (_connLock) {
        if (_disposed) return;
        if (_listens.TryGetValue(spec, out removed)) {
          _listens.Remove(spec);
          sendNow = _isReady;
        }
      }
      if (sendNow && removed != null) {
        var payload = new Dictionary<string, object>(3) {
          ["p"] = removed.Spec.Path.ToWireString(),
        };
        if (removed.Tag.HasValue) {
          payload["q"] = removed.Spec.Params.ToWireQueryParams();
          payload["t"] = removed.Tag.Value;
        }
        _ = SendActionAsync("n", payload, sensitive: false, callback: null);
      }
    }

    internal long EnqueuePut(
        RtdbPath path,
        object wireData,
        string compareHash,
        Action<string, string> onComplete) {
      return EnqueueWriteInternal("p", path, wireData, compareHash, onComplete);
    }

    internal long EnqueueMerge(
        RtdbPath path,
        object wireData,
        Action<string, string> onComplete) {
      return EnqueueWriteInternal("m", path, wireData, compareHash: null, onComplete);
    }

    internal void CancelWrite(long seqId) {
      if (seqId < 0) return;
      lock (_connLock) {
        _writes.Remove(seqId);
      }
    }

    private long EnqueueWriteInternal(
        string action,
        RtdbPath path,
        object wireData,
        string compareHash,
        Action<string, string> onComplete) {
      OutstandingWrite write = null;
      bool sendNow = false;
      bool disposed = false;
      lock (_connLock) {
        if (_disposed) {
          disposed = true;
        } else {
          long seq = ++_nextWriteSeq;
          write = new OutstandingWrite(seq, action, path, wireData, compareHash, onComplete);
          _writes[seq] = write;
          sendNow = _isReady;
        }
      }
      if (disposed) {
        onComplete?.Invoke("disconnected", "Database disposed.");
        return -1;
      }
      if (sendNow) {
        _ = CheckAuthAndSendWriteAsync(write);
      }
      return write.SeqId;
    }

    internal void EnqueueOnDisconnect(
        string action,
        RtdbPath path,
        object wireData,
        Action<string, string> onComplete) {
      OutstandingOnDisconnect item = null;
      bool sendNow = false;
      bool disposed = false;
      lock (_connLock) {
        if (_disposed) {
          disposed = true;
        } else {
          item = new OutstandingOnDisconnect(action, path, wireData, onComplete);
          sendNow = _isReady;
          if (!sendNow) {
            _pendingOnDisconnects.Enqueue(item);
          }
        }
      }
      if (disposed) {
        onComplete?.Invoke("disconnected", "Database disposed.");
        return;
      }
      if (sendNow) {
        _ = SendOnDisconnectNowAsync(item);
      }
    }

    internal void PurgeOutstandingWrites() {
      var writeCallbacks = new List<Action<string, string>>();
      var odCallbacks = new List<Action<string, string>>();
      lock (_connLock) {
        foreach (KeyValuePair<long, OutstandingWrite> kv in _writes) {
          if (kv.Value.OnComplete != null) {
            writeCallbacks.Add(kv.Value.OnComplete);
          }
        }
        _writes.Clear();
        while (_pendingOnDisconnects.Count > 0) {
          OutstandingOnDisconnect od = _pendingOnDisconnects.Dequeue();
          if (od.OnComplete != null) {
            odCallbacks.Add(od.OnComplete);
          }
        }
      }

      for (int i = 0; i < writeCallbacks.Count; i++) {
        writeCallbacks[i].Invoke("write_canceled", "Write was canceled locally via PurgeOutstandingWrites.");
      }
      for (int i = 0; i < odCallbacks.Count; i++) {
        odCallbacks[i].Invoke("write_canceled", "OnDisconnect was canceled locally via PurgeOutstandingWrites.");
      }
    }

    private async Task ConnectionLoopAsync() {
      bool consecutiveFailure = false;
      while (!_disposeCts.IsCancellationRequested) {
        Task waitOnlineTask = null;
        bool skipBackoff;
        bool forceRefresh;
        string redirectHost;
        string lastSessionId;
        CancellationTokenSource sessionCts = null;

        lock (_connLock) {
          if (_disposed) return;
          if (!_desiredOnline) {
            waitOnlineTask = _onlineSignal.Task;
          }
          skipBackoff = _immediateReconnect || !consecutiveFailure;
          _immediateReconnect = false;
          forceRefresh = _forceAuthRefresh;
          _forceAuthRefresh = false;
          redirectHost = _redirectHost;
          lastSessionId = _lastSessionId;
        }

        if (waitOnlineTask != null) {
          consecutiveFailure = false;
          try {
            Task completed = await Task.WhenAny(
                waitOnlineTask,
                Task.Delay(Timeout.Infinite, _disposeCts.Token)).ConfigureAwait(false);
            if (completed != waitOnlineTask) return;
          } catch (OperationCanceledException) {
            return;
          }
          continue;
        }

        if (!skipBackoff) {
          try {
            await _backoff.DelayOrThrowAsync(
                transportFailure: false,
                lastError: null,
                token: _disposeCts.Token,
                log: _repo.Log,
                context: "FirebaseDatabase.WebSocket").ConfigureAwait(false);
          } catch (OperationCanceledException) {
            return;
          }
          if (!IsDesiredOnline) {
            continue;
          }
        }

        sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        var sessionStopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_connLock) {
          if (_disposed) {
            sessionCts.Dispose();
            return;
          }
          if (!_desiredOnline) {
            sessionCts.Dispose();
            continue;
          }
          _activeSessionCts = sessionCts;
          _activeSessionStopTcs = sessionStopTcs;
        }

        WebSocket ws = null;
        var sendLock = new SemaphoreSlim(1, 1);
        bool handshakeSucceeded = false;

        try {
          string authToken = await FirebaseInterops.GetAuthTokenAsync(_app, forceRefresh).ConfigureAwait(false) ?? string.Empty;
          string appCheckToken = await FirebaseInterops.GetAppCheckTokenAsync(_app, limitedUse: false, forceRefresh: false).ConfigureAwait(false) ?? string.Empty;

          Uri wsUri = _parsedUrl.BuildWebSocketUri(redirectHost, lastSessionId);
          _repo.Log(LogLevel.Debug, $"Connecting to RTDB WebSocket: {wsUri}");

          using (var connectTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token)) {
            connectTimeoutCts.CancelAfter(ConnectTimeoutMs);
            if (CustomWebSocketConnector != null) {
              ws = await CustomWebSocketConnector(wsUri, connectTimeoutCts.Token).ConfigureAwait(false);
            } else {
              var cws = new ClientWebSocket();
              cws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
              if (!string.IsNullOrEmpty(appCheckToken)) {
                try {
                  cws.Options.SetRequestHeader("X-Firebase-AppCheck", appCheckToken);
                } catch (Exception ex) {
                  // Restricted header on some WebSocket runtimes; AppCheck is still sent via "appcheck" wire frame.
                  _repo.Log(LogLevel.Verbose, $"Skipping X-Firebase-AppCheck handshake header: {ex.Message}");
                }
              }
              try {
                cws.Options.SetRequestHeader(
                    "User-Agent",
                    $"Firebase/5/{FirebaseInterops.GetVersionInfoSdkVersion()}/unity/csharp");
              } catch (Exception ex) {
                // Some platforms disallow overriding User-Agent on ClientWebSocket; SDK version is sent via "s" stats frame.
                _repo.Log(LogLevel.Verbose, $"Skipping User-Agent handshake header: {ex.Message}");
              }
              await cws.ConnectAsync(wsUri, connectTimeoutCts.Token).ConfigureAwait(false);
              ws = cws;
            }
          }

          var helloTcs = new TaskCompletionSource<(long Ts, string SessionId, string Host)>(
              TaskCreationOptions.RunContinuationsAsynchronously);

          lock (_connLock) {
            _activeSocket = ws;
            _sendLock = sendLock;
            _lastAuthToken = authToken;
            _lastAppCheckToken = appCheckToken;
          }

          Task receiveTask = ReceiveFramesLoopAsync(ws, helloTcs, sessionCts.Token);

          using (var helloTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token)) {
            helloTimeoutCts.CancelAfter(ConnectTimeoutMs);
            Task finished = await Task.WhenAny(
                helloTcs.Task,
                receiveTask,
                Task.Delay(Timeout.Infinite, helloTimeoutCts.Token)).ConfigureAwait(false);
            if (finished != helloTcs.Task) {
              throw new WebSocketException("RTDB WebSocket closed or timed out before server hello handshake.");
            }
          }

          (long serverTs, string sessionId, string helloHost) = await helloTcs.Task.ConfigureAwait(false);
          handshakeSucceeded = true;
          consecutiveFailure = false;
          _backoff.Reset();

          lock (_connLock) {
            if (!string.IsNullOrEmpty(sessionId)) {
              _lastSessionId = sessionId;
            }
            if (_parsedUrl.Secure && !string.IsNullOrEmpty(helloHost) &&
                !string.Equals(helloHost, _parsedUrl.Host, StringComparison.OrdinalIgnoreCase)) {
              _redirectHost = helloHost;
            }
          }

          _repo.OnServerHello(serverTs);

          // Send first-connection SDK stats (best-effort)
          if (!_sentFirstStats) {
            _sentFirstStats = true;
            string cleanVer = (FirebaseInterops.GetVersionInfoSdkVersion() ?? "14-1-0").Replace('.', '-');
            var statsPayload = new Dictionary<string, object>(1) {
              ["c"] = new Dictionary<string, object>(1) {
                [$"sdk.unity.{cleanVer}"] = 1L,
              },
            };
            _ = SendActionOnSocketAsync(ws, sendLock, "s", statsPayload, sensitive: false, callback: null, sessionCts.Token);
          }

          // Send AppCheck & Auth tokens before restoring listens/writes
          if (!string.IsNullOrEmpty(appCheckToken)) {
            await SendAppCheckAsync(ws, sendLock, appCheckToken, sessionCts.Token).ConfigureAwait(false);
          }

          if (!string.IsNullOrEmpty(authToken)) {
            bool authOk = await SendInitialAuthAsync(ws, sendLock, authToken, sessionCts.Token).ConfigureAwait(false);
            if (!authOk) {
              consecutiveFailure = true;
              continue;
            }
          } else {
            _repo.OnAuthStatusChanged(false);
          }

          // Mark connection Ready & restore listens + pending writes + OnDisconnects!
          List<OutstandingListen> listensToRestore;
          List<OutstandingOnDisconnect> onDisconnectsToRestore;
          List<OutstandingWrite> writesToRestore;
          lock (_connLock) {
            if (!_desiredOnline || _disposed || sessionCts.IsCancellationRequested || sessionStopTcs.Task.IsCompleted) {
              continue;
            }
            _isReady = true;
            listensToRestore = new List<OutstandingListen>(_listens.Values);
            onDisconnectsToRestore = new List<OutstandingOnDisconnect>(_pendingOnDisconnects);
            _pendingOnDisconnects.Clear();
            writesToRestore = new List<OutstandingWrite>(_writes.Values);
          }

          _repo.OnConnected();

          for (int i = 0; i < listensToRestore.Count; i++) {
            await SendListenOnSocketAsync(ws, sendLock, listensToRestore[i], sessionCts.Token).ConfigureAwait(false);
          }
          for (int i = 0; i < onDisconnectsToRestore.Count; i++) {
            await SendOnDisconnectOnSocketAsync(ws, sendLock, onDisconnectsToRestore[i], sessionCts.Token).ConfigureAwait(false);
          }
          for (int i = 0; i < writesToRestore.Count; i++) {
            await SendWriteOnSocketAsync(ws, sendLock, writesToRestore[i], sessionCts.Token).ConfigureAwait(false);
          }

          using var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token);
          Task keepAliveTask = KeepAliveLoopAsync(ws, sendLock, keepAliveCts.Token);

          await Task.WhenAny(receiveTask, sessionStopTcs.Task).ConfigureAwait(false);
          SafeCancel(keepAliveCts);
          if (sessionStopTcs.Task.IsCompleted &&
              (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)) {
            bool sendLockAcquired = false;
            try {
              using var closeCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
              closeCts.CancelAfter(2000);
              await sendLock.WaitAsync(closeCts.Token).ConfigureAwait(false);
              sendLockAcquired = true;
              if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived) {
                await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", closeCts.Token)
                    .ConfigureAwait(false);
              }
            } catch (Exception) {
              // Ignore secondary close errors if the socket faulted, timed out, or peer already closed.
            } finally {
              if (sendLockAcquired) {
                sendLock.Release();
              }
            }
            await Task.WhenAny(receiveTask, Task.Delay(2000, _disposeCts.Token)).ConfigureAwait(false);
          }
          SafeCancel(sessionCts);
          try {
            await receiveTask.ConfigureAwait(false);
          } catch (Exception) {
            // Expected cancellation or socket-closed exception on teardown.
          }
          try {
            await keepAliveTask.ConfigureAwait(false);
          } catch (Exception) {
            // Expected cancellation or socket-closed exception when stopping keep-alive loop on teardown.
          }
        } catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested) {
          // Repo is disposing; clean up below and exit.
        } catch (Exception ex) {
          if (!_disposeCts.IsCancellationRequested && IsDesiredOnline) {
            consecutiveFailure = true;
            _repo.Log(LogLevel.Debug, $"RTDB WebSocket session ended: {ex.Message}");
          }
        } finally {
          // Synchronously clean up active socket & complete WebSocket close BEFORE next session starts!
          var disconnectedTransactions = new List<Action<string, string>>();
          lock (_connLock) {
            _isReady = false;
            if (ReferenceEquals(_activeSocket, ws)) {
              _activeSocket = null;
              _sendLock = null;
            }
            if (ReferenceEquals(_activeSessionCts, sessionCts)) {
              _activeSessionCts = null;
            }
            if (ReferenceEquals(_activeSessionStopTcs, sessionStopTcs)) {
              _activeSessionStopTcs = null;
            }
            _inFlightRpcs.Clear();

            // Cancel sent compare-and-put transactions so DatabaseRepo can re-run/retry them on reconnect!
            var txSeqs = new List<long>();
            foreach (KeyValuePair<long, OutstandingWrite> kv in _writes) {
              if (kv.Value.CompareHash != null && kv.Value.SentOnCurrentSocket) {
                txSeqs.Add(kv.Key);
                if (kv.Value.OnComplete != null) {
                  disconnectedTransactions.Add(kv.Value.OnComplete);
                }
              } else {
                kv.Value.SentOnCurrentSocket = false;
              }
            }
            for (int i = 0; i < txSeqs.Count; i++) {
              _writes.Remove(txSeqs[i]);
            }
          }

          if (ws != null) {
            try {
              if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived) {
                using var closeCts = new CancellationTokenSource(1500);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", closeCts.Token).ConfigureAwait(false);
              }
            } catch (Exception) {
              // Socket may already be aborted or reset by peer.
            }
            try {
              ws.Dispose();
            } catch (Exception) {
              // Ignore secondary disposal errors on an already-faulted WebSocket.
            }
          }

          sendLock.Dispose();
          sessionCts.Dispose();

          for (int i = 0; i < disconnectedTransactions.Count; i++) {
            disconnectedTransactions[i].Invoke("disconnected", "Connection lost before transaction ack.");
          }

          if (handshakeSucceeded && !_disposeCts.IsCancellationRequested) {
            _repo.OnDisconnected();
          }
        }
      }
    }

    private async Task KeepAliveLoopAsync(WebSocket ws, SemaphoreSlim sendLock, CancellationToken ct) {
      while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open) {
        await Task.Delay(KeepAliveIntervalMs, ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || ws.State != WebSocketState.Open) break;
        await SendRawFrameAsync(ws, sendLock, "0", ct).ConfigureAwait(false);
      }
    }

    private async Task ReceiveFramesLoopAsync(
        WebSocket ws,
        TaskCompletionSource<(long Ts, string SessionId, string Host)> helloTcs,
        CancellationToken ct) {
      byte[] byteBuf = ArrayPool<byte>.Shared.Rent(ReceiveChunkBytes);
      var msgStream = new MemoryStream(1024);
      var multiFrameBuilder = new StringBuilder();
      int expectedFrames = 0;

      try {
        while (!ct.IsCancellationRequested &&
               (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseSent)) {
          msgStream.SetLength(0);
          WebSocketReceiveResult recv;
          do {
            recv = await ws.ReceiveAsync(new ArraySegment<byte>(byteBuf), ct).ConfigureAwait(false);
            if (recv.MessageType == WebSocketMessageType.Close) {
              return;
            }
            if (recv.Count > 0) {
              msgStream.Write(byteBuf, 0, recv.Count);
            }
          } while (!recv.EndOfMessage);

          string frame = Encoding.UTF8.GetString(msgStream.GetBuffer(), 0, (int)msgStream.Length);
          if (expectedFrames > 0) {
            multiFrameBuilder.Append(frame);
            expectedFrames--;
            if (expectedFrames == 0) {
              string assembled = multiFrameBuilder.ToString();
              multiFrameBuilder.Clear();
              ProcessWireEnvelope(assembled, helloTcs);
            }
          } else {
            if (frame.Length <= 6 &&
                int.TryParse(frame, NumberStyles.None, CultureInfo.InvariantCulture, out int numFrames)) {
              if (numFrames > 0) {
                expectedFrames = numFrames;
                multiFrameBuilder.Clear();
              }
              // numFrames == 0 ("0") is a server keep-alive no-op.
              continue;
            }
            ProcessWireEnvelope(frame, helloTcs);
          }
        }
      } finally {
        ArrayPool<byte>.Shared.Return(byteBuf);
        msgStream.Dispose();
      }
    }

    private void ProcessWireEnvelope(
        string json,
        TaskCompletionSource<(long Ts, string SessionId, string Host)> helloTcs) {
      if (string.IsNullOrWhiteSpace(json)) return;
      Dictionary<string, object> env = JsonHelper.ParseObject(json);
      if (env == null) return;

      string envType = JsonHelper.GetString(env, "t");
      if (envType == "c") {
        Dictionary<string, object> ctrl = JsonHelper.GetMap(env, "d");
        if (ctrl == null) return;
        string ctrlType = JsonHelper.GetString(ctrl, "t");
        if (ctrlType == "h") {
          Dictionary<string, object> helloData = JsonHelper.GetMap(ctrl, "d");
          long ts = JsonHelper.GetLong(helloData, "ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
          string sessionId = JsonHelper.GetString(helloData, "s", string.Empty);
          string host = JsonHelper.GetString(helloData, "h", string.Empty);
          helloTcs.TrySetResult((ts, sessionId, host));
        } else if (ctrlType == "r") {
          string newHost = JsonHelper.GetString(ctrl, "d", string.Empty);
          CancellationTokenSource toCancel;
          lock (_connLock) {
            if (!string.IsNullOrEmpty(newHost)) {
              _redirectHost = newHost;
            }
            _immediateReconnect = true;
            toCancel = _activeSessionCts;
          }
          SafeCancel(toCancel);
        } else if (ctrlType == "s") {
          string reason = JsonHelper.GetString(ctrl, "d", "shutdown");
          _repo.Log(LogLevel.Warning, $"RTDB server shutdown control message: {reason}");
          CancellationTokenSource toCancel;
          lock (_connLock) toCancel = _activeSessionCts;
          SafeCancel(toCancel);
        }
      } else if (envType == "d") {
        Dictionary<string, object> dataMsg = JsonHelper.GetMap(env, "d");
        if (dataMsg == null) return;

        if (dataMsg.ContainsKey("r")) {
          long reqId = JsonHelper.GetLong(dataMsg, "r", -1);
          dataMsg.TryGetValue("b", out object respBodyObj);
          PendingRpc pending = null;
          lock (_connLock) {
            if (_inFlightRpcs.TryGetValue(reqId, out pending)) {
              _inFlightRpcs.Remove(reqId);
            }
          }
          if (pending?.Callback != null) {
            Dictionary<string, object> respMap = respBodyObj as Dictionary<string, object>;
            string status = JsonHelper.GetString(respMap, "s", "ok");
            object dVal = null;
            respMap?.TryGetValue("d", out dVal);
            pending.Callback(status, dVal);
          }
        } else if (dataMsg.ContainsKey("a")) {
          string action = JsonHelper.GetString(dataMsg, "a");
          Dictionary<string, object> body = JsonHelper.GetMap(dataMsg, "b");
          HandleServerPush(action, body);
        }
      }
    }

    private void HandleServerPush(string action, Dictionary<string, object> body) {
      if (body == null) return;
      switch (action) {
        case "d":
        case "m": {
          bool isMerge = action == "m";
          string pathStr = JsonHelper.GetString(body, "p", string.Empty);
          body.TryGetValue("d", out object payload);
          long? tag = body.ContainsKey("t") && body["t"] != null
              ? JsonHelper.GetLong(body, "t", 0)
              : (long?)null;
          if (isMerge && payload is Dictionary<string, object> emptyCheck && emptyCheck.Count == 0) {
            return;
          }
          _repo.OnServerDataPush(RtdbPath.ParseUnchecked(pathStr), payload, isMerge, tag);
          break;
        }
        case "c": {
          string pathStr = JsonHelper.GetString(body, "p", string.Empty);
          long? tag = body.ContainsKey("t") && body["t"] != null
              ? JsonHelper.GetLong(body, "t", 0)
              : (long?)null;
          RtdbPath revokedPath = RtdbPath.ParseUnchecked(pathStr);
          lock (_connLock) {
            var toRemove = new List<QuerySpec>();
            foreach (KeyValuePair<QuerySpec, OutstandingListen> kv in _listens) {
              if (tag.HasValue) {
                if (kv.Value.Tag == tag.Value) toRemove.Add(kv.Key);
              } else if (kv.Key.Path.Equals(revokedPath)) {
                toRemove.Add(kv.Key);
              }
            }
            for (int i = 0; i < toRemove.Count; i++) {
              _listens.Remove(toRemove[i]);
            }
          }
          _repo.OnListenRevoked(revokedPath, tag);
          break;
        }
        case "ac": {
          CancellationTokenSource toCancel;
          lock (_connLock) {
            _lastAuthToken = string.Empty;
            _forceAuthRefresh = true;
            toCancel = _activeSessionCts;
          }
          _repo.OnAuthStatusChanged(false);
          SafeCancel(toCancel);
          break;
        }
        case "sd": {
          string debugMsg = JsonHelper.GetString(body, "msg");
          if (!string.IsNullOrEmpty(debugMsg)) {
            _repo.Log(LogLevel.Info, $"RTDB Security Debug: {debugMsg}");
          }
          break;
        }
      }
    }

    private async Task<bool> SendInitialAuthAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        string authToken,
        CancellationToken ct) {
      var authTcs = new TaskCompletionSource<(string Status, object Data)>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var payload = new Dictionary<string, object>(1) { ["cred"] = authToken };
      await SendActionOnSocketAsync(
          ws,
          sendLock,
          "auth",
          payload,
          sensitive: true,
          callback: (s, d) => authTcs.TrySetResult((s, d)),
          ct).ConfigureAwait(false);

      Task finished = await Task.WhenAny(authTcs.Task, Task.Delay(ConnectTimeoutMs, ct)).ConfigureAwait(false);
      if (finished != authTcs.Task) {
        return false;
      }
      (string status, object data) = await authTcs.Task.ConfigureAwait(false);
      if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) {
        _repo.OnAuthStatusChanged(true);
        return true;
      }
      lock (_connLock) {
        _lastAuthToken = string.Empty;
        _forceAuthRefresh = true;
      }
      _repo.OnAuthStatusChanged(false);
      _repo.Log(LogLevel.Warning, $"RTDB authentication rejected ({status}): {data}");
      return false;
    }

    private Task SendAppCheckAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        string appCheckToken,
        CancellationToken ct) {
      var payload = new Dictionary<string, object>(1) { ["token"] = appCheckToken };
      return SendActionOnSocketAsync(ws, sendLock, "appcheck", payload, sensitive: true, callback: null, ct);
    }

    private async Task RefreshAuthIfChangedAsync(WebSocket ws, SemaphoreSlim sendLock, CancellationToken ct) {
      string currentToken = await FirebaseInterops.GetAuthTokenAsync(_app, false).ConfigureAwait(false) ?? string.Empty;
      bool changed;
      lock (_connLock) {
        changed = !string.Equals(currentToken, _lastAuthToken, StringComparison.Ordinal);
        if (changed) _lastAuthToken = currentToken;
      }
      if (!changed) return;

      if (string.IsNullOrEmpty(currentToken)) {
        await SendActionOnSocketAsync(
            ws, sendLock, "unauth", new Dictionary<string, object>(), sensitive: false, callback: null, ct).ConfigureAwait(false);
        _repo.OnAuthStatusChanged(false);
      } else {
        await SendActionOnSocketAsync(
            ws,
            sendLock,
            "auth",
            new Dictionary<string, object>(1) { ["cred"] = currentToken },
            sensitive: true,
            callback: (s, _) => _repo.OnAuthStatusChanged(s == "ok"),
            ct).ConfigureAwait(false);
      }
    }

    private async Task SendListenAsync(OutstandingListen listen) {
      WebSocket ws;
      SemaphoreSlim sendLock;
      CancellationToken ct;
      lock (_connLock) {
        if (!_isReady || _activeSocket == null) return;
        ws = _activeSocket;
        sendLock = _sendLock;
        ct = _activeSessionCts?.Token ?? CancellationToken.None;
      }
      try {
        await RefreshAuthIfChangedAsync(ws, sendLock, ct).ConfigureAwait(false);
        await SendListenOnSocketAsync(ws, sendLock, listen, ct).ConfigureAwait(false);
      } catch (Exception ex) {
        // Listen remains in _listens and is automatically re-sent upon WebSocket reconnect.
        _repo.Log(LogLevel.Debug, $"Transient error sending RTDB listen (will retry on reconnect): {ex.Message}");
      }
    }

    private Task SendListenOnSocketAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        OutstandingListen listen,
        CancellationToken ct) {
      var payload = new Dictionary<string, object>(3) {
        ["p"] = listen.Spec.Path.ToWireString(),
      };
      if (listen.Tag.HasValue) {
        payload["q"] = listen.Spec.Params.ToWireQueryParams();
        payload["t"] = listen.Tag.Value;
      }
      return SendActionOnSocketAsync(
          ws,
          sendLock,
          "q",
          payload,
          sensitive: false,
          callback: (status, data) => {
            bool isOk = string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase);
            bool hasNoIndexWarning = isOk && HasServerWarning(data, "no_index");
            if (!isOk) {
              lock (_connLock) {
                _listens.Remove(listen.Spec);
              }
            }
            listen.OnComplete?.Invoke(status, FormatErrorDetail(data), hasNoIndexWarning);
          },
          ct);
    }

    private async Task CheckAuthAndSendWriteAsync(OutstandingWrite write) {
      WebSocket ws;
      SemaphoreSlim sendLock;
      CancellationToken ct;
      lock (_connLock) {
        if (!_isReady || _activeSocket == null || !_writes.ContainsKey(write.SeqId)) return;
        ws = _activeSocket;
        sendLock = _sendLock;
        ct = _activeSessionCts?.Token ?? CancellationToken.None;
      }
      try {
        await RefreshAuthIfChangedAsync(ws, sendLock, ct).ConfigureAwait(false);
        await SendWriteOnSocketAsync(ws, sendLock, write, ct).ConfigureAwait(false);
      } catch (Exception ex) {
        // Write remains in _writes and is automatically replayed in order upon WebSocket reconnect.
        _repo.Log(LogLevel.Debug, $"Transient error sending RTDB write (will replay on reconnect): {ex.Message}");
      }
    }

    private Task SendWriteOnSocketAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        OutstandingWrite write,
        CancellationToken ct) {
      lock (_connLock) {
        if (!_writes.ContainsKey(write.SeqId)) return Task.CompletedTask;
        write.SentOnCurrentSocket = true;
      }
      var payload = new Dictionary<string, object>(3) {
        ["p"] = write.Path.ToWireString(),
        ["d"] = write.WireData,
      };
      if (write.CompareHash != null) {
        payload["h"] = write.CompareHash;
      }
      return SendActionOnSocketAsync(
          ws,
          sendLock,
          write.Action,
          payload,
          sensitive: false,
          callback: (status, data) => {
            bool wasPresent;
            lock (_connLock) {
              wasPresent = _writes.Remove(write.SeqId);
            }
            if (wasPresent) {
              write.OnComplete?.Invoke(status, FormatErrorDetail(data));
            }
          },
          ct);
    }

    private async Task SendOnDisconnectNowAsync(OutstandingOnDisconnect item) {
      WebSocket ws;
      SemaphoreSlim sendLock;
      CancellationToken ct;
      lock (_connLock) {
        if (!_isReady || _activeSocket == null) {
          _pendingOnDisconnects.Enqueue(item);
          return;
        }
        ws = _activeSocket;
        sendLock = _sendLock;
        ct = _activeSessionCts?.Token ?? CancellationToken.None;
      }
      try {
        await RefreshAuthIfChangedAsync(ws, sendLock, ct).ConfigureAwait(false);
        await SendOnDisconnectOnSocketAsync(ws, sendLock, item, ct).ConfigureAwait(false);
      } catch (Exception) {
        lock (_connLock) {
          if (!_disposed) _pendingOnDisconnects.Enqueue(item);
        }
      }
    }

    private Task SendOnDisconnectOnSocketAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        OutstandingOnDisconnect item,
        CancellationToken ct) {
      var payload = new Dictionary<string, object>(2) {
        ["p"] = item.Path.ToWireString(),
        ["d"] = item.WireData,
      };
      return SendActionOnSocketAsync(
          ws,
          sendLock,
          item.Action,
          payload,
          sensitive: false,
          callback: (status, data) => item.OnComplete?.Invoke(status, FormatErrorDetail(data)),
          ct);
    }

    private Task SendActionAsync(
        string action,
        Dictionary<string, object> body,
        bool sensitive,
        Action<string, object> callback) {
      WebSocket ws;
      SemaphoreSlim sendLock;
      CancellationToken ct;
      lock (_connLock) {
        if (!_isReady || _activeSocket == null) return Task.CompletedTask;
        ws = _activeSocket;
        sendLock = _sendLock;
        ct = _activeSessionCts?.Token ?? CancellationToken.None;
      }
      return SendActionOnSocketAsync(ws, sendLock, action, body, sensitive, callback, ct);
    }

    private async Task SendActionOnSocketAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        string action,
        Dictionary<string, object> body,
        bool sensitive,
        Action<string, object> callback,
        CancellationToken ct) {
      long reqId;
      lock (_connLock) {
        reqId = ++_nextRequestId;
        if (callback != null) {
          _inFlightRpcs[reqId] = new PendingRpc(reqId, action, callback);
        }
      }

      var envelope = new Dictionary<string, object>(2) {
        ["t"] = "d",
        ["d"] = new Dictionary<string, object>(3) {
          ["r"] = reqId,
          ["a"] = action,
          ["b"] = body,
        },
      };

      string json = JsonHelper.Serialize(envelope);
      if (!sensitive) {
        _repo.Log(LogLevel.Debug, $"RTDB -> {json}");
      }

      if (json.Length <= MaxFrameChars) {
        await SendRawFrameAsync(ws, sendLock, json, ct).ConfigureAwait(false);
        return;
      }

      int frameCount = (json.Length - 1) / MaxFrameChars + 1;
      await sendLock.WaitAsync(ct).ConfigureAwait(false);
      try {
        await SendSingleStringUnlockedAsync(ws, frameCount.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        for (int offset = 0; offset < json.Length; offset += MaxFrameChars) {
          int len = Math.Min(MaxFrameChars, json.Length - offset);
          await SendSingleStringUnlockedAsync(ws, json.Substring(offset, len), ct).ConfigureAwait(false);
        }
      } finally {
        sendLock.Release();
      }
    }

    private static async Task SendRawFrameAsync(
        WebSocket ws,
        SemaphoreSlim sendLock,
        string frame,
        CancellationToken ct) {
      await sendLock.WaitAsync(ct).ConfigureAwait(false);
      try {
        await SendSingleStringUnlockedAsync(ws, frame, ct).ConfigureAwait(false);
      } finally {
        sendLock.Release();
      }
    }

    private static async Task SendSingleStringUnlockedAsync(WebSocket ws, string text, CancellationToken ct) {
      int maxBytes = Encoding.UTF8.GetMaxByteCount(text.Length);
      if (maxBytes <= 4096) {
        byte[] small = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(small, 0, small.Length), WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
      } else if (maxBytes <= ReceiveChunkBytes) {
        byte[] pooled = ArrayPool<byte>.Shared.Rent(maxBytes);
        try {
          int written = Encoding.UTF8.GetBytes(text, 0, text.Length, pooled, 0);
          await ws.SendAsync(new ArraySegment<byte>(pooled, 0, written), WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        } finally {
          ArrayPool<byte>.Shared.Return(pooled);
        }
      } else {
        // Stream in 16KB UTF-8 segments so even huge frames never allocate >= 85KB on the LOH!
        byte[] pooled = ArrayPool<byte>.Shared.Rent(ReceiveChunkBytes);
        try {
          Encoder encoder = Encoding.UTF8.GetEncoder();
          char[] charWindow = ArrayPool<char>.Shared.Rent(4096);
          try {
            int pos = 0;
            while (pos < text.Length) {
              int cLen = Math.Min(4096, text.Length - pos);
              text.CopyTo(pos, charWindow, 0, cLen);
              pos += cLen;
              bool flush = pos >= text.Length;
              encoder.Convert(charWindow, 0, cLen, pooled, 0, pooled.Length, flush, out _, out int bytesUsed, out bool completed);
              await ws.SendAsync(
                  new ArraySegment<byte>(pooled, 0, bytesUsed),
                  WebSocketMessageType.Text,
                  endOfMessage: flush && completed,
                  ct).ConfigureAwait(false);
            }
          } finally {
            ArrayPool<char>.Shared.Return(charWindow);
          }
        } finally {
          ArrayPool<byte>.Shared.Return(pooled);
        }
      }
    }

    private static string FormatErrorDetail(object data) {
      if (data == null) return null;
      if (data is string s) return s;
      return JsonHelper.Serialize(data);
    }

    private static bool HasServerWarning(object data, string warning) {
      if (data is Dictionary<string, object> map &&
          map.TryGetValue("w", out object wObj) &&
          wObj is IList<object> list) {
        for (int i = 0; i < list.Count; i++) {
          if (list[i] is string s && string.Equals(s, warning, StringComparison.Ordinal)) {
            return true;
          }
        }
      }
      return false;
    }

    private static void SafeCancel(CancellationTokenSource cts) {
      if (cts == null) return;
      try {
        cts.Cancel();
      } catch (ObjectDisposedException) {
        // Benign race if session CTS was concurrently disposed during session teardown.
      }
    }

    public void Dispose() {
      CancellationTokenSource sessionCts;
      TaskCompletionSource<bool> stopTcs;
      lock (_connLock) {
        if (_disposed) return;
        _disposed = true;
        _desiredOnline = false;
        _isReady = false;
        stopTcs = _activeSessionStopTcs;
        _activeSessionStopTcs = null;
        sessionCts = _activeSessionCts;
        _listens.Clear();
        _writes.Clear();
        _pendingOnDisconnects.Clear();
        _inFlightRpcs.Clear();
      }
      stopTcs?.TrySetResult(true);
      SafeCancel(sessionCts);
      SafeCancel(_disposeCts);
    }
  }
}
