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
  /// Non-blocking concurrency scheduler for Firebase Storage operations.
  /// Mirrors the pool split and limits of the Firebase Android SDK's
  /// <c>com.google.firebase.storage.StorageTaskScheduler</c>
  /// (<c>UPLOAD_POOL_SIZE = 2</c>, <c>DOWNLOAD_POOL_SIZE = 3</c>, <c>COMMAND_POOL_SIZE = 5</c>):
  /// separating short-lived metadata/list/delete commands (5) from long-running uploads (2) and
  /// downloads (3) prevents large transfers from head-of-line blocking fast RPCs, bounds pooled
  /// upload buffer memory (up to 32 MB per active upload), and avoids mobile uplink contention.
  /// Unlike Android's thread-pool executors, concurrency is gated via asynchronous
  /// <see cref="SemaphoreSlim" /> instances so queued transfers do not occupy thread-pool threads,
  /// and paused transfers hand their slot back until resumed (see <see cref="Slot" />).
  /// </summary>
  internal static class StorageTaskScheduler {
    // Pool sizes match Firebase Android SDK's StorageTaskScheduler
    // (UPLOAD_POOL_SIZE = 2, DOWNLOAD_POOL_SIZE = 3, COMMAND_POOL_SIZE = 5).
    internal const int MaxConcurrentUploads = 2;
    internal const int MaxConcurrentDownloads = 3;
    internal const int MaxConcurrentCommands = 5;

    private static readonly SemaphoreSlim _uploadSemaphore =
        new SemaphoreSlim(MaxConcurrentUploads, MaxConcurrentUploads);
    private static readonly SemaphoreSlim _downloadSemaphore =
        new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
    private static readonly SemaphoreSlim _commandSemaphore =
        new SemaphoreSlim(MaxConcurrentCommands, MaxConcurrentCommands);

    /// <summary>
    /// A concurrency slot held by a running transfer. The slot is handed back to the scheduler
    /// while the transfer is parked at its pause gate and re-acquired on resume, so paused
    /// transfers never starve active ones. Not thread-safe: use only from the owning transfer's
    /// sequential async flow.
    /// </summary>
    internal sealed class Slot : IDisposable {
      private readonly SemaphoreSlim _semaphore;
      private bool _held;

      internal Slot(SemaphoreSlim semaphore) {
        _semaphore = semaphore;
        _held = true;
      }

      /// <summary>
      /// Equivalent to <see cref="StorageTask.WaitIfPausedAsync(CancellationToken)" />, but
      /// releases this slot for the duration of the pause. Throws
      /// <see cref="OperationCanceledException" /> if the transfer is cancelled while parked, in
      /// which case the slot is not re-acquired.
      /// </summary>
      internal async Task WaitIfPausedAsync(StorageTask task, CancellationToken token) {
        while (_held && task.IsPaused) {
          _held = false;
          _semaphore.Release();
          await task.WaitIfPausedAsync(token).ConfigureAwait(false);
          await _semaphore.WaitAsync(token).ConfigureAwait(false);
          _held = true;
        }
        await task.WaitIfPausedAsync(token).ConfigureAwait(false);
      }

      public void Dispose() {
        if (_held) {
          _held = false;
          _semaphore.Release();
        }
      }
    }

    internal static async Task<T> RunUploadAsync<T>(
        Func<Slot, Task<T>> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _uploadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      using (var slot = new Slot(_uploadSemaphore)) {
        return await action(slot).ConfigureAwait(false);
      }
    }

    internal static async Task<T> RunDownloadAsync<T>(
        Func<Slot, Task<T>> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      using (var slot = new Slot(_downloadSemaphore)) {
        return await action(slot).ConfigureAwait(false);
      }
    }

    internal static async Task RunDownloadAsync(
        Func<Slot, Task> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      using (var slot = new Slot(_downloadSemaphore)) {
        await action(slot).ConfigureAwait(false);
      }
    }

    internal static async Task<T> RunCommandAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _commandSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        return await action().ConfigureAwait(false);
      } finally {
        _commandSemaphore.Release();
      }
    }

    internal static async Task RunCommandAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _commandSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        await action().ConfigureAwait(false);
      } finally {
        _commandSemaphore.Release();
      }
    }
  }
}
