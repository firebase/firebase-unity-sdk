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
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.AI.Internal;
using Google.MiniJSON;

namespace Firebase.AI {

/// <summary>
/// A live, generative AI model for real-time interaction.
///
/// See the [Cloud
/// documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/multimodal-live)
/// for more details about the low-latency, two-way interactions that use text,
/// audio, and video input, with audio and text output.
///
/// > Warning: For Firebase AI, Live Model
/// is in Public Preview, which means that the feature is not subject to any SLA
/// or deprecation policy and could change in backwards-incompatible ways.
/// </summary>
public class LiveGenerativeModel {
  private readonly FirebaseApp _firebaseApp;

  // Various setting fields provided by the user.
  private readonly FirebaseAI.Backend _backend;
  private readonly string _modelName;
  private readonly LiveGenerationConfig? _liveConfig;
  private readonly Tool[] _tools;
  private readonly ModelContent? _systemInstruction;
  private readonly RequestOptions? _requestOptions;

  /// <summary>
  /// Intended for internal use only.
  /// Use `FirebaseAI.GetLiveModel` instead to ensure proper initialization and configuration of the `LiveGenerativeModel`.
  /// </summary>
  internal LiveGenerativeModel(FirebaseApp firebaseApp,
                               FirebaseAI.Backend backend,
                               string modelName,
                               LiveGenerationConfig? liveConfig = null,
                               Tool[] tools = null,
                               ModelContent? systemInstruction = null,
                               RequestOptions? requestOptions = null) {
    _firebaseApp = firebaseApp;
    _backend = backend;
    _modelName = modelName;
    _liveConfig = liveConfig;
    _tools = tools;
    _systemInstruction = systemInstruction;
    _requestOptions = requestOptions;
  }

  private string GetURL() {
    if (_backend.Provider == FirebaseAI.Backend.InternalProvider.VertexAI) {
      return "wss://firebasevertexai.googleapis.com/ws" +
             "/google.firebase.vertexai.v1beta.LlmBidiService/BidiGenerateContent" +
             $"/locations/{_backend.Location}" +
             $"?key={_firebaseApp.Options.ApiKey}";
    } else if (_backend.Provider == FirebaseAI.Backend.InternalProvider.GoogleAI) {
      return "wss://firebasevertexai.googleapis.com/ws" +
             "/google.firebase.vertexai.v1beta.GenerativeService/BidiGenerateContent" +
             $"?key={_firebaseApp.Options.ApiKey}";
    } else {
      throw new NotSupportedException($"Missing support for backend: {_backend.Provider}");
    }
  }
  
  private string GetModelName() {
    if (_backend.Provider == FirebaseAI.Backend.InternalProvider.VertexAI) {
      return $"projects/{_firebaseApp.Options.ProjectId}/locations/{_backend.Location}" +
             $"/publishers/google/models/{_modelName}";
    } else if (_backend.Provider == FirebaseAI.Backend.InternalProvider.GoogleAI) {
      return $"projects/{_firebaseApp.Options.ProjectId}" +
             $"/models/{_modelName}";
    } else {
      throw new NotSupportedException($"Missing support for backend: {_backend.Provider}");
    }
  }

  /// <summary>
  /// Establishes a connection to a live generation service.
  ///
  /// This function handles the WebSocket connection setup and returns an `LiveSession`
  /// object that can be used to communicate with the service.
  /// </summary>
  /// <param name="cancellationToken">The token that can be used to cancel the creation of the session.</param>
  /// <returns>The LiveSession, once it is established.</returns>
  public async Task<LiveSession> ConnectAsync(CancellationToken cancellationToken = default) {
    ClientWebSocket clientWebSocket = new();

    string endpoint = GetURL();

    // Set initial headers
    string version = FirebaseInterops.GetVersionInfoSdkVersion();
    clientWebSocket.Options.SetRequestHeader("x-goog-api-client", $"gl-csharp/8.0 fire/{version}");
    if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(_firebaseApp)) {
      clientWebSocket.Options.SetRequestHeader("X-Firebase-AppId", _firebaseApp.Options.AppId);
      clientWebSocket.Options.SetRequestHeader("X-Firebase-AppVersion", UnityEngine.Application.version);
    }
    // Add additional Firebase tokens to the header.
    await FirebaseInterops.AddFirebaseTokensAsync(clientWebSocket, _firebaseApp);

    // Add a timeout to the initial connection, using the RequestOptions.
    using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    TimeSpan connectionTimeout = _requestOptions?.Timeout ?? RequestOptions.DefaultTimeout;
    connectionCts.CancelAfter(connectionTimeout);

    await clientWebSocket.ConnectAsync(new Uri(endpoint), connectionCts.Token);

    if (clientWebSocket.State != WebSocketState.Open) {
      throw new WebSocketException("ClientWebSocket failed to connect, can't create LiveSession.");
    }
    
    try {
      // Send the initial setup message
      Dictionary<string, object> setupDict = new() {
        { "model", $"projects/{_firebaseApp.Options.ProjectId}/locations/{_backend.Location}/publishers/google/models/{_modelName}" }
      };
      if (_liveConfig != null) {
        setupDict["generationConfig"] = _liveConfig?.ToJson();
      }
      if (_systemInstruction.HasValue) {
        setupDict["systemInstruction"] = _systemInstruction?.ToJson();
      }
      if (_tools != null && _tools.Length > 0) {
        setupDict["tools"] = _tools.Select(t => t.ToJson()).ToList();
      }
      Dictionary<string, object> jsonDict = new() {
        { "setup", setupDict }
      };

      var byteArray = Encoding.UTF8.GetBytes(Json.Serialize(jsonDict));
      await clientWebSocket.SendAsync(new ArraySegment<byte>(byteArray), WebSocketMessageType.Binary, true, cancellationToken);

      return new LiveSession(clientWebSocket);
    } catch (Exception) {
      if (clientWebSocket.State == WebSocketState.Open) {
        // Try to clean up the WebSocket, to avoid leaking connections.
        await clientWebSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable,
            "Failed to send initial setup message.", CancellationToken.None);
      }
      throw;
    }
  }
}

}
