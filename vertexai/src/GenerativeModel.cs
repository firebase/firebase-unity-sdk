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
using Firebase.VertexAI.Internal;

namespace Firebase.VertexAI {

/// <summary>
/// A type that represents a remote multimodal model (like Gemini), with the ability to generate
/// content based on various input types.
/// </summary>
public class GenerativeModel {
  private readonly FirebaseApp _firebaseApp;

  // Various setting fields provided by the user.
  private readonly FirebaseAI.Backend _backend;
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
  /// Use `VertexAI.GetGenerativeModel` instead to ensure proper initialization and configuration of the `GenerativeModel`.
  /// </summary>
  internal GenerativeModel(FirebaseApp firebaseApp,
                           FirebaseAI.Backend backend,
                           string modelName,
                           GenerationConfig? generationConfig = null,
                           SafetySetting[] safetySettings = null,
                           Tool[] tools = null,
                           ToolConfig? toolConfig = null,
                           ModelContent? systemInstruction = null,
                           RequestOptions? requestOptions = null) {
    _firebaseApp = firebaseApp;
    _backend = backend;
    _modelName = modelName;
    _generationConfig = generationConfig;
    _safetySettings = safetySettings;
    _tools = tools;
    _toolConfig = toolConfig;
    // Make sure that the system instructions have the role "system".
    _systemInstruction = systemInstruction?.ConvertToSystem();
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
  /// <param name="text">The text given to the model as a prompt.</param>
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

  /// <summary>
  /// Generates new content as a stream from input `ModelContent` given to the model as a prompt.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      params ModelContent[] content) {
    return GenerateContentStreamAsync((IEnumerable<ModelContent>)content);
  }
  /// <summary>
  /// Generates new content as a stream from input text given to the model as a prompt.
  /// </summary>
  /// <param name="text">The text given to the model as a prompt.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      string text) {
    return GenerateContentStreamAsync(new ModelContent[] { ModelContent.Text(text) });
  }
  /// <summary>
  /// Generates new content as a stream from input `ModelContent` given to the model as a prompt.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>A stream of generated content responses from the model.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during content generation.</exception>
  public IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsync(
      IEnumerable<ModelContent> content) {
    return GenerateContentStreamAsyncInternal(content);
  }

  /// <summary>
  /// Counts the number of tokens in a prompt using the model's tokenizer.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>The `CountTokensResponse` of running the model's tokenizer on the input.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during the request.</exception>
  public Task<CountTokensResponse> CountTokensAsync(
      params ModelContent[] content) {
    return CountTokensAsync((IEnumerable<ModelContent>)content);
  }
  /// <summary>
  /// Counts the number of tokens in a prompt using the model's tokenizer.
  /// </summary>
  /// <param name="text">The text input given to the model as a prompt.</param>
  /// <returns>The `CountTokensResponse` of running the model's tokenizer on the input.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during the request.</exception>
  public Task<CountTokensResponse> CountTokensAsync(
      string text) {
    return CountTokensAsync(new ModelContent[] { ModelContent.Text(text) });
  }
  /// <summary>
  /// Counts the number of tokens in a prompt using the model's tokenizer.
  /// </summary>
  /// <param name="content">The input(s) given to the model as a prompt.</param>
  /// <returns>The `CountTokensResponse` of running the model's tokenizer on the input.</returns>
  /// <exception cref="VertexAIException">Thrown when an error occurs during the request.</exception>
  public Task<CountTokensResponse> CountTokensAsync(
      IEnumerable<ModelContent> content) {
    return CountTokensAsyncInternal(content);
  }

  /// <summary>
  /// Creates a new chat conversation using this model with the provided history.
  /// </summary>
  /// <param name="history">Initial content history to start with.</param>
  public Chat StartChat(params ModelContent[] history) {
    return StartChat((IEnumerable<ModelContent>)history);
  }
  /// <summary>
  /// Creates a new chat conversation using this model with the provided history.
  /// </summary>
  /// <param name="history">Initial content history to start with.</param>
  public Chat StartChat(IEnumerable<ModelContent> history) {
    return Chat.InternalCreateChat(this, history);
  }
#endregion

