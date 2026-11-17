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
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Firebase.Internal
{
  // Helper functions to help handling the Http calls.
  internal static class HttpHelpers
  {
    private const int DefaultMaxConnectionsPerServer = 16;

    /// <summary>
    /// Creates the common platform-native <see cref="HttpMessageHandler" /> shared across pure-C#
    /// Firebase Unity libraries (Storage, Functions, Firebase AI Logic).
    ///
    /// TODO(Unity 6000.6+): Revisit replacing <see cref="FirebaseHttpMessageHandler" /> once the
    /// minimum supported Unity version reaches Unity 6000.6+ (specifically 6000.6.2f1+ with
    /// UUM-150583 fixed in UnityHttpMessageHandler, or CoreCLR).
    /// </summary>
    internal static HttpMessageHandler CreateHttpMessageHandler(
        int maxConnectionsPerServer = DefaultMaxConnectionsPerServer)
    {
      return new FirebaseHttpMessageHandler(maxConnectionsPerServer);
    }

    /// <summary>
    /// Creates an <see cref="HttpClient" /> configured with the shared native
    /// <see cref="FirebaseHttpMessageHandler" /> and ExpectContinue disabled.
    /// </summary>
    internal static HttpClient CreateHttpClient(
        TimeSpan? timeout = null,
        int maxConnectionsPerServer = DefaultMaxConnectionsPerServer)
    {
      var client = new HttpClient(CreateHttpMessageHandler(maxConnectionsPerServer), disposeHandler: true);
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
