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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Idle-timeout watchdog for a single transfer attempt. Produces a <see cref="CancellationToken" />
  /// that is cancelled when no progress has been reported via <see cref="Touch" /> for
  /// <see cref="StallTimeout" />, or when the linked token is cancelled. Callers distinguish a stall
  /// from a cancellation via <see cref="TimedOut" />.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class StallWatchdog : IDisposable {
    /// <summary>
    /// How long a single request may make no progress before it is abandoned and retried.
    /// Mirrors the 60 second connect/read timeouts used by the native Android and iOS SDKs.
    /// </summary>
    internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    private readonly CancellationToken _linkedToken;
    private readonly CancellationTokenSource _cts;
    private readonly TimeSpan _timeout;

    internal StallWatchdog(CancellationToken linkedToken, TimeSpan? timeout = null) {
      _linkedToken = linkedToken;
      _timeout = timeout ?? StallTimeout;
      _cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
      _cts.CancelAfter(_timeout);
    }

    internal CancellationToken Token => _cts.Token;

    /// <summary>True if the watchdog (rather than the linked token) triggered cancellation.</summary>
    internal bool TimedOut => _cts.IsCancellationRequested && !_linkedToken.IsCancellationRequested;

    /// <summary>Resets the idle timer; call whenever bytes are sent or received.</summary>
    internal void Touch() {
      try {
        _cts.CancelAfter(_timeout);
      } catch (ObjectDisposedException) {
      }
    }

    /// <summary>
    /// Converts an <see cref="OperationCanceledException" /> raised while this watchdog was active
    /// into a retryable <see cref="HttpRequestException" /> if the cause was a stall, otherwise
    /// returns null so the caller can rethrow the original cancellation.
    /// </summary>
    internal HttpRequestException AsStallException(OperationCanceledException ex) {
      if (!TimedOut) {
        return null;
      }
      return new HttpRequestException(
          $"No data transferred for {(int)_timeout.TotalSeconds}s; connection assumed stalled.", ex);
    }

    public void Dispose() {
      _cts.Dispose();
    }
  }

  /// <summary>
  /// <see cref="HttpContent" /> over a byte array segment that writes in slices and notifies a
  /// <see cref="StallWatchdog" /> after each slice so slow-but-progressing uploads are not
  /// mistaken for stalls.
  /// </summary>
  internal sealed class WatchdogByteArrayContent : HttpContent {
    private const int SliceSize = 64 * 1024;

    private readonly byte[] _buffer;
    private readonly int _offset;
    private readonly int _count;
    private readonly StallWatchdog _watchdog;
    private readonly CancellationToken _token;

    internal WatchdogByteArrayContent(byte[] buffer, int offset, int count, StallWatchdog watchdog) {
      _buffer = buffer;
      _offset = offset;
      _count = count;
      _watchdog = watchdog;
      _token = watchdog.Token;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context) {
      int written = 0;
      while (written < _count) {
        int slice = Math.Min(SliceSize, _count - written);
        await stream.WriteAsync(_buffer, _offset + written, slice, _token).ConfigureAwait(false);
        written += slice;
        _watchdog.Touch();
      }
    }

    protected override bool TryComputeLength(out long length) {
      length = _count;
      return true;
    }
  }
}
