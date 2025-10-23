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
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.MiniJSON;
using Firebase.AI.Internal;
using System.Linq;
using System.Runtime.CompilerServices;
using System.IO;

namespace Firebase.AI
{
  using GenContentFunc = Func<string, IDictionary<string, object>,
        IEnumerable<ModelContent>, CancellationToken,
        Task<GenerateContentResponse>>;
  using StreamContentFunc = Func<string, IDictionary<string, object>,
        IEnumerable<ModelContent>, CancellationToken,
        IAsyncEnumerable<GenerateContentResponse>>;

  /// <summary>
  /// A type that represents a remote multimodal model (like Gemini), with the ability to generate
  /// content based on defined server prompt templates.
  /// </summary>
  public class TemplateGenerativeModel
  {
    private readonly FirebaseApp _firebaseApp;
    private readonly FirebaseAI.Backend _backend;

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Intended for internal use only.
    /// Use `FirebaseAI.GetTemplateGenerativeModel` instead to ensure proper
    /// initialization and configuration of the `TemplateGenerativeModel`.
    /// </summary>
    internal TemplateGenerativeModel(FirebaseApp firebaseApp,
                                     FirebaseAI.Backend backend,
                                     RequestOptions? requestOptions = null)
    {
      _firebaseApp = firebaseApp;
      _backend = backend;

      // Create a HttpClient using the timeout requested, or the default one.
      _httpClient = new HttpClient()
      {
        Timeout = requestOptions?.Timeout ?? RequestOptions.DefaultTimeout
      };
    }

    /// <summary>
    /// Generates new content by calling into a server prompt template.
    /// </summary>
    /// <param name="templateId">The id of the server prompt template to use.</param>
    /// <param name="inputs">Any input parameters expected by the server prompt template.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>The generated content response from the model.</returns>
    /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
    public Task<GenerateContentResponse> GenerateContentAsync(
        string templateId, IDictionary<string, object> inputs,
        CancellationToken cancellationToken = default)
    {
      return GenerateContentAsyncInternal(templateId, inputs, null, cancellationToken);
    }

    /// <summary>
    /// Generates new content as a stream by calling into a server prompt template.
    /// </summary>
    /// <param name="templateId">The id of the server prompt template to use.</param>
    /// <param name="inputs">Any input parameters expected by the server prompt template.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>A stream of generated content responses from the model.</returns>
    /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
    public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
            string templateId, IDictionary<string, object> inputs,
            CancellationToken cancellationToken = default)
    {
      return GenerateContentStreamAsyncInternal(templateId, inputs, null, cancellationToken);
    }

    /// <summary>
    /// Creates a new templated chat conversation using this model with the provided history.
    /// </summary>
    /// <param name="templateId">The id of the server prompt template to use.</param>
    /// <param name="history">Initial content history to start with.</param>
    public TemplateChatSession StartChat(string templateId, IEnumerable<ModelContent> history = null)
    {
      return new TemplateChatSession(
          GenerateContentAsyncInternal, GenerateContentStreamAsyncInternal,
          templateId, history);
    }

    private string MakeGenerateContentRequest(IDictionary<string, object> inputs,
        IEnumerable<ModelContent> chatHistory)
    {
      var jsonDict = new Dictionary<string, object>()
      {
        ["inputs"] = inputs
      };
      if (chatHistory != null)
      {
        jsonDict["history"] = chatHistory.Select(t => t.ToJson()).ToList();
      }
      return Json.Serialize(jsonDict);
    }

