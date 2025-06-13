namespace Firebase.AI {
  public readonly struct ImagenImageFormat {
    public enum FormatType { Png, Jpeg }

    public FormatType Type { get; }
    public int? CompressionQuality { get; } // Nullable for PNG

    private ImagenImageFormat(FormatType type, int? compressionQuality = null) {
      Type = type;
      CompressionQuality = compressionQuality;
    }

    public static ImagenImageFormat Png() {
      return new ImagenImageFormat(FormatType.Png);
    }

    public static ImagenImageFormat Jpeg(int? compressionQuality = null) {
      if (compressionQuality.HasValue && (compressionQuality < 0 || compressionQuality > 100)) {
        throw new System.ArgumentOutOfRangeException(nameof(compressionQuality), "Compression quality must be between 0 and 100.");
      }
      return new ImagenImageFormat(FormatType.Jpeg, compressionQuality);
    }

    // Helper method to convert to JSON dictionary for requests
    internal System.Collections.Generic.Dictionary<string, object> ToJson() {
      var jsonDict = new System.Collections.Generic.Dictionary<string, object>();
      jsonDict["type"] = Type.ToString().ToLowerInvariant();
      if (Type == FormatType.Jpeg && CompressionQuality.HasValue) {
        jsonDict["compressionQuality"] = CompressionQuality.Value;
      }
      return jsonDict;
    }
  }
}
