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
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Implements streaming resumable downloads (GetBytesAsync, GetFileAsync, GetStreamAsync)
  /// with HTTP Range resumption, ETag consistency checks, and ArrayPool buffer pooling.
  /// </summary>
  internal sealed class ResumableDownloadTask {
    internal const int BufferSize = 256 * 1024;
    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 30000;
    private const int MaxJitterMs = 250;
    private static readonly Random _jitterRandom = new Random();
    private static readonly object _jitterLock = new object();

    private readonly StorageReference _reference;
    private readonly StoragePath _path;
    private readonly StorageTask _storageTask;
    private readonly DownloadState _downloadState;
    private readonly TransferStateUpdater<DownloadState> _transferStateUpdater;
    private readonly ModuleLogger _logger;

    // Maximum number of times a non-streaming download restarts from byte 0 because the remote
    // object's ETag changed mid-transfer before giving up.
    private const int MaxETagRestarts = 1;

    internal StorageTask TaskController => _storageTask;

    internal ResumableDownloadTask(
        StorageReference reference,
        IProgress<DownloadState> progressHandler,
        CancellationToken cancelToken,
        StoragePath path = null) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _path = path;
      _logger = reference.Logger;
      _storageTask = new StorageTask(cancelToken, reference.Storage?.DisposeToken ?? default);
      _downloadState = new DownloadState(reference, -1);
      _transferStateUpdater = new TransferStateUpdater<DownloadState>(
          reference,
          progressHandler,
          _downloadState,
          _downloadState.State);
    }

    internal static Task<byte[]> GetBytesAsync(
        StorageReference reference,
        long maxDownloadSizeBytes,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      var downloadTask = new ResumableDownloadTask(reference, progressHandler, cancelToken, path);
      return downloadTask.RunGetBytesAsync(maxDownloadSizeBytes);
    }

    internal static Task GetFileAsync(
        StorageReference reference,
        string destinationFilePath,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(
          !string.IsNullOrEmpty(destinationFilePath),
          "destinationFilePath cannot be null or empty");
      var downloadTask = new ResumableDownloadTask(reference, progressHandler, cancelToken, path);
      return downloadTask.RunGetFileAsync(destinationFilePath);
    }

    internal static Task GetStreamAsync(
        StorageReference reference,
        Action<Stream> streamProcessor,
        IProgress<DownloadState> progressHandler = null,
        CancellationToken cancelToken = default,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckNotNull(streamProcessor, nameof(streamProcessor));
      var downloadTask = new ResumableDownloadTask(reference, progressHandler, cancelToken, path);
      return downloadTask.RunGetStreamProcessorAsync(streamProcessor);
    }

    internal static Task<Stream> GetStreamAsync(
        StorageReference reference,
        CancellationToken cancelToken = default,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      var downloadTask = new ResumableDownloadTask(reference, null, cancelToken, path);
      return downloadTask.RunOpenStreamAsync();
    }

    internal async Task<byte[]> RunGetBytesAsync(long maxDownloadSizeBytes) {
      try {
        StorageHttpClient.EnsureObjectReference(_reference, _path, "GetBytes");
        return await StorageTaskScheduler.RunDownloadAsync(
            async () => {
              using (var memoryStream = new MemoryStream()) {
                await DownloadToStreamAsync(
                    memoryStream,
                    maxDownloadSizeBytes,
                    resetStream: () => {
                      memoryStream.Position = 0;
                      memoryStream.SetLength(0);
                    }).ConfigureAwait(false);
                return memoryStream.ToArray();
              }
            },
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        throw;
      } catch (Exception) {
        _storageTask.MarkFaulted();
        throw;
      } finally {
        await _transferStateUpdater.FlushAsync().ConfigureAwait(false);
        _storageTask.Dispose();
      }
    }

    internal async Task RunGetFileAsync(string destinationFilePath) {
      string localPath = ResolveLocalFilePath(destinationFilePath);
      // Download into a uniquely named sibling temp file and move it into place on success so a
      // failed or cancelled download never leaves a truncated file at the destination, and two
      // concurrent downloads to the same destination cannot clobber each other's partial data.
      string tempPath = $"{localPath}.{Guid.NewGuid():N}.fbdownload";
      try {
        StorageHttpClient.EnsureObjectReference(_reference, _path, "GetFile");
        await StorageTaskScheduler.RunDownloadAsync(
            async () => {
              string directory = Path.GetDirectoryName(localPath);
              if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
              }

              using (var fileStream = new FileStream(
                  tempPath,
                  FileMode.Create,
                  FileAccess.Write,
                  FileShare.Read,
                  bufferSize: 4096,
                  useAsync: true)) {
                await DownloadToStreamAsync(
                    fileStream,
                    maxDownloadSizeBytes: 0,
                    resetStream: () => {
                      fileStream.Position = 0;
                      fileStream.SetLength(0);
                    }).ConfigureAwait(false);
                await fileStream.FlushAsync(_storageTask.CancellationToken).ConfigureAwait(false);
              }
              if (File.Exists(localPath)) {
                File.Delete(localPath);
              }
              File.Move(tempPath, localPath);
            },
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        throw;
      } catch (Exception) {
        _storageTask.MarkFaulted();
        throw;
      } finally {
        try {
          if (File.Exists(tempPath)) {
            File.Delete(tempPath);
          }
        } catch (Exception) {
          // Best-effort cleanup of the partial temp file.
        }
        await _transferStateUpdater.FlushAsync().ConfigureAwait(false);
        _storageTask.Dispose();
      }
    }

    internal async Task RunGetStreamProcessorAsync(Action<Stream> streamProcessor) {
      try {
        StorageHttpClient.EnsureObjectReference(_reference, _path, "GetStream");
        await StorageTaskScheduler.RunDownloadAsync(
            async () => {
              _storageTask.ThrowIfCanceled();
              _storageTask.TransitionToInProgress();

              using (var downloadStream = await OpenResumableStreamInternalAsync(
                  ownsStorageTask: false).ConfigureAwait(false)) {
                await Task.Run(
                    () => {
                      _storageTask.ThrowIfCanceled();
                      streamProcessor(downloadStream);
                      _storageTask.ThrowIfCanceled();
                    },
                    _storageTask.CancellationToken).ConfigureAwait(false);
              }
              _storageTask.MarkCompleted();
            },
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        throw;
      } catch (Exception) {
        if (_storageTask.IsCanceled) {
          _storageTask.ThrowIfCanceled();
        }
        _storageTask.MarkFaulted();
        throw;
      } finally {
        await _transferStateUpdater.FlushAsync().ConfigureAwait(false);
        _storageTask.Dispose();
      }
    }

    // Opens a live download stream. A scheduler slot is held only while the request is issued
    // and the response headers are read; it is released before the stream is handed to the
    // caller so that an undisposed stream can never starve other downloads.
    internal async Task<Stream> RunOpenStreamAsync() {
      try {
        StorageHttpClient.EnsureObjectReference(_reference, _path, "GetStream");
        return await StorageTaskScheduler.RunDownloadAsync(
            async () => {
              _storageTask.ThrowIfCanceled();
              _storageTask.TransitionToInProgress();
              return (Stream)await OpenResumableStreamInternalAsync(ownsStorageTask: true)
                  .ConfigureAwait(false);
            },
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        _storageTask.Dispose();
        throw;
      } catch (Exception) {
        if (_storageTask.IsCanceled) {
          _storageTask.MarkCanceled();
          _storageTask.Dispose();
          _storageTask.ThrowIfCanceled();
        }
        _storageTask.MarkFaulted();
        _storageTask.Dispose();
        throw;
      }
    }

    private async Task DownloadToStreamAsync(
        Stream destination,
        long maxDownloadSizeBytes,
        Action resetStream) {
      _storageTask.ThrowIfCanceled();
      _storageTask.TransitionToInProgress();

      FirebaseStorage storage = _reference.Storage;
      FirebaseApp app = storage?.App;
      TimeSpan maxDownloadRetryTime = storage?.MaxDownloadRetryTime ?? TimeSpan.FromMinutes(10);
      var retryStopwatch = Stopwatch.StartNew();
      int currentSleepMs = InitialBackoffMs;

      long resumeOffset = 0;
      string expectedETag = null;
      bool reportedAnyProgress = false;
      int etagRestarts = 0;

      byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
      try {
        while (true) {
          await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken)
              .ConfigureAwait(false);
          _storageTask.ThrowIfCanceled();

          HttpResponseMessage response = null;
          Exception lastNetworkException = null;
          bool pausedMidStream = false;
          bool networkUnavailable = false;
          bool etagMismatchRestart = false;

          CancellationToken chunkToken = _storageTask.GetChunkCancellationToken();
          StallWatchdog watchdog = null;
          try {
            watchdog = new StallWatchdog(chunkToken);
            CancellationToken readToken = watchdog.Token;
            response = await SendMediaGetRequestAsync(app, resumeOffset, readToken)
                .ConfigureAwait(false);
            watchdog.Touch();

            int statusCode = (int)response.StatusCode;
            _logger.LogMessage(LogLevel.Verbose,
                $"Download GET offset={resumeOffset} -> {statusCode}");
            if (resumeOffset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) {
              // All bytes already downloaded.
              if (!reportedAnyProgress) {
                ReportProgress();
              }
              _storageTask.MarkCompleted();
              return;
            }

            if (statusCode >= 200 && statusCode < 300) {
              string responseETag = GetResponseETag(response);
              if (resumeOffset > 0 &&
                  expectedETag != null &&
                  !string.IsNullOrEmpty(responseETag) &&
                  !string.Equals(expectedETag, responseETag, StringComparison.Ordinal)) {
                // Remote object mutated mid-download; truncate and restart from byte 0.
                if (++etagRestarts > MaxETagRestarts) {
                  throw new StorageException(
                      StorageException.ErrorUnknown,
                      (int)HttpStatusCode.PreconditionFailed,
                      "Remote object changed repeatedly during download; giving up.");
                }
                _logger.LogMessage(LogLevel.Warning,
                    "Remote object changed during download; restarting from byte 0.");
                resetStream();
                resumeOffset = 0;
                expectedETag = null;
                _downloadState.State.BytesTransferred = 0;
                etagMismatchRestart = true;
              } else {
                if (!string.IsNullOrEmpty(responseETag)) {
                  expectedETag = responseETag;
                }

                // If server ignored Range header and returned 200 OK from byte 0, reset destination.
                if (resumeOffset > 0 && response.StatusCode == HttpStatusCode.OK) {
                  resetStream();
                  resumeOffset = 0;
                  _downloadState.State.BytesTransferred = 0;
                }

                long totalBytes = ParseTotalBytesFromResponse(response, resumeOffset);
                if (totalBytes >= 0) {
                  _downloadState.State.TotalByteCount = totalBytes;
                }

                if (maxDownloadSizeBytes > 0 && totalBytes > maxDownloadSizeBytes) {
                  throw new IndexOutOfRangeException(
                      $"Streamed file size ({totalBytes} bytes) exceeded maximum allowed size of {maxDownloadSizeBytes} bytes.");
                }

                using (Stream contentStream = await response.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false))
                using (readToken.Register(() => {
                  try {
                    contentStream.Dispose();
                  } catch (Exception) {
                  }
                })) {
                  while (true) {
                    readToken.ThrowIfCancellationRequested();
                    int bytesRead = await contentStream.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        readToken).ConfigureAwait(false);
                    if (bytesRead <= 0) {
                      break;
                    }
                    watchdog.Touch();

                    if (maxDownloadSizeBytes > 0 &&
                        (resumeOffset + bytesRead) > maxDownloadSizeBytes) {
                      long exceededSize = Math.Max(
                          _downloadState.TotalByteCount,
                          resumeOffset + bytesRead);
                      throw new IndexOutOfRangeException(
                          $"Streamed file size ({exceededSize} bytes) exceeded maximum allowed size of {maxDownloadSizeBytes} bytes.");
                    }

                    await destination.WriteAsync(buffer, 0, bytesRead, readToken)
                        .ConfigureAwait(false);
                    resumeOffset += bytesRead;
                    _downloadState.State.BytesTransferred = resumeOffset;
                    ReportProgress();
                    reportedAnyProgress = true;
                  }
                }

                if (!reportedAnyProgress) {
                  _downloadState.State.BytesTransferred = resumeOffset;
                  ReportProgress();
                }

                _storageTask.MarkCompleted();
                return;
              }
            } else if (!StorageHttpClient.IsRetryableStatusCode(response.StatusCode)) {
              string errorBody = await StorageHttpClient.ReadResponseContentSafeAsync(response)
                  .ConfigureAwait(false);
              throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
            }
          } catch (IndexOutOfRangeException) {
            throw;
          } catch (StorageException) {
            throw;
          } catch (OperationCanceledException oce) {
            if (_storageTask.IsCanceled) {
              _storageTask.ThrowIfCanceled();
            }
            if (_storageTask.IsPaused) {
              pausedMidStream = true;
            } else {
              // Stall watchdog fired, or the HTTP stack raised a spurious cancellation; treat as
              // a transient network failure so the backoff path runs rather than spinning.
              lastNetworkException = watchdog?.AsStallException(oce) ?? (Exception)oce;
              networkUnavailable = true;
            }
          } catch (Exception ex) when (ex is HttpRequestException ||
                                       ex is IOException ||
                                       ex is SocketException ||
                                       ex is ObjectDisposedException) {
            if (_storageTask.IsCanceled) {
              _storageTask.ThrowIfCanceled();
            }
            if (_storageTask.IsPaused) {
              pausedMidStream = true;
            } else {
              lastNetworkException = ex;
              networkUnavailable = true;
            }
          } finally {
            response?.Dispose();
            watchdog?.Dispose();
          }

          if (etagMismatchRestart) {
            continue;
          }

          if (pausedMidStream) {
            await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken)
                .ConfigureAwait(false);
            continue;
          }

          if (networkUnavailable) {
            _logger.LogMessage(LogLevel.Warning,
                $"Download at offset {resumeOffset} failed: {lastNetworkException?.Message}");
          }

          int delayMs = currentSleepMs + GetRandomJitterMs();
          if (retryStopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > maxDownloadRetryTime) {
            _logger.LogMessage(LogLevel.Error,
                $"Download retry budget ({maxDownloadRetryTime}) exhausted at offset {resumeOffset}.");
            throw new StorageException(
                StorageException.ErrorRetryLimitExceeded,
                0,
                lastNetworkException?.Message ??
                StorageException.GetErrorMessageForCode(StorageException.ErrorRetryLimitExceeded),
                lastNetworkException);
          }

          _logger.LogMessage(LogLevel.Debug,
              $"Retrying download at offset {resumeOffset} in {delayMs} ms.");
          await Task.Delay(delayMs, _storageTask.CancellationToken).ConfigureAwait(false);
          if (currentSleepMs < MaxBackoffMs) {
            currentSleepMs = Math.Min(currentSleepMs * 2, MaxBackoffMs);
          }
        }
      } finally {
        ArrayPool<byte>.Shared.Return(buffer);
      }
    }

    private async Task<ResumableDownloadStream> OpenResumableStreamInternalAsync(
        bool ownsStorageTask) {
      FirebaseStorage storage = _reference.Storage;
      FirebaseApp app = storage?.App;
      TimeSpan maxDownloadRetryTime = storage?.MaxDownloadRetryTime ?? TimeSpan.FromMinutes(10);

      HttpResponseMessage initialResponse = null;
      try {
        try {
          initialResponse = await StorageHttpClient.SendWithExponentialBackoffAsync(
              StorageHttpClient.Client,
              () => CreateMediaRequestAsync(app, 0),
              maxDownloadRetryTime,
              HttpCompletionOption.ResponseHeadersRead,
              _storageTask.CancellationToken,
              StallWatchdog.StallTimeout).ConfigureAwait(false);
        } catch (OperationCanceledException) when (_storageTask.IsCanceled) {
          _storageTask.ThrowIfCanceled();
          throw;
        } catch (Exception ex) {
          if (_storageTask.IsCanceled) {
            _storageTask.ThrowIfCanceled();
          }
          StorageException wrapped = StorageHttpClient.WrapRetryFailure(ex);
          if (wrapped == null) {
            throw;
          }
          throw wrapped;
        }

        if (!initialResponse.IsSuccessStatusCode) {
          string errorBody = await StorageHttpClient.ReadResponseContentSafeAsync(initialResponse)
              .ConfigureAwait(false);
          int statusCode = (int)initialResponse.StatusCode;
          initialResponse.Dispose();
          throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
        }

        long totalBytes = ParseTotalBytesFromResponse(initialResponse, 0);
        if (totalBytes >= 0) {
          _downloadState.State.TotalByteCount = totalBytes;
        }
        string initialETag = GetResponseETag(initialResponse);
        Stream innerStream = await initialResponse.Content.ReadAsStreamAsync()
            .ConfigureAwait(false);

        return new ResumableDownloadStream(
            this,
            initialResponse,
            innerStream,
            initialETag,
            ownsStorageTask);
      } catch {
        initialResponse?.Dispose();
        throw;
      }
    }

    private async Task<HttpResponseMessage> SendMediaGetRequestAsync(
        FirebaseApp app,
        long resumeOffset,
        CancellationToken cancellationToken) {
      using (var request = await CreateMediaRequestAsync(app, resumeOffset).ConfigureAwait(false)) {
        return await StorageHttpClient.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
      }
    }

    private async Task<HttpRequestMessage> CreateMediaRequestAsync(
        FirebaseApp app,
        long resumeOffset) {
      string objectUrl = StorageHttpClient.GetObjectUrl(_reference, _path);
      string mediaUrl = $"{objectUrl}?alt=media";
      var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
      await StorageHttpClient.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
      if (resumeOffset > 0) {
        request.Headers.Range = new RangeHeaderValue(resumeOffset, null);
      }
      return request;
    }

    private static string GetResponseETag(HttpResponseMessage response) {
      if (response == null) {
        return null;
      }
      if (response.Headers?.ETag != null) {
        return response.Headers.ETag.Tag;
      }
      if (response.Headers != null &&
          response.Headers.TryGetValues("ETag", out IEnumerable<string> values)) {
        return values.FirstOrDefault();
      }
      return null;
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

    internal static string ResolveLocalFilePath(string filePath) {
      if (string.IsNullOrEmpty(filePath)) {
        return filePath;
      }
      if (filePath.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) {
        try {
          // Uri.LocalPath is already unescaped; do not unescape again or literal '%' sequences
          // in filenames will be corrupted.
          return new Uri(filePath).LocalPath;
        } catch (UriFormatException) {
          return filePath.Substring("file://".Length);
        }
      }
      return filePath;
    }

    private void ReportProgress() {
      _transferStateUpdater.ReportProgress(
          _downloadState.BytesTransferred,
          _downloadState.TotalByteCount);
    }

    private static int GetRandomJitterMs() {
      lock (_jitterLock) {
        return _jitterRandom.Next(MaxJitterMs);
      }
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
      private HttpResponseMessage _currentResponse;
      private Stream _currentStream;
      private string _expectedETag;
      private long _bytesReadTotal;
      private bool _reportedInitialProgress;
      private int _disposed;

      public ResumableDownloadStream(
          ResumableDownloadTask owner,
          HttpResponseMessage initialResponse,
          Stream initialStream,
          string initialETag,
          bool ownsStorageTask) {
        _owner = owner;
        _currentResponse = initialResponse;
        _currentStream = initialStream;
        _expectedETag = initialETag;
        _ownsStorageTask = ownsStorageTask;

        if (_owner._storageTask.CancellationToken.CanBeCanceled) {
          _cancelRegistration = _owner._storageTask.CancellationToken.Register(() => {
            try {
              _currentStream?.Dispose();
              _currentResponse?.Dispose();
            } catch (Exception) {
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
        return ReadAsync(buffer, offset, count, _owner._storageTask.CancellationToken)
            .GetAwaiter()
            .GetResult();
      }

      public override async Task<int> ReadAsync(
          byte[] buffer,
          int offset,
          int count,
          CancellationToken cancellationToken) {
        if (_disposed != 0) {
          throw new ObjectDisposedException(nameof(ResumableDownloadStream));
        }

        _owner._storageTask.ThrowIfCanceled(cancellationToken);

        FirebaseStorage storage = _owner._reference.Storage;
        FirebaseApp app = storage?.App;
        TimeSpan maxDownloadRetryTime = storage?.MaxDownloadRetryTime ?? TimeSpan.FromMinutes(10);
        var retryStopwatch = Stopwatch.StartNew();
        int currentSleepMs = InitialBackoffMs;

        while (true) {
          await _owner._storageTask.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
          _owner._storageTask.ThrowIfCanceled(cancellationToken);

          StallWatchdog watchdog = null;
          try {
            watchdog = new StallWatchdog(cancellationToken);
            if (_currentStream == null) {
              await ReconnectAsync(app, watchdog.Token).ConfigureAwait(false);
              if (_currentStream == null) {
                return 0;
              }
            }

            int read = await _currentStream.ReadAsync(buffer, offset, count, watchdog.Token)
                .ConfigureAwait(false);
            if (read > 0) {
              _bytesReadTotal += read;
              _owner._downloadState.State.BytesTransferred = _bytesReadTotal;
              _owner.ReportProgress();
              _reportedInitialProgress = true;
            } else if (!_reportedInitialProgress) {
              _owner._downloadState.State.BytesTransferred = _bytesReadTotal;
              _owner.ReportProgress();
              _reportedInitialProgress = true;
            }
            return read;
          } catch (OperationCanceledException oce) {
            _owner._storageTask.ThrowIfCanceled(cancellationToken);
            HttpRequestException stall = watchdog?.AsStallException(oce);
            if (stall == null) {
              throw;
            }
            // Connection stalled: drop it and fall through to the reconnect/backoff path below.
            DisposeCurrentStream();
            _owner._logger.LogMessage(LogLevel.Warning,
                $"Download stream stalled at offset {_bytesReadTotal}; reconnecting.");
            int stallDelayMs = currentSleepMs + GetRandomJitterMs();
            if (retryStopwatch.Elapsed + TimeSpan.FromMilliseconds(stallDelayMs) >
                maxDownloadRetryTime) {
              throw new StorageException(
                  StorageException.ErrorRetryLimitExceeded, 0, stall.Message, stall);
            }
            await Task.Delay(stallDelayMs, cancellationToken).ConfigureAwait(false);
            currentSleepMs = Math.Min(currentSleepMs * 2, MaxBackoffMs);
          } catch (StorageException) {
            throw;
          } catch (IOException) when (_owner._storageTask.IsCanceled ||
                                      cancellationToken.IsCancellationRequested) {
            _owner._storageTask.ThrowIfCanceled(cancellationToken);
            throw;
          } catch (ObjectDisposedException) when (_owner._storageTask.IsCanceled ||
                                                  cancellationToken.IsCancellationRequested) {
            _owner._storageTask.ThrowIfCanceled(cancellationToken);
            throw;
          } catch (Exception ex) when (ex is IOException ||
                                       ex is HttpRequestException ||
                                       ex is SocketException) {
            if (_owner._storageTask.IsCanceled || cancellationToken.IsCancellationRequested) {
              _owner._storageTask.ThrowIfCanceled(cancellationToken);
            }

            DisposeCurrentStream();
            _owner._logger.LogMessage(LogLevel.Warning,
                $"Download stream failed at offset {_bytesReadTotal}: {ex.Message}");

            int delayMs = currentSleepMs + GetRandomJitterMs();
            if (retryStopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > maxDownloadRetryTime) {
              throw new StorageException(
                  StorageException.ErrorRetryLimitExceeded,
                  0,
                  ex.Message,
                  ex);
            }

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            currentSleepMs = Math.Min(currentSleepMs * 2, MaxBackoffMs);
          } finally {
            watchdog?.Dispose();
          }
        }
      }

      private async Task ReconnectAsync(FirebaseApp app, CancellationToken cancellationToken) {
        HttpResponseMessage response = await _owner.SendMediaGetRequestAsync(
            app,
            _bytesReadTotal,
            cancellationToken).ConfigureAwait(false);

        if (_bytesReadTotal > 0 &&
            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) {
          response.Dispose();
          _currentResponse = null;
          _currentStream = null;
          return;
        }

        if (!response.IsSuccessStatusCode) {
          string errorBody = await StorageHttpClient.ReadResponseContentSafeAsync(response)
              .ConfigureAwait(false);
          int statusCode = (int)response.StatusCode;
          response.Dispose();
          if (StorageHttpClient.IsRetryableStatusCode((HttpStatusCode)statusCode)) {
            throw new IOException($"Transient HTTP status {statusCode} while reconnecting stream.");
          }
          throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
        }

        string newETag = GetResponseETag(response);
        if (_expectedETag != null &&
            !string.IsNullOrEmpty(newETag) &&
            !string.Equals(_expectedETag, newETag, StringComparison.Ordinal)) {
          response.Dispose();
          throw new StorageException(
              StorageException.ErrorUnknown,
              (int)HttpStatusCode.PreconditionFailed,
              "Remote object ETag changed during streaming download.");
        }

        Stream newStream = null;
        try {
          newStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
          if (_bytesReadTotal > 0 && response.StatusCode == HttpStatusCode.OK) {
            // The server ignored the Range header and restarted from byte 0, so drain the bytes
            // that have already been handed to the caller before resuming.
            byte[] skipBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try {
              long remaining = _bytesReadTotal;
              while (remaining > 0) {
                int toRead = (int)Math.Min(remaining, skipBuffer.Length);
                int skipped = await newStream.ReadAsync(
                    skipBuffer,
                    0,
                    toRead,
                    cancellationToken).ConfigureAwait(false);
                if (skipped <= 0) {
                  break;
                }
                remaining -= skipped;
              }
              if (remaining > 0) {
                throw new StorageException(
                    StorageException.ErrorUnknown,
                    (int)response.StatusCode,
                    "Server returned fewer bytes than already consumed while resuming download.");
              }
            } finally {
              ArrayPool<byte>.Shared.Return(skipBuffer);
            }
          }
        } catch (Exception) {
          newStream?.Dispose();
          response.Dispose();
          throw;
        }

        _currentResponse = response;
        _currentStream = newStream;
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

      private void DisposeCurrentStream() {
        try {
          _currentStream?.Dispose();
        } catch (Exception) {
        }
        try {
          _currentResponse?.Dispose();
        } catch (Exception) {
        }
        _currentStream = null;
        _currentResponse = null;
      }

      protected override void Dispose(bool disposing) {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) {
          _cancelRegistration.Dispose();
          DisposeCurrentStream();
          if (_ownsStorageTask) {
            _owner._storageTask.MarkCompleted();
            _owner._storageTask.Dispose();
          }
        }
        base.Dispose(disposing);
      }
    }
  }
}
