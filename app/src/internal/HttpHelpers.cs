/*
 * Copyright 2025 Google LLC
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
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal {
  // Helper functions to help handling the Http calls.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class HttpHelpers {
    internal const int PooledBufferSize = 64 * 1024; // < 85,000-byte LOH threshold.
    internal static Func<HttpMessageHandler> CustomHandlerFactory { get; set; }

    internal static HttpClient CreateHttpClient(TimeSpan? timeout = null, int maxConnectionsPerServer = 16) {
      HttpMessageHandler handler = CustomHandlerFactory?.Invoke();
      bool disposeHandler = handler == null;
      if (handler == null) {
        try { if (ServicePointManager.DefaultConnectionLimit < maxConnectionsPerServer) ServicePointManager.DefaultConnectionLimit = maxConnectionsPerServer; }
        catch (Exception) {
          // Unsupported on some AOT runtimes.
        }
        var defaultHandler = new HttpClientHandler();
        try { defaultHandler.MaxConnectionsPerServer = maxConnectionsPerServer; }
        catch (Exception) {
          // MonoWebRequestHandler throws NotImplementedException; ServicePointManager covers it.
        }
        handler = defaultHandler;
      }
      var client = new HttpClient(handler, disposeHandler) { DefaultRequestHeaders = { ExpectContinue = false } };
      if (timeout.HasValue) client.Timeout = timeout.Value;
      return client;
    }

    internal static void SetDataCollectionHeaders(HttpRequestMessage request, FirebaseApp firebaseApp) {
      if (firebaseApp == null || !FirebaseInterops.GetIsDataCollectionDefaultEnabled(firebaseApp)) return;
      string appId = firebaseApp.Options?.AppId, appVer = FirebaseInterops.GetApplicationVersion();
      if (!string.IsNullOrEmpty(appId)) request.Headers.TryAddWithoutValidation("X-Firebase-AppId", appId);
      if (!string.IsNullOrEmpty(appVer)) request.Headers.TryAddWithoutValidation("X-Firebase-AppVersion", appVer);
    }

    internal static async Task SetRequestHeaders(HttpRequestMessage request, FirebaseApp firebaseApp, string authPrefix = "Firebase", bool limitedUseAppCheckTokens = false) {
      request.Headers.TryAddWithoutValidation("x-goog-api-key", firebaseApp.Options.ApiKey);
      request.Headers.TryAddWithoutValidation("x-goog-api-client", $"gl-csharp/8.0 fire/{FirebaseInterops.GetVersionInfoSdkVersion()}");
      SetDataCollectionHeaders(request, firebaseApp);
      // Add additional Firebase tokens to the header.
      await FirebaseInterops.AddFirebaseTokensAsync(request, firebaseApp, authPrefix, limitedUseAppCheckTokens).ConfigureAwait(false);
    }

    internal static async Task<string> ReadErrorBodySafeAsync(HttpResponseMessage response) {
      if (response?.Content == null) return string.Empty;
      try { return await response.Content.ReadAsStringAsync().ConfigureAwait(false) ?? string.Empty; } catch (Exception) { return string.Empty; }
    }

    // Helper function to throw an exception if the Http Response indicates failure.
    // Useful as EnsureSuccessStatusCode can leave out relevant information.
    internal static async Task ValidateHttpResponse(HttpResponseMessage response) {
      if (response.IsSuccessStatusCode) return;
      // Status code indicates failure, try to read the content for more details
      string errorContent = "No error content available.";
      if (response.Content != null) {
        try { errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
        catch (Exception readEx) {
          // Handle being unable to read the content
          errorContent = $"Failed to read error content: {readEx.Message}";
        }
      }
      // Construct the exception with as much information as possible.
      var ex = new HttpRequestException($"HTTP request failed with status code: {(int)response.StatusCode} ({response.ReasonPhrase}).\nError Content: {errorContent}", null);
      ex.Data["StatusCode"] = response.StatusCode;
      throw ex;
    }

    internal static bool IsRetryableStatusCode(int statusCode) => (statusCode >= 500 && statusCode < 600) || statusCode == 408 || statusCode == 429;
    internal static bool IsTransportError(Exception ex) => ex switch {
      null => false,
      HttpRequestException hre when !hre.GetStatusCode().HasValue => true,
      IOException or SocketException or WebException or ObjectDisposedException or OperationCanceledException or AuthenticationException or NotImplementedException => true,
      AggregateException agg when agg.InnerExceptions.Count > 0 => AllTransport(agg),
      _ => false,
    };
    private static bool AllTransport(AggregateException agg) {
      foreach (Exception inner in agg.InnerExceptions) if (!IsTransportError(inner)) return false;
      return true;
    }

    internal static async Task<long> CopyToAsync(Stream source, Stream destination, CancellationToken cancellationToken = default, int bufferSize = PooledBufferSize) {
      byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
      try {
        long total = 0;
        for (int read; (read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0; total += read) {
          await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
        }
        return total;
      } finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
  }

  // Extension to get the StatusCode from the exception.
  internal static class HttpRequestExceptionExtensions {
    internal static HttpStatusCode? GetStatusCode(this HttpRequestException exception) =>
        exception.Data.Contains("StatusCode") ? (HttpStatusCode)exception.Data["StatusCode"] : null;
  }
}
