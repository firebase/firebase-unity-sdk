/*
 * Copyright 2018 Google LLC
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
using System.Threading.Tasks;
using System.Net.Http;
using Firebase.Functions.Internal;
using Firebase.Internal;
using System.Text;

namespace Firebase.Functions
{
  /// <summary>Represents a reference to a Google Cloud Functions HTTPS callable function.</summary>
  /// <remarks>
  ///   Represents a reference to a Google Cloud Functions HTTPS callable function.
  ///   (see <a href="https://cloud.google.com/functions/">Google Cloud Functions</a>)
  /// </remarks>
  public sealed class HttpsCallableReference
  {
    // Functions object this reference was created from.
    private readonly FirebaseFunctions _firebaseFunctions;
    private readonly string _url;
    /// <summary>
    /// Construct a wrapper around the HttpsCallableReferenceInternal object.
    /// </summary>
    internal HttpsCallableReference(FirebaseFunctions functions, string url)
    {
      _firebaseFunctions = functions;
      _url = url;
    }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   service which created this reference.
    /// </summary>
    /// <returns>
    ///   The
    ///   <see cref="FirebaseFunctions" />
    ///   service.
    /// </returns>
    public FirebaseFunctions Functions { get { return _firebaseFunctions; } }

    /// <summary>
    ///   ...
    /// </summary>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   with the result of the function call.
    /// </returns>
    public Task<HttpsCallableResult> CallAsync()
    {
      return CallAsync(null);
    }

    /// <summary>
    ///   ...
    /// </summary>
    /// <param name="data">The data to pass to the function.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   with the result of the function call.
    /// </returns>
    public Task<HttpsCallableResult> CallAsync(object data)
    {
      return InternalCallAsync(data);
    }

    private StringContent MakeFunctionsRequest(object data)
    {
      var encodedData = FunctionsSerializer.Serialize(data);
      return new StringContent(encodedData, Encoding.UTF8, "application/json");
    }

    private async Task<HttpsCallableResult> InternalCallAsync(object data)
    {
      HttpRequestMessage request = new(HttpMethod.Post, _url);
      // Functions uses Bearer tokens for authentication.
      // This is different from the default Firebase token prefix used by other Firebase services.
      await HttpHelpers.SetRequestHeaders(request, _firebaseFunctions.App, "Bearer");
      request.Content = MakeFunctionsRequest(data);

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Request:\n" + request.Content);
#endif
      // TODO pipe through cancellation tokens
      var response = await _firebaseFunctions.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
      if (!response.IsSuccessStatusCode)
      {
        string errorBody = "";
        if (response.Content != null)
        {
          try
          {
            errorBody = await response.Content.ReadAsStringAsync();
          }
          catch (Exception e)
          {
            UnityEngine.Debug.LogWarning("Failed to read error response: " + e.Message);
          }
        }
        throw FunctionsErrorParser.ParseError(response, errorBody);
      }

      string result = await response.Content.ReadAsStringAsync();

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Response:\n" + result);
#endif
      var responseData = FunctionsSerializer.Deserialize(result);
      return new HttpsCallableResult(responseData);
    }
  }
}
