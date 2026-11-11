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
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Platform;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Implements resumable downloads (GetBytesAsync, GetFileAsync, GetStreamAsync) on top of one
  /// engine, <see cref="ResumableDownloadStream" />: a live stream that resumes with HTTP Range
  /// requests, verifies the object's ETag across reconnects and reports progress. The buffered
  /// entry points simply copy that stream into their destination.
  /// </summary>
  internal sealed class ResumableDownloadTask {
    // Copy buffer size (256 KB) matches the Firebase Android SDK's FileDownloadTask
    // (PREFERRED_CHUNK_SIZE); retry timing comes from RetryBackoff.
    internal const int BufferSize = 256 * 1024;

    // Maximum number of times a buffered download restarts from byte 0 because the remote object
    // changed mid-transfer before giving up.
    private const int MaxRestarts = 1;

    private readonly StorageReference _reference;
    private readonly StorageTask _storageTask;
    private readonly DownloadState _downloadState;
    private readonly TransferStateUpdater<DownloadState> _transferStateUpdater;
    private readonly ModuleLogger _logger;
    private readonly FirebaseApp _app;
    private readonly TimeSpan _maxRetryTime;

    internal ResumableDownloadTask(
        StorageReference reference,
        IProgress<DownloadState> progressHandler,
        CancellationToken cancelToken,
        StorageController controller = null) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _logger = reference.Logger;
      FirebaseStorage storage = reference.Storage;
      _app = storage?.App;
      _maxRetryTime = storage?.MaxDownloadRetryTime ?? FirebaseStorage.DefaultMaxTransferRetryTime;
      _storageTask = StorageTask.CreateForTransfer(reference, cancelToken, controller);
      _downloadState = new DownloadState(reference, -1);
      _transferStateUpdater = new TransferStateUpdater<DownloadState>(progressHandler, _downloadState);
    }

    internal static Task<byte[]> GetBytesAsync(
        StorageReference reference,
        long maxDownloadSizeBytes,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      var downloadTask = new ResumableDownloadTask(
          reference, progressHandler, cancelToken, controller);
      return downloadTask.RunGetBytesAsync(maxDownloadSizeBytes);
    }

    internal static Task GetFileAsync(
        StorageReference reference,
        string destinationFilePath,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(
          !string.IsNullOrEmpty(destinationFilePath),
          "destinationFilePath cannot be null or empty");
      var downloadTask = new ResumableDownloadTask(
          reference, progressHandler, cancelToken, controller);
      return downloadTask.RunGetFileAsync(destinationFilePath);
    }

    internal static Task GetStreamAsync(
        StorageReference reference,
        Action<Stream> streamProcessor,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckNotNull(streamProcessor, nameof(streamProcessor));
      var downloadTask = new ResumableDownloadTask(
          reference, progressHandler, cancelToken, controller);
      return downloadTask.RunGetStreamProcessorAsync(streamProcessor);
    }

    internal static Task<Stream> GetStreamAsync(
        StorageReference reference,
        CancellationToken cancelToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      var downloadTask = new ResumableDownloadTask(reference, null, cancelToken);
      return downloadTask.RunOpenStreamAsync();
    }

    internal Task<byte[]> RunGetBytesAsync(long maxDownloadSizeBytes) {
      return _storageTask.RunTransferAsync(
          () => {
            StorageHttpClient.EnsureObjectReference(_reference, "GetBytes");
            return StorageTaskScheduler.RunDownloadAsync(
                async slot => {
                  _storageTask.Slot = slot;
                  using (var memoryStream = new MemoryStream()) {
                    await DownloadToStreamAsync(memoryStream, maxDownloadSizeBytes)
                        .ConfigureAwait(false);
                    return memoryStream.ToArray();
                  }
                },
                _storageTask.CancellationToken);
          },
          onCanceled: null,
          cleanupAsync: _transferStateUpdater.FlushAsync);
    }

    internal Task RunGetFileAsync(string destinationFilePath) {
      string localPath = IoUtil.ResolveLocalFilePath(destinationFilePath);
      // Download into a uniquely named sibling temp file and move it into place on success so a
      // failed or cancelled download never leaves a truncated file at the destination, and two
      // concurrent downloads to the same destination cannot clobber each other's partial data.
      string tempPath = $"{localPath}.{Guid.NewGuid():N}.fbdownload";
      return _storageTask.RunTransferAsync(
          () => {
            StorageHttpClient.EnsureObjectReference(_reference, "GetFile");
            return StorageTaskScheduler.RunDownloadAsync(
                async slot => {
                  _storageTask.Slot = slot;
                  string directory = Path.GetDirectoryName(localPath);
                  if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                  }
                  using (var fileStream = new FileStream(
                      tempPath,
                      FileMode.Create,
                      FileAccess.Write,
                      FileShare.Read,
                      IoUtil.FileStreamBufferSize,
                      useAsync: true)) {
                    await DownloadToStreamAsync(fileStream, maxDownloadSizeBytes: 0)
                        .ConfigureAwait(false);
                    await fileStream.FlushAsync(_storageTask.CancellationToken)
                        .ConfigureAwait(false);
                  }
                  // Replace keeps the existing file if the move fails, unlike delete-then-move.
                  if (File.Exists(localPath)) {
                    File.Replace(tempPath, localPath, null);
                  } else {
                    File.Move(tempPath, localPath);
                  }
                  return true;
                },
                _storageTask.CancellationToken);
          },
          onCanceled: null,
          cleanupAsync: () => {
            try {
              if (File.Exists(tempPath)) {
                File.Delete(tempPath);
              }
            } catch (Exception) {
              // Best-effort cleanup of the partial temp file.
            }
            return _transferStateUpdater.FlushAsync();
          });
    }

    internal Task RunGetStreamProcessorAsync(Action<Stream> streamProcessor) {
      return _storageTask.RunTransferAsync(
          () => {
            StorageHttpClient.EnsureObjectReference(_reference, "GetStream");
            return StorageTaskScheduler.RunDownloadAsync(
                async slot => {
                  _storageTask.Slot = slot;
                  _storageTask.ThrowIfCanceled();
                  _storageTask.TransitionToInProgress();

                  using (ResumableDownloadStream downloadStream =
                      await OpenStreamAsync(ownsStorageTask: false).ConfigureAwait(false)) {
                    await Task.Run(
                        () => {
                          _storageTask.ThrowIfCanceled();
                          streamProcessor(downloadStream);
                          _storageTask.ThrowIfCanceled();
                        },
                        _storageTask.CancellationToken).ConfigureAwait(false);
                  }
                  _storageTask.MarkCompleted();
                  return true;
                },
                _storageTask.CancellationToken);
          },
          onCanceled: null,
          cleanupAsync: _transferStateUpdater.FlushAsync);
    }

    // Opens a live download stream. A scheduler slot is held only while the request is issued
    // and the response headers are read; it is released before the stream is handed to the
    // caller so that an undisposed stream can never starve other downloads. Unlike the other
    // entry points the StorageTask outlives this call: the returned stream owns it.
    internal async Task<Stream> RunOpenStreamAsync() {
      try {
        StorageHttpClient.EnsureObjectReference(_reference, "GetStream");
        return await StorageTaskScheduler.RunDownloadAsync(
            async slot => {
              _storageTask.ThrowIfCanceled();
              _storageTask.TransitionToInProgress();
              return (Stream)await OpenStreamAsync(ownsStorageTask: true).ConfigureAwait(false);
            },
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        _storageTask.Dispose();
        throw;
      } catch (Exception) {
        bool canceled = _storageTask.IsCanceled;
        if (canceled) {
          _storageTask.MarkCanceled();
        } else {
          _storageTask.MarkFaulted();
        }
        _storageTask.Dispose();
        if (canceled) {
          _storageTask.ThrowIfCanceled();
        }
        throw;
      }
    }

    // Copies the object into destination (which must be at position 0 and seekable/truncatable so
    // the copy can restart if the remote object changes mid-download), enforcing
    // maxDownloadSizeBytes when positive.
    private async Task DownloadToStreamAsync(Stream destination, long maxDownloadSizeBytes) {
      _storageTask.ThrowIfCanceled();
      _storageTask.TransitionToInProgress();

      int restarts = 0;
      var buffer = new byte[BufferSize];
      while (true) {
        using (ResumableDownloadStream source =
            await OpenStreamAsync(ownsStorageTask: false).ConfigureAwait(false)) {
          try {
            long totalBytes = source.Length;
            if (maxDownloadSizeBytes > 0 && totalBytes > maxDownloadSizeBytes) {
              throw new IndexOutOfRangeException(
                  $"Streamed file size ({totalBytes} bytes) exceeded maximum allowed size " +
                  $"of {maxDownloadSizeBytes} bytes.");
            }
            if (destination is MemoryStream memoryStream &&
                totalBytes > memoryStream.Capacity && totalBytes <= int.MaxValue) {
              memoryStream.Capacity = (int)totalBytes;
            }

            while (true) {
              int read = await source.ReadAsync(buffer, 0, buffer.Length, default)
                  .ConfigureAwait(false);
              if (read <= 0) {
                break;
              }
              if (maxDownloadSizeBytes > 0 && source.Position > maxDownloadSizeBytes) {
                throw new IndexOutOfRangeException(
                    $"Streamed file size ({Math.Max(source.Length, source.Position)} bytes) " +
                    $"exceeded maximum allowed size of {maxDownloadSizeBytes} bytes.");
              }
              // Outside the retry step: a destination error propagates unchanged.
              await destination.WriteAsync(buffer, 0, read, _storageTask.CancellationToken)
                  .ConfigureAwait(false);
            }
            _storageTask.MarkCompleted();
            return;
          } catch (StorageException ex) when (ex.HttpResultCode == ObjectChangedStatus &&
                                               restarts++ < MaxRestarts) {
            // Remote object mutated mid-download; truncate and restart from byte 0.
            _logger.LogMessage(LogLevel.Warning,
                "Remote object changed during download; restarting from byte 0.");
            destination.Position = 0;
            destination.SetLength(0);
            _downloadState.BytesTransferred = 0;
          }
        }
      }
    }

    // Issues the first GET for a stream (retrying within the download budget) and wraps the
    // response in a ResumableDownloadStream.
    private async Task<ResumableDownloadStream> OpenStreamAsync(bool ownsStorageTask) {
      var backoff = new RetryBackoff(_maxRetryTime);
      HttpResponseMessage response = await StorageHttpClient.SendAsync(
          _storageTask,
          () => CreateMediaRequestAsync(0),
          backoff,
          HttpCompletionOption.ResponseHeadersRead,
          _logger,
          "Download stream open").ConfigureAwait(false);
      try {
        long totalBytes = ParseTotalBytesFromResponse(response, 0);
        if (totalBytes >= 0) {
          _downloadState.TotalByteCount = totalBytes;
        }
        Stream innerStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return new ResumableDownloadStream(
            this,
            response,
            innerStream,
            GetResponseETag(response),
            ownsStorageTask,
            backoff);
      } catch {
        response.Dispose();
        throw;
      }
    }

    private async Task<HttpResponseMessage> SendMediaGetRequestAsync(
        long resumeOffset,
        CancellationToken cancellationToken) {
      using (var request = await CreateMediaRequestAsync(resumeOffset).ConfigureAwait(false)) {
        return await StorageHttpClient.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
      }
    }

    private async Task<HttpRequestMessage> CreateMediaRequestAsync(long resumeOffset) {
      string objectUrl = StorageHttpClient.GetObjectUrl(_reference);
      var request = new HttpRequestMessage(HttpMethod.Get, $"{objectUrl}?alt=media");
      await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
      if (resumeOffset > 0) {
        request.Headers.Range = new RangeHeaderValue(resumeOffset, null);
      }
      return request;
    }

    private static string GetResponseETag(HttpResponseMessage response) {
      return response?.Headers?.ETag?.Tag ?? StorageHttpClient.GetHeaderFirstValue(response, "ETag");
    }

    private static long ParseTotalBytesFromResponse(
        HttpResponseMessage response,
        long resumeOffset) {
      if (response?.Content?.Headers == null) {
        return -1;
      }
      if (response.Content.Headers.ContentRange != null &&
          response.Content.Headers.ContentRange.Length.HasValue) {
        return response.Content.Headers.ContentRange.Length.Value;
      }
      if (response.Content.Headers.ContentLength.HasValue) {
        return resumeOffset + response.Content.Headers.ContentLength.Value;
      }
      return -1;
    }

    // Status carried by the StorageException raised when the remote object changed between two
    // requests of the same download (ETag or size mismatch); buffered downloads restart on it.
    private const int ObjectChangedStatus = (int)HttpStatusCode.PreconditionFailed;

    private static StorageException ObjectChangedException(string what) {
      return new StorageException(
          StorageException.ErrorUnknown,
          ObjectChangedStatus,
          $"Remote object {what} changed during download.");
    }

    /// <summary>
    /// Live, forward-only download stream. Reports progress on Read / ReadAsync and transparently
    /// reconnects with HTTP Range + ETag verification on transient mid-stream failures. The stream
    /// does not hold a scheduler slot, so leaving it undisposed cannot block other downloads; the
    /// finalizer closes the underlying connection as a safety net.
    /// </summary>
    private sealed class ResumableDownloadStream : Stream {
      private readonly ResumableDownloadTask _owner;
      private readonly bool _ownsStorageTask;
      private readonly CancellationTokenRegistration _cancelRegistration;
      // Shared across reads so consecutive failures accumulate against one budget, and reset
      // whenever a read succeeds.
      private readonly RetryBackoff _backoff;
      // Guards _currentResponse / _currentStream against the cancellation callback, which closes
      // the connection from another thread to abort a blocking read.
      private readonly object _connectionLock = new object();
      private HttpResponseMessage _currentResponse;
      private Stream _currentStream;
      private readonly string _expectedETag;
      private long _bytesReadTotal;
      private bool _reportedInitialProgress;
      private int _disposed;

      public ResumableDownloadStream(
          ResumableDownloadTask owner,
          HttpResponseMessage initialResponse,
          Stream initialStream,
          string initialETag,
          bool ownsStorageTask,
          RetryBackoff backoff) {
        _owner = owner;
        _currentResponse = initialResponse;
        _currentStream = initialStream;
        _expectedETag = initialETag;
        _ownsStorageTask = ownsStorageTask;
        _backoff = backoff;

        if (_owner._storageTask.CancellationToken.CanBeCanceled) {
          // Capture a weak reference so an undisposed stream stays collectable: the registration
          // is reachable from the FirebaseStorage dispose token, and a strong capture would keep
          // the stream (and its connection) alive until the app is disposed.
          var weakSelf = new WeakReference<ResumableDownloadStream>(this);
          _cancelRegistration = _owner._storageTask.CancellationToken.Register(() => {
            if (weakSelf.TryGetTarget(out ResumableDownloadStream self)) {
              self.DisposeCurrentStream();
            }
          });
        }
      }

      ~ResumableDownloadStream() {
        Dispose(false);
      }

      public override bool CanRead => _disposed == 0;
      public override bool CanSeek => false;
      public override bool CanWrite => false;

      /// <summary>Total object size if reported by the server, otherwise -1.</summary>
      public override long Length => _owner._downloadState.TotalByteCount;

      public override long Position {
        get => _bytesReadTotal;
        set => throw new NotSupportedException();
      }

      public override void Flush() {
      }

      public override int Read(byte[] buffer, int offset, int count) {
        // Reconnecting needs Auth / App Check tokens that are delivered by the Unity main loop, so
        // a blocking read on the main thread must not wait for one: it would deadlock the editor.
        FirebaseHandler handler = FirebaseHandler.DefaultInstance;
        bool allowReconnect = handler == null || !handler.IsMainThread();
        return ReadAsync(buffer, offset, count, default, allowReconnect).GetAwaiter().GetResult();
      }

      public override Task<int> ReadAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken) {
        return ReadAsync(buffer, offset, count, cancellationToken, allowReconnect: true);
      }

      private async Task<int> ReadAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken,
          bool allowReconnect) {
        ThrowIfDisposed();
        int read = 0;
        while (true) {
          bool done;
          try {
            done = await _owner._storageTask.RunAttemptAsync(
                async watchdog => {
                  try {
                    Stream current = CurrentStream;
                    if (current == null) {
                      if (!allowReconnect) {
                        throw new StorageException(
                            StorageException.ErrorUnknown,
                            0,
                            "The download stream lost its connection and cannot reconnect on the " +
                            "Unity main thread. Read it from a background thread or use ReadAsync.");
                      }
                      current = await ReconnectAsync(watchdog).ConfigureAwait(false);
                      if (current == null) {
                        // 416: the server has no bytes past the ones already delivered.
                        read = 0;
                      }
                    }
                    if (current != null) {
                      read = await current.ReadAsync(buffer, offset, count, watchdog.Token)
                          .ConfigureAwait(false);
                    }
                    if (read > 0 || !_reportedInitialProgress) {
                      _bytesReadTotal += read;
                      // Bytes arrived: the connection is healthy, so forget earlier failures.
                      _backoff.Reset();
                      _owner._downloadState.BytesTransferred = _bytesReadTotal;
                      _owner._transferStateUpdater.Report();
                      _reportedInitialProgress = true;
                    }
                  } catch {
                    // Whatever interrupted the read (pause, stall, network error), the connection
                    // is unusable: drop it so the next attempt reconnects with a Range request.
                    DisposeCurrentStream();
                    throw;
                  }
                },
                _backoff,
                _owner._logger,
                $"Download stream at offset {_bytesReadTotal}",
                cancellationToken).ConfigureAwait(false);
          } catch (Exception) when (_disposed != 0) {
            throw new ObjectDisposedException(nameof(ResumableDownloadStream));
          }
          if (done) {
            return read;
          }
        }
      }

      private Stream CurrentStream {
        get {
          lock (_connectionLock) {
            return _currentStream;
          }
        }
      }

      // Re-issues the media request from _bytesReadTotal and installs the new response stream.
      // Returns null if the server reports that no bytes remain (416).
      private async Task<Stream> ReconnectAsync(StallWatchdog watchdog) {
        HttpResponseMessage response = await _owner.SendMediaGetRequestAsync(
            _bytesReadTotal, watchdog.Token).ConfigureAwait(false);
        Stream newStream = null;
        try {
          if (_bytesReadTotal > 0 &&
              response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) {
            long? totalBytes = response.Content?.Headers?.ContentRange?.Length;
            if (totalBytes.HasValue && totalBytes.Value != _bytesReadTotal) {
              throw ObjectChangedException("size");
            }
            response.Dispose();
            return null;
          }
          if (!response.IsSuccessStatusCode) {
            throw await StorageHttpClient.CreateExceptionAsync(response).ConfigureAwait(false);
          }

          string newETag = GetResponseETag(response);
          if (_expectedETag != null &&
              !string.IsNullOrEmpty(newETag) &&
              !string.Equals(_expectedETag, newETag, StringComparison.Ordinal)) {
            throw ObjectChangedException("ETag");
          }

          newStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
          if (_bytesReadTotal > 0 && response.StatusCode == HttpStatusCode.OK) {
            // The server ignored the Range header and restarted from byte 0, so drain the bytes
            // that have already been handed to the caller before resuming.
            long skipped = await IoUtil.SkipAsync(
                newStream, _bytesReadTotal, watchdog.Token, watchdog.Touch).ConfigureAwait(false);
            if (skipped < _bytesReadTotal) {
              throw ObjectChangedException("size");
            }
          }

          lock (_connectionLock) {
            // The cancellation callback may have run while we were reconnecting; never install a
            // connection it cannot close.
            if (_disposed != 0 || _owner._storageTask.CancellationToken.IsCancellationRequested) {
              throw new OperationCanceledException(_owner._storageTask.CancellationToken);
            }
            _currentResponse = response;
            _currentStream = newStream;
          }
          return newStream;
        } catch {
          newStream?.Dispose();
          response.Dispose();
          throw;
        }
      }

      public override long Seek(long offset, SeekOrigin origin) {
        throw new NotSupportedException();
      }

      public override void SetLength(long value) {
        throw new NotSupportedException();
      }

      public override void Write(byte[] buffer, int offset, int count) {
        throw new NotSupportedException();
      }

      private void ThrowIfDisposed() {
        if (_disposed != 0) {
          throw new ObjectDisposedException(nameof(ResumableDownloadStream));
        }
      }

      private void DisposeCurrentStream() {
        Stream stream;
        HttpResponseMessage response;
        lock (_connectionLock) {
          stream = _currentStream;
          response = _currentResponse;
          _currentStream = null;
          _currentResponse = null;
        }
        try {
          stream?.Dispose();
        } catch (Exception) {
        }
        try {
          response?.Dispose();
        } catch (Exception) {
        }
      }

      protected override void Dispose(bool disposing) {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) {
          // Everything here is managed and safe to touch from the finalizer thread (the calls
          // only release registrations, close the connection and update task state), but it must
          // never throw there: an unhandled finalizer exception terminates the process on Mono.
          try {
            _cancelRegistration.Dispose();
            DisposeCurrentStream();
            if (_ownsStorageTask) {
              _owner._storageTask.MarkCompleted();
              _owner._storageTask.Dispose();
            }
          } catch (Exception) when (!disposing) {
          }
        }
        base.Dispose(disposing);
      }
    }
  }
}
