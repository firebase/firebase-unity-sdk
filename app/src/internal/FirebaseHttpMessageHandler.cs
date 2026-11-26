/*
 * Copyright 2026 Google LLC
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
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal {
  /// <summary>
  /// Shared <see cref="HttpMessageHandler" /> for pure-C# Firebase Unity libraries (Storage,
  /// Functions, and Firebase AI Logic) backed by <c>firebase-cpp-sdk</c>'s cross-platform native
  /// HTTP transport (<c>firebase_http_transport</c>, linked into <c>FirebaseCppApp</c>:
  /// <c>libcurl</c> on Desktop, <c>NSURLSession</c> on iOS/tvOS, and JNI
  /// <c>HttpURLConnection</c>/Conscrypt on Android) with zero <c>UnityEngine.Networking</c>
  /// dependency.
  ///
  /// <para>
  /// Why we use platform-native HTTP instead of managed <see cref="HttpClientHandler" /> or
  /// <c>UnityEngine.Networking</c>:
  /// <list type="bullet">
  ///   <item>
  ///     On Unity 2022.3 through Unity 6.x (legacy Mono and IL2CPP BCL), managed
  ///     <see cref="HttpClientHandler" /> routes TLS through <c>Mono.Unity.UnityTlsStream</c>
  ///     (mbedTLS with ~16 KB record framing), which caps single-stream download throughput around
  ///     ~10-12 MiB/s regardless of link speed.
  ///   </item>
  ///   <item>
  ///     We intentionally avoid <c>UnityEngine.Networking</c> (<c>UnityWebRequest</c> and
  ///     <c>UnityHttpMessageHandler</c>) to keep the C# transport Unity-free, avoid main-thread
  ///     dispatch hops, and preserve true upload/download streaming backpressure and immediate SSE
  ///     chunk delivery (for Storage <c>GetStreamAsync()</c>, Functions <c>StreamAsync()</c>, and
  ///     Firebase AI <c>GenerateContentStreamAsync()</c>). In addition,
  ///     <c>UnityHttpMessageHandler</c> does not exist on supported LTS releases (2022.3 / 6000.0 /
  ///     6000.3), and on Unity 6000.6.2f1 its internal <c>UploadHandlerStream</c> silently
  ///     truncates uploads of 256 KiB or more.
  ///   </item>
  /// </list>
  /// Routing <see cref="HttpClient" /> through <c>firebase-cpp-sdk</c>'s platform-native stacks
  /// restores native wire-speed transfers and consistent redirect/TLS behavior while preserving
  /// standard <see cref="HttpRequestMessage" />, <see cref="HttpResponseMessage" />, streaming
  /// uploads, <see cref="HttpCompletionOption.ResponseHeadersRead" />, SSE incremental reads, and
  /// <see cref="CancellationToken" /> semantics.
  /// </para>
  ///
  /// <para>
  /// When to revisit: Re-evaluate retiring this native HTTP bridge once the Firebase Unity SDK's
  /// minimum supported Unity version adopts Unity 7+ with CoreCLR and production .NET 10+ BCL
  /// support across desktop and mobile/IL2CPP, where standard <see cref="HttpClient" />
  /// (<c>SocketsHttpHandler</c> + native OS TLS) delivers native throughput with zero
  /// <c>UnityEngine</c> dependency.
  /// </para>
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class FirebaseHttpMessageHandler : HttpMessageHandler {
    private const long NoRequestContent = -2;
    private const long ChunkedRequestContent = -1;
    private const int ReadWouldBlock = -2;
    private const int ReadErrorOrCanceled = -1;

    private const int EventRequestSpaceAvailable = 1;
    private const int EventResponseHeadersReady = 2;
    private const int EventResponseBodyAvailable = 3;

    private const int CopyBufferSize = 64 * 1024;

    // Store an OperationSlot holding a WeakReference<NativeOperationState> plus a waiter-refcounted
    // StrongPin. While an async caller is actively awaiting EventPumpLoop (SendNativeAsync,
    // NativeRequestWriteStream.WriteAsync, or NativeResponseStream.ReadSlowPathAsync), StrongPin
    // roots NativeOperationState and its suspended async continuation chain even for unreferenced
    // `async void` callers. Once no awaiter is suspended, StrongPin drops back to null so callers
    // that omit `using` on HttpResponseMessage do not stay permanently rooted in static memory and
    // GC finalization reclaims the native C++ HttpOperation handle.
    private static readonly ConcurrentDictionary<long, OperationSlot>
        ActiveOperations = new ConcurrentDictionary<long, OperationSlot>();

    private static readonly object PumpLock = new object();
    private static Thread _pumpThread;
    private static volatile bool _domainUnloading;
    private static int _domainUnloadHooked;
    // 0 = unprobed, 1 = native available, -1 = fallback to HttpClientHandler.
    private static int _nativeAvailability;

    private readonly int _maxConnectionsPerServer;
    private HttpClientHandler _fallbackHandler;
    private readonly object _fallbackLock = new object();
    private int _disposed;

    internal FirebaseHttpMessageHandler(int maxConnectionsPerServer = 16) {
      _maxConnectionsPerServer = maxConnectionsPerServer;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) {
      if (request == null) {
        throw new ArgumentNullException(nameof(request));
      }
      if (request.RequestUri == null || !request.RequestUri.IsAbsoluteUri) {
        throw new InvalidOperationException("An absolute request URI is required.");
      }
      string scheme = request.RequestUri.Scheme;
      if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
        throw new ArgumentException(
            "Only 'http' and 'https' URI schemes are supported.", nameof(request));
      }
      if (Volatile.Read(ref _disposed) != 0) {
        throw new ObjectDisposedException(nameof(FirebaseHttpMessageHandler));
      }
      cancellationToken.ThrowIfCancellationRequested();

      if (!EnsureNativeTransportAvailable()) {
        return SendViaManagedFallbackAsync(request, cancellationToken);
      }
      return SendNativeAsync(request, cancellationToken);
    }

    protected override void Dispose(bool disposing) {
      if (Interlocked.Exchange(ref _disposed, 1) == 0 && disposing) {
        HttpClientHandler fallback;
        lock (_fallbackLock) {
          fallback = _fallbackHandler;
          _fallbackHandler = null;
        }
        fallback?.Dispose();
      }
      base.Dispose(disposing);
    }

    private Task<HttpResponseMessage> SendViaManagedFallbackAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) {
      HttpClientHandler fallback;
      lock (_fallbackLock) {
        if (_disposed != 0) {
          throw new ObjectDisposedException(nameof(FirebaseHttpMessageHandler));
        }
        if (_fallbackHandler == null) {
          _fallbackHandler = CreateFallbackHandler(_maxConnectionsPerServer);
        }
        fallback = _fallbackHandler;
      }
      var invoker = new HttpMessageInvoker(fallback, disposeHandler: false);
      return invoker.SendAsync(request, cancellationToken);
    }

    internal static HttpClientHandler CreateFallbackHandler(int maxConnectionsPerServer) {
      try {
        if (ServicePointManager.DefaultConnectionLimit < maxConnectionsPerServer) {
          ServicePointManager.DefaultConnectionLimit = maxConnectionsPerServer;
        }
      } catch (Exception) {
        // Ignore on runtimes where ServicePointManager is unsupported.
      }
      var handler = new HttpClientHandler();
      try {
        handler.MaxConnectionsPerServer = maxConnectionsPerServer;
      } catch (Exception) {
        // MonoWebRequestHandler throws NotImplementedException; ServicePointManager covers it.
      }
      return handler;
    }

    internal static bool IsNativeLibraryUnavailable(Exception ex) {
      Exception current = ex;
      while (current != null) {
        if (current is DllNotFoundException ||
            current is EntryPointNotFoundException ||
            current is BadImageFormatException) {
          return true;
        }
        // Only walk through TypeInitializationException wrappers (e.g. SWIG's
        // AppUtilPINVOKE -> SWIGExceptionHelper -> DllNotFoundException).
        if (current is TypeInitializationException) {
          current = current.InnerException;
          continue;
        }
        return false;
      }
      return false;
    }

    private static bool EnsureNativeTransportAvailable() {
      int state = Volatile.Read(ref _nativeAvailability);
      if (state != 0) {
        return state > 0;
      }
      try {
        // Probe P/Invoke symbol resolution with a harmless handle=0 poll so we do not arm
        // CompletionQueue.wake_requested before ActiveOperations has registered a request.
        AppUtil.HttpPollResponseHeadersInternal(0);
        Volatile.Write(ref _nativeAvailability, 1);
        return true;
      } catch (Exception ex) when (IsNativeLibraryUnavailable(ex)) {
        Volatile.Write(ref _nativeAvailability, -1);
        return false;
      }
    }

    private static void EnsureEventPumpStarted() {
      if (Interlocked.CompareExchange(ref _domainUnloadHooked, 1, 0) == 0) {
        AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
        AppDomain.CurrentDomain.ProcessExit += OnDomainUnload;
      }

      lock (PumpLock) {
        if (_domainUnloading || _pumpThread != null) {
          return;
        }
        _pumpThread = new Thread(EventPumpLoop) {
          Name = "FirebaseHttpEventPump",
          IsBackground = true
        };
        _pumpThread.Start();
      }
    }

    private static bool PruneAndCheckLiveOperations() {
      bool anyLive = false;
      foreach (KeyValuePair<long, OperationSlot> entry in ActiveOperations) {
        if (entry.Value.TryGetTarget(out NativeOperationState op) && op.IsLive) {
          anyLive = true;
        } else {
          ActiveOperations.TryRemove(entry.Key, out _);
        }
      }
      return anyLive;
    }

    private static void OnDomainUnload(object sender, EventArgs e) {
      _domainUnloading = true;
      foreach (KeyValuePair<long, OperationSlot> entry in ActiveOperations) {
        if (entry.Value.TryGetTarget(out NativeOperationState op)) {
          op.AbortAndRelease();
        }
      }
      try {
        AppUtil.HttpWakeEventPumpInternal();
      } catch (Exception) {
      }
      Thread pump;
      lock (PumpLock) {
        pump = _pumpThread;
      }
      if (pump != null && pump != Thread.CurrentThread) {
        try {
          pump.Join(250);
        } catch (Exception) {
        }
      }
    }

    private static void EventPumpLoop() {
      Thread selfThread = Thread.CurrentThread;
      try {
        while (!_domainUnloading) {
          long packed;
          try {
            packed = AppUtil.HttpWaitNextEventInternal(5000);
          } catch (Exception) {
            break;
          }
          if (packed == 0) {
            if (!PruneAndCheckLiveOperations()) {
              lock (PumpLock) {
                if (!PruneAndCheckLiveOperations()) {
                  if (_pumpThread == selfThread) {
                    _pumpThread = null;
                  }
                  return;
                }
              }
            }
            continue;
          }

          long handle = packed >> 8;
          int kind = (int)(packed & 0xFF);
          if (ActiveOperations.TryGetValue(handle, out OperationSlot slot)) {
            if (slot.TryGetTarget(out NativeOperationState op)) {
              op.OnNativeEvent(kind);
            } else {
              ActiveOperations.TryRemove(handle, out _);
            }
          }
        }
      } finally {
        lock (PumpLock) {
          if (_pumpThread == selfThread) {
            _pumpThread = null;
            if (!_domainUnloading && PruneAndCheckLiveOperations()) {
              _pumpThread = new Thread(EventPumpLoop) {
                Name = "FirebaseHttpEventPump",
                IsBackground = true
              };
              _pumpThread.Start();
            }
          }
        }
      }
    }

    private static async Task<HttpResponseMessage> SendNativeAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) {
      HttpContent requestContent = request.Content;
      long contentLength = NoRequestContent;
      if (requestContent != null) {
        long? computed = requestContent.Headers.ContentLength;
        contentLength = computed.HasValue ? computed.Value : ChunkedRequestContent;
      }

      string packedHeaders = PackRequestHeaders(request, requestContent);
      string proxyUrl = ResolveProxyUrl(request.RequestUri);
      string method = request.Method?.Method ?? "GET";

      long handle = AppUtil.HttpCreateRequestInternal(
          method,
          request.RequestUri.AbsoluteUri,
          packedHeaders,
          proxyUrl,
          contentLength);

      var opState = new NativeOperationState(handle);
      // Strongly pin opState across SendNativeAsync so suspended async continuations stay rooted
      // while awaiting headers even for unreferenced `async void` callers.
      opState.PinWaiter();
      // Register in ActiveOperations BEFORE starting or checking the event pump so EventPumpLoop's
      // idle exit under PumpLock can never race past an active request.
      ActiveOperations[handle] = opState.Slot;

      CancellationTokenRegistration sendCancelReg = default;
      Task uploadTask = null;
      try {
        try {
          EnsureEventPumpStarted();

          if (cancellationToken.CanBeCanceled) {
            sendCancelReg = cancellationToken.Register(state => {
              ((NativeOperationState)state).CancelNativeOnly();
            }, opState);
          }

          AppUtil.HttpStartRequestInternal(handle);

          if (requestContent != null && contentLength != 0) {
            uploadTask = StreamRequestBodyAsync(opState, requestContent, cancellationToken);
          }

          int statusCode = await WaitForResponseHeadersAsync(opState, uploadTask, cancellationToken)
              .ConfigureAwait(false);

          // If the upload task is still winding down or faulted after early server error headers,
          // observe its exception asynchronously so UnobservedTaskException is never raised.
          if (uploadTask != null) {
            ObserveBackgroundUploadTask(uploadTask);
          }

          // Detach the send-phase registration before returning; under ResponseHeadersRead the
          // caller governs stream lifetime via Stream.ReadAsync(token) & Dispose(), while under
          // ResponseContentRead NativeResponseContent.SerializeToStreamAsync forwards
          // cancellationToken directly into _stream.ReadAsync.
          sendCancelReg.Dispose();
          cancellationToken.ThrowIfCancellationRequested();

          string rawHeaders = AppUtil.HttpGetResponseHeadersInternal(handle);
          HttpResponseMessage response = BuildHttpResponseMessage(
              request, opState, statusCode, rawHeaders, cancellationToken);
          GC.KeepAlive(opState);
          return response;
        } catch (Exception ex) {
          sendCancelReg.Dispose();
          opState.AbortAndRelease();
          if (uploadTask != null) {
            ObserveBackgroundUploadTask(uploadTask);
          }
          if (cancellationToken.IsCancellationRequested) {
            throw new TaskCanceledException("The HTTP request was canceled.", ex, cancellationToken);
          }
          if (ex is HttpRequestException || ex is OperationCanceledException) {
            throw;
          }
          throw new HttpRequestException(ex.Message, ex);
        }
      } finally {
        opState.UnpinWaiter();
      }
    }

    private static void ObserveBackgroundUploadTask(Task uploadTask) {
      if (uploadTask.IsCompleted) {
        _ = uploadTask.Exception;
        return;
      }
      _ = uploadTask.ContinueWith(
          t => { _ = t.Exception; },
          CancellationToken.None,
          TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
    }

    private static async Task StreamRequestBodyAsync(
        NativeOperationState opState,
        HttpContent content,
        CancellationToken cancellationToken) {
      using (var requestStream = new NativeRequestWriteStream(opState, cancellationToken)) {
        await content.CopyToAsync(requestStream).ConfigureAwait(false);
        requestStream.Finish();
      }
    }

    private static async Task<int> WaitForResponseHeadersAsync(
        NativeOperationState opState,
        Task uploadTask,
        CancellationToken cancellationToken) {
      while (true) {
        cancellationToken.ThrowIfCancellationRequested();
        Task waitTask = opState.HeadersSignal.PrepareWait();
        int status = opState.PollResponseHeaders();
        if (status > 0) {
          return status;
        }
        if (status < 0) {
          cancellationToken.ThrowIfCancellationRequested();
          // If the request body stream itself threw (e.g. WatchdogByteArrayContent timeout or
          // caller stream I/O error), surface that exception directly.
          if (uploadTask != null && uploadTask.IsFaulted && uploadTask.Exception != null) {
            throw uploadTask.Exception.GetBaseException();
          }
          string err = opState.GetErrorMessage();
          throw new HttpRequestException(
              string.IsNullOrEmpty(err) ? "Native HTTP transport request failed." : err);
        }

        if (uploadTask != null && !uploadTask.IsCompleted) {
          Task finished = await Task.WhenAny(waitTask, uploadTask).ConfigureAwait(false);
          if (finished == uploadTask && uploadTask.IsFaulted && uploadTask.Exception != null) {
            // Give native headers one quick poll in case the server already sent an HTTP 4xx/5xx
            // response that caused the upload pipe to close.
            int racedStatus = opState.PollResponseHeaders();
            if (racedStatus > 0) {
              return racedStatus;
            }
            opState.CancelNativeOnly();
            throw uploadTask.Exception.GetBaseException();
          }
          if (finished == uploadTask && uploadTask.IsCanceled) {
            opState.CancelNativeOnly();
            cancellationToken.ThrowIfCancellationRequested();
            throw new TaskCanceledException(uploadTask);
          }
        } else {
          await waitTask.ConfigureAwait(false);
        }
      }
    }

    private static HttpResponseMessage BuildHttpResponseMessage(
        HttpRequestMessage request,
        NativeOperationState opState,
        int statusCode,
        string packedHeaders,
        CancellationToken sendCancellationToken) {
      var response = new HttpResponseMessage((HttpStatusCode)statusCode) {
        RequestMessage = request
      };
      var responseStream = new NativeResponseStream(opState);
      var responseContent = new NativeResponseContent(responseStream, sendCancellationToken);
      response.Content = responseContent;

      if (!string.IsNullOrEmpty(packedHeaders)) {
        int lineStart = 0;
        int firstNewline = packedHeaders.IndexOf('\n');
        if (firstNewline >= 0) {
          string reason = packedHeaders.Substring(0, firstNewline).Trim('\r');
          if (!string.IsNullOrEmpty(reason)) {
            response.ReasonPhrase = reason;
          }
          lineStart = firstNewline + 1;
        }

        while (lineStart < packedHeaders.Length) {
          int nextNewline = packedHeaders.IndexOf('\n', lineStart);
          int lineEnd = nextNewline >= 0 ? nextNewline : packedHeaders.Length;
          int tab = packedHeaders.IndexOf('\t', lineStart, lineEnd - lineStart);
          if (tab > lineStart) {
            string name = packedHeaders.Substring(lineStart, tab - lineStart).Trim();
            string value = packedHeaders.Substring(tab + 1, lineEnd - (tab + 1)).Trim('\r', ' ');
            if (!string.IsNullOrEmpty(name)) {
              if (!response.Headers.TryAddWithoutValidation(name, value)) {
                responseContent.Headers.TryAddWithoutValidation(name, value);
              }
            }
          }
          lineStart = nextNewline >= 0 ? nextNewline + 1 : packedHeaders.Length;
        }
      }

      return response;
    }

    private static string PackRequestHeaders(HttpRequestMessage request, HttpContent content) {
      var sb = new StringBuilder(256);
      AppendHeaders(sb, request.Headers, skipContentLength: false,
                    suppressExpectContinue: request.Headers.ExpectContinue == false);
      if (content != null) {
        // Content-Length is passed explicitly via contentLength so native backends configure
        // fixed-length streaming without emitting duplicate Content-Length headers.
        AppendHeaders(sb, content.Headers, skipContentLength: true, suppressExpectContinue: false);
      }
      return sb.ToString();
    }

    private static void AppendHeaders(
        StringBuilder sb,
        System.Net.Http.Headers.HttpHeaders headers,
        bool skipContentLength,
        bool suppressExpectContinue) {
      if (headers == null) {
        return;
      }
      foreach (KeyValuePair<string, IEnumerable<string>> kv in headers) {
        string key = SanitizeHeaderToken(kv.Key);
        if (string.IsNullOrEmpty(key)) {
          continue;
        }
        if (skipContentLength &&
            string.Equals(key, "Content-Length", StringComparison.OrdinalIgnoreCase)) {
          continue;
        }
        string separator = string.Equals(key, "User-Agent", StringComparison.OrdinalIgnoreCase)
            ? " "
            : ", ";
        string joined = SanitizeHeaderToken(string.Join(separator, kv.Value));
        if (suppressExpectContinue &&
            string.Equals(key, "Expect", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(joined) ||
             string.Equals(joined, "100-continue", StringComparison.OrdinalIgnoreCase))) {
          continue;
        }
        sb.Append(key).Append('\t').Append(joined).Append('\n');
      }
    }

    private static string SanitizeHeaderToken(string value) {
      if (string.IsNullOrEmpty(value)) {
        return string.Empty;
      }
      if (value.IndexOf('\r') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\t') < 0) {
        return value;
      }
      return value.Replace("\r", string.Empty)
                  .Replace("\n", string.Empty)
                  .Replace("\t", " ");
    }

    private static string ResolveProxyUrl(Uri requestUri) {
      try {
        IWebProxy proxy = WebRequest.DefaultWebProxy;
        if (proxy == null || proxy.IsBypassed(requestUri)) {
          return string.Empty;
        }
        Uri proxyUri = proxy.GetProxy(requestUri);
        if (proxyUri == null || Uri.Equals(proxyUri, requestUri)) {
          return string.Empty;
        }
        return proxyUri.AbsoluteUri.TrimEnd('/');
      } catch (Exception) {
        return string.Empty;
      }
    }

    /// <summary>
    /// Race-free async signal used between <see cref="EventPumpLoop" /> and awaiting operations.
    /// Calling <see cref="PrepareWait" /> before polling native state guarantees that a wakeup
    /// arriving while the P/Invoke is still returning completes the prepared task immediately.
    /// </summary>
    private sealed class AsyncSignal {
      private readonly object _lock = new object();
      private TaskCompletionSource<bool> _tcs;

      public Task PrepareWait() {
        lock (_lock) {
          if (_tcs == null || _tcs.Task.IsCompleted) {
            _tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
          }
          return _tcs.Task;
        }
      }

      public void Signal() {
        TaskCompletionSource<bool> tcs;
        lock (_lock) {
          if (_tcs == null) {
            _tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
          }
          tcs = _tcs;
        }
        tcs.TrySetResult(true);
      }
    }

    private sealed class OperationSlot {
      public readonly WeakReference<NativeOperationState> Weak;
      public NativeOperationState StrongPin;

      public OperationSlot(NativeOperationState op) {
        Weak = new WeakReference<NativeOperationState>(op);
      }

      public bool TryGetTarget(out NativeOperationState op) {
        NativeOperationState strong = Volatile.Read(ref StrongPin);
        if (strong != null) {
          op = strong;
          return true;
        }
        return Weak.TryGetTarget(out op);
      }
    }

    private sealed class NativeOperationState {
      private readonly object _handleLock = new object();
      private readonly OperationSlot _slot;
      private long _handle;
      private int _waiterPinCount;

      public readonly AsyncSignal WriteSignal = new AsyncSignal();
      public readonly AsyncSignal HeadersSignal = new AsyncSignal();
      public readonly AsyncSignal ReadSignal = new AsyncSignal();

      public NativeOperationState(long handle) {
        _handle = handle;
        _slot = new OperationSlot(this);
      }

      ~NativeOperationState() {
        AbortAndRelease();
      }

      public OperationSlot Slot => _slot;

      public void PinWaiter() {
        lock (_handleLock) {
          if (_handle == 0) {
            return;
          }
          if (++_waiterPinCount == 1) {
            Volatile.Write(ref _slot.StrongPin, this);
          }
        }
      }

      public void UnpinWaiter() {
        lock (_handleLock) {
          if (_waiterPinCount > 0 && --_waiterPinCount == 0) {
            Volatile.Write(ref _slot.StrongPin, null);
          }
        }
      }

      public bool IsLive {
        get {
          lock (_handleLock) {
            return _handle != 0;
          }
        }
      }

      public void OnNativeEvent(int kind) {
        switch (kind) {
          case EventRequestSpaceAvailable:
            WriteSignal.Signal();
            break;
          case EventResponseHeadersReady:
            HeadersSignal.Signal();
            break;
          case EventResponseBodyAvailable:
            ReadSignal.Signal();
            break;
          default:
            WriteSignal.Signal();
            HeadersSignal.Signal();
            ReadSignal.Signal();
            break;
        }
      }

      public int TryWriteRequestBody(byte[] buffer, int offset, int count) {
        lock (_handleLock) {
          if (_handle == 0) {
            return -1;
          }
          GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
          try {
            IntPtr ptr = IntPtr.Add(pin.AddrOfPinnedObject(), offset);
            return AppUtil.HttpTryWriteRequestBodyInternal(_handle, ptr, count);
          } finally {
            pin.Free();
          }
        }
      }

      public void FinishRequestBody() {
        lock (_handleLock) {
          if (_handle != 0) {
            AppUtil.HttpFinishRequestBodyInternal(_handle);
          }
        }
      }

      public int PollResponseHeaders() {
        lock (_handleLock) {
          return _handle == 0 ? -1 : AppUtil.HttpPollResponseHeadersInternal(_handle);
        }
      }

      public int TryReadResponseBody(byte[] buffer, int offset, int count) {
        lock (_handleLock) {
          if (_handle == 0) {
            return ReadErrorOrCanceled;
          }
          GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
          try {
            IntPtr ptr = IntPtr.Add(pin.AddrOfPinnedObject(), offset);
            return AppUtil.HttpTryReadResponseBodyInternal(_handle, ptr, count);
          } finally {
            pin.Free();
          }
        }
      }

      public string GetErrorMessage() {
        lock (_handleLock) {
          return _handle == 0 ? string.Empty : AppUtil.HttpGetErrorInternal(_handle);
        }
      }

      public void CancelNativeOnly() {
        long h;
        lock (_handleLock) {
          h = _handle;
        }
        if (h != 0) {
          try {
            AppUtil.HttpCancelRequestInternal(h);
          } catch (Exception) {
          }
        }
        WriteSignal.Signal();
        HeadersSignal.Signal();
        ReadSignal.Signal();
      }

      public void AbortAndRelease() {
        long h;
        lock (_handleLock) {
          h = _handle;
          _handle = 0;
          _waiterPinCount = 0;
          Volatile.Write(ref _slot.StrongPin, null);
        }
        if (h != 0) {
          ActiveOperations.TryRemove(h, out _);
          try {
            AppUtil.HttpDestroyRequestInternal(h);
          } catch (Exception) {
          }
          GC.SuppressFinalize(this);
        }
        WriteSignal.Signal();
        HeadersSignal.Signal();
        ReadSignal.Signal();
      }
    }

    private sealed class NativeRequestWriteStream : Stream {
      private readonly NativeOperationState _op;
      private readonly CancellationToken _token;
      private bool _finished;

      public NativeRequestWriteStream(NativeOperationState op, CancellationToken token) {
        _op = op;
        _token = token;
      }

      public override bool CanRead => false;
      public override bool CanSeek => false;
      public override bool CanWrite => true;
      public override long Length => throw new NotSupportedException();
      public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      public override void Flush() {
      }

      public void Finish() {
        if (!_finished) {
          _finished = true;
          _op.FinishRequestBody();
        }
      }

      public override void Write(byte[] buffer, int offset, int count) {
        WriteAsync(buffer, offset, count, _token).GetAwaiter().GetResult();
      }

      public override async Task WriteAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken) {
        if (buffer == null) {
          throw new ArgumentNullException(nameof(buffer));
        }
        if (offset < 0 || count < 0 || offset > buffer.Length || count > buffer.Length - offset) {
          throw new ArgumentOutOfRangeException();
        }
        if (count == 0) {
          return;
        }

        CancellationToken effectiveToken = cancellationToken.CanBeCanceled
            ? cancellationToken
            : _token;

        _op.PinWaiter();
        try {
          int writtenTotal = 0;
          while (writtenTotal < count) {
            effectiveToken.ThrowIfCancellationRequested();
            _token.ThrowIfCancellationRequested();

            Task waitTask = _op.WriteSignal.PrepareWait();
            int accepted = _op.TryWriteRequestBody(
                buffer, offset + writtenTotal, count - writtenTotal);
            if (accepted < 0) {
              effectiveToken.ThrowIfCancellationRequested();
              _token.ThrowIfCancellationRequested();
              string err = _op.GetErrorMessage();
              throw new IOException(
                  string.IsNullOrEmpty(err) ? "Native HTTP upload stream closed." : err);
            }
            writtenTotal += accepted;
            if (writtenTotal < count) {
              if (effectiveToken.CanBeCanceled) {
                var cancelTcs = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (effectiveToken.Register(
                    s => ((TaskCompletionSource<bool>)s).TrySetResult(true), cancelTcs)) {
                  await Task.WhenAny(waitTask, cancelTcs.Task).ConfigureAwait(false);
                }
              } else {
                await waitTask.ConfigureAwait(false);
              }
            }
          }
        } finally {
          _op.UnpinWaiter();
        }
      }

      public override int Read(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
      }

      public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException();
      }

      public override void SetLength(long value) {
        throw new NotSupportedException();
      }
    }

    private sealed class NativeResponseContent : HttpContent {
      private readonly NativeResponseStream _stream;
      private readonly CancellationToken _sendCancellationToken;

      public NativeResponseContent(
          NativeResponseStream stream,
          CancellationToken sendCancellationToken) {
        _stream = stream;
        _sendCancellationToken = sendCancellationToken;
      }

      protected override Task<Stream> CreateContentReadStreamAsync() {
        return Task.FromResult<Stream>(_stream);
      }

      protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context) {
        // Under HttpCompletionOption.ResponseContentRead, Unity's HttpClient.FinishSendAsyncBuffered
        // calls LoadIntoBufferAsync -> SerializeToStreamAsync(Stream, TransportContext) without
        // forwarding its linked CancellationToken. Using _sendCancellationToken ensures
        // HttpClient.Timeout, caller CancellationTokens, and Storage's StallWatchdog cancel
        // mid-body stalls deterministically.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try {
          while (true) {
            int read = await _stream.ReadAsync(buffer, 0, buffer.Length, _sendCancellationToken)
                .ConfigureAwait(false);
            if (read <= 0) {
              break;
            }
            await stream.WriteAsync(buffer, 0, read, _sendCancellationToken).ConfigureAwait(false);
          }
        } finally {
          ArrayPool<byte>.Shared.Return(buffer);
        }
      }

      protected override bool TryComputeLength(out long length) {
        // NativeResponseStream is non-seekable; do not read Headers.ContentLength here because
        // HttpContentHeaders.ContentLength invokes TryComputeLength when Content-Length is absent
        // (e.g. chunked transfer encoding), which would recurse infinitely.
        length = 0;
        return false;
      }

      protected override void Dispose(bool disposing) {
        if (disposing) {
          _stream.Dispose();
        }
        base.Dispose(disposing);
      }
    }

    private sealed class NativeResponseStream : Stream {
      private readonly NativeOperationState _op;
      private int _disposed;
      private bool _eof;

      public NativeResponseStream(NativeOperationState op) {
        _op = op;
      }

      ~NativeResponseStream() {
        Dispose(false);
      }

      public override bool CanRead => Volatile.Read(ref _disposed) == 0;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
      }

      public override void Flush() {
      }

      public override int Read(byte[] buffer, int offset, int count) {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
      }

      public override Task<int> ReadAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken) {
        if (buffer == null) {
          throw new ArgumentNullException(nameof(buffer));
        }
        if (offset < 0 || count < 0 || offset > buffer.Length || count > buffer.Length - offset) {
          throw new ArgumentOutOfRangeException();
        }
        if (Volatile.Read(ref _disposed) != 0) {
          return Task.FromException<int>(new ObjectDisposedException(nameof(NativeResponseStream)));
        }
        if (cancellationToken.IsCancellationRequested) {
          _op.AbortAndRelease();
          return Task.FromCanceled<int>(cancellationToken);
        }
        if (count == 0 || _eof) {
          return Task.FromResult(0);
        }

        // Synchronous fast path: while the native engine is streaming ahead into its 2 MiB buffer,
        // most reads copy straight out of native memory with zero Task or TCS allocation.
        Task waitTask = _op.ReadSignal.PrepareWait();
        int rc = _op.TryReadResponseBody(buffer, offset, count);
        if (rc > 0) {
          return Task.FromResult(rc);
        }
        if (rc == 0) {
          _eof = true;
          // Eagerly release the native operation handle at EOF so callers that omit `using` on
          // HttpResponseMessage do not retain native memory until GC finalization.
          _op.AbortAndRelease();
          return Task.FromResult(0);
        }
        if (rc == ReadErrorOrCanceled) {
          return Task.FromException<int>(CreateReadFailureException(cancellationToken));
        }

        return ReadSlowPathAsync(buffer, offset, count, waitTask, cancellationToken);
      }

      private async Task<int> ReadSlowPathAsync(
          byte[] buffer,
          int offset,
          int count,
          Task initialWaitTask,
          CancellationToken cancellationToken) {
        // Pin NativeOperationState in ActiveOperations while suspended on EventPumpLoop so GC
        // cannot collect the operation or its continuation chain for callers using
        // CancellationToken.None or a disposed send-phase CTS.
        _op.PinWaiter();
        try {
          Task waitTask = initialWaitTask;
          while (true) {
            if (cancellationToken.CanBeCanceled) {
              CancellationTokenRegistration reg = default;
              bool registered = false;
              try {
                reg = cancellationToken.Register(
                    state => ((NativeOperationState)state).AbortAndRelease(), _op);
                registered = true;
              } catch (ObjectDisposedException) {
                // If the caller's CTS was already disposed without cancelling (e.g. HttpClient's
                // send-phase CTS after ResponseHeadersRead), await waitTask without registration.
              }
              try {
                await waitTask.ConfigureAwait(false);
              } finally {
                if (registered) {
                  reg.Dispose();
                }
              }
            } else {
              await waitTask.ConfigureAwait(false);
            }

            if (Volatile.Read(ref _disposed) != 0) {
              throw new ObjectDisposedException(nameof(NativeResponseStream));
            }
            if (cancellationToken.IsCancellationRequested) {
              _op.AbortAndRelease();
              throw new OperationCanceledException(cancellationToken);
            }

            waitTask = _op.ReadSignal.PrepareWait();
            int rc = _op.TryReadResponseBody(buffer, offset, count);
            if (rc > 0) {
              return rc;
            }
            if (rc == 0) {
              _eof = true;
              _op.AbortAndRelease();
              return 0;
            }
            if (rc == ReadErrorOrCanceled) {
              throw CreateReadFailureException(cancellationToken);
            }
          }
        } finally {
          _op.UnpinWaiter();
        }
      }

      private Exception CreateReadFailureException(CancellationToken cancellationToken) {
        if (Volatile.Read(ref _disposed) != 0) {
          return new ObjectDisposedException(nameof(NativeResponseStream));
        }
        if (cancellationToken.IsCancellationRequested) {
          _op.AbortAndRelease();
          return new OperationCanceledException(cancellationToken);
        }
        string err = _op.GetErrorMessage();
        _op.AbortAndRelease();
        return new IOException(
            string.IsNullOrEmpty(err) ? "Native HTTP response stream aborted." : err);
      }

      public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException();
      }

      public override void SetLength(long value) {
        throw new NotSupportedException();
      }

      public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
      }

      protected override void Dispose(bool disposing) {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) {
          _op.AbortAndRelease();
          GC.SuppressFinalize(this);
        }
        base.Dispose(disposing);
      }
    }
  }
}
