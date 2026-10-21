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
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal
{
  // Helper functions to help handling the Http calls.
  internal static class HttpHelpers
  {
    private const int InitialBackoffMs = 1000;
    private const int MaxBackoffMs = 30000;
    private const int MaxJitterMs = 250;
    private static readonly Random _jitterRandom = new Random();
    private static readonly object _jitterLock = new object();

    private static int GetRandomJitterMs()
    {
      lock (_jitterLock)
      {
        return _jitterRandom.Next(MaxJitterMs);
      }
    }
    internal static async Task SetRequestHeaders(HttpRequestMessage request, FirebaseApp firebaseApp, string authPrefix = "Firebase", bool limitedUseAppCheckTokens = false)
    {
      request.Headers.Add("x-goog-api-key", firebaseApp.Options.ApiKey);
      string version = FirebaseInterops.GetVersionInfoSdkVersion();
      request.Headers.Add("x-goog-api-client", $"gl-csharp/8.0 fire/{version}");
      if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(firebaseApp))
      {
        request.Headers.Add("X-Firebase-AppId", firebaseApp.Options.AppId);
        request.Headers.Add("X-Firebase-AppVersion", FirebaseInterops.GetApplicationVersion());
      }
      // Add additional Firebase tokens to the header.
      await FirebaseInterops.AddFirebaseTokensAsync(request, firebaseApp, authPrefix, limitedUseAppCheckTokens);
    }

    // Sets the standard Firebase Storage REST headers on an HttpRequestMessage.
    // Note: Firebase Storage backend rejects 'x-goog-api-client' (b/74440917) and requires
    // 'X-Firebase-Storage-Version' instead.
    internal static async Task SetStorageRequestHeaders(
        HttpRequestMessage request,
        FirebaseApp firebaseApp,
        bool limitedUseAppCheckTokens = false)
    {
      string version = FirebaseInterops.GetVersionInfoSdkVersion();
      request.Headers.TryAddWithoutValidation("X-Firebase-Storage-Version", $"Unity/{version}");
      if (firebaseApp != null)
      {
        string appId = firebaseApp.Options?.AppId;
        if (!string.IsNullOrEmpty(appId))
        {
          request.Headers.TryAddWithoutValidation("x-firebase-gmpid", appId);
        }
        if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(firebaseApp))
        {
          if (!string.IsNullOrEmpty(appId))
          {
            request.Headers.TryAddWithoutValidation("X-Firebase-AppId", appId);
          }
          string appVersion = FirebaseInterops.GetApplicationVersion();
          if (!string.IsNullOrEmpty(appVersion))
          {
            request.Headers.TryAddWithoutValidation("X-Firebase-AppVersion", appVersion);
          }
        }
        await FirebaseInterops.AddFirebaseTokensAsync(
            request, firebaseApp, "Firebase", limitedUseAppCheckTokens);
      }
    }

    // Returns true if the HTTP status code represents a transient / retryable server or rate-limit error.
    internal static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
      int code = (int)statusCode;
      return (code >= 500 && code < 600) ||
             statusCode == HttpStatusCode.RequestTimeout ||
             code == 429;
    }

    // Safely reads the response body as a string, returning an empty string on failure or null content.
    internal static async Task<string> ReadResponseContentSafeAsync(HttpResponseMessage response)
    {
      if (response?.Content == null)
      {
        return string.Empty;
      }
      try
      {
        return await response.Content.ReadAsStringAsync() ?? string.Empty;
      }
      catch (Exception)
      {
        return string.Empty;
      }
    }

    // Sends an HTTP request created by requestFactory with non-blocking exponential backoff and jitter,
    // bounded by maxRetryTime. Treats offline/network exceptions as retryable with flat 1s polling.
    internal static async Task<HttpResponseMessage> SendWithExponentialBackoffAsync(
        HttpClient client,
        Func<Task<HttpRequestMessage>> requestFactory,
        TimeSpan maxRetryTime,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
      var stopwatch = Stopwatch.StartNew();
      int currentSleepMs = InitialBackoffMs;
      Exception lastNetworkException = null;

      while (true)
      {
        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response = null;
        bool networkUnavailable = false;
        lastNetworkException = null;

        try
        {
          using (var request = await requestFactory().ConfigureAwait(false))
          {
            response = await client.SendAsync(request, completionOption, cancellationToken)
                .ConfigureAwait(false);
          }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   ex is IOException ||
                                   ex is SocketException ||
                                   ex is OperationCanceledException)
        {
          lastNetworkException = ex;
          networkUnavailable = true;
        }

        if (response != null)
        {
          int code = (int)response.StatusCode;
          // 2xx or 308 (GCS Resume Incomplete) are successful responses.
          if ((code >= 200 && code < 300) || code == 308)
          {
            return response;
          }

          if (!IsRetryableStatusCode(response.StatusCode))
          {
            return response;
          }
        }

        int delayMs = currentSleepMs + GetRandomJitterMs();
        if (stopwatch.Elapsed + TimeSpan.FromMilliseconds(delayMs) > maxRetryTime)
        {
          if (response != null)
          {
            return response;
          }
          throw lastNetworkException ?? new HttpRequestException("Network request timed out after retry deadline.");
        }

        response?.Dispose();
        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

        if (networkUnavailable)
        {
          currentSleepMs = InitialBackoffMs;
        }
        else if (currentSleepMs < MaxBackoffMs)
        {
          currentSleepMs = Math.Min(currentSleepMs * 2, MaxBackoffMs);
        }
      }
    }

    // Helper function to throw an exception if the Http Response indicates failure.
    // Useful as EnsureSuccessStatusCode can leave out relevant information.
    internal static async Task ValidateHttpResponse(HttpResponseMessage response)
    {
      if (response.IsSuccessStatusCode)
      {
        return;
      }

      // Status code indicates failure, try to read the content for more details
      string errorContent = "No error content available.";
      if (response.Content != null)
      {
        try
        {
          errorContent = await response.Content.ReadAsStringAsync();
        }
        catch (Exception readEx)
        {
          // Handle being unable to read the content
          errorContent = $"Failed to read error content: {readEx.Message}";
        }
      }

      // Construct the exception with as much information as possible.
      var ex = new HttpRequestException(
        $"HTTP request failed with status code: {(int)response.StatusCode} ({response.ReasonPhrase}).\n" +
        $"Error Content: {errorContent}",
        null
      );
      ex.Data["StatusCode"] = response.StatusCode;

      throw ex;
    }
  }

  // Extension to get the StatusCode from the exception.
  internal static class HttpRequestExceptionExtensions
  {
    internal static HttpStatusCode? GetStatusCode(this HttpRequestException exception)
    {
      if (exception.Data.Contains("StatusCode"))
      {
        return (HttpStatusCode)exception.Data["StatusCode"];
      }
      return null;
    }
  }
}
