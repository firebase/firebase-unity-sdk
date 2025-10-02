/*
 * Copyright 2025 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may not obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Firebase.AI {

/// <summary>
/// An aspect ratio for images.
/// </summary>
public enum AspectRatio {
  /// <summary>
  /// Square (1:1) aspect ratio.
  /// </summary>
  SQUARE_1_1,
  /// <summary>
  /// Portrait (2:3) aspect ratio.
  /// </summary>
  PORTRAIT_2_3,
  /// <summary>
  /// Landscape (3:2) aspect ratio.
  /// </summary>
  LANDSCAPE_3_2,
  /// <summary>
  /// Portrait (3:4) aspect ratio.
  /// </summary>
  PORTRAIT_3_4,
  /// <summary>
  /// Landscape (4:3) aspect ratio.
  /// </summary>
  LANDSCAPE_4_3,
  /// <summary>
  /// Portrait (4:5) aspect ratio.
  /// </summary>
  PORTRAIT_4_5,
  /// <summary>
  /// Landscape (5:4) aspect ratio.
  /// </summary>
  LANDSCAPE_5_4,
  /// <summary>
  /// Portrait (9:16) aspect ratio.
  /// </summary>
  PORTRAIT_9_16,
  /// <summary>
  /// Landscape (16:9) aspect ratio.
  /// </summary>
  LANDSCAPE_16_9,
  /// <summary>
  /// Landscape (21:9) aspect ratio.
  /// </summary>
  LANDSCAPE_21_9,
}

/// <summary>
/// Configuration options for generating images.
/// </summary>
public readonly struct ImageConfig {
  /// <summary>
  /// The aspect ratio of generated images.
  /// </summary>
  public AspectRatio? AspectRatio { get; }

  /// <summary>
  /// Initializes configuration options for generating images.
  /// </summary>
  /// <param name="aspectRatio">The aspect ratio of generated images.</param>
  public ImageConfig(AspectRatio? aspectRatio = null) {
    AspectRatio = aspectRatio;
  }

  private static string ConvertAspectRatio(AspectRatio aspectRatio) {
    return aspectRatio switch {
      AspectRatio.SQUARE_1_1 => "1:1",
      AspectRatio.PORTRAIT_2_3 => "2:3",
      AspectRatio.LANDSCAPE_3_2 => "3:2",
      AspectRatio.PORTRAIT_3_4 => "3:4",
      AspectRatio.LANDSCAPE_4_3 => "4:3",
      AspectRatio.PORTRAIT_4_5 => "4:5",
      AspectRatio.LANDSCAPE_5_4 => "5:4",
      AspectRatio.PORTRAIT_9_16 => "9:16",
      AspectRatio.LANDSCAPE_16_9 => "16:9",
      AspectRatio.LANDSCAPE_21_9 => "21:9",
      _ => aspectRatio.ToString(), // Fallback
    };
  }

  internal Dictionary<string, object> ToJson() {
    var jsonDict = new System.Collections.Generic.Dictionary<string, object>();
    if (AspectRatio.HasValue) {
      jsonDict["aspectRatio"] = ConvertAspectRatio(AspectRatio.Value);
    }
    return jsonDict;
  }
}

}
