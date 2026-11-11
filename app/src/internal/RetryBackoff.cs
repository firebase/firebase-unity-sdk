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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal {
  /// <summary>
  /// Retry budget and backoff policy shared by every Storage network loop; the C# counterpart of
  /// the Firebase Android SDK's <c>ExponentialBackoffSender</c>. Retryable HTTP statuses
  /// (5xx / 408 / 429) back off exponentially from 1s up to 30s, while transport failures
  /// (offline, connection reset, stall) are re-polled at a flat 1s: on flaky mobile links fast
  /// re-polling recovers far sooner and a failed connect is cheap. Every delay adds up to 250ms
  /// of jitter.
  /// <para>
  /// The budget measures time spent failing, not wall-clock time: the clock only starts with the
  /// first failure, and <see cref="Reset" /> forgets earlier failures once bytes flow again or a
  /// paused transfer resumes, so a slow but healthy transfer never exhausts its retry time on its
  /// own.
  /// </para>
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class RetryBackoff {
    // Mirrors Firebase Android SDK's ExponentialBackoffSender (NETWORK_STATUS_POLL_INTERVAL = 1000,
    // MAXIMUM_WAIT_TIME_MILLI = 30000, RND_MAX = 250).
    internal const int InitialBackoffMs = 1000, MaxBackoffMs = 30000, MaxJitterMs = 250;
    private static readonly Random _jitterRandom = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly Func<Exception, Exception> _wrapExhaustedError;
    private readonly int _initialBackoffMs, _maxBackoffMs, _maxJitterMs;
    private int _currentSleepMs;

    internal RetryBackoff(TimeSpan maxRetryTime, Func<Exception, Exception> wrapExhaustedError = null,
        int initialBackoffMs = InitialBackoffMs, int maxBackoffMs = MaxBackoffMs, int maxJitterMs = MaxJitterMs) {
      MaxRetryTime = maxRetryTime;
      _wrapExhaustedError = wrapExhaustedError;
      (_initialBackoffMs, _maxBackoffMs, _maxJitterMs, _currentSleepMs) = (initialBackoffMs, maxBackoffMs, maxJitterMs, initialBackoffMs);
    }

    /// <summary>Total time the operation may spend failing before it gives up.</summary>
    internal TimeSpan MaxRetryTime { get; }

    /// <summary>
    /// Forgets earlier failures: bytes arrived, a chunk committed, or the transfer resumed.
    /// </summary>
    internal void Reset() { _stopwatch.Reset(); _currentSleepMs = _initialBackoffMs; }

    /// <summary>
    /// Waits before the next attempt, or throws once the budget is spent: passes
    /// <paramref name="lastError" /> through <c>_wrapExhaustedError</c> if provided, otherwise
    /// rethrows <paramref name="lastError" /> or throws a <see cref="TimeoutException" /> when
    /// <paramref name="lastError" /> is <c>null</c> (and likewise falls back to
    /// <see cref="TimeoutException" /> if <c>_wrapExhaustedError</c> returns <c>null</c>).
    /// </summary>
    /// <param name="transportFailure">True if the attempt failed at the transport level (flat 1s
    /// re-poll) rather than with a retryable HTTP status (exponential backoff).</param>
    /// <param name="lastError">The failure being retried.</param>
    /// <param name="token">Aborts the wait.</param>
    /// <param name="log">Optional log callback receiving a debug line per retry and an error when
    /// giving up.</param>
    /// <param name="context">Describes the attempt in log lines, e.g. the transfer offset.</param>
    internal async Task DelayOrThrowAsync(
        bool transportFailure, Exception lastError, CancellationToken token = default,
        Action<LogLevel, string> log = null, string context = null) {
      if (!_stopwatch.IsRunning) _stopwatch.Start();
      int delayMs = _currentSleepMs;
      if (_maxJitterMs > 0) lock (_jitterRandom) delayMs += _jitterRandom.Next(_maxJitterMs);
      if (_stopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > MaxRetryTime) {
        log?.Invoke(LogLevel.Error, $"{context}: retry budget ({MaxRetryTime}) exhausted.");
        Exception wrapped = _wrapExhaustedError != null ? _wrapExhaustedError(lastError) : lastError;
        throw wrapped ?? new TimeoutException($"Retry budget ({MaxRetryTime}) exhausted.", lastError);
      }
      log?.Invoke(LogLevel.Debug, $"{context}: retrying in {delayMs} ms.");
      await Task.Delay(delayMs, token).ConfigureAwait(false);
      _currentSleepMs = transportFailure ? _initialBackoffMs : Math.Min(_currentSleepMs * 2, _maxBackoffMs);
    }
  }
}
