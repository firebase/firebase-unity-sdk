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

namespace Firebase.AI {

/// <summary>
/// The entry point for all Firebase AI SDK functionality.
/// </summary>
public class FirebaseAI {

  /// <summary>
  /// Defines which backend AI service is being used, provided to `FirebaseAI.GetInstance`.
  /// </summary>
  public readonly struct Backend {
    /// <summary>
    /// Intended for internal use only.
    /// Defines the possible types of backend providers.
    /// </summary>
    internal enum InternalProvider {
      GoogleAI,
      VertexAI
    }

    /// <summary>
    /// Intended for internal use only.
    /// The backend provider being used.
    /// </summary>
    internal InternalProvider Provider { get; }
    /// <summary>
    /// Intended for internal use only.
    /// The region identifier used by the Vertex AI backend.
    /// </summary>
    internal string Location { get; }

    private Backend(InternalProvider provider, string location = null) {
      Provider = provider;
      Location = location;
    }

    /// <summary>
    /// The Google AI backend service configuration.
    /// </summary>
    public static Backend GoogleAI() {
      return new Backend(InternalProvider.GoogleAI);
    }

    /// <summary>
    /// The Vertex AI backend service configuration.
    /// </summary>
    /// <param name="location">The region identifier, defaulting to `us-central1`; see [Vertex AI
    ///     regions](https://cloud.google.com/vertex-ai/generative-ai/docs/learn/locations#available-regions)
    ///     for a list of supported regions.</param>
    public static Backend VertexAI(string location = "us-central1") {
      if (string.IsNullOrWhiteSpace(location) || location.Contains("/")) {
        throw new ArgumentException(
            $"The location argument must be non-empty, and not contain special characters like '/'");
      }

      return new Backend(InternalProvider.VertexAI, location);
    }

    public override readonly string ToString() {
      return $"FirebaseAIBackend|{Provider}|{Location}";
    }
  }

  private static readonly ConcurrentDictionary<string, FirebaseAI> _instances = new();

  private readonly FirebaseApp _firebaseApp;
  private readonly Backend _backend;

  private FirebaseAI(FirebaseApp firebaseApp, Backend backend) {
    _firebaseApp = firebaseApp;
    _backend = backend;
  }

  /// <summary>
  /// Returns a `FirebaseAI` instance with the default `FirebaseApp` and GoogleAI Backend.
  /// </summary>
  public static FirebaseAI DefaultInstance {
    get {
      return GetInstance();
    }
  }

  /// <summary>
  /// Returns a `FirebaseAI` instance with the default `FirebaseApp` and the given Backend.
  /// </summary>
  /// <param name="backend">The backend AI service to use.</param>
  /// <returns>A configured instance of `FirebaseAI`.</returns>
  public static FirebaseAI GetInstance(Backend? backend = null) {
    return GetInstance(FirebaseApp.DefaultInstance, backend);
  }
  /// <summary>
  /// Returns a `FirebaseAI` instance with the given `FirebaseApp` and Backend.
  /// </summary>
  /// <param name="app">The custom `FirebaseApp` used for initialization.</param>
  /// <param name="backend">The backend AI service to use.</param>
  /// <returns>A configured instance of `FirebaseAI`.</returns>
  public static FirebaseAI GetInstance(FirebaseApp app, Backend? backend = null) {
    if (app == null) {
      throw new ArgumentNullException(nameof(app));
    }

    Backend resolvedBackend = backend ?? Backend.GoogleAI();

    // FirebaseAI instances are keyed by a combination of the app name and backend.
    string key = $"{app.Name}::{resolvedBackend}";
    if (_instances.ContainsKey(key)) {
      return _instances[key];
    }

    return _instances.GetOrAdd(key, _ => new FirebaseAI(app, resolvedBackend));
  }

  /// <summary>
  /// Initializes a generative model with the given parameters.
  /// 
  /// - Note: Refer to [Gemini models](https://firebase.google.com/docs/vertex-ai/gemini-models) for
  /// guidance on choosing an appropriate model for your use case.
  /// </summary>
  /// <param name="modelName">The name of the model to use; see
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
    return new GenerativeModel(_firebaseApp, _backend, modelName,
        generationConfig, safetySettings, tools,
        toolConfig, systemInstruction, requestOptions);
  }

  /// <summary>
  /// Initializes a `LiveGenerativeModel` for real-time interaction.
  /// 
  /// - Note: Refer to [Gemini models](https://firebase.google.com/docs/vertex-ai/gemini-models) for
  /// guidance on choosing an appropriate model for your use case.
  /// </summary>
  /// <param name="modelName">The name of the model to use; see
  ///     [available model names
  ///     ](https://firebase.google.com/docs/vertex-ai/gemini-models#available-model-names) for a
  ///     list of supported model names.</param>
  /// <param name="liveGenerationConfig">The content generation parameters your model should use.</param>
  /// <param name="tools">A list of `Tool` objects that the model may use to generate the next response.</param>
  /// <param name="systemInstruction">Instructions that direct the model to behave a certain way.</param>
  /// <param name="requestOptions">Configuration parameters for sending requests to the backend.</param>
  /// <returns>The initialized `LiveGenerativeModel` instance.</returns>
  public LiveGenerativeModel GetLiveModel(
      string modelName,
      LiveGenerationConfig? liveGenerationConfig = null,
      Tool[] tools = null,
      ModelContent? systemInstruction = null,
      RequestOptions? requestOptions = null) {
    return new LiveGenerativeModel(_firebaseApp, _backend, modelName,
        liveGenerationConfig, tools,
        systemInstruction, requestOptions);
  }
  
  /// <summary>
  /// Initializes an `ImagenModel` with the given parameters.
  ///
  /// - Important: Only Imagen 3 models (named `imagen-3.0-*`) are supported.
  /// </summary>
  /// <param name="modelName">The name of the Imagen 3 model to use, for example `"imagen-3.0-generate-002"`;
  ///     see [model versions](https://firebase.google.com/docs/vertex-ai/models) for a list of
  ///     supported Imagen 3 models.</param>
  /// <param name="generationConfig">Configuration options for generating images with Imagen.</param>
  /// <param name="safetySettings">Settings describing what types of potentially harmful content your model
  ///     should allow.</param>
  /// <param name="requestOptions">Configuration parameters for sending requests to the backend.</param>
  /// <returns>The initialized `ImagenModel` instance.</returns>
  public ImagenModel GetImagenModel(
      string modelName,
      ImagenGenerationConfig? generationConfig = null,
      ImagenSafetySettings? safetySettings = null,
      RequestOptions? requestOptions = null) {
    return new ImagenModel(_firebaseApp, _backend, modelName,
        generationConfig, safetySettings, requestOptions);    
  }
}

}
