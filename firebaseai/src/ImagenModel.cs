using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.MiniJSON; // Assuming MiniJSON
using Firebase.AI.Internal; // For FirebaseInterops and potentially other internal helpers

namespace Firebase.AI {
  public class ImagenModel {
    private readonly FirebaseApp _firebaseApp;
    private readonly FirebaseAI.Backend _backend;
    private readonly string _modelName;
    private readonly ImagenGenerationConfig? _generationConfig;
    private readonly ImagenSafetySettings? _safetySettings;
    private readonly RequestOptions? _requestOptions;
    private readonly HttpClient _httpClient;

    internal ImagenModel(
        FirebaseApp firebaseApp,
        FirebaseAI.Backend backend,
        string modelName,
        ImagenGenerationConfig? generationConfig,
        ImagenSafetySettings? safetySettings,
        RequestOptions? requestOptions) {
      _firebaseApp = firebaseApp ?? throw new ArgumentNullException(nameof(firebaseApp));
      _backend = backend; // Assuming Backend is a struct and already validated
      _modelName = !string.IsNullOrWhiteSpace(modelName) ? modelName
          : throw new ArgumentException("Model name cannot be null or whitespace.", nameof(modelName));
      _generationConfig = generationConfig;
      _safetySettings = safetySettings;
      _requestOptions = requestOptions;

      _httpClient = new HttpClient {
        Timeout = _requestOptions?.Timeout ?? RequestOptions.DefaultTimeout
      };
    }

    public Task<ImagenGenerationResponse<ImagenInlineImage>> GenerateImagesAsync(
        string prompt, CancellationToken cancellationToken = default) {
      return GenerateImagesAsyncInternal<ImagenInlineImage>(prompt, null, cancellationToken);
    }

    public Task<ImagenGenerationResponse<ImagenGcsImage>> GenerateImagesAsync(
        string prompt, System.Uri gcsUri, CancellationToken cancellationToken = default) {
      if (gcsUri == null) throw new ArgumentNullException(nameof(gcsUri));
      return GenerateImagesAsyncInternal<ImagenGcsImage>(prompt, gcsUri, cancellationToken);
    }

    private async Task<ImagenGenerationResponse<T>> GenerateImagesAsyncInternal<T>(
        string prompt, System.Uri gcsUri, CancellationToken cancellationToken) where T : IImagenImage {
      if (string.IsNullOrWhiteSpace(prompt)) {
        throw new ArgumentException("Prompt cannot be null or whitespace.", nameof(prompt));
      }

      HttpRequestMessage request = new(HttpMethod.Post, GetGenerateImagesURL());
      await SetRequestHeaders(request);

      string bodyJson = MakeGenerateImagesRequest(prompt, gcsUri);
      request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

      #if FIREBASE_LOG_REST_CALLS
      UnityEngine.Debug.Log($"Imagen Request: {bodyJson}");
      #endif

      try {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        await ValidateHttpResponse(response); // Similar to GenerativeModel's helper

        string result = await response.Content.ReadAsStringAsync();

        #if FIREBASE_LOG_REST_CALLS
        UnityEngine.Debug.Log($"Imagen Response: {result}");
        #endif

        return ImagenGenerationResponse<T>.FromJson(result);
      } catch (HttpRequestException e) {
        // Log or handle more gracefully
        UnityEngine.Debug.LogError($"Imagen API request failed: {e.Message}");
        throw; // Re-throw or wrap in a Firebase-specific exception
      }
    }

    private string GetGenerateImagesURL() {
      // Construct the URL based on the backend provider
      // This is an example, the exact URL structure needs to be verified against Imagen API docs
      // Assuming VertexAI backend for Imagen, similar to Gemini
      if (_backend.Provider == FirebaseAI.Backend.InternalProvider.VertexAI) {
         // Example: "https://firebaseml.googleapis.com/v1beta/projects/{PROJECT_ID}/locations/{LOCATION}/publishers/google/models/{MODEL_NAME}:generateImages"
         // Note: The problem description uses "firebasevertexai.googleapis.com" for Gemini. Assuming similar for Imagen.
        return $"https://firebasevertexai.googleapis.com/v1beta/projects/{_firebaseApp.Options.ProjectId}/locations/{_backend.Location}/publishers/google/models/{_modelName}:generateImages";
      }
      // Fallback or error for other backends if Imagen is Vertex-specific
      throw new NotSupportedException($"Backend {_backend.Provider} is not supported for ImagenModel.");
    }

    private async Task SetRequestHeaders(HttpRequestMessage request) {
      // Similar to GenerativeModel.SetRequestHeaders
      request.Headers.Add("x-goog-api-key", _firebaseApp.Options.ApiKey);
      string version = FirebaseInterops.GetVersionInfoSdkVersion(); // Assuming this exists
      request.Headers.Add("x-goog-api-client", $"gl-csharp/fire-{version}"); // Adjusted client name
      if (FirebaseInterops.GetIsDataCollectionDefaultEnabled(_firebaseApp)) {
          request.Headers.Add("X-Firebase-AppId", _firebaseApp.Options.AppId);
      }
      // Add additional Firebase tokens to the header.
      await FirebaseInterops.AddFirebaseTokensAsync(request, _firebaseApp);
    }

    private string MakeGenerateImagesRequest(string prompt, System.Uri gcsUri) {
      var requestDict = new Dictionary<string, object> {
        ["prompt"] = new Dictionary<string, string> { { "text", prompt } } // Assuming prompt is always text for now
      };

      if (_generationConfig.HasValue) {
        var configDict = _generationConfig.Value.ToJson();
        foreach(var kvp in configDict) requestDict[kvp.Key] = kvp.Value;
      }

      if (_safetySettings.HasValue) {
        // Assuming safety settings are top-level in the request body
        // This might need to be nested under a specific key like "safetySettings"
        var safetyDict = _safetySettings.Value.ToJson();
        foreach(var kvp in safetyDict) requestDict[kvp.Key] = kvp.Value;
      }

      if (gcsUri != null) {
        // Add GCS URI for output, structure depends on API spec
        requestDict["outputGcsUri"] = gcsUri.ToString();
      }

      // Add other parameters like "model" if required by the backend at this stage.
      // requestDict["model"] = _modelName; // Or prefixed model path

      return Json.Serialize(requestDict);
    }

    // Helper function to throw an exception if the Http Response indicates failure.
    // Copied from GenerativeModel.cs for now, consider moving to a shared utility if common
    private async Task ValidateHttpResponse(HttpResponseMessage response) {
        if (response.IsSuccessStatusCode) {
            return;
        }

        string errorContent = "No error content available.";
        if (response.Content != null) {
            try {
                errorContent = await response.Content.ReadAsStringAsync();
            } catch (Exception readEx) {
                errorContent = $"Failed to read error content: {readEx.Message}";
            }
        }
        var ex = new HttpRequestException(
            $"HTTP request failed with status code: {(int)response.StatusCode} ({response.ReasonPhrase}).\n" +
            $"Error Content: {errorContent}"
        );
        UnityEngine.Debug.LogError($"Request failed: {ex.Message} Full error: {errorContent}");
        throw ex;
    }
  }
}