    private async Task<GenerateContentResponse> GenerateContentAsyncInternal(
        string templateId, IDictionary<string, object> inputs,
        IEnumerable<ModelContent> chatHistory,
        CancellationToken cancellationToken)
    {
      HttpRequestMessage request = new(HttpMethod.Post,
          HttpHelpers.GetTemplateURL(_firebaseApp, _backend, templateId) + ":templateGenerateContent");

      // Set the request headers
      await HttpHelpers.SetRequestHeaders(request, _firebaseApp);

      // Set the content
      string bodyJson = MakeGenerateContentRequest(inputs, chatHistory);
      request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Request:\n" + bodyJson);
#endif

      var response = await _httpClient.SendAsync(request, cancellationToken);
      await HttpHelpers.ValidateHttpResponse(response);

      string result = await response.Content.ReadAsStringAsync();

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Response:\n" + result);
#endif

      return GenerateContentResponse.FromJson(result, _backend.Provider);
    }

    private async IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsyncInternal(
        string templateId, IDictionary<string, object> inputs,
        IEnumerable<ModelContent> chatHistory,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      HttpRequestMessage request = new(HttpMethod.Post,
          HttpHelpers.GetTemplateURL(_firebaseApp, _backend, templateId) + ":templateStreamGenerateContent");

      // Set the request headers
      await HttpHelpers.SetRequestHeaders(request, _firebaseApp);

      // Set the content
      string bodyJson = MakeGenerateContentRequest(inputs, chatHistory);
      request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

#if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log("Request:\n" + bodyJson);
#endif

      var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      await HttpHelpers.ValidateHttpResponse(response);

      // We are expecting a Stream as the response, so handle that.
      using var stream = await response.Content.ReadAsStreamAsync();
      using var reader = new StreamReader(stream);

      string line;
      while ((line = await reader.ReadLineAsync()) != null)
      {
        // Only pass along strings that begin with the expected prefix.
        if (line.StartsWith(HttpHelpers.StreamPrefix))
        {
#if FIREBASE_LOG_REST_CALLS
          UnityEngine.Debug.Log("Streaming Response:\n" + line);
#endif

          yield return GenerateContentResponse.FromJson(line[HttpHelpers.StreamPrefix.Length..], _backend.Provider);
        }
      }
    }
  }

  /// <summary>
  /// An object that represents a back-and-forth chat with a model using a server prompt template,
  /// capturing the history and saving the context in memory between each message sent.
  /// </summary>
  public class TemplateChatSession
  {
    private readonly GenContentFunc _genContentFunc;
    private readonly StreamContentFunc _streamContentFunc;
    private readonly string _templateId;
    private readonly List<ModelContent> _chatHistory;

    /// <summary>
    /// The previous content from the chat that has been successfully sent and received from the
    /// model. This will be provided to the model for each message sent as context for the discussion.
    /// </summary>
    public IReadOnlyList<ModelContent> History => _chatHistory;

    internal TemplateChatSession(GenContentFunc genContentFunc, StreamContentFunc streamContentFunc,
        string templateId, IEnumerable<ModelContent> initialHistory)
    {
      _genContentFunc = genContentFunc;
      _streamContentFunc = streamContentFunc;
      _templateId = templateId;
      if (initialHistory != null)
      {
        _chatHistory = new List<ModelContent>(initialHistory);
      }
      else
      {
        _chatHistory = new List<ModelContent>();
      }
    }

    /// <summary>
    /// TODO
    /// </summary>
    /// <param name="message"></param>
    /// <param name="inputs"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<GenerateContentResponse> SendMessageAsync(ModelContent message,
        IDictionary<string, object> inputs, CancellationToken cancellationToken = default)
    {
      // Set up the context to send in the request
      List<ModelContent> fullHistory = new(_chatHistory)
      {
        // Make sure that the requests are set to to role "user".
        FirebaseAIExtensions.ConvertToUser(message)
      };

      var response = await _genContentFunc(_templateId, inputs,
          fullHistory, cancellationToken);

      // Only after getting a valid response, add both to the history for later.
      // But either way pass the response along to the user.
      if (response.Candidates.Any())
      {
        ModelContent responseContent = response.Candidates.First().Content;

        _chatHistory.Add(FirebaseAIExtensions.ConvertToUser(message));
        _chatHistory.Add(responseContent.ConvertToModel());
      }

      return response;
    }
  }
}
