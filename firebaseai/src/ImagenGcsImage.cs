using System;

namespace Firebase.AI {
  public readonly struct ImagenGcsImage : IImagenImage {
    public string MimeType { get; }
    public System.Uri GcsUri { get; }

    public ImagenGcsImage(string mimeType, System.Uri gcsUri) {
      MimeType = mimeType;
      GcsUri = gcsUri;
    }
  }
}
