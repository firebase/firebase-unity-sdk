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
using System.IO;
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
  private readonly FirebaseApp _firebaseApp;

  // Various setting fields provided by the user.
  private readonly string _location;
  private readonly string _modelName;
  private readonly GenerationConfig? _generationConfig;
  private readonly SafetySetting[] _safetySettings;
  private readonly Tool[] _tools;
  private readonly ToolConfig? _toolConfig;
  private readonly ModelContent? _systemInstruction;
  private readonly RequestOptions? _requestOptions;

  private readonly HttpClient _httpClient;
  // String prefix to look for when handling streaming a response.
  private const string StreamPrefix = "data: ";

  /// <summary>
  /// Intended for internal use only.
  /// Use `VertexAI.GetInstance` instead to ensure proper initialization and configuration of the `GenerativeModel`.
  /// </summary>
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
  /// Generates new content from input `ModelContent` given to the model as a prompt.
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
  /// Generates new content from input `ModelContent` given to the model as a prompt.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>The generated content response from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public Task<GenerateContentResponse> GenerateContentAsync(
      IEnumerable<ModelContent> content) {
    return GenerateContentAsyncInternal(content);
  }

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
    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":generateContent");

    // Set the request headers
    SetRequestHeaders(request);

    // Set the content
    string bodyJson = ModelContentsToJson(content);
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

    HttpResponseMessage response = await _httpClient.SendAsync(request);
    // TODO: Convert any timeout exception into a VertexAI equivalent
    // TODO: Convert any HttpRequestExceptions, see:
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-9.0
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode?view=net-9.0
    response.EnsureSuccessStatusCode();

    string result = await response.Content.ReadAsStringAsync();
    return GenerateContentResponse.FromJson(result);
  }

  private async IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsyncInternal(
      IEnumerable<ModelContent> content) {
    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":streamGenerateContent?alt=sse");

    // Set the request headers
    SetRequestHeaders(request);

    // Set the content
    string bodyJson = ModelContentsToJson(content);
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

    HttpResponseMessage response =
        await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    // TODO: Convert any timeout exception into a VertexAI equivalent
    // TODO: Convert any HttpRequestExceptions, see:
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-9.0
    // https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpresponsemessage.ensuresuccessstatuscode?view=net-9.0
    response.EnsureSuccessStatusCode();

    // We are expecting a Stream as the response, so handle that.
    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);

    string line;
    while ((line = await reader.ReadLineAsync()) != null) {
      // Only pass along strings that begin with the expected prefix.
      if (line.StartsWith(StreamPrefix)) {
        yield return GenerateContentResponse.FromJson(line[StreamPrefix.Length..]);
      }
    }
  }

  private async Task<CountTokensResponse> CountTokensAsyncInternal(
      IEnumerable<ModelContent> content) {
    // TODO: Implementation
    await Task.CompletedTask;
    throw new NotImplementedException();
  }

  private string GetURL() {
    return "https://firebasevertexai.googleapis.com/v1beta" +
        "/projects/" + _firebaseApp.Options.ProjectId +
        "/locations/" + _location +
        "/publishers/google/models/" + _modelName;
  }

  private void SetRequestHeaders(HttpRequestMessage request) {
    request.Headers.Add("x-goog-api-key", _firebaseApp.Options.ApiKey);
    // TODO: Get the Version from the Firebase.VersionInfo.SdkVersion (requires exposing it via App)
    request.Headers.Add("x-goog-api-client", "genai-csharp/0.1.0");
  }

  private string ModelContentsToJson(IEnumerable<ModelContent> contents) {
    Dictionary<string, object> jsonDict = new() {
      // Convert the Contents into a list of Json dictionaries
      ["contents"] = contents.Select(c => c.ToJson()).ToList()
    };
    if (_generationConfig.HasValue) {
      jsonDict["generationConfig"] = _generationConfig?.ToJson();
    }
    if (_safetySettings != null && _safetySettings.Length > 0) {
      jsonDict["safetySettings"] = _safetySettings.Select(s => s.ToJson()).ToList();
    }
    // TODO: Tool and ToolConfig (Part of FunctionCalling)
    if (_systemInstruction.HasValue) {
      jsonDict["systemInstruction"] = _systemInstruction?.ToJson();
    }

    return Json.Serialize(jsonDict);
  }
}

}
