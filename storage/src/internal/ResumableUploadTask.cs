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
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Implements the Google Cloud Storage adaptive resumable upload protocol
  /// (start -> query -> chunked upload with adaptive 256KB..32MB buffer pooling -> finalize).
  /// </summary>
  internal sealed class ResumableUploadTask {
    internal const int PreferredChunkSize = 256 * 1024;
    internal const int MaxChunkSize = 32 * 1024 * 1024;

    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 30000;
    private const int MaxJitterMs = 250;
    private static readonly Random _jitterRandom = new Random();
    private static readonly object _jitterLock = new object();

    private readonly StorageReference _reference;
    private readonly StoragePath _path;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly StorageMetadata _metadata;
    private readonly StorageTask _storageTask;
    private readonly UploadState _uploadState;
    private readonly TransferStateUpdater<UploadState> _transferStateUpdater;
    private Uri _sessionUri;

    internal StorageTask TaskController => _storageTask;

    internal ResumableUploadTask(
        StorageReference reference,
        Stream stream,
        long totalByteCount,
        bool ownsStream,
        StorageMetadata metadata,
        IProgress<UploadState> progressHandler,
        CancellationToken cancelToken,
        Uri previousSessionUri = null,
        StoragePath path = null) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _stream = Preconditions.CheckNotNull(stream, nameof(stream));
      _ownsStream = ownsStream;
      _metadata = metadata;
      _path = path;
      _sessionUri = previousSessionUri;
      _storageTask = new StorageTask(cancelToken);
      _uploadState = new UploadState(reference, totalByteCount, previousSessionUri);
      _transferStateUpdater = new TransferStateUpdater<UploadState>(
          reference,
          progressHandler,
          _uploadState,
          _uploadState.State);
    }

    internal static Task<StorageMetadata> PutBytesAsync(
        StorageReference reference,
        byte[] bytes,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(bytes != null, "bytes cannot be null");

      var stream = new MemoryStream(bytes, writable: false);
      var metadata = MetadataChange.Build(customMetadata);
      var uploadTask = new ResumableUploadTask(
          reference,
          stream,
          bytes.Length,
          ownsStream: true,
          metadata,
          progressHandler,
          cancelToken,
          previousSessionUri,
          path);
      return uploadTask.RunAsync();
    }

    internal static Task<StorageMetadata> PutStreamAsync(
        StorageReference reference,
        Stream stream,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(stream != null, "stream cannot be null");

      var metadata = MetadataChange.Build(customMetadata);
      // Note: UIHandlerAutomated.cs explicitly asserts that uploadState.TotalByteCount == -1
      // when uploading from a stream via PutStreamAsync.
      var uploadTask = new ResumableUploadTask(
          reference,
          stream,
          totalByteCount: -1,
          ownsStream: false,
          metadata,
          progressHandler,
          cancelToken,
          previousSessionUri,
          path);
      return uploadTask.RunAsync();
    }

    internal static Task<StorageMetadata> PutFileAsync(
        StorageReference reference,
        string filePath,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StoragePath path = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(filePath != null, "filePath cannot be null");

      string localPath = ResolveLocalFilePath(filePath);
      if (!File.Exists(localPath)) {
        var tcs = new TaskCompletionSource<StorageMetadata>();
        tcs.SetException(new FileNotFoundException($"{localPath} not found", filePath));
        return tcs.Task;
      }

      long fileLength;
      FileStream fileStream;
      try {
        fileLength = new FileInfo(localPath).Length;
        fileStream = new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
      } catch (FileNotFoundException) {
        var tcs = new TaskCompletionSource<StorageMetadata>();
        tcs.SetException(new FileNotFoundException($"{localPath} not found", filePath));
        return tcs.Task;
      } catch (Exception ex) {
        var tcs = new TaskCompletionSource<StorageMetadata>();
        tcs.SetException(ex);
        return tcs.Task;
      }

      var metadata = MetadataChange.Build(customMetadata);
      var uploadTask = new ResumableUploadTask(
          reference,
          fileStream,
          fileLength,
          ownsStream: true,
          metadata,
          progressHandler,
          cancelToken,
          previousSessionUri,
          path);
      return uploadTask.RunAsync();
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

    internal async Task<StorageMetadata> RunAsync() {
      try {
        return await StorageTaskScheduler.RunUploadAsync(
            ExecuteUploadAsync,
            _storageTask.CancellationToken).ConfigureAwait(false);
      } catch (OperationCanceledException) {
        _storageTask.MarkCanceled();
        FireAndForgetCancelSession();
        throw;
      } catch (Exception) {
        _storageTask.MarkFaulted();
        throw;
      } finally {
        if (_ownsStream) {
          try {
            _stream.Dispose();
          } catch (Exception) {
          }
        }
        _storageTask.Dispose();
      }
    }

    private async Task<StorageMetadata> ExecuteUploadAsync() {
      _storageTask.ThrowIfCanceled();
      _storageTask.TransitionToInProgress();

      FirebaseStorage storage = _reference.Storage;
      FirebaseApp app = storage?.App;
      TimeSpan maxUploadRetryTime = storage?.MaxUploadRetryTime ?? TimeSpan.FromMinutes(10);

      long bytesUploaded = 0;
      bool needServerQuery = _sessionUri != null;

      await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken).ConfigureAwait(false);

      if (_sessionUri == null) {
        _sessionUri = await StartSessionAsync(app, maxUploadRetryTime).ConfigureAwait(false);
        _uploadState.State.UploadSessionUri = _sessionUri;
        ReportProgress();
      }

      if (needServerQuery) {
        var queryResult = await QuerySessionWithRetryAsync(
            app,
            maxUploadRetryTime,
            Stopwatch.StartNew()).ConfigureAwait(false);
        if (queryResult.IsFinal) {
          StorageMetadata completedMetadata = await ResolveFinalizedMetadataAsync(
              queryResult.ResponseBody).ConfigureAwait(false);
          _uploadState.State.BytesTransferred = queryResult.ServerOffset > 0
              ? queryResult.ServerOffset
              : Math.Max(completedMetadata?.SizeBytes ?? 0, _uploadState.TotalByteCount);
          _transferStateUpdater.SetMetadata(completedMetadata);
          _storageTask.MarkCompleted();
          return completedMetadata;
        }
        bytesUploaded = await AdvanceStreamToOffsetAsync(
            bytesUploaded,
            queryResult.ServerOffset,
            _storageTask.CancellationToken).ConfigureAwait(false);
        _uploadState.State.BytesTransferred = bytesUploaded;
        ReportProgress();
      }

      int currentChunkSize = PreferredChunkSize;
      byte[] lookaheadByte = new byte[1];
      bool hasLookahead = false;
      bool reachedEof = false;

      while (true) {
        await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken).ConfigureAwait(false);
        _storageTask.ThrowIfCanceled();

        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(currentChunkSize);
        try {
          int bytesInChunk = 0;
          long chunkStartOffset = bytesUploaded;

          if (hasLookahead && bytesInChunk < currentChunkSize) {
            rentedBuffer[0] = lookaheadByte[0];
            bytesInChunk = 1;
            hasLookahead = false;
          }

          while (bytesInChunk < currentChunkSize && !reachedEof) {
            int read = await _stream.ReadAsync(
                rentedBuffer,
                bytesInChunk,
                currentChunkSize - bytesInChunk,
                _storageTask.CancellationToken).ConfigureAwait(false);
            if (read <= 0) {
              reachedEof = true;
              break;
            }
            bytesInChunk += read;
          }

          if (!reachedEof) {
            long totalByteCount = _uploadState.TotalByteCount;
            if (totalByteCount >= 0 && (chunkStartOffset + bytesInChunk) >= totalByteCount) {
              reachedEof = true;
            } else if (bytesInChunk == currentChunkSize) {
              int peekRead = await _stream.ReadAsync(
                  lookaheadByte,
                  0,
                  1,
                  _storageTask.CancellationToken).ConfigureAwait(false);
              if (peekRead <= 0) {
                reachedEof = true;
              } else {
                hasLookahead = true;
              }
            }
          }

          bool isFinalChunk = reachedEof && !hasLookahead;

          // Upload the current chunk with retry, pause/resume, and server offset recovery.
          var retryStopwatch = Stopwatch.StartNew();
          int currentSleepMs = InitialBackoffMs;
          int offsetWithinBuffer = 0;

          while (true) {
            await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken)
                .ConfigureAwait(false);
            _storageTask.ThrowIfCanceled();

            int bytesToSend = bytesInChunk - offsetWithinBuffer;
            long uploadOffset = chunkStartOffset + offsetWithinBuffer;
            string uploadCommand = isFinalChunk
                ? (bytesToSend > 0 ? "upload, finalize" : "finalize")
                : "upload";

            HttpResponseMessage response = null;
            Exception lastException = null;
            bool pausedMidChunk = false;
            bool networkError = false;

            CancellationToken chunkToken = _storageTask.GetChunkCancellationToken();
            try {
              using (var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri)) {
                await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", uploadCommand);
                request.Headers.TryAddWithoutValidation(
                    "X-Goog-Upload-Offset",
                    uploadOffset.ToString());
                request.Content = new ByteArrayContent(rentedBuffer, offsetWithinBuffer, bytesToSend);

                response = await StorageHttpClient.Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    chunkToken).ConfigureAwait(false);
              }
            } catch (OperationCanceledException) {
              if (_storageTask.IsCanceled) {
                _storageTask.ThrowIfCanceled();
              }
              // Pause() canceled the chunk token; wait at pause gate and then recover offset.
              pausedMidChunk = true;
            } catch (Exception ex) when (ex is HttpRequestException ||
                                         ex is IOException ||
                                         ex is SocketException ||
                                         ex is ObjectDisposedException) {
              if (_storageTask.IsCanceled) {
                _storageTask.ThrowIfCanceled();
              }
              if (_storageTask.IsPaused) {
                pausedMidChunk = true;
              } else {
                lastException = ex;
                networkError = true;
              }
            }

            if (pausedMidChunk) {
              response?.Dispose();
              await _storageTask.WaitIfPausedAsync(_storageTask.CancellationToken)
                  .ConfigureAwait(false);
              var queryAfterPause = await QuerySessionWithRetryAsync(
                  app,
                  maxUploadRetryTime,
                  retryStopwatch).ConfigureAwait(false);
              if (queryAfterPause.IsFinal) {
                bytesUploaded = chunkStartOffset + bytesInChunk;
                _uploadState.State.BytesTransferred = bytesUploaded;
                StorageMetadata completedMetadata = await ResolveFinalizedMetadataAsync(
                    queryAfterPause.ResponseBody).ConfigureAwait(false);
                _transferStateUpdater.SetMetadata(completedMetadata);
                _storageTask.MarkCompleted();
                return completedMetadata;
              }
              offsetWithinBuffer = await SynchronizeBufferOrStreamAsync(
                  chunkStartOffset,
                  bytesInChunk,
                  hasLookahead,
                  queryAfterPause.ServerOffset).ConfigureAwait(false);
              _uploadState.State.BytesTransferred = queryAfterPause.ServerOffset;
              ReportProgress();
              if (offsetWithinBuffer < 0) {
                bytesUploaded = queryAfterPause.ServerOffset;
                hasLookahead = false;
                reachedEof = false;
                break;
              }
              if (offsetWithinBuffer == bytesInChunk && !isFinalChunk) {
                bytesUploaded = queryAfterPause.ServerOffset;
                break;
              }
              continue;
            }

            if (response != null) {
              try {
                int statusCode = (int)response.StatusCode;
                if ((statusCode >= 200 && statusCode < 300) || statusCode == 308) {
                  string responseBody = await HttpHelpers.ReadResponseContentSafeAsync(response)
                      .ConfigureAwait(false);
                  string uploadStatus = GetHeaderFirstValue(response, "X-Goog-Upload-Status");

                  bytesUploaded = chunkStartOffset + bytesInChunk;
                  _uploadState.State.BytesTransferred = bytesUploaded;
                  ReportProgress();

                  if (isFinalChunk ||
                      string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase)) {
                    StorageMetadata finalMetadata = await ResolveFinalizedMetadataAsync(
                        responseBody).ConfigureAwait(false);
                    _transferStateUpdater.SetMetadata(finalMetadata);
                    _storageTask.MarkCompleted();
                    return finalMetadata;
                  }

                  // Chunk succeeded; scale chunk size up to MaxChunkSize (32 MB).
                  if (currentChunkSize < MaxChunkSize) {
                    currentChunkSize = Math.Min(currentChunkSize * 2, MaxChunkSize);
                  }
                  break;
                }

                if (!HttpHelpers.IsRetryableStatusCode(response.StatusCode)) {
                  string errorBody = await HttpHelpers.ReadResponseContentSafeAsync(response)
                      .ConfigureAwait(false);
                  throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
                }
              } finally {
                response.Dispose();
              }
            }

            // Transient failure: reset chunk size to PreferredChunkSize (256 KB) for future chunks,
            // sleep with exponential backoff + jitter, and query the server offset.
            currentChunkSize = PreferredChunkSize;
            int delayMs = currentSleepMs + GetRandomJitterMs();
            if (retryStopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > maxUploadRetryTime) {
              throw new StorageException(
                  StorageException.ErrorRetryLimitExceeded,
                  0,
                  lastException?.Message ??
                  StorageException.GetErrorMessageForCode(StorageException.ErrorRetryLimitExceeded));
            }

            await Task.Delay(delayMs, _storageTask.CancellationToken).ConfigureAwait(false);
            if (networkError) {
              currentSleepMs = InitialBackoffMs;
            } else if (currentSleepMs < MaxBackoffMs) {
              currentSleepMs = Math.Min(currentSleepMs * 2, MaxBackoffMs);
            }

            var recoveryResult = await QuerySessionWithRetryAsync(
                app,
                maxUploadRetryTime,
                retryStopwatch).ConfigureAwait(false);
            if (recoveryResult.IsFinal) {
              bytesUploaded = chunkStartOffset + bytesInChunk;
              _uploadState.State.BytesTransferred = bytesUploaded;
              StorageMetadata completedMetadata = await ResolveFinalizedMetadataAsync(
                  recoveryResult.ResponseBody).ConfigureAwait(false);
              _transferStateUpdater.SetMetadata(completedMetadata);
              _storageTask.MarkCompleted();
              return completedMetadata;
            }

            offsetWithinBuffer = await SynchronizeBufferOrStreamAsync(
                chunkStartOffset,
                bytesInChunk,
                hasLookahead,
                recoveryResult.ServerOffset).ConfigureAwait(false);
            _uploadState.State.BytesTransferred = recoveryResult.ServerOffset;
            ReportProgress();
            if (offsetWithinBuffer < 0) {
              bytesUploaded = recoveryResult.ServerOffset;
              hasLookahead = false;
              reachedEof = false;
              break;
            }
            if (offsetWithinBuffer == bytesInChunk && !isFinalChunk) {
              bytesUploaded = recoveryResult.ServerOffset;
              break;
            }
          }
        } finally {
          ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
      }
    }

    private async Task<Uri> StartSessionAsync(FirebaseApp app, TimeSpan maxUploadRetryTime) {
      string bucketObjectsUrl = StorageHttpClient.GetBucketObjectsUrl(_reference, _path);
      string objectPath = StorageHttpClient.GetObjectPath(_reference, _path);
      string startUrl =
          $"{bucketObjectsUrl}?name={StoragePath.EscapePath(objectPath)}&uploadType=resumable";

      string contentType = !string.IsNullOrEmpty(_metadata?.ContentType)
          ? _metadata.ContentType
          : "application/octet-stream";
      string metadataJson = _metadata != null ? _metadata.ToJson() : "{}";
      if (string.IsNullOrEmpty(metadataJson)) {
        metadataJson = "{}";
      }

      HttpResponseMessage response = null;
      try {
        try {
          response = await HttpHelpers.SendWithExponentialBackoffAsync(
              StorageHttpClient.Client,
              async () => {
                var request = new HttpRequestMessage(HttpMethod.Post, startUrl);
                await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "start");
                request.Headers.TryAddWithoutValidation(
                    "X-Goog-Upload-Header-Content-Type",
                    contentType);
                if (_uploadState.TotalByteCount >= 0) {
                  request.Headers.TryAddWithoutValidation(
                      "X-Goog-Upload-Header-Content-Length",
                      _uploadState.TotalByteCount.ToString());
                }
                request.Content = new StringContent(metadataJson, Encoding.UTF8, "application/json");
                return request;
              },
              maxUploadRetryTime,
              HttpCompletionOption.ResponseContentRead,
              _storageTask.CancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
          _storageTask.ThrowIfCanceled();
          throw;
        } catch (Exception ex) {
          throw new StorageException(
              StorageException.ErrorRetryLimitExceeded,
              0,
              ex.Message);
        }

        string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
        int statusCode = (int)response.StatusCode;
        if (!((statusCode >= 200 && statusCode < 300) || statusCode == 308)) {
          throw StorageException.CreateFromHttpResponse(statusCode, body);
        }

        string uploadUrl = GetHeaderFirstValue(response, "X-Goog-Upload-URL");
        if (string.IsNullOrEmpty(uploadUrl)) {
          throw new StorageException(
              StorageException.ErrorUnknown,
              statusCode,
              "Resumable upload start response did not include an X-Goog-Upload-URL header.");
        }

        return new Uri(uploadUrl);
      } finally {
        response?.Dispose();
      }
    }

    private struct SessionQueryResult {
      public long ServerOffset;
      public bool IsFinal;
      public string ResponseBody;
    }

    private async Task<SessionQueryResult> QuerySessionWithRetryAsync(
        FirebaseApp app,
        TimeSpan maxUploadRetryTime,
        Stopwatch stopwatch) {
      TimeSpan remainingBudget = maxUploadRetryTime - stopwatch.Elapsed;
      if (remainingBudget < TimeSpan.Zero) {
        remainingBudget = TimeSpan.Zero;
      }

      HttpResponseMessage response = null;
      try {
        try {
          response = await HttpHelpers.SendWithExponentialBackoffAsync(
              StorageHttpClient.Client,
              async () => {
                var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri);
                await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "query");
                request.Content = new ByteArrayContent(Array.Empty<byte>());
                return request;
              },
              remainingBudget,
              HttpCompletionOption.ResponseContentRead,
              _storageTask.CancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
          _storageTask.ThrowIfCanceled();
          throw;
        } catch (Exception ex) {
          throw new StorageException(
              StorageException.ErrorRetryLimitExceeded,
              0,
              ex.Message);
        }

        string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
        int statusCode = (int)response.StatusCode;
        if (!((statusCode >= 200 && statusCode < 300) || statusCode == 308)) {
          throw StorageException.CreateFromHttpResponse(statusCode, body);
        }

        string uploadStatus = GetHeaderFirstValue(response, "X-Goog-Upload-Status");
        string sizeReceivedStr = GetHeaderFirstValue(response, "X-Goog-Upload-Size-Received");
        long serverOffset = 0;
        if (!string.IsNullOrEmpty(sizeReceivedStr)) {
          long.TryParse(sizeReceivedStr, out serverOffset);
        }

        bool isFinal = string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase);
        return new SessionQueryResult {
          ServerOffset = serverOffset,
          IsFinal = isFinal,
          ResponseBody = body
        };
      } finally {
        response?.Dispose();
      }
    }

    private async Task<StorageMetadata> ResolveFinalizedMetadataAsync(string responseBody) {
      if (!string.IsNullOrEmpty(responseBody)) {
        return StorageMetadata.FromJson(responseBody, _reference);
      }
      return await StorageHttpClient.GetMetadataAsync(
          _reference,
          _path,
          _storageTask.CancellationToken).ConfigureAwait(false);
    }

    private async Task<int> SynchronizeBufferOrStreamAsync(
        long chunkStartOffset,
        int bytesInChunk,
        bool hasLookahead,
        long serverOffset) {
      if (serverOffset >= chunkStartOffset && serverOffset <= chunkStartOffset + bytesInChunk) {
        return (int)(serverOffset - chunkStartOffset);
      }
      if (_stream.CanSeek) {
        _stream.Seek(serverOffset, SeekOrigin.Begin);
        return -1;
      }
      long currentStreamOffset = chunkStartOffset + bytesInChunk + (hasLookahead ? 1 : 0);
      if (serverOffset >= currentStreamOffset) {
        long reachedOffset = await AdvanceStreamToOffsetAsync(
            currentStreamOffset,
            serverOffset,
            _storageTask.CancellationToken).ConfigureAwait(false);
        if (reachedOffset < serverOffset) {
          throw new StorageException(
              StorageException.ErrorUnknown,
              0,
              "Upload stream ended before reaching the server-reported offset.");
        }
        return -1;
      }
      throw new StorageException(
          StorageException.ErrorUnknown,
          0,
          "Cannot rewind non-seekable upload stream to earlier server offset.");
    }

    private async Task<long> AdvanceStreamToOffsetAsync(
        long currentOffset,
        long targetOffset,
        CancellationToken cancellationToken) {
      if (targetOffset <= currentOffset) {
        if (_stream.CanSeek && targetOffset >= 0) {
          _stream.Seek(targetOffset, SeekOrigin.Begin);
          return targetOffset;
        }
        return currentOffset;
      }

      if (_stream.CanSeek) {
        _stream.Seek(targetOffset, SeekOrigin.Begin);
        return targetOffset;
      }

      byte[] skipBuffer = ArrayPool<byte>.Shared.Rent(PreferredChunkSize);
      try {
        long remaining = targetOffset - currentOffset;
        while (remaining > 0) {
          int toRead = (int)Math.Min(remaining, skipBuffer.Length);
          int read = await _stream.ReadAsync(skipBuffer, 0, toRead, cancellationToken)
              .ConfigureAwait(false);
          if (read <= 0) {
            break;
          }
          currentOffset += read;
          remaining -= read;
        }
        return currentOffset;
      } finally {
        ArrayPool<byte>.Shared.Return(skipBuffer);
      }
    }

    private void FireAndForgetCancelSession() {
      Uri sessionUri = _sessionUri;
      if (sessionUri == null) {
        return;
      }
      FirebaseApp app = _reference?.Storage?.App;
      _ = Task.Run(async () => {
        try {
          using (var request = new HttpRequestMessage(HttpMethod.Post, sessionUri)) {
            await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "cancel");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            using (var response = await StorageHttpClient.Client.SendAsync(request)
                .ConfigureAwait(false)) {
            }
          }
        } catch (Exception) {
          // Best-effort cancellation notification to GCS.
        }
      });
    }

    private void ReportProgress() {
      _transferStateUpdater.ReportProgress(
          _uploadState.BytesTransferred,
          _uploadState.TotalByteCount,
          _sessionUri);
    }

    private static string GetHeaderFirstValue(HttpResponseMessage response, string headerName) {
      if (response == null) {
        return null;
      }
      if (response.Headers != null &&
          response.Headers.TryGetValues(headerName, out IEnumerable<string> values)) {
        return values.FirstOrDefault();
      }
      if (response.Content?.Headers != null &&
          response.Content.Headers.TryGetValues(headerName, out IEnumerable<string> contentValues)) {
        return contentValues.FirstOrDefault();
      }
      return null;
    }

    private static int GetRandomJitterMs() {
      lock (_jitterLock) {
        return _jitterRandom.Next(MaxJitterMs);
      }
    }
  }
}
