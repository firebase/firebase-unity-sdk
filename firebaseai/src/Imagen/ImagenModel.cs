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
using System.Threading;
using System.Threading.Tasks;
using Firebase.AI.Internal;
using Google.MiniJSON;

namespace Firebase.AI {
  
  /// <summary>
  /// Represents a remote Imagen model with the ability to generate images using text prompts.
  ///
  /// See the [generate images
  /// documentation](https://firebase.google.com/docs/vertex-ai/generate-images-imagen?platform=unity)
  /// for more details about the image generation capabilities offered by the Imagen model in the
  /// Firebase AI SDK SDK.
  ///
  /// > Warning: For Firebase AI SDK, image generation using Imagen 3 models is in Public
  /// Preview, which means that the feature is not subject to any SLA or deprecation policy and
  /// could change in backwards-incompatible ways.
  /// </summary>
  public class ImagenModel {
    private readonly FirebaseApp _firebaseApp;
    private readonly FirebaseAI.Backend _backend;
    private readonly string _modelName;
    private readonly ImagenGenerationConfig? _generationConfig;
    private readonly ImagenSafetySettings? _safetySettings;
    private readonly RequestOptions? _requestOptions;

    private readonly HttpClient _httpClient;

    internal ImagenModel(FirebaseApp firebaseApp,
                        FirebaseAI.Backend backend,
                        string modelName,
                        ImagenGenerationConfig? generationConfig = null,
                        ImagenSafetySettings? safetySettings = null,
                        RequestOptions? requestOptions = null) {
      _firebaseApp = firebaseApp;
      _backend = backend;
      _modelName = modelName;
      _generationConfig = generationConfig;
      _safetySettings = safetySettings;
      _requestOptions = requestOptions;

      // Create a HttpClient using the timeout requested, or the default one.
      _httpClient = new HttpClient() {
        Timeout = requestOptions?.Timeout ?? RequestOptions.DefaultTimeout
      };
    }

    /// <summary>
    /// Generates images using the Imagen model and returns them as inline data.
    ///
    /// > Warning: For Firebase AI SDK, image generation using Imagen 3 models is in Public
    /// Preview, which means that the feature is not subject to any SLA or deprecation policy and
    /// could change in backwards-incompatible ways.
    /// </summary>
    /// <param name="prompt">A text prompt describing the image(s) to generate.</param>
    /// <param name="cancellationToken">An optional token to cancel the operation.</param>
    /// <returns>The generated content response from the model.</returns>
    /// <exception cref="HttpRequestException">Thrown when an error occurs during content generation.</exception>
    public Task<ImagenGenerationResponse<ImagenInlineImage>> GenerateImagesAsync(
        string prompt, CancellationToken cancellationToken = default) {
      return GenerateImagesAsyncInternal(prompt, cancellationToken);
    }

    private async Task<ImagenGenerationResponse<ImagenInlineImage>> GenerateImagesAsyncInternal(
        string prompt, CancellationToken cancellationToken) {
      HttpRequestMessage request = new(HttpMethod.Post,
          HttpHelpers.GetURL(_firebaseApp, _backend, _modelName) + ":predict");

      // Set the request headers
      await HttpHelpers.SetRequestHeaders(request, _firebaseApp);

      // Set the content
      string bodyJson = MakeGenerateImagenRequest(prompt);
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

      return ImagenGenerationResponse<ImagenInlineImage>.FromJson(result);
    }

    private string MakeGenerateImagenRequest(string prompt) {
      Dictionary<string, object> jsonDict = MakeGenerateImagenRequestAsDictionary(prompt);
      return Json.Serialize(jsonDict);
    }

    private Dictionary<string, object> MakeGenerateImagenRequestAsDictionary(
        string prompt) {
      Dictionary<string, object> parameters = new() {
        // These values are hardcoded to true for AI Monitoring.
        ["includeRaiReason"] = true,
        ["includeSafetyAttributes"] = true,
      };
      // Merge the settings into a single parameter dictionary
      if (_generationConfig != null) {
        _generationConfig?.ToJson().ToList()
            .ForEach(x => parameters.Add(x.Key, x.Value));
      } else {
        // We want the change the default behavior for sampleCount to return 1.
        parameters["sampleCount"] = 1;
      }
      if (_safetySettings != null) {
        _safetySettings?.ToJson().ToList()
            .ForEach(x => parameters.Add(x.Key, x.Value));
      }

      Dictionary<string, object> jsonDict = new() {
        ["instances"] = new Dictionary<string, object>() {
          ["prompt"] = prompt,
        },
        ["parameters"] = parameters,
      };

      return jsonDict;
    }
  }

}
