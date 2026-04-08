/*
 * Copyright 2026 Google LLC
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

using System.Collections.Generic;

namespace Firebase.AI
{
  /// <summary>
  /// Configuration options for generating images with Gemini models.
  /// </summary>
  public readonly struct ImageConfig
  {
    /// <summary>
    /// The aspect ratio of generated images.
    /// </summary>
    public readonly struct AspectRatio : System.IEquatable<AspectRatio>
    {
      public string Value { get; }
      public AspectRatio(string value) { Value = value; }

      public static readonly AspectRatio Square1x1 = new AspectRatio("1:1");
      public static readonly AspectRatio Portrait9x16 = new AspectRatio("9:16");
      public static readonly AspectRatio Landscape16x9 = new AspectRatio("16:9");
      public static readonly AspectRatio Portrait3x4 = new AspectRatio("3:4");
      public static readonly AspectRatio Landscape4x3 = new AspectRatio("4:3");
      public static readonly AspectRatio Portrait2x3 = new AspectRatio("2:3");
      public static readonly AspectRatio Landscape3x2 = new AspectRatio("3:2");
      public static readonly AspectRatio Portrait4x5 = new AspectRatio("4:5");
      public static readonly AspectRatio Landscape5x4 = new AspectRatio("5:4");
      public static readonly AspectRatio Portrait1x4 = new AspectRatio("1:4");
      public static readonly AspectRatio Landscape4x1 = new AspectRatio("4:1");
      public static readonly AspectRatio Portrait1x8 = new AspectRatio("1:8");
      public static readonly AspectRatio Landscape8x1 = new AspectRatio("8:1");
      public static readonly AspectRatio Ultrawide21x9 = new AspectRatio("21:9");

      public override string ToString() => Value;

      public bool Equals(AspectRatio other) => Value == other.Value;
      public override bool Equals(object obj) => obj is AspectRatio other && Equals(other);
      public override int GetHashCode() => Value?.GetHashCode() ?? 0;

      public static bool operator ==(AspectRatio left, AspectRatio right) => left.Equals(right);
      public static bool operator !=(AspectRatio left, AspectRatio right) => !left.Equals(right);
    }

    /// <summary>
    /// The size of images to generate.
    /// </summary>
    public readonly struct ImageSize : System.IEquatable<ImageSize>
    {
      public string Value { get; }
      public ImageSize(string value) { Value = value; }

      public static readonly ImageSize Size512 = new ImageSize("512");
      public static readonly ImageSize Size1K = new ImageSize("1K");
      public static readonly ImageSize Size2K = new ImageSize("2K");
      public static readonly ImageSize Size4K = new ImageSize("4K");

      public override string ToString() => Value;

      public bool Equals(ImageSize other) => Value == other.Value;
      public override bool Equals(object obj) => obj is ImageSize other && Equals(other);
      public override int GetHashCode() => Value?.GetHashCode() ?? 0;

      public static bool operator ==(ImageSize left, ImageSize right) => left.Equals(right);
      public static bool operator !=(ImageSize left, ImageSize right) => !left.Equals(right);
    }

    public AspectRatio? AspectRatio { get; }
    public ImageSize? ImageSize { get; }

    public ImageConfig(AspectRatio? aspectRatio = null, ImageSize? imageSize = null)
    {
      AspectRatio = aspectRatio;
      ImageSize = imageSize;
    }

    internal Dictionary<string, object> ToJson()
    {
      Dictionary<string, object> jsonDict = new();
      if (AspectRatio?.Value != null) jsonDict["aspectRatio"] = AspectRatio.Value.Value;
      if (ImageSize?.Value != null) jsonDict["imageSize"] = ImageSize.Value.Value;
      return jsonDict;
    }
  }
}
