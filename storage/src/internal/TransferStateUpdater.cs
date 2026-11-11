/*
 * Copyright 2017 Google LLC
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
using Firebase.Platform;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Delivers a transfer's progress object (<see cref="UploadState" /> or
  /// <see cref="DownloadState" />) to the caller's <see cref="IProgress{T}" /> on the Unity main
  /// thread, so handlers may touch UnityEngine objects as they could with the previous native
  /// implementation. Reports are coalesced: at most one delivery is queued at a time and later
  /// reports fold into it, matching the one-callback-per-poll behaviour of the native SDK. The
  /// transfer engines update the state object directly and call <see cref="Report" />.
  /// </summary>
  internal sealed class TransferStateUpdater<T> {
    private readonly IProgress<T> _handler;
    private readonly T _state;
    private readonly object _lock = new object();
    // 1 while a delivery is queued on the main thread but has not run yet.
    private int _pending;
    private Task _lastDelivery = Task.FromResult(true);

    internal TransferStateUpdater(IProgress<T> handler, T state) {
      _handler = handler;
      _state = state;
    }

    /// <summary>Queues a progress callback with the current state, unless one is already queued.</summary>
    internal void Report() {
      if (_handler == null || Interlocked.Exchange(ref _pending, 1) != 0) {
        return;
      }
      if (FirebaseHandler.DefaultInstance == null) {
        // The main loop has been torn down (editor exit / domain reload); nobody would run the job.
        Interlocked.Exchange(ref _pending, 0);
        return;
      }
      lock (_lock) {
        _lastDelivery = FirebaseHandler.RunOnMainThreadAsync(() => {
          Deliver();
          return true;
        });
      }
    }

    // Runs the queued callback, if it has not been delivered already.
    private void Deliver() {
      if (Interlocked.Exchange(ref _pending, 0) == 0) {
        return;
      }
      try {
        _handler.Report(_state);
      } catch (Exception ex) {
        // Never let a user callback exception abort the transfer; just log it.
        ExceptionAggregator.LogException(ex);
      }
    }

    /// <summary>
    /// Completes once every queued progress callback has run, so callers never observe a
    /// callback after the transfer's task completes. When called on the main thread itself (the
    /// transfer unwound through a main-thread continuation) the queued callback could not run
    /// before the task completes, so it is delivered inline instead; when the main loop is gone
    /// there is nothing to wait for.
    /// </summary>
    internal async Task FlushAsync() {
      if (_handler == null) {
        return;
      }
      Task pending;
      lock (_lock) {
        pending = _lastDelivery;
      }
      while (!pending.IsCompleted) {
        FirebaseHandler handler = FirebaseHandler.DefaultInstance;
        if (handler == null) {
          return;
        }
        if (handler.IsMainThread()) {
          Deliver();
          return;
        }
        await Task.WhenAny(pending, Task.Delay(250)).ConfigureAwait(false);
      }
    }
  }
}
