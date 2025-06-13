using System.Collections.Generic;
using System.Linq;
using Google.MiniJSON; // Assuming MiniJSON is available and used elsewhere in the SDK

namespace Firebase.AI {
  public readonly struct ImagenGenerationResponse<T> where T : IImagenImage {
    public IReadOnlyList<T> Images { get; }
    public string FilteredReason { get; }

    // Internal constructor for creating from parsed data
    internal ImagenGenerationResponse(IReadOnlyList<T> images, string filteredReason) {
      Images = images;
      FilteredReason = filteredReason;
    }

    // Static factory method to parse JSON
    // Note: This is a simplified parser. Error handling and robustness should match SDK standards.
    internal static ImagenGenerationResponse<T> FromJson(string jsonString) {
      if (string.IsNullOrEmpty(jsonString)) {
        return new ImagenGenerationResponse<T>(System.Array.Empty<T>(), "Empty or null JSON response");
      }

      object jsonData = Json.Deserialize(jsonString);
      if (!(jsonData is Dictionary<string, object> responseMap)) {
        return new ImagenGenerationResponse<T>(System.Array.Empty<T>(), "Invalid JSON format: Expected a dictionary at the root.");
      }

      List<T> images = new List<T>();
      string filteredReason = responseMap.ContainsKey("filteredReason") ? responseMap["filteredReason"] as string : null;

      if (responseMap.ContainsKey("images") && responseMap["images"] is List<object> imagesList) {
        foreach (var imgObj in imagesList) {
          if (imgObj is Dictionary<string, object> imgMap) {
            string mimeType = imgMap.ContainsKey("mimeType") ? imgMap["mimeType"] as string : "application/octet-stream";

            if (typeof(T) == typeof(ImagenInlineImage)) {
              if (imgMap.ContainsKey("imageBytes") && imgMap["imageBytes"] is string base64Data) {
                byte[] data = System.Convert.FromBase64String(base64Data);
                images.Add((T)(IImagenImage)new ImagenInlineImage(mimeType, data));
              }
            } else if (typeof(T) == typeof(ImagenGcsImage)) {
              if (imgMap.ContainsKey("gcsUri") && imgMap["gcsUri"] is string uriString) {
                if (System.Uri.TryCreate(uriString, System.UriKind.Absolute, out System.Uri gcsUri)) {
                   images.Add((T)(IImagenImage)new ImagenGcsImage(mimeType, gcsUri));
                }
              }
            }
          }
        }
      }

      // If no specific images are found, but there's a top-level "image" field (for single image responses)
      // This part might need adjustment based on actual API response for single vs multiple images.
      // The provided API doc implies a list `Images` always.
      // For now, sticking to the `images` list.

      return new ImagenGenerationResponse<T>(images.AsReadOnly(), filteredReason);
    }
  }
}