  private async Task<GenerateContentResponse> GenerateContentAsyncInternal(
      IEnumerable<ModelContent> content) {
    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":generateContent");

    // Set the request headers
    await SetRequestHeaders(request);

    // Set the content
    string bodyJson = MakeGenerateContentRequest(content);
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

#if FIREBASE_LOG_REST_CALLS
    UnityEngine.Debug.Log("Request:\n" + bodyJson);
#endif

    HttpResponseMessage response;
    try {
      response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();
    } catch (TaskCanceledException e) when (e.InnerException is TimeoutException) {
      throw new VertexAIRequestTimeoutException("Request timed out.", e);
    } catch (HttpRequestException e) {
      // TODO: Convert to a more precise exception when possible.
      throw new VertexAIException("HTTP request failed.", e);
    }

    string result = await response.Content.ReadAsStringAsync();

#if FIREBASE_LOG_REST_CALLS
    UnityEngine.Debug.Log("Response:\n" + result);
#endif

    return GenerateContentResponse.FromJson(result, _backend.Provider);
  }

  private async IAsyncEnumerable<GenerateContentResponse> GenerateContentStreamAsyncInternal(
      IEnumerable<ModelContent> content) {
    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":streamGenerateContent?alt=sse");

    // Set the request headers
    await SetRequestHeaders(request);

    // Set the content
    string bodyJson = MakeGenerateContentRequest(content);
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

#if FIREBASE_LOG_REST_CALLS
    UnityEngine.Debug.Log("Request:\n" + bodyJson);
