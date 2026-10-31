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
  /// Wraps a master CancellationTokenSource and a per-chunk CancellationTokenSource,
  /// providing Pause(), Resume(), and Cancel() with clean state transitions between chunks.
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
    private readonly CancellationTokenSource _masterCts;
    private readonly CancellationTokenRegistration _userTokenRegistration;
    private readonly CancellationTokenRegistration _ownerTokenRegistration;
    private CancellationTokenSource _chunkCts;
    private TaskCompletionSource<bool> _pauseGate;
    private InternalState _state = InternalState.Queued;
    private bool _disposed;

    /// <param name="userCancellationToken">Token supplied by the caller of the public API.</param>
    /// <param name="ownerCancellationToken">
    /// Token owned by the <see cref="FirebaseStorage" /> instance; cancelled when the instance or
    /// its <see cref="FirebaseApp" /> is disposed so in-flight transfers are aborted.
    /// </param>
    internal StorageTask(CancellationToken userCancellationToken = default,
                         CancellationToken ownerCancellationToken = default) {
      _userToken = userCancellationToken;
      _masterCts = CancellationTokenSource.CreateLinkedTokenSource(
          userCancellationToken, ownerCancellationToken);

      if (userCancellationToken.CanBeCanceled) {
        _userTokenRegistration = userCancellationToken.Register(OnExternalCancelRequested);
      }
      if (ownerCancellationToken.CanBeCanceled) {
        _ownerTokenRegistration = ownerCancellationToken.Register(OnExternalCancelRequested);
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
                 _masterCts.IsCancellationRequested;
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

    internal CancellationToken CancellationToken => _masterCts.Token;

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
    /// Pauses an in-progress or queued transfer. Cancels the active chunk token so any
    /// in-flight HTTP request releases its connection immediately.
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
    /// Awaited between chunks by ResumableUploadTask and ResumableDownloadTask.
    /// Transitions Pausing -> Paused and waits until Resume() or Cancel() is called.
    /// </summary>
    internal async Task WaitIfPausedAsync(CancellationToken token) {
      while (true) {
        Task waitTask = null;
        lock (_stateLock) {
          if (_masterCts.IsCancellationRequested ||
              token.IsCancellationRequested ||
              _state == InternalState.Canceling ||
              _state == InternalState.Canceled) {
            _state = InternalState.Canceled;
            ThrowCanceled(token);
          }

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
          waitTask = _pauseGate.Task;
        }

        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _masterCts.Token, token)) {
          var tcs = new TaskCompletionSource<bool>(
              TaskCreationOptions.RunContinuationsAsynchronously);
          using (linkedCts.Token.Register(() => tcs.TrySetCanceled())) {
            var completed = await Task.WhenAny(waitTask, tcs.Task).ConfigureAwait(false);
            if (completed == tcs.Task || waitTask.IsCanceled || linkedCts.IsCancellationRequested) {
              lock (_stateLock) {
                _state = InternalState.Canceled;
              }
              ThrowCanceled(token);
            }
          }
        }
      }
    }

    /// <summary>
    /// Returns a CancellationToken for a single chunk transfer, linked to the user's
    /// CancellationToken, the master cancel token, and the per-chunk pause token.
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
        _chunkCts = CancellationTokenSource.CreateLinkedTokenSource(_masterCts.Token);
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

    internal void ThrowIfCanceled(CancellationToken token = default) {
      if (_userToken.IsCancellationRequested) {
        MarkCanceled();
        throw new OperationCanceledException(_userToken);
      }
      if (token.IsCancellationRequested) {
        MarkCanceled();
        throw new OperationCanceledException(token);
      }
      if (_masterCts.IsCancellationRequested || IsCanceled) {
        MarkCanceled();
        throw new OperationCanceledException(
            _userToken.CanBeCanceled ? _userToken : _masterCts.Token);
      }
    }

    private void ThrowCanceled(CancellationToken token) {
      if (_userToken.IsCancellationRequested) {
        throw new OperationCanceledException(_userToken);
      }
      if (token.IsCancellationRequested) {
        throw new OperationCanceledException(token);
      }
      throw new OperationCanceledException(_masterCts.Token);
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
