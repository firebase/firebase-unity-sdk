Get Started with Firebase AI
===========================================

Thank you for installing the Firebase AI Unity SDK.

The Firebase AI SDK for Unity gives you access to Google's state-of-the-art generative AI models. This SDK is built specifically for use with Unity and mobile developers, offering security options against unauthorized clients as well as integrations with other Firebase services.

With this, you can add AI personalization to your app, build an AI chat experience, create AI-powered optimizations and automation, generate images, and much more!

### Links

* [Firebase Games Homepage](https://firebase.google.com/games/)
* [Vertex AI Homepage](https://firebase.google.com/docs/vertex-ai)
* [Contact](https://firebase.google.com/support/contact/)
* [Firebase Unity GitHub](https://github.com/firebase/firebase-unity-sdk)

##### Discussion

* [Stack overflow](https://stackoverflow.com/questions/tagged/firebase)
* [Slack community](https://firebase-community.slack.com/)
* [Google groups](https://groups.google.com/forum/#!forum/firebase-talk)

## Available Models

The Firebase AI SDK for Unity currently supports the following model families:

### Gemini API

The Firebase AI Gemini API gives you access to the latest generative AI models from Google: the Gemini models. These models are excellent for text generation, summarization, chat applications, and more.

_(Refer to the [Firebase documentation](https://firebase.google.com/docs/vertex-ai/gemini-models) for more detailed examples on using the Gemini API.)_

### Imagen API

The Firebase AI Imagen API allows you to generate and manipulate images using Google's advanced image generation models. You can create novel images from text prompts, edit existing images, and more.

#### Initializing ImagenModel

First, initialize `FirebaseAI` and then get an `ImagenModel` instance. You can optionally provide generation configuration and safety settings at this stage.

```csharp
using Firebase;
using Firebase.AI;
using UnityEngine; // Required for Debug.Log and Texture2D

public class ImagenExample : MonoBehaviour
{
    async void Start()
    {
        FirebaseApp app = FirebaseApp.DefaultInstance; // Or your specific app

        // Initialize the Vertex AI backend service (recommended for Imagen)
        var ai = FirebaseAI.GetInstance(app, FirebaseAI.Backend.VertexAI());

        // Create an `ImagenModel` instance with a model that supports your use case
        // Consult Imagen documentation for the latest model names.
        var model = ai.GetImagenModel(
          modelName: "imagen-3.0-generate-002", // Example model name, replace with a valid one
          generationConfig: new ImagenGenerationConfig(numberOfImages: 1)); // Request 1 image

        // Provide an image generation prompt
        var prompt = "A photo of a futuristic car driving on Mars at sunset.";

        // To generate an image and receive it as inline data (byte array)
        var response = await model.GenerateImagesAsync(prompt: prompt);

        // If fewer images were generated than were requested,
        // then `filteredReason` will describe the reason they were filtered out
        if (!string.IsNullOrEmpty(response.FilteredReason)) {
          UnityEngine.Debug.Log($"Image generation partially filtered: {response.FilteredReason}");
        }

        if (response.Images != null && response.Images.Count > 0)
        {
            foreach (var image in response.Images) {
              // Assuming image is ImagenInlineImage
              Texture2D tex = image.AsTexture2D();
              if (tex != null)
              {
                  UnityEngine.Debug.Log($"Image generated with MIME type: {image.MimeType}, Size: {tex.width}x{tex.height}");
                  // Process the image (e.g., display it on a UI RawImage)
                  // Example: rawImageComponent.texture = tex;
              }
            }
        }
        else
        {
            UnityEngine.Debug.Log("No images were generated. Check FilteredReason or logs for more details.");
        }
    }
}
```

#### Generating Images to Google Cloud Storage (GCS)

Imagen can also output generated images directly to a Google Cloud Storage bucket. This is useful for workflows where images don't need to be immediately processed on the client.

```csharp
// (Inside an async method, assuming 'model' is an initialized ImagenModel)
var gcsUri = new System.Uri("gs://your-gcs-bucket-name/path/to/output_image.png");
var gcsResponse = await model.GenerateImagesAsync(prompt: "A fantasy castle in the clouds", gcsUri: gcsUri);

if (gcsResponse.Images != null && gcsResponse.Images.Count > 0) {
    foreach (var imageRef in gcsResponse.Images) {
        // imageRef will be an ImagenGcsImage instance
        UnityEngine.Debug.Log($"Image generation requested to GCS. Output URI: {imageRef.GcsUri}, MIME Type: {imageRef.MimeType}");
        // Further processing might involve triggering a cloud function or another backend process
        // that reads from this GCS URI.
    }
}
```

#### Configuration Options

When working with Imagen, you can customize the generation process using several configuration structs:

*   **`ImagenGenerationConfig`**: Controls aspects like the number of images to generate (`NumberOfImages`), the desired aspect ratio (`ImagenAspectRatio`), the output image format (`ImagenImageFormat`), and whether to add a watermark (`AddWatermark`). You can also specify a `NegativePrompt`.
*   **`ImagenSafetySettings`**: Allows you to configure safety filters for generated content, such as `SafetyFilterLevel` (e.g., `BlockMediumAndAbove`) and `PersonFilterLevel` (e.g., `BlockAll`).
*   **`ImagenImageFormat`**: Defines the output image format. Use static methods like `ImagenImageFormat.Png()` or `ImagenImageFormat.Jpeg(int? compressionQuality = null)`.
*   **`ImagenAspectRatio`**: An enum to specify common aspect ratios like `Square1x1`, `Portrait9x16`, etc.

These configuration types are available in the `Firebase.AI` namespace. Refer to the API documentation or inline comments in the SDK for more details on their usage.
