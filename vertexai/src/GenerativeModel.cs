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
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Google.MiniJSON;

namespace Firebase.VertexAI {

/// <summary>
/// A type that represents a remote multimodal model (like Gemini), with the ability to generate
/// content based on various input types.
/// </summary>
public class GenerativeModel {
  private FirebaseApp _firebaseApp;

  // Various setting fields provided by the user
  private string _location;
  private string _modelName;
  private GenerationConfig? _generationConfig;
  private SafetySetting[] _safetySettings;
  private Tool[] _tools;
  private ToolConfig? _toolConfig;
  private ModelContent? _systemInstruction;
  private RequestOptions? _requestOptions;

  HttpClient _httpClient;

  internal GenerativeModel(FirebaseApp firebaseApp,
                           string location,
                           string modelName,
                           GenerationConfig? generationConfig = null,
                           SafetySetting[] safetySettings = null,
                           Tool[] tools = null,
                           ToolConfig? toolConfig = null,
                           ModelContent? systemInstruction = null,
                           RequestOptions? requestOptions = null) {
    _firebaseApp = firebaseApp;
    _location = location;
    _modelName = modelName;
    _generationConfig = generationConfig;
    _safetySettings = safetySettings;
    _tools = tools;
    _toolConfig = toolConfig;
    _systemInstruction = systemInstruction;
    _requestOptions = requestOptions;

    // Create a HttpClient using the timeout requested, or the default one.
    _httpClient = new HttpClient() {
      Timeout = requestOptions?.Timeout ?? RequestOptions.DefaultTimeout
    };
  }

#region Public API
  /// <summary>
  /// Generates new content from input ModelContent given to the model as a prompt.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>The generated content response from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> GenerateContentAsync(
      params ModelContent[] content) {
    return GenerateContentAsync((IEnumerable<ModelContent>)content);
  }
  /// <summary>
  /// Generates new content from input text given to the model as a prompt.
  /// </summary>
  /// <param name="content">The text given to the model as a prompt.</param>
  /// <returns>The generated content response from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> GenerateContentAsync(
      string text) {
    return GenerateContentAsync(new ModelContent[] { ModelContent.Text(text) });
  }
  /// <summary>
  /// Generates new content from input ModelContent given to the model as a prompt.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>The generated content response from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> GenerateContentAsync(
      IEnumerable<ModelContent> content) {
    return GenerateContentAsyncInternal(content);
  }

#define HIDE_IASYNCENUMERABLE
#if !defined(HIDE_IASYNCENUMERABLE)
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      params ModelContent[] content) {
    return GenerateContentStreamAsync((IEnumerable<ModelContent>)content);
  }
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      string text) {
    return GenerateContentStreamAsync(new ModelContent[] { ModelContent.Text(text) });
  }
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      IEnumerable<ModelContent> content) {
    return GenerateContentStreamAsyncInternal(content);
  }
#endif

  public Task<CountTokensResponse> CountTokensAsync(
      params ModelContent[] content) {
    return CountTokensAsync((IEnumerable<ModelContent>)content);
  }
  public Task<CountTokensResponse> CountTokensAsync(
      string text) {
    return CountTokensAsync(new ModelContent[] { ModelContent.Text(text) });
  }
  public Task<CountTokensResponse> CountTokensAsync(
      IEnumerable<ModelContent> content) {
    return CountTokensAsyncInternal(content);
  }

  public Chat StartChat(params ModelContent[] history) {
    return StartChat((IEnumerable<ModelContent>)history);
  }
  public Chat StartChat(IEnumerable<ModelContent> history) {
    // TODO: Implementation
    throw new NotImplementedException();
  }
#endregion

  private async Task<GenerateContentResponse> GenerateContentAsyncInternal(
      IEnumerable<ModelContent> content) {
    string bodyJson = ModelContentsToJson(content);

    UnityEngine.Debug.Log($"Going to try to send: {bodyJson}");

    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":generateContent");

    // Set the request headers
    request.Headers.Add("x-goog-api-key", _firebaseApp.Options.ApiKey);
    request.Headers.Add("x-goog-api-client", "genai-csharp/0.1.0");

    // Set the content
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

   UnityEngine.Debug.Log("Request? " + request);

    HttpResponseMessage response = await _httpClient.SendAsync(request);
    // TODO: Convert any timeout exception into a VertexAI equivalent
    // TODO: Convert any HttpRequestExceptions, see:
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-9.0
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode?view=net-9.0
    response.EnsureSuccessStatusCode();

    string result = await response.Content.ReadAsStringAsync();

    UnityEngine.Debug.Log("Got a valid response at least: \n" + result);

    return GenerateContentResponse.FromJson(result);
  }

#if !defined(HIDE_IASYNCENUMERABLE)
  private async IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsyncInternal(
      IEnumerable<ModelContent> content) {
    // TODO: Implementation
    await Task.CompletedTask;
    yield return new GenerateContentResponse();
    throw new NotImplementedException();
  }
#endif

  private async Task<CountTokensResponse> CountTokensAsyncInternal(
      IEnumerable<ModelContent> content) {
    // TODO: Implementation
    await Task.CompletedTask;
    throw new NotImplementedException();
  }

  private string GetURL() {
    return "https://firebaseml.googleapis.com/v2beta" +
        "/projects/" + _firebaseApp.Options.ProjectId +
        "/locations/" + _location +
        "/publishers/google/models/" + _modelName;
  }

  private string ModelContentsToJson(IEnumerable<ModelContent> contents) {
    Dictionary<string, object> jsonDict = new()
    {
      // Convert the Contents into a list of Json dictionaries
      ["contents"] = contents.Select(c => c.ToJson()).ToList()
      // TODO: All the other settings
    };

    return Json.Serialize(jsonDict);
  }
}

}
