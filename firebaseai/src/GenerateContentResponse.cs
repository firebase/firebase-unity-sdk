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
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// The model's response to a generate content request.
/// </summary>
public readonly struct GenerateContentResponse {
  private readonly ReadOnlyCollection<Candidate> _candidates;

  /// <summary>
  /// A list of candidate response content, ordered from best to worst.
  /// </summary>
  public IEnumerable<Candidate> Candidates {
    get {
      return _candidates ?? new ReadOnlyCollection<Candidate>(new List<Candidate>());
    }
  }

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
  /// The response's content as text, if it exists.
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
  /// Returns function calls found in any `Part`s of the first candidate of the response, if any.
  /// </summary>
  public IEnumerable<ModelContent.FunctionCallPart> FunctionCalls {
    get {
      return Candidates.FirstOrDefault().Content.Parts.OfType<ModelContent.FunctionCallPart>();
    }
  }

  // Hidden constructor, users don't need to make this.
  private GenerateContentResponse(List<Candidate> candidates, PromptFeedback? promptFeedback,
      UsageMetadata? usageMetadata) {
    _candidates = new ReadOnlyCollection<Candidate>(candidates ?? new List<Candidate>());
    PromptFeedback = promptFeedback;
    UsageMetadata = usageMetadata;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static GenerateContentResponse FromJson(string jsonString,
      FirebaseAI.Backend.InternalProvider backend) {
    return FromJson(Json.Deserialize(jsonString) as Dictionary<string, object>, backend);
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static GenerateContentResponse FromJson(Dictionary<string, object> jsonDict,
      FirebaseAI.Backend.InternalProvider backend) {
    return new GenerateContentResponse(
      jsonDict.ParseObjectList("candidates", (d) => Candidate.FromJson(d, backend)),
      jsonDict.ParseNullableObject("promptFeedback",
          Firebase.AI.PromptFeedback.FromJson),
      jsonDict.ParseNullableObject("usageMetadata",
          Firebase.AI.UsageMetadata.FromJson));
  }
}

/// <summary>
/// A type describing possible reasons to block a prompt.
/// </summary>
public enum BlockReason {
  /// <summary>
  /// A new and not yet supported value.
  /// </summary>
  Unknown = 0,
  /// <summary>
  /// The prompt was blocked because it was deemed unsafe.
  /// </summary>
  Safety,
  /// <summary>
  /// All other block reasons.
  /// </summary>
  Other,
  /// <summary>
  /// The prompt was blocked because it contained terms from the terminology blocklist.
  /// </summary>
  Blocklist,
  /// <summary>
  /// The prompt was blocked due to prohibited content.
  /// </summary>
  ProhibitedContent,
}

/// <summary>
/// A metadata struct containing any feedback the model had on the prompt it was provided.
/// </summary>
public readonly struct PromptFeedback {
  private readonly ReadOnlyCollection<SafetyRating> _safetyRatings;

  /// <summary>
  /// The reason a prompt was blocked, if it was blocked.
  /// </summary>
  public BlockReason? BlockReason { get; }
  /// <summary>
  /// A human-readable description of the `BlockReason`.
  /// </summary>
  public string BlockReasonMessage { get; }
  /// <summary>
  /// The safety ratings of the prompt.
  /// </summary>
  public IEnumerable<SafetyRating> SafetyRatings {
    get {
      return _safetyRatings ?? new ReadOnlyCollection<SafetyRating>(new List<SafetyRating>());
    }
  }

  // Hidden constructor, users don't need to make this.
  private PromptFeedback(BlockReason? blockReason, string blockReasonMessage,
                         List<SafetyRating> safetyRatings) {
    BlockReason = blockReason;
    BlockReasonMessage = blockReasonMessage;
    _safetyRatings = new ReadOnlyCollection<SafetyRating>(safetyRatings ?? new List<SafetyRating>());
  }

  private static BlockReason ParseBlockReason(string str) {
    return str switch {
      "SAFETY" => Firebase.AI.BlockReason.Safety,
      "OTHER" => Firebase.AI.BlockReason.Other,
      "BLOCKLIST" => Firebase.AI.BlockReason.Blocklist,
      "PROHIBITED_CONTENT" => Firebase.AI.BlockReason.ProhibitedContent,
      _ => Firebase.AI.BlockReason.Unknown,
    };
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static PromptFeedback FromJson(Dictionary<string, object> jsonDict) {
    return new PromptFeedback(
      jsonDict.ParseNullableEnum("blockReason", ParseBlockReason),
      jsonDict.ParseValue<string>("blockReasonMessage"),
      jsonDict.ParseObjectList("safetyRatings", SafetyRating.FromJson));
  }
}

/// <summary>
/// Token usage metadata for processing the generate content request.
/// </summary>
public readonly struct UsageMetadata {
  /// <summary>
  /// The number of tokens in the request prompt.
  /// </summary>
  public int PromptTokenCount { get; }
  /// <summary>
  /// The total number of tokens across the generated response candidates.
  /// </summary>
  public int CandidatesTokenCount { get; }
  /// <summary>
  /// The total number of tokens in both the request and response.
  /// </summary>
  public int TotalTokenCount { get; }

  private readonly ReadOnlyCollection<ModalityTokenCount> _promptTokensDetails;
  public IEnumerable<ModalityTokenCount> PromptTokensDetails {
    get {
      return _promptTokensDetails ?? new ReadOnlyCollection<ModalityTokenCount>(new List<ModalityTokenCount>());
    }
  }

  private readonly ReadOnlyCollection<ModalityTokenCount> _candidatesTokensDetails;
  public IEnumerable<ModalityTokenCount> CandidatesTokensDetails {
    get {
      return _candidatesTokensDetails ?? new ReadOnlyCollection<ModalityTokenCount>(new List<ModalityTokenCount>());
    }
  }

  // Hidden constructor, users don't need to make this.
  private UsageMetadata(int promptTC, int candidatesTC, int totalTC,
                        List<ModalityTokenCount> promptDetails, List<ModalityTokenCount> candidateDetails) {
    PromptTokenCount = promptTC;
    CandidatesTokenCount = candidatesTC;
    TotalTokenCount = totalTC;
    _promptTokensDetails =
        new ReadOnlyCollection<ModalityTokenCount>(promptDetails ?? new List<ModalityTokenCount>());
    _candidatesTokensDetails =
        new ReadOnlyCollection<ModalityTokenCount>(candidateDetails ?? new List<ModalityTokenCount>());

  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static UsageMetadata FromJson(Dictionary<string, object> jsonDict) {
    return new UsageMetadata(
      jsonDict.ParseValue<int>("promptTokenCount"),
      jsonDict.ParseValue<int>("candidatesTokenCount"),
      jsonDict.ParseValue<int>("totalTokenCount"),
      jsonDict.ParseObjectList("promptTokensDetails", ModalityTokenCount.FromJson),
      jsonDict.ParseObjectList("candidatesTokensDetails", ModalityTokenCount.FromJson));
  }
}

}