#endif

    HttpResponseMessage response;
    try {
      response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
      response.EnsureSuccessStatusCode();
    } catch (TaskCanceledException e) when (e.InnerException is TimeoutException) {
      throw new VertexAIRequestTimeoutException("Request timed out.", e);
    } catch (HttpRequestException e) {
      // TODO: Convert to a more precise exception when possible.
      throw new VertexAIException("HTTP request failed.", e);
    }

    // We are expecting a Stream as the response, so handle that.
    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);

    string line;
    while ((line = await reader.ReadLineAsync()) != null) {
      // Only pass along strings that begin with the expected prefix.
      if (line.StartsWith(StreamPrefix)) {
#if FIREBASE_LOG_REST_CALLS
        UnityEngine.Debug.Log("Streaming Response:\n" + line);
#endif

        yield return GenerateContentResponse.FromJson(line[StreamPrefix.Length..], _backend.Provider);
      }
    }
  }

  private async Task<CountTokensResponse> CountTokensAsyncInternal(
      IEnumerable<ModelContent> content) {
    HttpRequestMessage request = new(HttpMethod.Post, GetURL() + ":countTokens");

    // Set the request headers
    await SetRequestHeaders(request);

    // Set the content
    string bodyJson = MakeCountTokensRequest(content);
    request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

#if FIREBASE_LOG_REST_CALLS
    UnityEngine.Debug.Log("CountTokensRequest:\n" + bodyJson);
#endif

    HttpResponseMessage response;
    try {
      response = await _httpClient.SendAsync(request);
      response.EnsureSuccessStatusCode();
    } catch (TaskCanceledException e) when (e.InnerException is TimeoutException) {
      throw new VertexAIRequestTimeoutException("Request timed out.", e);
    } catch (HttpRequestException e) {
      // TODO: Convert to a more precise exception when possible.
      throw new VertexAIException("HTTP request failed.", e);
    }

    string result = await response.Content.ReadAsStringAsync();

#if FIREBASE_LOG_REST_CALLS
    UnityEngine.Debug.Log("CountTokensResponse:\n" + result);
#endif

    return CountTokensResponse.FromJson(result);
  }

  private string GetURL() {
    if (_backend.Provider == FirebaseAI.Backend.InternalProvider.VertexAI) {
      return "https://firebasevertexai.googleapis.com/v1beta" +
          "/projects/" + _firebaseApp.Options.ProjectId +
          "/locations/" + _backend.Location +
          "/publishers/google/models/" + _modelName;
    } else if (_backend.Provider == FirebaseAI.Backend.InternalProvider.GoogleAI) {
      return "https://firebasevertexai.googleapis.com/v1beta" +
          "/projects/" + _firebaseApp.Options.ProjectId +
          "/models/" + _modelName;
    } else {
      throw new NotSupportedException($"Missing support for backend: {_backend.Provider}");
    }
  }

  private async Task SetRequestHeaders(HttpRequestMessage request) {
    request.Headers.Add("x-goog-api-key", _firebaseApp.Options.ApiKey);
    string version = FirebaseInterops.GetVersionInfoSdkVersion();
    request.Headers.Add("x-goog-api-client", $"genai-csharp/{version}");
    if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(_firebaseApp)) {
      request.Headers.Add("X-Firebase-AppId", _firebaseApp.Options.AppId);
      request.Headers.Add("X-Firebase-AppVersion", version);
    }
    // Add additional Firebase tokens to the header.
    await FirebaseInterops.AddFirebaseTokensAsync(request, _firebaseApp);
  }

  private string MakeGenerateContentRequest(IEnumerable<ModelContent> contents) {
    Dictionary<string, object> jsonDict = MakeGenerateContentRequestAsDictionary(contents);
    return Json.Serialize(jsonDict);
  }

  private Dictionary<string, object> MakeGenerateContentRequestAsDictionary(
      IEnumerable<ModelContent> contents) {
    Dictionary<string, object> jsonDict = new() {
      // Convert the Contents into a list of Json dictionaries
      ["contents"] = contents.Select(c => c.ToJson()).ToList()
    };
    if (_generationConfig.HasValue) {
      jsonDict["generationConfig"] = _generationConfig?.ToJson();
    }
    if (_safetySettings != null && _safetySettings.Length > 0) {
      jsonDict["safetySettings"] = _safetySettings.Select(s => s.ToJson(_backend.Provider)).ToList();
    }
    if (_tools != null && _tools.Length > 0) {
      jsonDict["tools"] = _tools.Select(t => t.ToJson()).ToList();
    }
    if (_toolConfig.HasValue) {
      jsonDict["toolConfig"] = _toolConfig?.ToJson();
    }
    if (_systemInstruction.HasValue) {
      jsonDict["systemInstruction"] = _systemInstruction?.ToJson();
    }

    return jsonDict;
  }

  // CountTokensRequest is a subset of the full info needed for GenerateContent
  private string MakeCountTokensRequest(IEnumerable<ModelContent> contents) {
    Dictionary<string, object> jsonDict;
    switch (_backend.Provider) {
      case FirebaseAI.Backend.InternalProvider.GoogleAI:
        jsonDict = new() {
          ["generateContentRequest"] = MakeGenerateContentRequestAsDictionary(contents)
        };
        // GoogleAI wants the model name included as well.
        ((Dictionary<string, object>)jsonDict["generateContentRequest"])["model"] =
            $"models/{_modelName}";
        break;
      case FirebaseAI.Backend.InternalProvider.VertexAI:
        jsonDict = new() {
          // Convert the Contents into a list of Json dictionaries
          ["contents"] = contents.Select(c => c.ToJson()).ToList()
        };
        if (_generationConfig.HasValue) {
          jsonDict["generationConfig"] = _generationConfig?.ToJson();
        }
        if (_tools != null && _tools.Length > 0) {
          jsonDict["tools"] = _tools.Select(t => t.ToJson()).ToList();
        }
        if (_systemInstruction.HasValue) {
          jsonDict["systemInstruction"] = _systemInstruction?.ToJson();
        }
        break;
      default:
        throw new NotSupportedException($"Missing support for backend: {_backend.Provider}");
    }

    return Json.Serialize(jsonDict);
  }
}

}
