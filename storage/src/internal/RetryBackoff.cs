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

namespace Firebase.Storage.Internal {
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
  internal sealed class RetryBackoff {
    // Mirrors Firebase Android SDK's ExponentialBackoffSender (NETWORK_STATUS_POLL_INTERVAL = 1000,
    // MAXIMUM_WAIT_TIME_MILLI = 30000, RND_MAX = 250).
    internal const int InitialBackoffMs = 1000;
    internal const int MaxBackoffMs = 30000;
    internal const int MaxJitterMs = 250;

    private static readonly Random _jitterRandom = new Random();

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private int _currentSleepMs = InitialBackoffMs;

    internal RetryBackoff(TimeSpan maxRetryTime) {
      MaxRetryTime = maxRetryTime;
    }

    /// <summary>Total time the operation may spend failing before it gives up.</summary>
    internal TimeSpan MaxRetryTime { get; }

    /// <summary>
    /// Forgets earlier failures: bytes arrived, a chunk committed, or the transfer resumed.
    /// </summary>
    internal void Reset() {
      _stopwatch.Reset();
      _currentSleepMs = InitialBackoffMs;
    }

    /// <summary>
    /// Waits before the next attempt of a transfer, or throws once the budget is spent: a
    /// <see cref="StorageException" /> in <paramref name="lastError" /> (a retryable HTTP status,
    /// which keeps its real status code and server message) is rethrown as-is, anything else is
    /// wrapped in <see cref="StorageException.ErrorRetryLimitExceeded" />.
    /// </summary>
    /// <param name="transportFailure">True if the attempt failed at the transport level (flat 1s
    /// re-poll) rather than with a retryable HTTP status (exponential backoff).</param>
    /// <param name="lastError">The failure being retried.</param>
    /// <param name="logger">Receives a debug line per retry and an error when giving up.</param>
    /// <param name="context">Describes the attempt in log lines, e.g. the transfer offset.</param>
    /// <param name="token">Aborts the wait.</param>
    internal async Task DelayOrThrowAsync(
        bool transportFailure,
        Exception lastError,
        ModuleLogger logger,
        string context,
        CancellationToken token) {
      if (!_stopwatch.IsRunning) {
        _stopwatch.Start();
      }
      int delayMs;
      lock (_jitterRandom) {
        delayMs = _currentSleepMs + _jitterRandom.Next(MaxJitterMs);
      }
      if (_stopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > MaxRetryTime) {
        logger?.LogMessage(LogLevel.Error, $"{context}: retry budget ({MaxRetryTime}) exhausted.");
        throw lastError as StorageException ?? new StorageException(
            StorageException.ErrorRetryLimitExceeded,
            0,
            lastError?.Message ??
            StorageException.GetErrorMessageForCode(StorageException.ErrorRetryLimitExceeded),
            lastError);
      }
      logger?.LogMessage(LogLevel.Debug, $"{context}: retrying in {delayMs} ms.");
      await Task.Delay(delayMs, token).ConfigureAwait(false);
      _currentSleepMs = transportFailure
          ? InitialBackoffMs
          : Math.Min(_currentSleepMs * 2, MaxBackoffMs);
    }
  }
}
