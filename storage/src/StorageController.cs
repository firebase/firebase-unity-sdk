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
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  ///   Controls an in-flight upload or download, allowing the caller to pause, resume or cancel
  ///   it.
  /// </summary>
  /// <remarks>
  ///   Construct a <see cref="StorageController" /> and pass it to
  ///   <see cref="StorageReference.PutBytesAsync" />, <see cref="StorageReference.PutFileAsync" />,
  ///   <see cref="StorageReference.PutStreamAsync" />, <see cref="StorageReference.GetBytesAsync(long, IProgress{DownloadState}, System.Threading.CancellationToken, StorageController)" />,
  ///   <see cref="StorageReference.GetFileAsync" /> or
  ///   <see cref="StorageReference.GetStreamAsync(Action{System.IO.Stream}, IProgress{DownloadState}, System.Threading.CancellationToken, StorageController)" />
  ///   to associate it with that transfer. A controller is associated with one transfer at a
  ///   time; once that transfer completes, faults or is canceled it may be reused for another.
  ///   <para />
  ///   <see cref="Pause" />, <see cref="Resume" /> and <see cref="Cancel" /> may be called from any
  ///   thread. Calling <see cref="Pause" /> or <see cref="Cancel" /> before the controller has
  ///   been passed to a transfer is allowed: the transfer will start in the paused state, or be
  ///   canceled immediately, respectively.
  ///   <para />
  ///   Pausing an upload abandons the chunk currently in flight; on <see cref="Resume" /> the
  ///   server-committed offset is queried and the upload continues from there. Pausing a download
  ///   closes the current connection; on <see cref="Resume" /> the download continues with an HTTP
  ///   <c>Range</c> request from the last byte written. Canceling behaves exactly like canceling
  ///   the <see cref="System.Threading.CancellationToken" /> passed to the transfer: the returned
  ///   <see cref="System.Threading.Tasks.Task" /> ends in the <c>Canceled</c> state and, for
  ///   uploads, the resumable session is discarded.
  /// </remarks>
  public sealed class StorageController {
    private readonly object _lock = new object();
    private StorageTask _task;
    private StorageReference _reference;
    // Requests recorded before a transfer is attached.
    private bool _pauseRequested;
    private bool _cancelRequested;

    /// <summary>
    ///   Creates a controller that is not yet associated with a transfer.
    /// </summary>
    public StorageController() { }

    /// <summary>
    ///   The <see cref="StorageReference" /> of the transfer this controller is associated with,
    ///   or null if it has not been passed to a transfer yet.
    /// </summary>
    public StorageReference Reference {
      get {
        lock (_lock) {
          return _reference;
        }
      }
    }

    /// <summary>
    ///   True if the associated transfer is paused, or if <see cref="Pause" /> was called before
    ///   the controller was associated with a transfer.
    /// </summary>
    public bool IsPaused {
      get {
        StorageTask task;
        lock (_lock) {
          if (_task == null) {
            return _pauseRequested;
          }
          task = _task;
        }
        return task.IsPaused;
      }
    }

    /// <summary>
    ///   True if the associated transfer has been canceled, or if <see cref="Cancel" /> was called
    ///   before the controller was associated with a transfer.
    /// </summary>
    public bool IsCanceled {
      get {
        StorageTask task;
        lock (_lock) {
          if (_task == null) {
            return _cancelRequested;
          }
          task = _task;
        }
        return task.IsCanceled;
      }
    }

    /// <summary>
    ///   True while the associated transfer is queued, running or paused; false before a transfer
    ///   is attached and after it completes, faults or is canceled.
    /// </summary>
    public bool IsInProgress {
      get {
        StorageTask task;
        lock (_lock) {
          task = _task;
        }
        return task != null && !task.IsCompleted;
      }
    }

    /// <summary>
    ///   Pauses the associated transfer. The returned <see cref="System.Threading.Tasks.Task" />
    ///   stays pending until <see cref="Resume" /> or <see cref="Cancel" /> is called.
    /// </summary>
    /// <returns>
    ///   true if the transfer was running (or queued) and is now pausing; false if it was already
    ///   paused, completed, faulted or canceled.
    /// </returns>
    public bool Pause() {
      StorageTask task;
      lock (_lock) {
        if (_task == null) {
          if (_cancelRequested || _pauseRequested) {
            return false;
          }
          _pauseRequested = true;
          return true;
        }
        task = _task;
      }
      return task.Pause();
    }

    /// <summary>
    ///   Resumes a paused transfer from the last server-committed offset.
    /// </summary>
    /// <returns>true if the transfer was paused and is now resuming; false otherwise.</returns>
    public bool Resume() {
      StorageTask task;
      lock (_lock) {
        if (_task == null) {
          bool wasPaused = _pauseRequested;
          _pauseRequested = false;
          return wasPaused;
        }
        task = _task;
      }
      return task.Resume();
    }

    /// <summary>
    ///   Cancels the associated transfer. Equivalent to canceling the
    ///   <see cref="System.Threading.CancellationToken" /> passed to the transfer.
    /// </summary>
    /// <returns>true if the transfer was in progress or paused and is now canceling; false if it
    /// had already completed, faulted or been canceled.</returns>
    public bool Cancel() {
      StorageTask task;
      lock (_lock) {
        if (_task == null) {
          if (_cancelRequested) {
            return false;
          }
          _cancelRequested = true;
          _pauseRequested = false;
          return true;
        }
        task = _task;
      }
      return task.Cancel();
    }

    // Associates this controller with a transfer. Any Pause()/Cancel() recorded before the
    // transfer started is applied to it now.
    internal void Attach(StorageTask task, StorageReference reference) {
      bool pause;
      bool cancel;
      lock (_lock) {
        if (_task != null && !_task.IsCompleted) {
          throw new InvalidOperationException(
              "This StorageController is already controlling a transfer that has not finished. " +
              "Use a separate StorageController for each concurrent transfer.");
        }
        _task = task;
        _reference = reference;
        pause = _pauseRequested;
        cancel = _cancelRequested;
        _pauseRequested = false;
        _cancelRequested = false;
      }
      if (cancel) {
        task.Cancel();
      } else if (pause) {
        task.Pause();
      }
    }
  }
}
