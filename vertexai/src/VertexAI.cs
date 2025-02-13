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
using System.Collections.Concurrent;

namespace Firebase.VertexAI {

/// <summary>
/// The entry point for all Vertex AI in Firebase functionality.
/// </summary>
public class VertexAI {

  private static readonly ConcurrentDictionary<string, VertexAI> _instances = new();

  private FirebaseApp _firebaseApp;
  private string _location;

  private VertexAI(FirebaseApp firebaseApp, string location) {
    _firebaseApp = firebaseApp;
    _location = location;
  }

  /// <summary>
  /// Returns a VertexAI initialized with the default `FirebaseApp` and location.
  /// </summary>
  public static VertexAI DefaultInstance => GetInstance();

  /// <summary>
  /// Creates an instance of `VertexAI` configured with the default `FirebaseApp`, and the given location.
  /// </summary>
  /// <param name="location">The region identifier, defaulting to `us-central1`; see [Vertex AI
  ///     regions](https://cloud.google.com/vertex-ai/generative-ai/docs/learn/locations#available-regions)
  ///     for a list of supported regions.</param>
  /// <returns>A configured instance of `VertexAI`.</returns>
  public static VertexAI GetInstance(string location = "us-central1") {
    return GetInstance(FirebaseApp.DefaultInstance, location);
  }

  /// <summary>
  /// Creates an instance of `VertexAI` configured with the given FirebaseApp and location.
  /// </summary>
  /// <param name="app">The custom `FirebaseApp` used for initialization.</param>
  /// <param name="location">The region identifier, defaulting to `us-central1`; see [Vertex AI
  ///     regions](https://cloud.google.com/vertex-ai/generative-ai/docs/learn/locations#available-regions)
  ///     for a list of supported regions.</param>
  /// <returns>A configured instance of `VertexAI`.</returns>
  public static VertexAI GetInstance(FirebaseApp app, string location = "us-central1") {
    if (app == null) {
      throw new ArgumentNullException(nameof(app));
    }

    if (string.IsNullOrWhiteSpace(location) || location.Contains("/")) {
      throw new VertexAIInvalidLocationException(location);
    }

    // VertexAI instances are keyed by a combination of the app name and location.
    string key = $"{app.Name}::{location}";
    if (_instances.ContainsKey(key)) {
      return _instances[key];
    }

    return _instances.GetOrAdd(key, _ => new VertexAI(app, location));
  }

  /// <summary>
  /// Initializes a generative model with the given parameters.
  /// 
  /// - Note: Refer to [Gemini models](https://firebase.google.com/docs/vertex-ai/gemini-models) for
  /// guidance on choosing an appropriate model for your use case.
  /// </summary>
  /// <param name="modelName">The name of the model to use, for example `"gemini-1.5-flash"`; see
  ///     [available model names
  ///     ](https://firebase.google.com/docs/vertex-ai/gemini-models#available-model-names) for a
  ///     list of supported model names.</param>
  /// <param name="generationConfig">The content generation parameters your model should use.</param>
  /// <param name="safetySettings">A value describing what types of harmful content your model should allow.</param>
  /// <param name="tools">A list of `Tool` objects that the model may use to generate the next response.</param>
  /// <param name="toolConfig">Tool configuration for any `Tool` specified in the request.</param>
  /// <param name="systemInstruction">Instructions that direct the model to behave a certain way;
  ///     currently only text content is supported.</param>
  /// <param name="requestOptions">Configuration parameters for sending requests to the backend.</param>
  /// <returns>The initialized `GenerativeModel` instance.</returns>
  public GenerativeModel GetGenerativeModel(
      string modelName,
      GenerationConfig? generationConfig = null,
      SafetySetting[] safetySettings = null,
      Tool[] tools = null,
      ToolConfig? toolConfig = null,
      ModelContent? systemInstruction = null,
      RequestOptions? requestOptions = null) {
    return new GenerativeModel(_firebaseApp, _location, modelName,
        generationConfig, safetySettings, tools,
        toolConfig, systemInstruction, requestOptions);
  }
}

}
