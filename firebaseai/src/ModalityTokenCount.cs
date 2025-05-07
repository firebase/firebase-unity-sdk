/*
 * Copyright 2025 Google LLC
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
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// Content part modality.
/// </summary>
public enum ContentModality {
  /// <summary>
  /// A new and not yet supported value.
  /// </summary>
  Unknown = 0,
  /// <summary>
  /// Plain text.
  /// </summary>
  Text,
  /// <summary>
  /// Image.
  /// </summary>
  Image,
  /// <summary>
  /// Video.
  /// </summary>
  Video,
  /// <summary>
  /// Audio.
  /// </summary>
  Audio,
  /// <summary>
  /// Document, e.g. PDF.
  /// </summary>
  Document,
}

/// <summary>
/// Represents token counting info for a single modality.
/// </summary>
public readonly struct ModalityTokenCount {
  /// <summary>
  /// The modality associated with this token count.
  /// </summary>
  public ContentModality Modality { get; }
  /// <summary>
  /// The number of tokens counted.
  /// </summary>
  public int TokenCount { get; }

  // Hidden constructor, users don't need to make this
  private ModalityTokenCount(ContentModality modality, int tokenCount) {
    Modality = modality;
    TokenCount = tokenCount;
  }

  private static ContentModality ParseModality(string str) {
    return str switch {
      "TEXT" => ContentModality.Text,
      "IMAGE" => ContentModality.Image,
      "VIDEO" => ContentModality.Video,
      "AUDIO" => ContentModality.Audio,
      "DOCUMENT" => ContentModality.Document,
      _ => ContentModality.Unknown,
    };
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static ModalityTokenCount FromJson(Dictionary<string, object> jsonDict) {
    return new ModalityTokenCount(
      jsonDict.ParseEnum("modality", ParseModality),
      jsonDict.ParseValue<int>("tokenCount"));
  }
}

}
