using UnityEngine;

namespace Firebase.AI {
  public readonly struct ImagenInlineImage : IImagenImage {
    public string MimeType { get; }
    public byte[] Data { get; }

    public ImagenInlineImage(string mimeType, byte[] data) {
      MimeType = mimeType;
      Data = data;
    }

    public UnityEngine.Texture2D AsTexture2D() {
      // Implementation will be added in a later step.
      // For now, it can return null or throw a NotImplementedException.
      if (Data == null || Data.Length == 0) {
        return null;
      }
      Texture2D tex = new Texture2D(2, 2); // Dimensions will be determined by image data
      // ImageConversion.LoadImage will resize the texture dimensions.
      if (ImageConversion.LoadImage(tex, Data)) {
        return tex;
      }
      return null;
    }
  }
}
