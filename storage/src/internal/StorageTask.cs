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
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Thread-safe task controller for Firebase Storage resumable uploads and downloads.
  /// Wraps a master CancellationTokenSource and a per-attempt CancellationTokenSource,
  /// providing Pause(), Resume(), and Cancel() with clean state transitions between attempts,
  /// and <see cref="RunAttemptAsync" />, the retry step shared by every transfer loop.
  /// </summary>
  internal sealed class StorageTask : IDisposable {
    internal enum InternalState {
      Queued,
      InProgress,
      Pausing,
      Paused,
      Canceling,
      Canceled,
      Completed,
      Faulted
    }

    private readonly object _stateLock = new object();
    private readonly CancellationToken _userToken;
    private readonly CancellationToken _ownerToken;
    private readonly CancellationTokenSource _masterCts;
    // Cached so the token stays queryable after _masterCts is disposed (CancellationTokenSource.Token
    // throws ObjectDisposedException; a CancellationToken value does not).
    private readonly CancellationToken _masterToken;
    private readonly CancellationTokenRegistration _userTokenRegistration;
    private readonly CancellationTokenRegistration _ownerTokenRegistration;
    private CancellationTokenSource _chunkCts;
    private TaskCompletionSource<bool> _pauseGate;
    private InternalState _state = InternalState.Queued;
    private bool _explicitCancel;
    private bool _disposed;

    /// <param name="userCancellationToken">Token supplied by the caller of the public API.</param>
    /// <param name="ownerCancellationToken">
    /// Token owned by the <see cref="FirebaseStorage" /> instance; cancelled when the instance or
    /// its <see cref="FirebaseApp" /> is disposed so in-flight transfers are aborted.
    /// </param>
    internal StorageTask(CancellationToken userCancellationToken = default,
                         CancellationToken ownerCancellationToken = default) {
      _userToken = userCancellationToken;
      _ownerToken = ownerCancellationToken;
      _masterCts = CancellationTokenSource.CreateLinkedTokenSource(
          userCancellationToken, ownerCancellationToken);
      _masterToken = _masterCts.Token;

      if (userCancellationToken.CanBeCanceled) {
        _userTokenRegistration = userCancellationToken.Register(OnExternalCancelRequested);
      }
      if (ownerCancellationToken.CanBeCanceled) {
        _ownerTokenRegistration = ownerCancellationToken.Register(OnExternalCancelRequested);
      }
    }

    /// <summary>
    /// Creates the task for a transfer on <paramref name="reference" />, linked to its
    /// <see cref="FirebaseStorage" /> dispose token, and attaches <paramref name="controller" />
    /// if one was supplied. A controller that is already driving another transfer throws
    /// <see cref="InvalidOperationException" />, in which case the new task is disposed.
    /// </summary>
    internal static StorageTask CreateForTransfer(
        StorageReference reference,
        CancellationToken userCancellationToken,
        StorageController controller) {
      var task = new StorageTask(userCancellationToken, reference.Storage?.DisposeToken ?? default);
      if (controller != null) {
        try {
          controller.Attach(task, reference);
        } catch {
          task.Dispose();
          throw;
        }
      }
      return task;
    }

    /// <summary>
    /// Scheduler slot held by the transfer, if any. It is handed back while the transfer is parked
    /// at the pause gate (see <see cref="StorageTaskScheduler.Slot.WaitIfPausedAsync" />).
    /// </summary>
    internal StorageTaskScheduler.Slot Slot { get; set; }

    /// <summary>
    /// Runs one attempt of a transfer step (a chunk upload, a ranged download, a session control
    /// request) under a fresh per-attempt cancellation token and a <see cref="StallWatchdog" />,
    /// absorbing the failures a retry loop should survive:
    /// <list type="bullet">
    /// <item>a <see cref="StorageException" /> whose status is retryable (5xx / 408 / 429) or a
    /// transport failure (see <see cref="StorageHttpClient.IsTransportException" />) waits out
    /// the <paramref name="backoff" /> and returns false, or throws the failure (with its real
    /// HTTP status and body) once the budget is spent;</item>
    /// <item>an attempt severed by <see cref="Pause" /> parks at the pause gate until
    /// <see cref="Resume" />, resets the budget and returns false;</item>
    /// <item>cancellation of the transfer or of <paramref name="callerToken" /> always wins and is
    /// raised as <see cref="OperationCanceledException" />.</item>
    /// </list>
    /// Returns true when <paramref name="attempt" /> completed. Anything else it throws (a fatal
    /// <see cref="StorageException" />, a local I/O error) propagates unchanged.
    /// </summary>
    internal async Task<bool> RunAttemptAsync(
        Func<StallWatchdog, Task> attempt,
        RetryBackoff backoff,
        ModuleLogger logger,
        string context,
        CancellationToken callerToken = default) {
      await WaitIfPausedAsync(Slot, callerToken).ConfigureAwait(false);
      ThrowIfCanceled(callerToken);

      Exception error;
      bool transportFailure;
      CancellationTokenSource attemptCts =
          CancellationTokenSource.CreateLinkedTokenSource(GetChunkCancellationToken(), callerToken);
      var watchdog = new StallWatchdog(attemptCts.Token);
      try {
        await attempt(watchdog).ConfigureAwait(false);
        return true;
      } catch (StorageException ex) when (StorageHttpClient.IsRetryableStatusCode(ex.HttpResultCode)) {
        error = ex;
        transportFailure = false;
        logger?.LogMessage(LogLevel.Warning, $"{context}: HTTP {ex.HttpResultCode}.");
      } catch (Exception ex) when (StorageHttpClient.IsTransportException(ex)) {
        // Cancellation wins over retry; after that, a request cut short by Pause() is not a failure.
        ThrowIfCanceled(callerToken);
        if (IsPaused) {
          await WaitIfPausedAsync(Slot, callerToken).ConfigureAwait(false);
          backoff.Reset();
          return false;
        }
        // A watchdog timeout surfaces as a retryable HttpRequestException; any other cancellation
        // raised by the HTTP stack is kept as a transient failure rather than aborting the transfer.
        error = ex is OperationCanceledException canceled
            ? (Exception)watchdog.AsStallException(canceled) ?? canceled
            : ex;
        transportFailure = true;
        logger?.LogMessage(LogLevel.Warning, $"{context} failed: {error.Message}");
      } finally {
        watchdog.Dispose();
        attemptCts.Dispose();
      }

      // Wait before the next attempt. Pause() cuts the wait short (the next attempt then parks at
      // the gate) and resets the budget, matching a pause during the attempt itself.
      try {
        await backoff.DelayOrThrowAsync(
            transportFailure, error, logger, context, GetChunkCancellationToken())
            .ConfigureAwait(false);
      } catch (OperationCanceledException) when (!_masterToken.IsCancellationRequested &&
                                                 !callerToken.IsCancellationRequested &&
                                                 IsPaused) {
        backoff.Reset();
      }
      return false;
    }

    /// <summary>
    /// Runs the body of a transfer and maps its outcome onto this task's terminal state: a
    /// cancellation marks the task cancelled (as does any failure observed after cancellation,
    /// which is re-raised as <see cref="OperationCanceledException" />) and anything else marks
    /// it faulted. <paramref name="onCanceled" /> runs when the caller (token or controller)
    /// cancelled the transfer, but not when it was aborted by disposing the
    /// <see cref="FirebaseStorage" /> or <see cref="FirebaseApp" />. <paramref name="cleanupAsync" />
    /// (progress flush, temporary files) runs on every path before the task is disposed.
    /// </summary>
    internal async Task<T> RunTransferAsync<T>(
        Func<Task<T>> body,
        Action onCanceled,
        Func<Task> cleanupAsync) {
      try {
        return await body().ConfigureAwait(false);
      } catch (OperationCanceledException) {
        MarkCanceled();
        InvokeOnCanceled(onCanceled);
        throw;
      } catch (Exception) {
        if (IsCanceled) {
          MarkCanceled();
          InvokeOnCanceled(onCanceled);
          ThrowIfCanceled();
        }
        MarkFaulted();
        throw;
      } finally {
        if (cleanupAsync != null) {
          await cleanupAsync().ConfigureAwait(false);
        }
        Dispose();
      }
    }

    /// <inheritdoc cref="RunTransferAsync{T}" />
    internal Task RunTransferAsync(Func<Task> body, Action onCanceled, Func<Task> cleanupAsync) {
      return RunTransferAsync(
          async () => {
            await body().ConfigureAwait(false);
            return true;
          },
          onCanceled,
          cleanupAsync);
    }

    private void InvokeOnCanceled(Action onCanceled) {
      if (onCanceled == null) {
        return;
      }
      bool ownerOnly;
      lock (_stateLock) {
        ownerOnly = _ownerToken.IsCancellationRequested &&
                    !_userToken.IsCancellationRequested &&
                    !_explicitCancel;
      }
      if (!ownerOnly) {
        onCanceled();
      }
    }

    internal InternalState State {
      get {
        lock (_stateLock) {
          return _state;
        }
      }
    }

    public bool IsPaused {
      get {
        lock (_stateLock) {
          return _state == InternalState.Pausing || _state == InternalState.Paused;
        }
      }
    }

    public bool IsInProgress {
      get {
        lock (_stateLock) {
          return _state == InternalState.Queued || _state == InternalState.InProgress;
        }
      }
    }

    public bool IsCanceled {
      get {
        lock (_stateLock) {
          return _state == InternalState.Canceling ||
                 _state == InternalState.Canceled ||
                 _masterToken.IsCancellationRequested;
        }
      }
    }

    public bool IsCompleted {
      get {
        lock (_stateLock) {
          return _state == InternalState.Completed ||
                 _state == InternalState.Faulted ||
                 _state == InternalState.Canceled;
        }
      }
    }

    internal CancellationToken CancellationToken => _masterToken;

    internal CancellationToken UserCancellationToken => _userToken;

    private void OnExternalCancelRequested() {
      TaskCompletionSource<bool> gateToCancel = null;
      CancellationTokenSource chunkToCancel = null;
      lock (_stateLock) {
        if (_state != InternalState.Completed &&
            _state != InternalState.Faulted &&
            _state != InternalState.Canceled) {
          _state = InternalState.Canceling;
          gateToCancel = _pauseGate;
          _pauseGate = null;
          chunkToCancel = _chunkCts;
        }
      }
      try {
        chunkToCancel?.Cancel();
      } catch (ObjectDisposedException) {
      }
      gateToCancel?.TrySetCanceled();
    }

    /// <summary>
    /// Transitions from Queued to InProgress when the transfer acquires a scheduler slot.
    /// </summary>
    internal void TransitionToInProgress() {
      lock (_stateLock) {
        if (_state == InternalState.Queued) {
          _state = InternalState.InProgress;
        }
      }
    }

    /// <summary>
    /// Pauses an in-progress or queued transfer. Cancels the active attempt token so any
    /// in-flight HTTP request or backoff wait releases immediately.
    /// </summary>
    public bool Pause() {
      CancellationTokenSource chunkToCancel = null;
      lock (_stateLock) {
        if (_state != InternalState.InProgress && _state != InternalState.Queued) {
          return false;
        }
        _state = _state == InternalState.Queued ? InternalState.Paused : InternalState.Pausing;
        if (_pauseGate == null || _pauseGate.Task.IsCompleted) {
          _pauseGate = new TaskCompletionSource<bool>(
              TaskCreationOptions.RunContinuationsAsynchronously);
        }
        chunkToCancel = _chunkCts;
      }

      try {
        chunkToCancel?.Cancel();
      } catch (ObjectDisposedException) {
      }
      return true;
    }

    /// <summary>
    /// Resumes a paused or pausing transfer, unblocking the pause gate so the transfer
    /// loop continues from the server-committed offset.
    /// </summary>
    public bool Resume() {
      TaskCompletionSource<bool> gateToRelease = null;
      lock (_stateLock) {
        if (_state != InternalState.Paused && _state != InternalState.Pausing) {
          return false;
        }
        _state = InternalState.InProgress;
        gateToRelease = _pauseGate;
        _pauseGate = null;
      }

      gateToRelease?.TrySetResult(true);
      return true;
    }

    /// <summary>
    /// Cancels the transfer, signaling the master CancellationTokenSource and unblocking
    /// any active pause gate.
    /// </summary>
    public bool Cancel() {
      TaskCompletionSource<bool> gateToCancel = null;
      CancellationTokenSource chunkToCancel = null;
      lock (_stateLock) {
        if (_state == InternalState.Completed ||
            _state == InternalState.Faulted ||
            _state == InternalState.Canceling ||
            _state == InternalState.Canceled) {
          return false;
        }
        _state = InternalState.Canceling;
        _explicitCancel = true;
        gateToCancel = _pauseGate;
        _pauseGate = null;
        chunkToCancel = _chunkCts;
      }

      try {
        chunkToCancel?.Cancel();
      } catch (ObjectDisposedException) {
      }
      try {
        _masterCts.Cancel();
      } catch (ObjectDisposedException) {
      }
      gateToCancel?.TrySetCanceled();
      return true;
    }

    /// <summary>
    /// Transitions Pausing -> Paused and waits until Resume() or Cancel() is called. When the
    /// transfer holds a scheduler <paramref name="slot" />, the slot is handed back for the
    /// duration of the pause (see <see cref="StorageTaskScheduler.Slot.WaitIfPausedAsync" />).
    /// </summary>
    internal Task WaitIfPausedAsync(StorageTaskScheduler.Slot slot, CancellationToken callerToken) {
      return slot != null ? slot.WaitIfPausedAsync(this, callerToken) : WaitIfPausedAsync(callerToken);
    }

    /// <summary>
    /// Transitions Pausing -> Paused and waits until Resume() or Cancel() is called. Cancellation
    /// of the transfer, or of <paramref name="callerToken" />, is raised as
    /// <see cref="OperationCanceledException" />. <paramref name="onPark" /> runs, under the
    /// state lock, each time the transfer is about to wait at the gate, so a holder of a scheduler
    /// slot can hand it back without racing a concurrent <see cref="Pause" />.
    /// </summary>
    internal async Task WaitIfPausedAsync(CancellationToken callerToken = default,
                                          Action onPark = null) {
      while (true) {
        Task gate;
        lock (_stateLock) {
          if (_masterToken.IsCancellationRequested ||
              _state == InternalState.Canceling ||
              _state == InternalState.Canceled) {
            _state = InternalState.Canceled;
            throw new OperationCanceledException(
                _userToken.IsCancellationRequested ? _userToken : _masterToken);
          }
          callerToken.ThrowIfCancellationRequested();

          if (_state == InternalState.Pausing) {
            _state = InternalState.Paused;
          }
          if (_state != InternalState.Paused) {
            return;
          }
          if (_pauseGate == null) {
            _pauseGate = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
          }
          gate = _pauseGate.Task;
          onPark?.Invoke();
        }

        // Resume() completes the gate and Cancel() / owner disposal cancel it; a caller-scoped
        // token needs its own wake-up. Task.WhenAny never throws, so the loop re-evaluates the
        // state above after any of them fires.
        if (callerToken.CanBeCanceled) {
          var wakeUp = new TaskCompletionSource<bool>(
              TaskCreationOptions.RunContinuationsAsynchronously);
          using (callerToken.Register(() => wakeUp.TrySetResult(true))) {
            await Task.WhenAny(gate, wakeUp.Task).ConfigureAwait(false);
          }
        } else {
          await Task.WhenAny(gate).ConfigureAwait(false);
        }
      }
    }

    /// <summary>
    /// Returns a CancellationToken for a single attempt, linked to the master token (user token,
    /// owner token and Cancel()) and cancelled by Pause(). Each call retires the previous token.
    /// </summary>
    internal CancellationToken GetChunkCancellationToken() {
      lock (_stateLock) {
        if (_disposed) {
          return new CancellationToken(canceled: true);
        }
        try {
          _chunkCts?.Dispose();
        } catch (ObjectDisposedException) {
        }
        _chunkCts = CancellationTokenSource.CreateLinkedTokenSource(_masterToken);
        if (_state == InternalState.Pausing ||
            _state == InternalState.Paused ||
            _state == InternalState.Canceling ||
            _state == InternalState.Canceled) {
          _chunkCts.Cancel();
        }
        return _chunkCts.Token;
      }
    }

    internal void MarkCompleted() {
      lock (_stateLock) {
        _state = InternalState.Completed;
      }
    }

    internal void MarkFaulted() {
      lock (_stateLock) {
        _state = InternalState.Faulted;
      }
    }

    internal void MarkCanceled() {
      lock (_stateLock) {
        _state = InternalState.Canceled;
      }
    }

    /// <summary>
    /// Throws <see cref="OperationCanceledException" /> if the transfer is cancelled, marking it
    /// cancelled, or if <paramref name="callerToken" /> is cancelled. A caller-scoped token (for
    /// example the token of one <c>ReadAsync</c> call on a download stream) aborts only that call
    /// and leaves the transfer usable.
    /// </summary>
    internal void ThrowIfCanceled(CancellationToken callerToken = default) {
      if (_masterToken.IsCancellationRequested || IsCanceled) {
        MarkCanceled();
        throw new OperationCanceledException(
            _userToken.IsCancellationRequested ? _userToken : _masterToken);
      }
      callerToken.ThrowIfCancellationRequested();
    }

    public void Dispose() {
      lock (_stateLock) {
        if (_disposed) {
          return;
        }
        _disposed = true;
      }
      _userTokenRegistration.Dispose();
      _ownerTokenRegistration.Dispose();
      try {
        _chunkCts?.Dispose();
      } catch (ObjectDisposedException) {
      }
      try {
        _masterCts.Dispose();
      } catch (ObjectDisposedException) {
      }
    }
  }
}
