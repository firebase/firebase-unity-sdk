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
  /// Bounds concurrent uploads (2), downloads (3), and metadata/list/delete commands (5)
  /// using asynchronous semaphores without blocking thread-pool threads. Paused transfers give
  /// their slot back to the scheduler until they are resumed (see <see cref="Slot" />).
  /// </summary>
  internal static class StorageTaskScheduler {
    private static readonly SemaphoreSlim _uploadSemaphore = new SemaphoreSlim(2, 2);
    private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3, 3);
    private static readonly SemaphoreSlim _commandSemaphore = new SemaphoreSlim(5, 5);

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
      /// Equivalent to <see cref="StorageTask.WaitIfPausedAsync" />, but releases this slot for
      /// the duration of the pause. Throws <see cref="OperationCanceledException" /> if the
      /// transfer is cancelled while parked, in which case the slot is not re-acquired.
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
