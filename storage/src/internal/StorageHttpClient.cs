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
using System.Net.Http;
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

    private static HttpClient _httpClient = CreateDefaultHttpClient();

    internal static HttpClient Client {
      get { return _httpClient; }
      set { _httpClient = value ?? CreateDefaultHttpClient(); }
    }

    private static HttpClient CreateDefaultHttpClient() {
      return new HttpClient {
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
        FirebaseStorage storage = reference.Storage;
        FirebaseApp app = storage?.App;
        TimeSpan maxRetryTime = storage?.MaxOperationRetryTime ?? TimeSpan.FromMinutes(2);
        string url = GetObjectUrl(reference, path);

        HttpResponseMessage response = null;
        try {
          try {
            response = await HttpHelpers.SendWithExponentialBackoffAsync(
                Client,
                async () => {
                  var request = new HttpRequestMessage(HttpMethod.Get, url);
                  await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                  return request;
                },
                maxRetryTime,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
          } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
          } catch (Exception ex) {
            throw new StorageException(
                StorageException.ErrorRetryLimitExceeded,
                0,
                ex.Message);
          }

          string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
          if (!response.IsSuccessStatusCode) {
            throw StorageException.CreateFromHttpResponse((int)response.StatusCode, body);
          }

          return StorageMetadata.FromJson(body, reference);
        } finally {
          response?.Dispose();
        }
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
        FirebaseStorage storage = reference.Storage;
        FirebaseApp app = storage?.App;
        TimeSpan maxRetryTime = storage?.MaxOperationRetryTime ?? TimeSpan.FromMinutes(2);
        string url = GetObjectUrl(reference, path);
        string jsonPayload = metadata.ToJson() ?? "{}";

        HttpResponseMessage response = null;
        try {
          try {
            response = await HttpHelpers.SendWithExponentialBackoffAsync(
                Client,
                async () => {
                  var request = new HttpRequestMessage(PatchMethod, url) {
                    Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
                  };
                  await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                  return request;
                },
                maxRetryTime,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
          } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
          } catch (Exception ex) {
            throw new StorageException(
                StorageException.ErrorRetryLimitExceeded,
                0,
                ex.Message);
          }

          string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
          if (!response.IsSuccessStatusCode) {
            throw StorageException.CreateFromHttpResponse((int)response.StatusCode, body);
          }

          return StorageMetadata.FromJson(body, reference);
        } finally {
          response?.Dispose();
        }
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
        FirebaseStorage storage = reference.Storage;
        FirebaseApp app = storage?.App;
        TimeSpan maxRetryTime = storage?.MaxOperationRetryTime ?? TimeSpan.FromMinutes(2);
        string url = GetObjectUrl(reference, path);

        HttpResponseMessage response = null;
        try {
          try {
            response = await HttpHelpers.SendWithExponentialBackoffAsync(
                Client,
                async () => {
                  var request = new HttpRequestMessage(HttpMethod.Delete, url);
                  await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                  return request;
                },
                maxRetryTime,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
          } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
          } catch (Exception ex) {
            throw new StorageException(
                StorageException.ErrorRetryLimitExceeded,
                0,
                ex.Message);
          }

          if (!response.IsSuccessStatusCode) {
            string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
            throw StorageException.CreateFromHttpResponse((int)response.StatusCode, body);
          }
        } finally {
          response?.Dispose();
        }
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
        FirebaseApp app = storage?.App;
        TimeSpan maxRetryTime = storage?.MaxOperationRetryTime ?? TimeSpan.FromMinutes(2);
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

        HttpResponseMessage response = null;
        try {
          try {
            response = await HttpHelpers.SendWithExponentialBackoffAsync(
                Client,
                async () => {
                  var request = new HttpRequestMessage(HttpMethod.Get, url);
                  await HttpHelpers.SetStorageRequestHeaders(request, app).ConfigureAwait(false);
                  return request;
                },
                maxRetryTime,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken).ConfigureAwait(false);
          } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
          } catch (Exception ex) {
            throw new StorageException(
                StorageException.ErrorRetryLimitExceeded,
                0,
                ex.Message);
          }

          string body = await HttpHelpers.ReadResponseContentSafeAsync(response).ConfigureAwait(false);
          if (!response.IsSuccessStatusCode) {
            throw StorageException.CreateFromHttpResponse((int)response.StatusCode, body);
          }

          string bucket = path != null ? path.Bucket : reference.Bucket;
          return StorageListResult.FromJson(body, storage, bucket);
        } finally {
          response?.Dispose();
        }
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
      return FirebaseStorage.ConstructFormattedUri(
          $"{objectUrl}?alt=media&token={Uri.EscapeDataString(token)}");
    }
  }
}
