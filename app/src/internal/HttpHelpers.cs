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
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Internal
{
  // Helper functions to help handling the Http calls.
  internal static class HttpHelpers
  {
    /// <summary>
    /// Creates the shared platform-native <see cref="HttpMessageHandler" /> backed by
    /// <c>firebase-cpp-sdk</c>'s <c>firebase_http_transport</c> (<c>libcurl</c> on Desktop,
    /// <c>NSURLSession</c> on iOS/tvOS, and JNI <c>HttpURLConnection</c> on Android), used by
    /// Storage, Functions, and Firebase AI Logic.
    ///
    /// See <see cref="FirebaseHttpMessageHandler" /> for why pure-C# Firebase Unity products use
    /// the native transport instead of managed <c>UnityTlsStream</c> or
    /// <c>UnityEngine.Networking</c>, and when to revisit once Unity 7+ (CoreCLR / .NET 10+ BCL)
    /// becomes the minimum supported runtime.
    /// </summary>
    internal static HttpMessageHandler CreateHttpMessageHandler()
    {
      return new FirebaseHttpMessageHandler();
    }

    /// <summary>
    /// Creates an <see cref="HttpClient" /> configured with the shared native
    /// <see cref="FirebaseHttpMessageHandler" /> and ExpectContinue disabled.
    /// </summary>
    internal static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
      var client = new HttpClient(CreateHttpMessageHandler(), disposeHandler: true);
      if (timeout.HasValue)
      {
        client.Timeout = timeout.Value;
      }
      client.DefaultRequestHeaders.ExpectContinue = false;
      return client;
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

    // Helper function to throw an exception if the Http Response indicates failure.
    // Useful as EnsureSuccessStatusCode can leave out relevant information.
    internal static async Task ValidateHttpResponse(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
      if (response.IsSuccessStatusCode)
      {
        return;
      }

      // Status code indicates failure, try to read the content for more details.
      // Register response disposal on the cancellation token so stalled non-2xx
      // error bodies on ResponseHeadersRead requests abort promptly.
      string errorContent = "No error content available.";
      if (response.Content != null)
      {
        using var registration = cancellationToken.Register(() => response.Dispose());
        try
        {
          errorContent = await response.Content.ReadAsStringAsync();
        }
        catch (Exception readEx)
        {
          cancellationToken.ThrowIfCancellationRequested();
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

    // Reads a line from a streaming response reader, translating any stream-disposal
    // exception triggered by cancellation registration into OperationCanceledException.
    internal static async Task<string> ReadLineWithCancellationAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
      try
      {
        return await reader.ReadLineAsync();
      }
      catch (Exception) when (cancellationToken.IsCancellationRequested)
      {
        cancellationToken.ThrowIfCancellationRequested();
        throw;
      }
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
