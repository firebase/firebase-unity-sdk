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
using System.Collections.ObjectModel;
using System.Linq;
using Google.MiniJSON;

namespace Firebase.VertexAI {

/// <summary>
/// The model's response to a generate content request.
/// </summary>
public readonly struct GenerateContentResponse {
  private readonly ReadOnlyCollection<Candidate> _candidates;

  /// <summary>
  /// A list of candidate response content, ordered from best to worst.
  /// </summary>
  public IEnumerable<Candidate> Candidates =>
      _candidates ?? new ReadOnlyCollection<Candidate>(new List<Candidate>());

  /// <summary>
  /// A value containing the safety ratings for the response, or,
  /// if the request was blocked, a reason for blocking the request.
  /// </summary>
  public PromptFeedback? PromptFeedback { get; }

  /// <summary>
  /// Token usage metadata for processing the generate content request.
  /// </summary>
  public UsageMetadata? UsageMetadata { get; }

  /// <summary>
  /// The response's content as text, if it exists
  /// </summary>
  public string Text {
    get {
      // Concatenate all of the text parts from the first candidate.
      return string.Join(" ",
          Candidates.FirstOrDefault().Content.Parts
          .OfType<ModelContent.TextPart>().Select(tp => tp.Text));
    }
  }

  /// <summary>
  /// Returns function calls found in any Parts of the first candidate of the response, if any.
  /// </summary>
  public IEnumerable<ModelContent.FunctionCallPart> FunctionCalls {
    get {
      return Candidates.FirstOrDefault().Content.Parts.OfType<ModelContent.FunctionCallPart>();
    }
  }

  // Hidden constructor, users don't need to make this, though they still technically can.
  internal GenerateContentResponse(List<Candidate> candidates, PromptFeedback? promptFeedback,
      UsageMetadata? usageMetadata) {
    _candidates = new ReadOnlyCollection<Candidate>(candidates ?? new List<Candidate>());
    PromptFeedback = promptFeedback;
    UsageMetadata = usageMetadata;
  }

  internal static GenerateContentResponse FromJson(string jsonString) {
    return FromJson(Json.Deserialize(jsonString) as Dictionary<string, object>);
  }

  internal static GenerateContentResponse FromJson(Dictionary<string, object> jsonDict) {
    // Parse the Candidates
    List<Candidate> candidates = new();
    if (jsonDict.TryGetValue("candidates", out object candidatesObject)) {
      if (candidatesObject is not List<object> listOfCandidateObjects) {
        throw new VertexAISerializationException("Invalid JSON format: 'candidates' is not a list.");
      }

      candidates = listOfCandidateObjects
          .Select(o => o as Dictionary<string, object>)
          .Where(dict => dict != null)
          .Select(Candidate.FromJson)
          .ToList();
    }

    // TODO: Parse PromptFeedback and UsageMetadata

    return new GenerateContentResponse(candidates, null, null);
  }
}

public enum BlockReason {
  Unknown,
  Safety,
  Other,
  Blocklist,
  ProhibitedContent,
}

public readonly struct PromptFeedback {
  public BlockReason? BlockReason { get; }
  public string BlockReasonMessage { get; }
  public IEnumerable<SafetyRating> SafetyRatings { get; }

  // Hidden constructor, users don't need to make this
}

public readonly struct UsageMetadata {
  public int PromptTokenCount { get; }
  public int CandidatesTokenCount { get; }
  public int TotalTokenCount { get; }

  // Hidden constructor, users don't need to make this
}

}
