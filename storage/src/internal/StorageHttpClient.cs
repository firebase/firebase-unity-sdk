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
  /// Shared HTTP client and REST command operations for Firebase Storage.
  /// </summary>
  internal static class StorageHttpClient {
    private static readonly HttpMethod PatchMethod = new HttpMethod("PATCH");

    // Upper bound on simultaneous connections to the Storage host. The scheduler allows up to
    // 2 uploads + 3 downloads + 5 commands in flight, so this must exceed 10 or commands queue
    // behind long transfers (Mono's default per-host limit is 2).
    private const int MaxConnectionsPerServer = 16;

    private static readonly HttpClient _httpClient = CreateDefaultHttpClient();

    internal static HttpClient Client {
      get { return _httpClient; }
    }

    private static HttpClient CreateDefaultHttpClient() {
      try {
        if (ServicePointManager.DefaultConnectionLimit < MaxConnectionsPerServer) {
          ServicePointManager.DefaultConnectionLimit = MaxConnectionsPerServer;
        }
      } catch (Exception) {
        // Ignore on runtimes where ServicePointManager is unsupported.
      }
      var handler = new HttpClientHandler();
      try {
        handler.MaxConnectionsPerServer = MaxConnectionsPerServer;
      } catch (Exception) {
        // MonoWebRequestHandler (Windows Mono / iOS IL2CPP) throws NotImplementedException;
        // other runtimes may throw PlatformNotSupportedException. ServicePointManager fallback
        // above covers MonoWebRequestHandler.
      }
      // Per-attempt stall detection is handled by StallWatchdog (see StorageTask.RunAttemptAsync)
      // so the client-wide timeout is disabled.
      var client = new HttpClient(handler) {
        Timeout = Timeout.InfiniteTimeSpan
      };
      // Mono defaults to "Expect: 100-continue", which costs a round trip on every upload chunk.
      client.DefaultRequestHeaders.ExpectContinue = false;
      return client;
    }

    // Sets the standard Firebase Storage REST headers on an HttpRequestMessage.
    // Note: Firebase Storage backend rejects 'x-goog-api-client' (b/74440917) and requires
    // 'X-Firebase-Storage-Version' instead.
    internal static async Task SetStorageRequestHeaders(
        HttpRequestMessage request,
        FirebaseApp firebaseApp,
        bool limitedUseAppCheckTokens = false) {
      string version = FirebaseInterops.GetVersionInfoSdkVersion();
      request.Headers.TryAddWithoutValidation("X-Firebase-Storage-Version", $"Unity/{version}");
      if (firebaseApp != null) {
        string appId = firebaseApp.Options?.AppId;
        if (!string.IsNullOrEmpty(appId)) {
          request.Headers.TryAddWithoutValidation("x-firebase-gmpid", appId);
        }
        if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(firebaseApp)) {
          if (!string.IsNullOrEmpty(appId)) {
            request.Headers.TryAddWithoutValidation("X-Firebase-AppId", appId);
          }
          string appVersion = FirebaseInterops.GetApplicationVersion();
          if (!string.IsNullOrEmpty(appVersion)) {
            request.Headers.TryAddWithoutValidation("X-Firebase-AppVersion", appVersion);
          }
        }
        await FirebaseInterops.AddFirebaseTokensAsync(
            request, firebaseApp, "Firebase", limitedUseAppCheckTokens).ConfigureAwait(false);
      }
    }

    // Returns true if the HTTP status code represents a transient / retryable server or rate-limit error.
    internal static bool IsRetryableStatusCode(int statusCode) {
      return (statusCode >= 500 && statusCode < 600) ||
             statusCode == (int)HttpStatusCode.RequestTimeout ||
             statusCode == 429;
    }

    // Returns true for 2xx, or 308 "Resume Incomplete", which the resumable upload protocol uses
    // to acknowledge a chunk or a session query.
    internal static bool IsSuccessOrResumeIncomplete(int statusCode) {
      return (statusCode >= 200 && statusCode < 300) || statusCode == 308;
    }

    // Returns the first value of a response or content header, or null if it is absent.
    internal static string GetHeaderFirstValue(HttpResponseMessage response, string headerName) {
      if (response?.Headers != null &&
          response.Headers.TryGetValues(headerName, out IEnumerable<string> values)) {
        return values.FirstOrDefault();
      }
      if (response?.Content?.Headers != null &&
          response.Content.Headers.TryGetValues(headerName, out IEnumerable<string> contentValues)) {
        return contentValues.FirstOrDefault();
      }
      return null;
    }

    // Safely reads the response body as a string, returning an empty string on failure or null content.
    internal static async Task<string> ReadResponseContentSafeAsync(HttpResponseMessage response) {
      if (response?.Content == null) {
        return string.Empty;
      }
      try {
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty;
      } catch (Exception) {
        return string.Empty;
      }
    }

    /// <summary>
    /// Builds the <see cref="StorageException" /> for a non-success <paramref name="response" />
    /// from its status code and error body, and disposes the response.
    /// </summary>
    internal static async Task<StorageException> CreateExceptionAsync(HttpResponseMessage response) {
      int statusCode = (int)response.StatusCode;
      string errorBody = await ReadResponseContentSafeAsync(response).ConfigureAwait(false);
      response.Dispose();
      return StorageException.CreateFromHttpResponse(statusCode, errorBody);
    }

    /// <summary>
    /// Sends the request built by <paramref name="requestFactory" /> on behalf of
    /// <paramref name="task" />, retrying within <paramref name="backoff" /> (see
    /// <see cref="StorageTask.RunAttemptAsync" /> for the retry, pause and cancellation rules).
    /// Any response other than 2xx / 308 is converted to a <see cref="StorageException" />. The
    /// caller owns the returned response.
    /// </summary>
    internal static async Task<HttpResponseMessage> SendAsync(
        StorageTask task,
        Func<Task<HttpRequestMessage>> requestFactory,
        RetryBackoff backoff,
        HttpCompletionOption completionOption,
        ModuleLogger logger,
        string description) {
      HttpResponseMessage response = null;
      while (!await task.RunAttemptAsync(
          async watchdog => {
            using (HttpRequestMessage request = await requestFactory().ConfigureAwait(false)) {
              response = await Client.SendAsync(request, completionOption, watchdog.Token)
                  .ConfigureAwait(false);
            }
            if (!IsSuccessOrResumeIncomplete((int)response.StatusCode)) {
              throw await CreateExceptionAsync(response).ConfigureAwait(false);
            }
          },
          backoff,
          logger,
          description).ConfigureAwait(false)) {
      }
      return response;
    }


    /// <summary>
    /// Throws if no bucket is configured for the reference; every REST URL needs one.
    /// </summary>
    internal static void EnsureBucket(StoragePath resolved, string operation) {
      if (string.IsNullOrEmpty(resolved.Bucket)) {
        throw new StorageException(
            StorageException.ErrorBucketNotFound,
            0,
            $"{operation} failed: no Cloud Storage bucket is configured. Set " +
            "AppOptions.StorageBucket or use FirebaseStorage.GetInstance(\"gs://<bucket>\").");
      }
    }

    /// <summary>
    /// Throws if the reference does not identify a single object (i.e. it is the bucket root or
    /// no bucket is configured). Object-level operations are invalid on such references and
    /// would otherwise be sent to the bucket's collection endpoint.
    /// </summary>
    internal static void EnsureObjectReference(StorageReference reference, string operation) {
      StoragePath resolved = reference.StoragePath;
      EnsureBucket(resolved, operation);
      if (resolved.IsRoot) {
        throw new StorageException(
            StorageException.ErrorUnknown,
            0,
            $"{operation} cannot be performed on the root of a bucket. Use a child reference " +
            "such as storage.GetReference(\"path/to/object\").");
      }
    }

    internal static string GetBucketObjectsUrl(StorageReference reference) {
      FirebaseStorage storage = reference.Storage;
      return reference.StoragePath.GetHttpBucketObjectsUrl(
          storage?.EmulatorHost, storage?.EmulatorPort ?? 0);
    }

    internal static string GetObjectUrl(StorageReference reference) {
      FirebaseStorage storage = reference.Storage;
      return reference.StoragePath.GetHttpObjectUrl(
          storage?.EmulatorHost, storage?.EmulatorPort ?? 0);
    }

    /// <summary>
    /// True if <paramref name="ex" /> is a transport-level failure that a retry loop should absorb:
    /// connection reset/refused, DNS failure, stalled or aborted request, or the HTTP stack tearing
    /// down a stream. Mono's <c>MonoWebRequestHandler</c> (Windows Mono, iOS/Android IL2CPP) reports
    /// an aborted read as <see cref="WebException" />, sometimes wrapped in an
    /// <see cref="AggregateException" />, rather than the <see cref="OperationCanceledException" /> /
    /// <see cref="IOException" /> that SocketsHttpHandler raises, so those are classified here too.
    /// Local I/O (the caller's stream or file) is read and written outside the retry step, so
    /// its errors propagate unchanged and are never retried.
    /// </summary>
    internal static bool IsTransportException(Exception ex) {
      switch (ex) {
        case null:
          return false;
        case HttpRequestException _:
        case IOException _:
        case SocketException _:
        case WebException _:
        case ObjectDisposedException _:
        case OperationCanceledException _:
        // A TLS failure in the middle of a transfer (corrupted record, renegotiation refused by a
        // middlebox) kills the connection; the next attempt opens a new one.
        case System.Security.Authentication.AuthenticationException _:
          return true;
        case AggregateException agg:
          if (agg.InnerExceptions.Count == 0) {
            return false;
          }
          foreach (Exception inner in agg.InnerExceptions) {
            if (!IsTransportException(inner)) {
              return false;
            }
          }
          return true;
        default:
          return false;
      }
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
      var backoff = new RetryBackoff(
          storage?.MaxOperationRetryTime ?? FirebaseStorage.DefaultMaxOperationRetryTime);
      ModuleLogger logger = reference.Logger;
      string description = $"{method.Method} {url}";

      logger.LogMessage(LogLevel.Debug, description);
      using (var task = new StorageTask(cancellationToken, storage?.DisposeToken ?? default))
      using (HttpResponseMessage response = await SendAsync(
          task,
          async () => {
            var request = new HttpRequestMessage(method, url);
            if (contentFactory != null) {
              request.Content = contentFactory();
            }
            await SetStorageRequestHeaders(request, app).ConfigureAwait(false);
            return request;
          },
          backoff,
          HttpCompletionOption.ResponseContentRead,
          logger,
          description).ConfigureAwait(false)) {
        logger.LogMessage(LogLevel.Debug, $"{description} -> {(int)response.StatusCode}");
        return readBody
            ? await ReadResponseContentSafeAsync(response).ConfigureAwait(false)
            : null;
      }
    }

    internal static Task<StorageMetadata> GetMetadataAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, "GetMetadata");
        string body = await SendCommandAsync(
            reference,
            HttpMethod.Get,
            GetObjectUrl(reference),
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
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckNotNull(metadataChange, nameof(metadataChange));
      StorageMetadata metadata = MetadataChange.Build(metadataChange);
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, "UpdateMetadata");
        string jsonPayload = metadata.ToJson() ?? "{}";
        string body = await SendCommandAsync(
            reference,
            PatchMethod,
            GetObjectUrl(reference),
            () => new StringContent(jsonPayload, Encoding.UTF8, "application/json"),
            readBody: true,
            cancellationToken).ConfigureAwait(false);
        return StorageMetadata.FromJson(body, reference);
      }, cancellationToken);
    }

    internal static Task DeleteAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      return StorageTaskScheduler.RunCommandAsync(async () => {
        EnsureObjectReference(reference, "Delete");
        await SendCommandAsync(
            reference,
            HttpMethod.Delete,
            GetObjectUrl(reference),
            null,
            readBody: false,
            cancellationToken).ConfigureAwait(false);
        return true;
      }, cancellationToken);
    }

    internal static Task<StorageListResult> ListAsync(
        StorageReference reference,
        int maxResults = 1000,
        string pageToken = null,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      Preconditions.CheckArgument(
          maxResults > 0 && maxResults <= 1000, "maxResults must be between 1 and 1000");
      return StorageTaskScheduler.RunCommandAsync(async () => {
        StoragePath resolved = reference.StoragePath;
        EnsureBucket(resolved, "List");
        var queryParams = new List<string> {
          "delimiter=%2F"
        };
        if (!resolved.IsRoot) {
          queryParams.Add($"prefix={StoragePath.EscapePath(resolved.ObjectPath.Trim('/') + "/")}");
        }
        queryParams.Add($"maxResults={maxResults}");
        if (!string.IsNullOrEmpty(pageToken)) {
          queryParams.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        }

        string url = $"{GetBucketObjectsUrl(reference)}?{string.Join("&", queryParams.ToArray())}";
        string body = await SendCommandAsync(
            reference,
            HttpMethod.Get,
            url,
            null,
            readBody: true,
            cancellationToken).ConfigureAwait(false);
        return StorageListResult.FromJson(body, reference.Storage, resolved.Bucket);
      }, cancellationToken);
    }

    internal static async Task<Uri> GetDownloadUrlAsync(
        StorageReference reference,
        CancellationToken cancellationToken = default) {
      Preconditions.CheckNotNull(reference, nameof(reference));
      EnsureObjectReference(reference, "GetDownloadUrl");
      StorageMetadata metadata = await GetMetadataAsync(reference, cancellationToken)
          .ConfigureAwait(false);
      string token = metadata?.GetFirstDownloadToken();
      if (string.IsNullOrEmpty(token)) {
        throw new StorageException(
            StorageException.ErrorUnknown,
            0,
            "Unable to obtain download URL: no download token available.");
      }

      string objectUrl = GetObjectUrl(reference);
      // System.Uri on the .NET Standard 2.1 runtimes Unity ships preserves "%2F" in the path, so
      // no further canonicalization work-around is required here.
      return new Uri($"{objectUrl}?alt=media&token={Uri.EscapeDataString(token)}");
    }
  }
}
