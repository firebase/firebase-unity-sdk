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
  /// using asynchronous semaphores without blocking thread-pool threads.
  /// </summary>
  internal static class StorageTaskScheduler {
    private static readonly SemaphoreSlim _uploadSemaphore = new SemaphoreSlim(2, 2);
    private static readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3, 3);
    private static readonly SemaphoreSlim _commandSemaphore = new SemaphoreSlim(5, 5);

    internal static async Task<T> RunUploadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _uploadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        return await action().ConfigureAwait(false);
      } finally {
        _uploadSemaphore.Release();
      }
    }

    internal static async Task RunUploadAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _uploadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        await action().ConfigureAwait(false);
      } finally {
        _uploadSemaphore.Release();
      }
    }

    internal static async Task<T> RunDownloadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        return await action().ConfigureAwait(false);
      } finally {
        _downloadSemaphore.Release();
      }
    }

    internal static async Task RunDownloadAsync(
        Func<Task> action,
        CancellationToken cancellationToken = default) {
      if (action == null) {
        throw new ArgumentNullException(nameof(action));
      }
      await _downloadSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      try {
        await action().ConfigureAwait(false);
      } finally {
        _downloadSemaphore.Release();
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
