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
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Shared HTTP client and REST command operations for Firebase Storage.
  /// </summary>
  internal static class StorageHttpClient {
    private static readonly HttpMethod PatchMethod = new HttpMethod("PATCH");

    // Upper bound on simultaneous connections to the Storage host. The scheduler allows up to
    // 2 uploads + 3 downloads + 5 commands in flight, so this must exceed 10 or commands queue
    // behind long transfers (Mono's default per-host limit is 2).
    private const int MaxConnectionsPerServer = 16;

    private static HttpClient _httpClient = CreateDefaultHttpClient();

    internal static HttpClient Client {
      get { return _httpClient; }
      set { _httpClient = value ?? CreateDefaultHttpClient(); }
    }

    private static HttpClient CreateDefaultHttpClient() {
      var handler = new HttpClientHandler();
      try {
        handler.MaxConnectionsPerServer = MaxConnectionsPerServer;
      } catch (PlatformNotSupportedException) {
        // Some runtimes don't allow this to be configured; fall back to the default.
      }
      // Per-attempt stall detection is handled by StallWatchdog / SendWithExponentialBackoffAsync
      // so the client-wide timeout is disabled.
      return new HttpClient(handler) {
        Timeout = Timeout.InfiniteTimeSpan
      };
    }

    internal static StoragePath GetStoragePath(StorageReference reference, StoragePath path = null) {
      if (path != null) {
        return path;
      }
      return new StoragePath(reference?.Storage, reference?.Bucket, reference?.Path);
    }

    internal static string GetObjectPath(StorageReference reference, StoragePath path = null) {
      return GetStoragePath(reference, path).ObjectPath;
    }

    internal static bool IsRootPath(StorageReference reference, StoragePath path = null) {
      return GetStoragePath(reference, path).IsRoot;
    }

    /// <summary>
    /// Throws if the reference does not identify a single object (i.e. it is the bucket root or
    /// no bucket is configured). Object-level operations are invalid on such references and
    /// would otherwise be sent to the bucket's collection endpoint.
    /// </summary>
    internal static void EnsureObjectReference(StorageReference reference, StoragePath path,
                                               string operation) {
      StoragePath resolved = GetStoragePath(reference, path);
      if (string.IsNullOrEmpty(resolved.Bucket)) {
        throw new StorageException(
            StorageException.ErrorBucketNotFound,
            0,
            $"{operation} failed: no Cloud Storage bucket is configured. Set " +
            "AppOptions.StorageBucket or use FirebaseStorage.GetInstance(\"gs://<bucket>\").");
      }
      if (resolved.IsRoot) {
        throw new StorageException(
            StorageException.ErrorUnknown,
            0,
            $"{operation} cannot be performed on the root of a bucket. Use a child reference " +
            "such as storage.GetReference(\"path/to/object\").");
      }
    }

    internal static string GetBucketObjectsUrl(StorageReference reference, StoragePath path = null) {
      StoragePath resolvedPath = GetStoragePath(reference, path);
      FirebaseStorage storage = reference?.Storage ?? resolvedPath.Storage;
      string emulatorHost = storage?.EmulatorHost;
      int emulatorPort = storage?.EmulatorPort ?? 0;
      return resolvedPath.GetHttpBucketObjectsUrl(emulatorHost, emulatorPort);
    }

    internal static string GetObjectUrl(StorageReference reference, StoragePath path = null) {
      StoragePath resolvedPath = GetStoragePath(reference, path);
      FirebaseStorage storage = reference?.Storage ?? resolvedPath.Storage;
      string emulatorHost = storage?.EmulatorHost;
      int emulatorPort = storage?.EmulatorPort ?? 0;
      return resolvedPath.GetHttpObjectUrl(emulatorHost, emulatorPort);
    }

    /// <summary>
    /// Converts a transport-level failure raised by a retry loop into a
    /// <see cref="StorageException" /> with <see cref="StorageException.ErrorRetryLimitExceeded" />.
    /// Returns null for exceptions that are not network related so the caller can rethrow them
    /// unchanged (e.g. programming errors or <see cref="StorageException" /> itself).
    /// </summary>
    internal static StorageException WrapRetryFailure(Exception ex) {
      if (ex is StorageException) {
        return null;
      }
      if (ex is HttpRequestException || ex is IOException || ex is SocketException ||
          ex is OperationCanceledException) {
        return new StorageException(
            StorageException.ErrorRetryLimitExceeded,
            0,
            ex.Message,
            ex);
      }
      return null;
    }

    /// <summary>
    /// Sends a single REST command (metadata, list, delete) with retry and returns the body of a
    /// successful response; non-success responses are converted to <see cref="StorageException" />.
    /// </summary>
    private static async Task<string> SendCommandAsync(
        StorageReference reference,
        HttpMethod method,
        string url,
        Func<HttpContent> contentFactory,
        bool readBody,
        CancellationToken cancellationToken) {
      FirebaseStorage storage = reference.Storage;
      FirebaseApp app = storage?.App;
      TimeSpan maxRetryTime = storage?.MaxOperationRetryTime ?? TimeSpan.FromMinutes(2);
      ModuleLogger logger = reference.Logger;

      logger.LogMessage(LogLevel.Debug, $"{method.Method} {url}");
      HttpResponseMessage response = null;
      try {
        try {
          response = await HttpHelpers.SendWithExponentialBackoffAsync(
              Client,
              async () => {
                var request = new HttpRequestMessage(method, url);
                if (contentFactory != null) {
                  request.Content = contentFactory();
                }
                await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                return request;
              },
              maxRetryTime,
              HttpCompletionOption.ResponseContentRead,
              cancellationToken,
              StallWatchdog.StallTimeout).ConfigureAwait(false);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
          throw;
        } catch (Exception ex) {
          StorageException wrapped = WrapRetryFailure(ex);
          if (wrapped == null) {
            throw;
          }
          logger.LogMessage(LogLevel.Warning,
                            $"{method.Method} {url} failed after retries: {ex.Message}");
          throw wrapped;
        }

        int statusCode = (int)response.StatusCode;
        logger.LogMessage(LogLevel.Debug, $"{method.Method} {url} -> {statusCode}");
        if (!response.IsSuccessStatusCode) {
          string errorBody = await HttpHelpers.ReadResponseContentSafeAsync(response)
              .ConfigureAwait(false);
          throw StorageException.CreateFromHttpResponse(statusCode, errorBody);
        }
        return readBody
            ? await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false)
            : null;
      } finally {
        response?.Dispose();
      }
    }

    internal static Task<StorageMetadata> GetMetadataAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      return GetMetadataAsync(reference, null, cancellationToken);
    }

    internal static Task<StorageMetadata> GetMetadataAsync(
        StorageReference reference,
        StoragePath path,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, path, "GetMetadata");
        string body = await SendCommandAsync(
            reference,
            HttpMethod.Get,
            GetObjectUrl(reference, path),
            null,
            readBody: true,
            cancellationToken).ConfigureAwait(false);
        return StorageMetadata.FromJson(body, reference);
      }, cancellationToken);
    }

    internal static Task<StorageMetadata> UpdateMetadataAsync(
        StorageReference reference,
        MetadataChange metadataChange,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(metadataChange, nameof(metadataChange));
      return UpdateMetadataAsync(reference, null, MetadataChange.Build(metadataChange), cancellationToken);
    }

    internal static Task<StorageMetadata> UpdateMetadataAsync(
        StorageReference reference,
        StorageMetadata metadata,
        CancellationToken cancellationToken = default) {
      return UpdateMetadataAsync(reference, null, metadata, cancellationToken);
    }

    internal static Task<StorageMetadata> UpdateMetadataAsync(
        StorageReference reference,
        StoragePath path,
        MetadataChange metadataChange,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(metadataChange, nameof(metadataChange));
      return UpdateMetadataAsync(reference, path, MetadataChange.Build(metadataChange), cancellationToken);
    }

    internal static Task<StorageMetadata> UpdateMetadataAsync(
        StorageReference reference,
        StoragePath path,
        StorageMetadata metadata,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckNotNull(metadata, nameof(metadata));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, path, "UpdateMetadata");
        string jsonPayload = metadata.ToJson() ?? "{}";
        string body = await SendCommandAsync(
            reference,
            PatchMethod,
            GetObjectUrl(reference, path),
            () => new StringContent(jsonPayload, Encoding.UTF8, "application/json"),
            readBody: true,
            cancellationToken).ConfigureAwait(false);
        return StorageMetadata.FromJson(body, reference);
      }, cancellationToken);
    }

    internal static Task DeleteAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      return DeleteAsync(reference, null, cancellationToken);
    }

    internal static Task DeleteAsync(
        StorageReference reference,
        StoragePath path,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, path, "Delete");
        await SendCommandAsync(
            reference,
            HttpMethod.Delete,
            GetObjectUrl(reference, path),
            null,
            readBody: false,
            cancellationToken).ConfigureAwait(false);
      }, cancellationToken);
    }

    internal static Task<StorageListResult> ListAsync(
        StorageReference reference,
        int maxResults = 1000,
        string pageToken = null,
        CancellationToken cancellationToken = default) {
      return ListAsync(reference, null, maxResults, pageToken, cancellationToken);
    }

    internal static Task<StorageListResult> ListAsync(
        StorageReference reference,
        StoragePath path,
        int maxResults = 1000,
        string pageToken = null,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        FirebaseStorage storage = reference.Storage;
        string baseUrl = GetBucketObjectsUrl(reference, path);

        var queryParams = new List<string> {
          "delimiter=%2F"
        };

        if (!IsRootPath(reference, path)) {
          string objectPath = GetObjectPath(reference, path).Trim('/');
          if (!string.IsNullOrEmpty(objectPath)) {
            queryParams.Add($"prefix={StoragePath.EscapePath(objectPath + "/")}");
          }
        }

        if (maxResults > 0) {
          queryParams.Add($"maxResults={maxResults}");
        }

        if (!string.IsNullOrEmpty(pageToken)) {
          queryParams.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        }

        string url = $"{baseUrl}?{string.Join("&", queryParams.ToArray())}";
        string body = await SendCommandAsync(
            reference,
            HttpMethod.Get,
            url,
            null,
            readBody: true,
            cancellationToken).ConfigureAwait(false);

        string bucket = path != null ? path.Bucket : reference.Bucket;
        return StorageListResult.FromJson(body, storage, bucket);
      }, cancellationToken);
    }

    internal static Task<Uri> GetDownloadUrlAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      return GetDownloadUrlAsync(reference, null, cancellationToken);
    }

    internal static async Task<Uri> GetDownloadUrlAsync(
        StorageReference reference,
        StoragePath path,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      EnsureObjectReference(reference, path, "GetDownloadUrl");
      StorageMetadata metadata = await GetMetadataAsync(reference, path, cancellationToken)
          .ConfigureAwait(false);
      string token = metadata?.GetFirstDownloadToken();
      if (string.IsNullOrEmpty(token)) {
        throw new StorageException(
            StorageException.ErrorUnknown,
            0,
            "Unable to obtain download URL: no download token available.");
      }

      string objectUrl = GetObjectUrl(reference, path);
      // System.Uri on the .NET Standard 2.1 runtimes Unity ships preserves "%2F" in the path, so
      // no further canonicalization work-around is required here.
      return new Uri($"{objectUrl}?alt=media&token={Uri.EscapeDataString(token)}");
    }
  }
}
