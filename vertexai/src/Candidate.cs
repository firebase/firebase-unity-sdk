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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Firebase.VertexAI {

public enum FinishReason {
  Unknown,
  Stop,
  MaxTokens,
  Safety,
  Recitation,
  Other,
  Blocklist,
  ProhibitedContent,
  SPII,
  MalformedFunctionCall,
}

/// <summary>
/// A struct representing a possible reply to a content generation prompt.
/// Each content generation prompt may produce multiple candidate responses.
/// </summary>
public readonly struct Candidate {
  private readonly ReadOnlyCollection<SafetyRating> _safetyRatings;

  /// <summary>
  /// The response’s content.
  /// </summary>
  public ModelContent Content { get; }

  /// <summary>
  /// The safety rating of the response content.
  /// </summary>
  public IEnumerable<SafetyRating> SafetyRatings =>
      _safetyRatings ?? new ReadOnlyCollection<SafetyRating>(new List<SafetyRating>());

  /// <summary>
  /// The reason the model stopped generating content, if it exists;
  /// for example, if the model generated a predefined stop sequence.
  /// </summary>
  public FinishReason? FinishReason { get; }

  /// <summary>
  /// Cited works in the model’s response content, if it exists.
  /// </summary>
  public CitationMetadata? CitationMetadata { get; }

  // Hidden constructor, users don't need to make this, though they still technically can.
  internal Candidate(ModelContent content, List<SafetyRating> safetyRatings,
      FinishReason? finishReason, CitationMetadata? citationMetadata) {
    Content = content;
    _safetyRatings = new ReadOnlyCollection<SafetyRating>(safetyRatings ?? new List<SafetyRating>());
    FinishReason = finishReason;
    CitationMetadata = citationMetadata;
  }

  internal static Candidate FromJson(Dictionary<string, object> jsonDict) {
    ModelContent content = new();
    if (jsonDict.TryGetValue("content", out object contentObj)) {
      if (contentObj is not Dictionary<string, object> contentDict) {
        throw new VertexAISerializationException("Invalid JSON format: 'content' is not a dictionary.");
      }
      // We expect this to be another dictionary to convert
      content = ModelContent.FromJson(contentDict);
    }

    // TODO: Parse SafetyRatings, FinishReason, and CitationMetadata
    return new Candidate(content, null, null, null);
  }
}

}
