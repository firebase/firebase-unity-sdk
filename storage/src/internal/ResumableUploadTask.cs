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
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Implements the Google Cloud Storage adaptive resumable upload protocol
  /// (start -> query -> chunked upload with adaptive 256KB..32MB buffer pooling -> finalize).
  /// </summary>
  internal sealed class ResumableUploadTask {
    // Chunk scaling bounds (256 KB -> 32 MB) match the Firebase Android SDK's UploadTask
    // (PREFERRED_CHUNK_SIZE, MAXIMUM_CHUNK_SIZE); retry timing comes from RetryBackoff.
    internal const int PreferredChunkSize = 256 * 1024;
    internal const int MaxChunkSize = 32 * 1024 * 1024;

    private readonly StorageReference _reference;
    private readonly StoragePath _path;
    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private readonly StorageMetadata _metadata;
    private readonly StorageTask _storageTask;
    private readonly UploadState _uploadState;
    private readonly TransferStateUpdater<UploadState> _transferStateUpdater;
    // Position of the stream when the upload started; server offsets are relative to this.
    private readonly long _streamBaseOffset;
    private readonly ModuleLogger _logger;
    private readonly FirebaseApp _app;
    private readonly TimeSpan _maxRetryTime;
    private Uri _sessionUri;
    // Scheduler slot held while ExecuteUploadAsync runs; yielded while parked at the pause gate.
    private StorageTaskScheduler.Slot _slot;

    internal ResumableUploadTask(
        StorageReference reference,
        Stream stream,
        long totalByteCount,
        bool ownsStream,
        StorageMetadata metadata,
        IProgress<UploadState> progressHandler,
        CancellationToken cancelToken,
        Uri previousSessionUri = null,
        StoragePath path = null,
        StorageController controller = null) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _stream = Preconditions.CheckNotNull(stream, nameof(stream));
      _ownsStream = ownsStream;
      _metadata = metadata;
      _path = path;
      _sessionUri = previousSessionUri;
      _streamBaseOffset = stream.CanSeek ? stream.Position : 0;
      _logger = reference.Logger;
      FirebaseStorage storage = reference.Storage;
      _app = storage?.App;
      _maxRetryTime = storage?.MaxUploadRetryTime ?? TimeSpan.FromMinutes(10);
      _storageTask = StorageTask.CreateForTransfer(reference, cancelToken, controller);
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
        StoragePath path = null,
        StorageController controller = null) {
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
          path,
          controller);
      return uploadTask.RunAsync();
    }

    internal static Task<StorageMetadata> PutStreamAsync(
        StorageReference reference,
        Stream stream,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StoragePath path = null,
        StorageController controller = null) {
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
          path,
          controller);
      return uploadTask.RunAsync();
    }

    internal static Task<StorageMetadata> PutFileAsync(
        StorageReference reference,
        string filePath,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StoragePath path = null,
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(filePath != null, "filePath cannot be null");

      string localPath = IoUtil.ResolveLocalFilePath(filePath);
      long fileLength;
      FileStream fileStream;
      try {
        if (!File.Exists(localPath)) {
          throw new FileNotFoundException($"{localPath} not found", filePath);
        }
        fileLength = new FileInfo(localPath).Length;
        fileStream = new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
      } catch (Exception ex) {
        var tcs = new TaskCompletionSource<StorageMetadata>();
        tcs.SetException(ex is FileNotFoundException
            ? new FileNotFoundException($"{localPath} not found", filePath)
            : ex);
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
          path,
          controller);
      return uploadTask.RunAsync();
    }

    internal Task<StorageMetadata> RunAsync() {
      return _storageTask.RunTransferAsync(
          () => {
            StorageHttpClient.EnsureObjectReference(_reference, _path, "Upload");
            return StorageTaskScheduler.RunUploadAsync(
                slot => {
                  _slot = slot;
                  return ExecuteUploadAsync();
                },
                _storageTask.CancellationToken);
          },
          onCanceled: FireAndForgetCancelSession,
          cleanupAsync: () => {
            if (_ownsStream) {
              try {
                _stream.Dispose();
              } catch (Exception) {
              }
            }
            return _transferStateUpdater.FlushAsync();
          });
    }

    private Task WaitIfPausedAsync() {
      return _storageTask.WaitIfPausedAsync(_slot, _storageTask.CancellationToken);
    }

    private async Task<StorageMetadata> ExecuteUploadAsync() {
      _storageTask.ThrowIfCanceled();
      _storageTask.TransitionToInProgress();

      long bytesUploaded = 0;
      bool needServerQuery = _sessionUri != null;

      await WaitIfPausedAsync().ConfigureAwait(false);

      if (_sessionUri == null) {
        _sessionUri = await StartSessionAsync().ConfigureAwait(false);
        _uploadState.State.UploadSessionUri = _sessionUri;
        _logger.LogMessage(LogLevel.Debug, $"Started resumable upload session {_sessionUri}");
        ReportProgress();
      } else {
        _logger.LogMessage(LogLevel.Debug, $"Resuming upload session {_sessionUri}");
      }

      if (needServerQuery) {
        SessionQueryResult queryResult = await QuerySessionAsync(_maxRetryTime).ConfigureAwait(false);
        if (queryResult.IsFinal) {
          // The previous session already finished; report its size and hand back its metadata.
          StorageMetadata completedMetadata = await ResolveFinalizedMetadataAsync(
              queryResult.ResponseBody).ConfigureAwait(false);
          _uploadState.State.BytesTransferred = queryResult.ServerOffset > 0
              ? queryResult.ServerOffset
              : Math.Max(completedMetadata?.SizeBytes ?? 0, _uploadState.TotalByteCount);
          return Complete(completedMetadata);
        }
        bytesUploaded = await AdvanceStreamToOffsetAsync(bytesUploaded, queryResult.ServerOffset)
            .ConfigureAwait(false);
        _uploadState.State.BytesTransferred = bytesUploaded;
        ReportProgress();
      }

      int currentChunkSize = PreferredChunkSize;
      byte[] lookaheadByte = new byte[1];
      bool hasLookahead = false;
      bool reachedEof = false;

      while (true) {
        await WaitIfPausedAsync().ConfigureAwait(false);
        _storageTask.ThrowIfCanceled();

        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(currentChunkSize);
        try {
          int bytesInChunk = 0;
          long chunkStartOffset = bytesUploaded;

          if (hasLookahead) {
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

          // Upload the current chunk with retry, pause/resume, and server offset recovery. The
          // retry budget measures time spent failing, not wall-clock time (see RetryBackoff), and
          // is reset after a pause, so a slow but healthy chunk (or a long pause) never exhausts
          // MaxUploadRetryTime on its own.
          var backoff = new RetryBackoff(_maxRetryTime);
          int offsetWithinBuffer = 0;

          while (true) {
            await WaitIfPausedAsync().ConfigureAwait(false);
            _storageTask.ThrowIfCanceled();

            int bytesToSend = bytesInChunk - offsetWithinBuffer;
            long uploadOffset = chunkStartOffset + offsetWithinBuffer;
            string uploadCommand = isFinalChunk
                ? (bytesToSend > 0 ? "upload, finalize" : "finalize")
                : "upload";

            HttpResponseMessage response = null;
            Exception lastException = null;
            bool paused = false;
            bool transportFailure = false;

            CancellationToken chunkToken = _storageTask.GetChunkCancellationToken();
            StallWatchdog watchdog = null;
            try {
              watchdog = new StallWatchdog(chunkToken);
              using (var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri)) {
                await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
                request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", uploadCommand);
                request.Headers.TryAddWithoutValidation(
                    "X-Goog-Upload-Offset",
                    uploadOffset.ToString());
                request.Content = new WatchdogByteArrayContent(
                    rentedBuffer, offsetWithinBuffer, bytesToSend, watchdog);

                response = await StorageHttpClient.Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    watchdog.Token).ConfigureAwait(false);
              }
            } catch (Exception ex) when (StorageHttpClient.IsTransportException(ex)) {
              if (_storageTask.ClassifyAttemptFailure(ex, watchdog, default, out lastException) ==
                  StorageTask.AttemptFailure.Paused) {
                paused = true;
              } else {
                transportFailure = true;
                _logger.LogMessage(LogLevel.Warning,
                    $"Upload chunk at offset {uploadOffset} failed: {lastException.Message}");
              }
            } finally {
              watchdog?.Dispose();
            }

            if (response != null) {
              try {
                int statusCode = (int)response.StatusCode;
                _logger.LogMessage(LogLevel.Verbose,
                    $"Upload {uploadCommand} offset={uploadOffset} bytes={bytesToSend} -> {statusCode}");
                if (StorageHttpClient.IsSuccessOrResumeIncomplete(statusCode)) {
                  string responseBody = await StorageHttpClient.ReadResponseContentSafeAsync(response)
                      .ConfigureAwait(false);
                  string uploadStatus =
                      StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Status");

                  bytesUploaded = chunkStartOffset + bytesInChunk;
                  _uploadState.State.BytesTransferred = bytesUploaded;
                  ReportProgress();

                  if (isFinalChunk ||
                      string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase)) {
                    return await CompleteAsync(responseBody).ConfigureAwait(false);
                  }

                  // Chunk succeeded; scale chunk size up to MaxChunkSize (32 MB).
                  if (currentChunkSize < MaxChunkSize) {
                    currentChunkSize = Math.Min(currentChunkSize * 2, MaxChunkSize);
                  }
                  break;
                }

                if (!StorageHttpClient.IsRetryableStatusCode(response.StatusCode)) {
                  string errorBody = await StorageHttpClient.ReadResponseContentSafeAsync(response)
                      .ConfigureAwait(false);
                  throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
                }
              } finally {
                response.Dispose();
              }
            }

            if (paused) {
              // Pause() severed the request: park at the gate, then resume with a fresh budget.
              await WaitIfPausedAsync().ConfigureAwait(false);
              backoff.Reset();
            } else {
              // Transient failure: drop back to 256 KB chunks and back off before resynchronizing.
              currentChunkSize = PreferredChunkSize;
              await backoff.DelayOrThrowAsync(
                  transportFailure,
                  lastException,
                  _logger,
                  $"Upload chunk at offset {uploadOffset}",
                  _storageTask.CancellationToken).ConfigureAwait(false);
            }

            // Ask the server how much it has committed and continue from there.
            SessionQueryResult recovery = await QuerySessionAsync(backoff.Remaining)
                .ConfigureAwait(false);
            if (recovery.IsFinal) {
              bytesUploaded = chunkStartOffset + bytesInChunk;
              _uploadState.State.BytesTransferred = bytesUploaded;
              return await CompleteAsync(recovery.ResponseBody).ConfigureAwait(false);
            }

            _logger.LogMessage(LogLevel.Debug,
                $"Server reports {recovery.ServerOffset} bytes received; resynchronizing.");
            offsetWithinBuffer = await SynchronizeBufferOrStreamAsync(
                chunkStartOffset,
                bytesInChunk,
                hasLookahead,
                recovery.ServerOffset).ConfigureAwait(false);
            _uploadState.State.BytesTransferred = recovery.ServerOffset;
            ReportProgress();
            if (offsetWithinBuffer < 0) {
              // The stream was repositioned to the server offset; refill from there.
              bytesUploaded = recovery.ServerOffset;
              hasLookahead = false;
              reachedEof = false;
              break;
            }
            if (offsetWithinBuffer == bytesInChunk && !isFinalChunk) {
              // The server already has this whole chunk; move on to the next one.
              bytesUploaded = recovery.ServerOffset;
              break;
            }
          }
        } finally {
          ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
      }
    }

    private async Task<Uri> StartSessionAsync() {
      string bucketObjectsUrl = StorageHttpClient.GetBucketObjectsUrl(_reference, _path);
      string objectPath = StorageHttpClient.GetObjectPath(_reference, _path);
      string startUrl =
          $"{bucketObjectsUrl}?name={StoragePath.EscapePath(objectPath)}&uploadType=resumable";

      string contentType = !string.IsNullOrEmpty(_metadata?.ContentType)
          ? _metadata.ContentType
          : "application/octet-stream";
      string metadataJson = _metadata?.ToJson();
      if (string.IsNullOrEmpty(metadataJson)) {
        metadataJson = "{}";
      }

      using (HttpResponseMessage response = await StorageHttpClient.SendTransferRequestAsync(
          _storageTask,
          async () => {
            var request = new HttpRequestMessage(HttpMethod.Post, startUrl);
            await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
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
          _maxRetryTime,
          HttpCompletionOption.ResponseContentRead,
          _logger,
          "Upload session start").ConfigureAwait(false)) {
        string uploadUrl = StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-URL");
        if (string.IsNullOrEmpty(uploadUrl)) {
          throw new StorageException(
              StorageException.ErrorUnknown,
              (int)response.StatusCode,
              "Resumable upload start response did not include an X-Goog-Upload-URL header.");
        }
        return new Uri(uploadUrl);
      }
    }

    private struct SessionQueryResult {
      public long ServerOffset;
      public bool IsFinal;
      public string ResponseBody;
    }

    // Asks the server how many bytes of the session it has committed, retrying within retryBudget.
    private async Task<SessionQueryResult> QuerySessionAsync(TimeSpan retryBudget) {
      using (HttpResponseMessage response = await StorageHttpClient.SendTransferRequestAsync(
          _storageTask,
          async () => {
            var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri);
            await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "query");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            return request;
          },
          retryBudget,
          HttpCompletionOption.ResponseContentRead,
          _logger,
          "Upload session query").ConfigureAwait(false)) {
        string body = await StorageHttpClient.ReadResponseContentSafeAsync(response)
            .ConfigureAwait(false);
        string uploadStatus = StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Status");
        long.TryParse(
            StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Size-Received"),
            out long serverOffset);
        return new SessionQueryResult {
          ServerOffset = serverOffset,
          IsFinal = string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase),
          ResponseBody = body
        };
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

    // Publishes the finalized metadata to the progress handler and completes the task.
    private StorageMetadata Complete(StorageMetadata metadata) {
      _transferStateUpdater.SetMetadata(metadata);
      _storageTask.MarkCompleted();
      return metadata;
    }

    private async Task<StorageMetadata> CompleteAsync(string finalizeResponseBody) {
      return Complete(await ResolveFinalizedMetadataAsync(finalizeResponseBody).ConfigureAwait(false));
    }

    // Repositions the upload after the server reported serverOffset committed bytes. Returns the
    // offset within the current chunk buffer to resume from, or -1 if the stream itself was moved
    // to serverOffset and the chunk must be refilled.
    private async Task<int> SynchronizeBufferOrStreamAsync(
        long chunkStartOffset,
        int bytesInChunk,
        bool hasLookahead,
        long serverOffset) {
      if (serverOffset >= chunkStartOffset && serverOffset <= chunkStartOffset + bytesInChunk) {
        return (int)(serverOffset - chunkStartOffset);
      }
      if (_stream.CanSeek) {
        _stream.Seek(_streamBaseOffset + serverOffset, SeekOrigin.Begin);
        return -1;
      }
      long currentStreamOffset = chunkStartOffset + bytesInChunk + (hasLookahead ? 1 : 0);
      if (serverOffset >= currentStreamOffset) {
        long reachedOffset = await AdvanceStreamToOffsetAsync(currentStreamOffset, serverOffset)
            .ConfigureAwait(false);
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

    // Moves the stream to targetOffset (seeking when possible, otherwise reading forward) and
    // returns the offset actually reached.
    private async Task<long> AdvanceStreamToOffsetAsync(long currentOffset, long targetOffset) {
      if (_stream.CanSeek) {
        if (targetOffset < 0) {
          return currentOffset;
        }
        _stream.Seek(_streamBaseOffset + targetOffset, SeekOrigin.Begin);
        return targetOffset;
      }
      if (targetOffset <= currentOffset) {
        return currentOffset;
      }
      return currentOffset + await IoUtil.SkipAsync(
          _stream,
          targetOffset - currentOffset,
          _storageTask.CancellationToken).ConfigureAwait(false);
    }

    private void FireAndForgetCancelSession() {
      Uri sessionUri = _sessionUri;
      if (sessionUri == null) {
        return;
      }
      _ = Task.Run(async () => {
        try {
          using (var request = new HttpRequestMessage(HttpMethod.Post, sessionUri)) {
            await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "cancel");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            using (await StorageHttpClient.Client.SendAsync(request).ConfigureAwait(false)) {
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
  }
}
