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
    public enum AspectRatio
    {
      Square1x1,
      Portrait9x16,
      Landscape16x9,
      Portrait3x4,
      Landscape4x3,
      Portrait2x3,
      Landscape3x2,
      Portrait4x5,
      Landscape5x4,
      Portrait1x4,
      Landscape4x1,
      Portrait1x8,
      Landscape8x1,
      Ultrawide21x9
    }

    /// <summary>
    /// The size of images to generate.
    /// </summary>
    public enum ImageSize
    {
      Size512,
      Size1K,
      Size2K,
      Size4K
    }

    public AspectRatio? Ratio { get; }
    public ImageSize? Size { get; }

    public ImageConfig(AspectRatio? aspectRatio = null, ImageSize? imageSize = null)
    {
      Ratio = aspectRatio;
      Size = imageSize;
    }

    internal static string ConvertAspectRatio(AspectRatio aspectRatio)
    {
      return aspectRatio switch
      {
        AspectRatio.Square1x1 => "1:1",
        AspectRatio.Portrait9x16 => "9:16",
        AspectRatio.Landscape16x9 => "16:9",
        AspectRatio.Portrait3x4 => "3:4",
        AspectRatio.Landscape4x3 => "4:3",
        AspectRatio.Portrait2x3 => "2:3",
        AspectRatio.Landscape3x2 => "3:2",
        AspectRatio.Portrait4x5 => "4:5",
        AspectRatio.Landscape5x4 => "5:4",
        AspectRatio.Portrait1x4 => "1:4",
        AspectRatio.Landscape4x1 => "4:1",
        AspectRatio.Portrait1x8 => "1:8",
        AspectRatio.Landscape8x1 => "8:1",
        AspectRatio.Ultrawide21x9 => "21:9",
        _ => aspectRatio.ToString(),
      };
    }

    internal static string ConvertImageSize(ImageSize imageSize)
    {
      return imageSize switch
      {
        ImageSize.Size512 => "512",
        ImageSize.Size1K => "1K",
        ImageSize.Size2K => "2K",
        ImageSize.Size4K => "4K",
        _ => imageSize.ToString(),
      };
    }

    internal Dictionary<string, object> ToJson()
    {
      Dictionary<string, object> jsonDict = new();
      if (Ratio.HasValue) jsonDict["aspectRatio"] = ConvertAspectRatio(Ratio.Value);
      if (Size.HasValue) jsonDict["imageSize"] = ConvertImageSize(Size.Value);
      return jsonDict;
    }
  }
}
