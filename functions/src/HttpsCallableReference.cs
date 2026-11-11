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
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Functions.Internal;
using Firebase.Internal;

namespace Firebase.Functions
{
  /// <summary>Represents a reference to a Google Cloud Functions HTTPS callable function.</summary>
  /// <remarks>
  ///   Represents a reference to a Google Cloud Functions HTTPS callable function.
  ///   (see <a href="https://cloud.google.com/functions/">Google Cloud Functions</a>)
  /// </remarks>
  public sealed class HttpsCallableReference
  {
    private readonly FirebaseFunctions _firebaseFunctions;
    private readonly string _url;
    private readonly HttpsCallableOptions _options;

    internal HttpsCallableReference(FirebaseFunctions functions, string url, HttpsCallableOptions options = null)
    {
      _firebaseFunctions = functions;
      _url = url;
      _options = options;
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
    ///   Calls the HTTPS callable function with no arguments.
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
    ///   Calls the HTTPS callable function with the specified data.
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

    private static StringContent MakeFunctionsRequest(object data)
    {
      var encodedData = FunctionsSerializer.Serialize(data);
      return new StringContent(encodedData, Encoding.UTF8, "application/json");
    }

    private async Task<HttpsCallableResult> InternalCallAsync(object data)
    {
      using HttpRequestMessage request = new(HttpMethod.Post, _url);
      bool limitedUseAppCheckTokens = _options != null && _options.LimitedUseAppCheckTokens;
      await HttpHelpers.SetRequestHeaders(request, _firebaseFunctions.App, "Bearer", limitedUseAppCheckTokens).ConfigureAwait(false);
      request.Content = MakeFunctionsRequest(data);

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Request:\n" + request.Content);
#endif
      using var response = await _firebaseFunctions.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        string errorBody = await HttpHelpers.ReadErrorBodySafeAsync(response).ConfigureAwait(false);
        throw FunctionsErrorParser.ParseError(response, errorBody);
      }

      if (response.Content == null)
      {
        throw new FunctionsException(FunctionsErrorCode.Internal, "Response content is null.");
      }

      string result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Response:\n" + result);
#endif
      var responseData = FunctionsSerializer.Deserialize(result);
      return new HttpsCallableResult(responseData);
    }

    /// <summary>
    /// Calls the function and returns a stream of responses.
    /// </summary>
    /// <returns>An IAsyncEnumerable yielding StreamResponse objects.</returns>
    public IAsyncEnumerable<StreamResponse> StreamAsync(CancellationToken cancellationToken = default)
    {
      return StreamAsync(null, cancellationToken);
    }

    /// <summary>
    /// Calls the function with data and returns a stream of responses.
    /// </summary>
    /// <param name="data">The data to pass to the function.</param>
    /// <returns>An IAsyncEnumerable yielding StreamResponse objects.</returns>
    public IAsyncEnumerable<StreamResponse> StreamAsync(object data, CancellationToken cancellationToken = default)
    {
      return InternalStreamAsync(data, cancellationToken);
    }

    private async IAsyncEnumerable<StreamResponse> InternalStreamAsync(object data, CancellationToken originalToken, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(originalToken, cancellationToken);
      var linkedToken = cts.Token;

      using HttpRequestMessage request = new(HttpMethod.Post, _url);
      bool limitedUseAppCheckTokens = _options != null && _options.LimitedUseAppCheckTokens;
      await HttpHelpers.SetRequestHeaders(request, _firebaseFunctions.App, "Bearer", limitedUseAppCheckTokens).ConfigureAwait(false);
      request.Content = MakeFunctionsRequest(data);
      request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Request:\n" + request.Content);
#endif
      using var response = await _firebaseFunctions.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        string errorBody = await HttpHelpers.ReadErrorBodySafeAsync(response).ConfigureAwait(false);
        throw FunctionsErrorParser.ParseError(response, errorBody);
      }

      if (response.Content == null)
      {
        throw new FunctionsException(FunctionsErrorCode.Internal, "Response content is null.");
      }

      await foreach (string jsonText in SseLineReader.ReadDataFramesAsync(response, linkedToken, multiLineEvents: true).ConfigureAwait(false))
      {
#if FIREBASE_LOG_REST_CALLS
        UnityEngine.Debug.Log("Streaming Response:\n" + jsonText);
#endif
        yield return FunctionsSerializer.DeserializeStreamResponse(jsonText);
      }
    }
  }
}
