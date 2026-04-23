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
    public readonly struct AspectRatio
    {
      public string Value { get; }

      /// <summary>
      /// Constructs a custom AspectRatio, instead of one of the presets.
      /// Note that the backend model needs to support the requested ratio.
      /// </summary>
      public AspectRatio(string value) { Value = value; }

      public static readonly AspectRatio Square1x1 = new("1:1");
      public static readonly AspectRatio Portrait9x16 = new("9:16");
      public static readonly AspectRatio Landscape16x9 = new("16:9");
      public static readonly AspectRatio Portrait3x4 = new("3:4");
      public static readonly AspectRatio Landscape4x3 = new("4:3");
      public static readonly AspectRatio Portrait2x3 = new("2:3");
      public static readonly AspectRatio Landscape3x2 = new("3:2");
      public static readonly AspectRatio Portrait4x5 = new("4:5");
      public static readonly AspectRatio Landscape5x4 = new("5:4");
      public static readonly AspectRatio Portrait1x4 = new("1:4");
      public static readonly AspectRatio Landscape4x1 = new("4:1");
      public static readonly AspectRatio Portrait1x8 = new("1:8");
      public static readonly AspectRatio Landscape8x1 = new("8:1");
      public static readonly AspectRatio Ultrawide21x9 = new("21:9");

      public override string ToString() => Value;
    }

    /// <summary>
    /// The size of images to generate.
    /// </summary>
    public readonly struct ImageSize
    {
      public string Value { get; }

      /// <summary>
      /// Constructs a custom ImageSize, instead of one of the presets.
      /// Note that the backend model needs to support the requested size.
      /// </summary>
      public ImageSize(string value) { Value = value; }

      public static readonly ImageSize Size512 = new("512");
      public static readonly ImageSize Size1K = new("1K");
      public static readonly ImageSize Size2K = new("2K");
      public static readonly ImageSize Size4K = new("4K");

      public override string ToString() => Value;
    }

    public AspectRatio? Ratio { get; }
    public ImageSize? Size { get; }

    /// <summary>
    /// Creates a new `ImageConfig` with the given settings.
    /// </summary>
    public ImageConfig(AspectRatio? aspectRatio = null, ImageSize? imageSize = null)
    {
      Ratio = aspectRatio;
      Size = imageSize;
    }

    internal Dictionary<string, object> ToJson()
    {
      Dictionary<string, object> jsonDict = new();
      if (Ratio?.Value is string aspectRatio) jsonDict["aspectRatio"] = aspectRatio;
      if (Size?.Value is string imageSize) jsonDict["imageSize"] = imageSize;
      return jsonDict;
    }
  }
}
