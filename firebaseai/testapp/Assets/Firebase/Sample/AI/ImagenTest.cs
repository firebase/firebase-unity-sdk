using UnityEngine;
using Firebase;
using Firebase.AI;
using System.Threading.Tasks;
using System.Collections.Generic; // For IReadOnlyList
using System.Threading; // Required for ContinueWithOnMainThread if not implicitly available through Firebase.Extensions

// It's good practice to ensure Firebase.Extensions is included for ContinueWithOnMainThread
// If it's not automatically part of the testapp's setup, this might be needed:
// using Firebase.Extensions;

public class ImagenTest : MonoBehaviour {
  private FirebaseApp app;
  private FirebaseAI ai;
  // NOTE: Replace with an actual, available Imagen model for testing.
  // This model name is a placeholder and might not be valid.
  // Consult Imagen documentation for suitable test model names.
  private const string TestImagenModelName = "gemini-1.5-flash-preview-0514"; // Placeholder, needs valid Imagen model
  private const string TestGcsBucketPath = "gs://your-firebase-project-bucket/imagen_test_output/"; // Replace!

  void Start() {
    Debug.Log("ImagenTest: Initializing Firebase...");
    Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task => {
      if (task.Result == Firebase.DependencyStatus.Available) {
        app = Firebase.FirebaseApp.DefaultInstance;
        // Assuming Imagen is primarily a Vertex AI feature based on previous class structures.
        // If GoogleAI backend is also supported for Imagen, this could be configurable.
        ai = FirebaseAI.GetInstance(app, FirebaseAI.Backend.VertexAI("us-central1"));
        Debug.Log("ImagenTest: Firebase initialized. Starting tests...");
        RunAllTests();
      } else {
        Debug.LogError("ImagenTest: Could not resolve all Firebase dependencies: " + task.Result);
      }
    });
  }

  async void RunAllTests() {
    Debug.Log("===== Starting Imagen Tests =====");
    await TestGenerateInlineImageSimple();
    await TestGenerateInlineImageWithConfig();
    await TestGenerateInlineImageWithSafetySettings();
    await TestGenerateGcsImageSimple();
    // TODO: Add a test for FilteredReason if a reliable way to trigger it can be found.
    Debug.Log("===== Imagen Tests Concluded =====");
  }

  async Task TestGenerateInlineImageSimple() {
    Debug.Log("TestGenerateInlineImageSimple: Starting...");
    if (ai == null) {
      Debug.LogError("TestGenerateInlineImageSimple: FirebaseAI not initialized.");
      return;
    }

    var model = ai.GetImagenModel(TestImagenModelName);
    var prompt = "A watercolor painting of a serene lake at sunset.";

    try {
      var response = await model.GenerateImagesAsync(prompt: prompt);

      if (response.Images != null && response.Images.Count > 0) {
        Debug.Log($"TestGenerateInlineImageSimple: Received {response.Images.Count} image(s).");
        bool allImagesValid = true;
        foreach (var image in response.Images) {
          if (image.Data != null && image.Data.Length > 0) {
            Debug.Log($"TestGenerateInlineImageSimple: Image data is not empty (MIME: {image.MimeType}).");
            Texture2D tex = image.AsTexture2D();
            if (tex != null && tex.width > 0 && tex.height > 0) {
              Debug.Log($"TestGenerateInlineImageSimple: AsTexture2D() successful. Texture size: {tex.width}x{tex.height}");
              // Clean up texture if not needed further
              Object.Destroy(tex);
            } else {
              Debug.LogError("TestGenerateInlineImageSimple: AsTexture2D() failed or returned invalid texture.");
              allImagesValid = false;
            }
          } else {
            Debug.LogError("TestGenerateInlineImageSimple: Image data is null or empty.");
            allImagesValid = false;
          }
        }
        if (allImagesValid) Debug.Log("TestGenerateInlineImageSimple: PASS (All images processed successfully)");
        else Debug.LogError("TestGenerateInlineImageSimple: FAIL (One or more images had issues, see logs)");

      } else if (response.FilteredReason != null) {
         Debug.LogWarning($"TestGenerateInlineImageSimple: No images received. FilteredReason: {response.FilteredReason}. This might be a PASS if the prompt was designed to be filtered.");
      }
      else {
        Debug.LogError($"TestGenerateInlineImageSimple: FAIL - No images received and no FilteredReason provided.");
      }
    } catch (System.Exception e) {
      Debug.LogError($"TestGenerateInlineImageSimple: FAILED with exception: {e}");
    }
  }

  async Task TestGenerateInlineImageWithConfig() {
    Debug.Log("TestGenerateInlineImageWithConfig: Starting...");
    if (ai == null) {
      Debug.LogError("TestGenerateInlineImageWithConfig: FirebaseAI not initialized.");
      return;
    }

    // Request 2 images, specific aspect ratio
    var config = new ImagenGenerationConfig(
        numberOfImages: 2,
        aspectRatio: ImagenAspectRatio.Square1x1,
        imageFormat: ImagenImageFormat.Jpeg(80) // Request JPEG with quality
    );
    var model = ai.GetImagenModel(modelName: TestImagenModelName, generationConfig: config);
    var prompt = "Two robots playing poker in a futuristic casino, 1x1 aspect ratio, jpeg format";

    try {
      var response = await model.GenerateImagesAsync(prompt: prompt);

      if (response.Images != null && response.Images.Count == 2) {
        Debug.Log($"TestGenerateInlineImageWithConfig: Received expected 2 images.");
        bool allImagesValid = true;
        foreach(var image in response.Images) {
            if (image.MimeType != "image/jpeg") {
                 Debug.LogError($"TestGenerateInlineImageWithConfig: Expected image/jpeg, got {image.MimeType}");
                 allImagesValid = false;
            }
            Texture2D tex = image.AsTexture2D();
            if (tex == null || tex.width == 0 || tex.height == 0 ) {
                Debug.LogError($"TestGenerateInlineImageWithConfig: AsTexture2D failed for an image.");
                allImagesValid = false;
            } else {
                 Debug.Log($"TestGenerateInlineImageWithConfig: Image {response.Images.IndexOf(image)+1} texture loaded: {tex.width}x{tex.height}");
                 Object.Destroy(tex);
            }
        }
        if (allImagesValid) Debug.Log("TestGenerateInlineImageWithConfig: PASS");
        else Debug.LogError("TestGenerateInlineImageWithConfig: FAIL (Issues with image properties or count, see logs)");

      } else if (response.Images != null) {
        Debug.LogError($"TestGenerateInlineImageWithConfig: FAIL - Expected 2 images, but got {response.Images.Count}. FilteredReason: {response.FilteredReason}");
      } else if (response.FilteredReason != null) {
         Debug.LogWarning($"TestGenerateInlineImageWithConfig: No images received. FilteredReason: {response.FilteredReason}. This might be a PASS if the prompt was designed to be filtered.");
      }
      else {
        Debug.LogError($"TestGenerateInlineImageWithConfig: FAIL - No images received and no FilteredReason provided.");
      }
    } catch (System.Exception e) {
      Debug.LogError($"TestGenerateInlineImageWithConfig: FAILED with exception: {e}");
    }
  }

  async Task TestGenerateInlineImageWithSafetySettings() {
    Debug.Log("TestGenerateInlineImageWithSafetySettings: Starting...");
    if (ai == null) {
      Debug.LogError("TestGenerateInlineImageWithSafetySettings: FirebaseAI not initialized.");
      return;
    }

    // Example: Block potentially sensitive content to a high degree
    var safety = new ImagenSafetySettings(
        safetyFilterLevel: ImagenSafetySettings.SafetyFilterLevel.BlockMediumAndAbove,
        personFilterLevel: ImagenSafetySettings.PersonFilterLevel.BlockAll
    );
    var model = ai.GetImagenModel(modelName: TestImagenModelName, safetySettings: safety);
    // This prompt is neutral, but safety settings are applied.
    // To properly test filtering, a prompt designed to be filtered would be needed,
    // and then checking response.FilteredReason would be the main assertion.
    var prompt = "A simple landscape with a house and a tree.";

    try {
      var response = await model.GenerateImagesAsync(prompt: prompt);

      if (response.FilteredReason != null) {
        Debug.LogWarning($"TestGenerateInlineImageWithSafetySettings: Images were filtered. Reason: {response.FilteredReason}. This is the expected outcome if the prompt triggered safety filters.");
        Debug.Log("TestGenerateInlineImageWithSafetySettings: PASS (Filtered as potentially expected with strict settings)");
      } else if (response.Images != null && response.Images.Count > 0) {
        Debug.Log($"TestGenerateInlineImageWithSafetySettings: Received {response.Images.Count} image(s). Prompt did not trigger strict safety filters or filters are not aggressive for this prompt.");
        // This is also a valid outcome if the prompt is truly benign.
        Debug.Log("TestGenerateInlineImageWithSafetySettings: PASS (Images generated, prompt was considered safe)");
      } else {
        Debug.LogError($"TestGenerateInlineImageWithSafetySettings: FAIL - No images and no filtered reason.");
      }
    } catch (System.Exception e) {
      Debug.LogError($"TestGenerateInlineImageWithSafetySettings: FAILED with exception: {e}");
    }
  }

  async Task TestGenerateGcsImageSimple() {
    Debug.Log("TestGenerateGcsImageSimple: Starting...");
    if (ai == null) {
      Debug.LogError("TestGenerateGcsImageSimple: FirebaseAI not initialized.");
      return;
    }

    var model = ai.GetImagenModel(TestImagenModelName);
    var prompt = "A detailed schematic of a futuristic spacecraft, GCS output.";
    // Ensure TestGcsBucketPath ends with a '/'
    var gcsUri = new System.Uri(TestGcsBucketPath + "gcs_image_test_" + System.DateTime.Now.Ticks + ".png");

    try {
      // This test relies on the service account having write permissions to the GCS bucket.
      // In many automated test environments, this might be hard to guarantee or test directly.
      // The primary check here is that the API call doesn't fail and returns a GCS URI.
      var response = await model.GenerateImagesAsync(prompt: prompt, gcsUri: gcsUri);

      if (response.Images != null && response.Images.Count > 0) {
        Debug.Log($"TestGenerateGcsImageSimple: Received {response.Images.Count} GCS image reference(s).");
        bool allUrisValid = true;
        foreach (var image in response.Images) {
          if (image.GcsUri != null && image.GcsUri.ToString().StartsWith("gs://")) {
            Debug.Log($"TestGenerateGcsImageSimple: GCS Image URI is valid: {image.GcsUri} (MIME: {image.MimeType}).");
            // Note: We can't easily verify the content of the GCS URI here without GCS client libs.
          } else {
            Debug.LogError("TestGenerateGcsImageSimple: GCS Image URI is null or invalid.");
            allUrisValid = false;
          }
        }
        if (allUrisValid) Debug.Log("TestGenerateGcsImageSimple: PASS (API call succeeded, GCS URIs look valid)");
        else Debug.LogError("TestGenerateGcsImageSimple: FAIL (One or more GCS URIs were invalid, see logs)");

      } else if (response.FilteredReason != null) {
         Debug.LogWarning($"TestGenerateGcsImageSimple: No GCS images generated. FilteredReason: {response.FilteredReason}.");
      }
      else {
        Debug.LogError($"TestGenerateGcsImageSimple: FAIL - No GCS image references received and no FilteredReason provided.");
      }
    } catch (System.Exception e) {
      // This could be due to various reasons: no permission to GCS bucket, invalid model, API errors.
      Debug.LogError($"TestGenerateGcsImageSimple: FAILED with exception: {e}. " +
                     "Ensure the GCS bucket and path are correctly configured and the service account has write permissions.");
    }
  }
  // Helper to ensure Firebase.Extensions.TaskExtension.ContinueWithOnMainThread is available
  // This can be called in Start() if there are issues with ContinueWithOnMainThread.
  void CheckExtensions() {
    #if !FIREBASE_EXTENSIONS_PRESENT  // Define this if you check for extensions explicitly
    if (System.Type.GetType("Firebase.Extensions.TaskExtension, Firebase.TaskExtension") == null) {
        Debug.LogError("Firebase.Extensions.TaskExtension not found. " +
                       "Please ensure Firebase Extensions (Firebase.TaskExtension.dll) is part of your project.");
    }
    #endif
  }
}
