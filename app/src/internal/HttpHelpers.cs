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
    internal static async Task SetRequestHeaders(HttpRequestMessage request, FirebaseApp firebaseApp)
    {
      request.Headers.Add("x-goog-api-key", firebaseApp.Options.ApiKey);
      string version = FirebaseInterops.GetVersionInfoSdkVersion();
      request.Headers.Add("x-goog-api-client", $"gl-csharp/8.0 fire/{version}");
      if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(firebaseApp))
      {
        request.Headers.Add("X-Firebase-AppId", firebaseApp.Options.AppId);
        request.Headers.Add("X-Firebase-AppVersion", UnityEngine.Application.version);
      }
      // Add additional Firebase tokens to the header.
      await FirebaseInterops.AddFirebaseTokensAsync(request, firebaseApp);
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
