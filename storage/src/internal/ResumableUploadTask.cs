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
using Firebase.Internal;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Implements the Google Cloud Storage adaptive resumable upload protocol
  /// (start -> query -> chunked upload with adaptive 256KB..32MB chunks -> finalize).
  /// </summary>
  internal sealed class ResumableUploadTask {
    // Chunk scaling bounds (256 KB -> 32 MB) match the Firebase Android SDK's UploadTask
    // (PREFERRED_CHUNK_SIZE, MAXIMUM_CHUNK_SIZE); retry timing comes from RetryBackoff.
    internal const int PreferredChunkSize = 256 * 1024;
    internal const int MaxChunkSize = 32 * 1024 * 1024;

    private readonly StorageReference _reference;
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
    // Chunk buffer, grown as the chunk size scales up. Buffers up to 1 MB are rented from
    // ArrayPool<byte>.Shared and returned when ExecuteUploadAsync exits.
    private const int MaxPooledBufferSize = 1024 * 1024;
    private byte[] _chunkBuffer;
    private bool _chunkBufferPooled;

    internal ResumableUploadTask(
        StorageReference reference,
        Stream stream,
        long totalByteCount,
        bool ownsStream,
        StorageMetadata metadata,
        IProgress<UploadState> progressHandler,
        CancellationToken cancelToken,
        Uri previousSessionUri = null,
        StorageController controller = null) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _stream = Preconditions.CheckNotNull(stream, nameof(stream));
      _ownsStream = ownsStream;
      _metadata = metadata;
      _sessionUri = previousSessionUri;
      _streamBaseOffset = stream.CanSeek ? stream.Position : 0;
      _logger = reference.Logger;
      FirebaseStorage storage = reference.Storage;
      _app = storage?.App;
      _maxRetryTime = storage?.MaxUploadRetryTime ?? TimeSpan.FromMinutes(10);
      _storageTask = StorageTask.CreateForTransfer(reference, cancelToken, controller);
      _uploadState = new UploadState(reference, totalByteCount, previousSessionUri);
      _transferStateUpdater = new TransferStateUpdater<UploadState>(progressHandler, _uploadState);
    }

    internal static Task<StorageMetadata> PutBytesAsync(
        StorageReference reference,
        byte[] bytes,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default,
        Uri previousSessionUri = null,
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(bytes != null, "bytes cannot be null");

      var uploadTask = new ResumableUploadTask(
          reference,
          new MemoryStream(bytes, writable: false),
          bytes.Length,
          ownsStream: true,
          MetadataChange.Build(customMetadata),
          progressHandler,
          cancelToken,
          previousSessionUri,
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
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(stream != null, "stream cannot be null");

      // Note: UIHandlerAutomated.cs explicitly asserts that uploadState.TotalByteCount == -1
      // when uploading from a stream via PutStreamAsync.
      var uploadTask = new ResumableUploadTask(
          reference,
          stream,
          totalByteCount: -1,
          ownsStream: false,
          MetadataChange.Build(customMetadata),
          progressHandler,
          cancelToken,
          previousSessionUri,
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
        StorageController controller = null) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(filePath != null, "filePath cannot be null");

      string localPath = IoUtil.ResolveLocalFilePath(filePath);
      FileStream fileStream;
      try {
        if (!File.Exists(localPath)) {
          throw new FileNotFoundException($"{localPath} not found", filePath);
        }
        fileStream = new FileStream(
            localPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
      } catch (Exception ex) {
        var tcs = new TaskCompletionSource<StorageMetadata>();
        tcs.SetException(ex);
        return tcs.Task;
      }

      ResumableUploadTask uploadTask;
      try {
        uploadTask = new ResumableUploadTask(
            reference,
            fileStream,
            fileStream.Length,
            ownsStream: true,
            MetadataChange.Build(customMetadata),
            progressHandler,
            cancelToken,
            previousSessionUri,
              controller);
      } catch {
        fileStream.Dispose();
        throw;
      }
      return uploadTask.RunAsync();
    }

    internal Task<StorageMetadata> RunAsync() {
      return _storageTask.RunTransferAsync(
          () => {
            StorageHttpClient.EnsureObjectReference(_reference, "Upload");
            return StorageTaskScheduler.RunUploadAsync(
                slot => {
                  _storageTask.Slot = slot;
                  return ExecuteUploadAsync();
                },
                _storageTask.CancellationToken);
          },
          onCanceled: FireAndForgetCancelSession,
          cleanupAsync: () => {
            ReleaseChunkBuffer();
            if (_ownsStream) {
              try {
                _stream.Dispose();
              } catch (Exception) {
              }
            }
            return _transferStateUpdater.FlushAsync();
          });
    }

    private async Task<StorageMetadata> ExecuteUploadAsync() {
      _storageTask.ThrowIfCanceled();
      _storageTask.TransitionToInProgress();

      // The retry budget measures time spent failing, not wall-clock time (see RetryBackoff), and
      // is reset whenever a chunk commits or a pause ends, so a slow but healthy upload (or a long
      // pause) never exhausts MaxUploadRetryTime on its own.
      RetryBackoff backoff = StorageHttpClient.CreateRetryBackoff(_maxRetryTime);
      long bytesUploaded = 0;

      if (_sessionUri == null) {
        _sessionUri = await StartSessionAsync(backoff).ConfigureAwait(false);
        _uploadState.UploadSessionUri = _sessionUri;
        _logger.LogMessage(LogLevel.Debug, $"Started resumable upload session {_sessionUri}");
        ReportProgress();
      } else {
        _logger.LogMessage(LogLevel.Debug, $"Resuming upload session {_sessionUri}");
        SessionQueryResult queryResult = await QuerySessionAsync(backoff).ConfigureAwait(false);
        if (queryResult.IsFinal) {
          // The previous session already finished; report its size and hand back its metadata.
          StorageMetadata completedMetadata = await ResolveFinalizedMetadataAsync(
              queryResult.ResponseBody).ConfigureAwait(false);
          _uploadState.BytesTransferred = queryResult.ServerOffset > 0
              ? queryResult.ServerOffset
              : Math.Max(completedMetadata?.SizeBytes ?? 0, _uploadState.TotalByteCount);
          return Complete(completedMetadata);
        }
        bytesUploaded = await AdvanceStreamToOffsetAsync(0, queryResult.ServerOffset)
            .ConfigureAwait(false);
        if (bytesUploaded < queryResult.ServerOffset) {
          throw new StorageException(
              StorageException.ErrorUnknown,
              0,
              "Upload stream ended before reaching the server-reported offset.");
        }
        _uploadState.BytesTransferred = bytesUploaded;
        ReportProgress();
      }

      int initialChunkSize =
          (_uploadState.TotalByteCount >= 0 && _uploadState.TotalByteCount < PreferredChunkSize)
              ? Math.Max(1, (int)_uploadState.TotalByteCount)
              : PreferredChunkSize;
      int currentChunkSize = initialChunkSize;
      byte[] lookaheadByte = new byte[1];
      bool hasLookahead = false;
      bool reachedEof = false;

      while (true) {
        byte[] buffer = GetChunkBuffer(currentChunkSize);
        int bytesInChunk = 0;
        long chunkStartOffset = bytesUploaded;

        {
          if (hasLookahead) {
            buffer[0] = lookaheadByte[0];
            bytesInChunk = 1;
            hasLookahead = false;
          }
          while (bytesInChunk < currentChunkSize && !reachedEof) {
            int read = await _stream.ReadAsync(
                buffer,
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
              // Peek one byte so a stream of unknown length can mark its last chunk as final.
              int peekRead = await _stream.ReadAsync(
                  lookaheadByte, 0, 1, _storageTask.CancellationToken).ConfigureAwait(false);
              if (peekRead <= 0) {
                reachedEof = true;
              } else {
                hasLookahead = true;
              }
            }
          }
        }

        bool isFinalChunk = reachedEof && !hasLookahead;
        int offsetWithinBuffer = 0;
        StorageException offsetMismatchError = null;

        // Send the buffered chunk, in slices of at most currentChunkSize, with retry, pause/resume
        // and server offset recovery. Every non-final slice is a multiple of 256 KB: the buffer
        // was filled to a 256 KB * 2^n size and the server only ever commits whole 256 KB units.
        while (true) {
          int bytesToSend = Math.Min(bytesInChunk - offsetWithinBuffer, currentChunkSize);
          bool sliceIsFinal = isFinalChunk && offsetWithinBuffer + bytesToSend == bytesInChunk;
          long uploadOffset = chunkStartOffset + offsetWithinBuffer;
          string uploadCommand = sliceIsFinal
              ? (bytesToSend > 0 ? "upload, finalize" : "finalize")
              : "upload";
          string responseBody = null;
          string uploadStatus = null;
          bool offsetMismatch = false;

          bool sent = await _storageTask.RunAttemptAsync(
              async watchdog => {
                HttpResponseMessage response;
                using (var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri)) {
                  await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
                  request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
                  request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", uploadCommand);
                  request.Headers.TryAddWithoutValidation(
                      "X-Goog-Upload-Offset", uploadOffset.ToString());
                  request.Content = new WatchdogByteArrayContent(
                      buffer, offsetWithinBuffer, bytesToSend, watchdog);
                  response = await StorageHttpClient.Client.SendAsync(
                      request, HttpCompletionOption.ResponseContentRead, watchdog.Token)
                      .ConfigureAwait(false);
                }
                int statusCode = (int)response.StatusCode;
                _logger.LogMessage(LogLevel.Verbose,
                    $"Upload {uploadCommand} offset={uploadOffset} bytes={bytesToSend} -> {statusCode}");
                if (StorageHttpClient.IsSuccessOrResumeIncomplete(statusCode)) {
                  responseBody = await StorageHttpClient.ReadResponseContentSafeAsync(response)
                      .ConfigureAwait(false);
                  uploadStatus = StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Status");
                  response.Dispose();
                  return;
                }
                StorageException error = await StorageHttpClient.CreateExceptionAsync(response)
                    .ConfigureAwait(false);
                if (statusCode == 400 && offsetMismatchError == null) {
                  // A 400 can mean the server committed more of an aborted chunk than we think;
                  // re-synchronize with one query before treating it as fatal.
                  offsetMismatchError = error;
                  offsetMismatch = true;
                  return;
                }
                throw error;
              },
              backoff,
              _logger,
              $"Upload chunk at offset {uploadOffset}").ConfigureAwait(false);

          if (sent && !offsetMismatch) {
            bytesUploaded = uploadOffset + bytesToSend;
            _uploadState.BytesTransferred = bytesUploaded;
            ReportProgress();
            backoff.Reset();
            offsetMismatchError = null;
            if (sliceIsFinal ||
                string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase)) {
              return await CompleteAsync(responseBody).ConfigureAwait(false);
            }
            // Slice committed; scale the chunk size up towards MaxChunkSize (32 MB).
            offsetWithinBuffer += bytesToSend;
            currentChunkSize = Math.Min(currentChunkSize * 2, MaxChunkSize);
            if (offsetWithinBuffer >= bytesInChunk) {
              break;
            }
            continue;
          }

          if (!sent) {
            // The attempt failed or was paused: drop back to the initial slice size so a flaky link
            // does not keep timing out on 32 MB payloads.
            currentChunkSize = initialChunkSize;
          }

          // Ask the server how much it has committed and continue from there.
          SessionQueryResult recovery = await QuerySessionAsync(backoff).ConfigureAwait(false);
          if (recovery.IsFinal) {
            bytesUploaded = chunkStartOffset + bytesInChunk;
            _uploadState.BytesTransferred = bytesUploaded;
            return await CompleteAsync(recovery.ResponseBody).ConfigureAwait(false);
          }
          if (offsetMismatch && recovery.ServerOffset == uploadOffset) {
            // The offsets agree, so the 400 was a genuine rejection.
            throw offsetMismatchError;
          }

          _logger.LogMessage(LogLevel.Debug,
              $"Server reports {recovery.ServerOffset} bytes received; resynchronizing.");
          offsetWithinBuffer = await SynchronizeBufferOrStreamAsync(
              chunkStartOffset,
              bytesInChunk,
              hasLookahead,
              recovery.ServerOffset).ConfigureAwait(false);
          _uploadState.BytesTransferred = recovery.ServerOffset;
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
      }
    }

    private byte[] GetChunkBuffer(int size) {
      if (_chunkBuffer == null || _chunkBuffer.Length < size) {
        ReleaseChunkBuffer();
        if (size <= MaxPooledBufferSize) {
          _chunkBuffer = ArrayPool<byte>.Shared.Rent(size);
          _chunkBufferPooled = true;
        } else {
          _chunkBuffer = new byte[size];
          _chunkBufferPooled = false;
        }
      }
      return _chunkBuffer;
    }

    private void ReleaseChunkBuffer() {
      if (_chunkBuffer != null && _chunkBufferPooled) {
        ArrayPool<byte>.Shared.Return(_chunkBuffer);
      }
      _chunkBuffer = null;
      _chunkBufferPooled = false;
    }

    private async Task<Uri> StartSessionAsync(RetryBackoff backoff) {
      string bucketObjectsUrl = StorageHttpClient.GetBucketObjectsUrl(_reference);
      string startUrl = $"{bucketObjectsUrl}?name=" +
          $"{StoragePath.EscapePath(_reference.StoragePath.ObjectPath)}&uploadType=resumable";

      string contentType = !string.IsNullOrEmpty(_metadata?.ContentType)
          ? _metadata.ContentType
          : "application/octet-stream";
      string metadataJson = _metadata?.ToJson();
      if (string.IsNullOrEmpty(metadataJson)) {
        metadataJson = "{}";
      }

      using (HttpResponseMessage response = await StorageHttpClient.SendAsync(
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
          backoff,
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

    // Asks the server how many bytes of the session it has committed, retrying within backoff.
    private async Task<SessionQueryResult> QuerySessionAsync(RetryBackoff backoff) {
      using (HttpResponseMessage response = await StorageHttpClient.SendAsync(
          _storageTask,
          async () => {
            var request = new HttpRequestMessage(HttpMethod.Post, _sessionUri);
            await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "query");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            return request;
          },
          backoff,
          HttpCompletionOption.ResponseContentRead,
          _logger,
          "Upload session query").ConfigureAwait(false)) {
        string body = await StorageHttpClient.ReadResponseContentSafeAsync(response)
            .ConfigureAwait(false);
        string uploadStatus = StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Status");
        bool isFinal = string.Equals(uploadStatus, "final", StringComparison.OrdinalIgnoreCase);
        if (!long.TryParse(
                StorageHttpClient.GetHeaderFirstValue(response, "X-Goog-Upload-Size-Received"),
                out long serverOffset) || serverOffset < 0) {
          if (!isFinal) {
            throw new StorageException(
                StorageException.ErrorUnknown,
                (int)response.StatusCode,
                "Resumable upload query response did not include a valid " +
                "X-Goog-Upload-Size-Received header.");
          }
          serverOffset = 0;
        }
        return new SessionQueryResult {
          ServerOffset = serverOffset,
          IsFinal = isFinal,
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
          _storageTask.CancellationToken).ConfigureAwait(false);
    }

    // Publishes the finalized metadata to the progress handler and completes the task.
    private StorageMetadata Complete(StorageMetadata metadata) {
      _uploadState.Metadata = metadata;
      ReportProgress();
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
        if (await AdvanceStreamToOffsetAsync(0, serverOffset).ConfigureAwait(false) < serverOffset) {
          throw new StorageException(
              StorageException.ErrorUnknown,
              0,
              "Upload stream ended before reaching the server-reported offset.");
        }
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
    // returns the offset actually reached, which is smaller than targetOffset only if the stream
    // is shorter than the server-committed data.
    private async Task<long> AdvanceStreamToOffsetAsync(long currentOffset, long targetOffset) {
      if (_stream.CanSeek) {
        if (targetOffset < 0) {
          return currentOffset;
        }
        long reachable = Math.Min(targetOffset, Math.Max(0, _stream.Length - _streamBaseOffset));
        _stream.Seek(_streamBaseOffset + reachable, SeekOrigin.Begin);
        return reachable;
      }
      if (targetOffset <= currentOffset) {
        return currentOffset;
      }
      return currentOffset + await IoUtil.SkipAsync(
          _stream,
          targetOffset - currentOffset,
          _storageTask.CancellationToken).ConfigureAwait(false);
    }

    // Best-effort "cancel" of the resumable session so the server discards the partial object.
    private void FireAndForgetCancelSession() {
      Uri sessionUri = _sessionUri;
      if (sessionUri == null) {
        return;
      }
      _ = Task.Run(async () => {
        try {
          using (var timeout = new CancellationTokenSource(StallWatchdog.StallTimeout))
          using (var request = new HttpRequestMessage(HttpMethod.Post, sessionUri)) {
            await StorageHttpClient.SetStorageRequestHeaders(request, _app).ConfigureAwait(false);
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Protocol", "resumable");
            request.Headers.TryAddWithoutValidation("X-Goog-Upload-Command", "cancel");
            request.Content = new ByteArrayContent(Array.Empty<byte>());
            using (await StorageHttpClient.Client.SendAsync(request, timeout.Token)
                .ConfigureAwait(false)) {
            }
          }
        } catch (Exception) {
        }
      });
    }

    private void ReportProgress() {
      _transferStateUpdater.Report();
    }
  }
}
