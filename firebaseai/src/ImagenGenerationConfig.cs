namespace Firebase.AI {
  public enum ImagenAspectRatio {
    Square1x1,
    Portrait9x16,
    Landscape16x9,
    Portrait3x4,
    Landscape4x3
  }

  public readonly struct ImagenGenerationConfig {
    public string NegativePrompt { get; }
    public int? NumberOfImages { get; }
    public ImagenAspectRatio? AspectRatio { get; }
    public ImagenImageFormat? ImageFormat { get; }
    public bool? AddWatermark { get; }

    public ImagenGenerationConfig(
      string negativePrompt = null,
      int? numberOfImages = null,
      ImagenAspectRatio? aspectRatio = null,
      ImagenImageFormat? imageFormat = null,
      bool? addWatermark = null
    ) {
      NegativePrompt = negativePrompt;
      NumberOfImages = numberOfImages;
      AspectRatio = aspectRatio;
      ImageFormat = imageFormat;
      AddWatermark = addWatermark;
    }

    // Helper method to convert to JSON dictionary for requests
    internal System.Collections.Generic.Dictionary<string, object> ToJson() {
      var jsonDict = new System.Collections.Generic.Dictionary<string, object>();
      if (!string.IsNullOrEmpty(NegativePrompt)) {
        jsonDict["negativePrompt"] = NegativePrompt;
      }
      if (NumberOfImages.HasValue) {
        jsonDict["numberOfImages"] = NumberOfImages.Value;
      }
      if (AspectRatio.HasValue) {
        jsonDict["aspectRatio"] = AspectRatio.Value.ToString();
      }
      if (ImageFormat.HasValue) {
        jsonDict["imageFormat"] = ImageFormat.Value.ToJson();
      }
      if (AddWatermark.HasValue) {
        jsonDict["addWatermark"] = AddWatermark.Value;
      }
      return jsonDict;
    }
  }
}
