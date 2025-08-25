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
using System.Linq;
using Google.MiniJSON;
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// The model's response to a generate content request.
/// </summary>
public readonly struct GenerateContentResponse {
  private readonly IReadOnlyList<Candidate> _candidates;

  /// <summary>
  /// A list of candidate response content, ordered from best to worst.
  /// </summary>
  public IReadOnlyList<Candidate> Candidates {
    get {
      return _candidates ?? new List<Candidate>();
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
      // Concatenate all of the text parts that aren't thoughts from the first candidate.
      return string.Join(" ",
          Candidates.FirstOrDefault().Content.Parts
          .OfType<ModelContent.TextPart>().Where(tp => !tp.IsThought).Select(tp => tp.Text));
    }
  }
  
  /// <summary>
  /// A summary of the model's thinking process, if available.
  /// 
  /// Note that Thought Summaries are only available when `IncludeThoughts` is enabled
  /// in the `ThinkingConfig`. For more information, see the
  /// [Thinking](https://firebase.google.com/docs/ai-logic/thinking) documentation.
  /// </summary>
  public string ThoughtSummary {
    get {
      // Concatenate all of the text parts that are thoughts from the first candidate.
      return string.Join(" ",
          Candidates.FirstOrDefault().Content.Parts
          .OfType<ModelContent.TextPart>().Where(tp => tp.IsThought).Select(tp => tp.Text));
    }
  }

  /// <summary>
  /// Returns function calls found in any `Part`s of the first candidate of the response, if any.
  /// </summary>
  public IReadOnlyList<ModelContent.FunctionCallPart> FunctionCalls {
    get {
      return Candidates.FirstOrDefault().Content.Parts
          .OfType<ModelContent.FunctionCallPart>().Where(tp => !tp.IsThought).ToList();
    }
  }

  // Hidden constructor, users don't need to make this.
  private GenerateContentResponse(List<Candidate> candidates, PromptFeedback? promptFeedback,
      UsageMetadata? usageMetadata) {
    _candidates = candidates;
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
  private readonly IReadOnlyList<SafetyRating> _safetyRatings;

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
  public IReadOnlyList<SafetyRating> SafetyRatings {
    get {
      return _safetyRatings ?? new List<SafetyRating>();
    }
  }

  // Hidden constructor, users don't need to make this.
  private PromptFeedback(BlockReason? blockReason, string blockReasonMessage,
                         List<SafetyRating> safetyRatings) {
    BlockReason = blockReason;
    BlockReasonMessage = blockReasonMessage;
    _safetyRatings = safetyRatings;
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
/// Metadata returned to the client when grounding is enabled.
///
/// > Important: If using Grounding with Google Search, you are required to comply with the
/// "Grounding with Google Search" usage requirements for your chosen API provider:
/// [Gemini Developer API](https://ai.google.dev/gemini-api/terms#grounding-with-google-search)
/// or Vertex AI Gemini API (see [Service Terms](https://cloud.google.com/terms/service-terms)
/// section within the Service Specific Terms).
/// </summary>
public readonly struct GroundingMetadata {
  private readonly IReadOnlyList<string> _webSearchQueries;
  private readonly IReadOnlyList<GroundingChunk> _groundingChunks;
  private readonly IReadOnlyList<GroundingSupport> _groundingSupports;

  /// <summary>
  /// A list of web search queries that the model performed to gather the grounding information.
  /// These can be used to allow users to explore the search results themselves.
  /// </summary>
  public IReadOnlyList<string> WebSearchQueries {
    get {
      return _webSearchQueries ?? new List<string>();
    }
  }

  /// <summary>
  /// A list of `GroundingChunk` structs. Each chunk represents a piece of retrieved content
  /// (e.g., from a web page) that the model used to ground its response.
  /// </summary>
  public IReadOnlyList<GroundingChunk> GroundingChunks {
    get {
      return _groundingChunks ?? new List<GroundingChunk>();
    }
  }

  /// <summary>
  /// A list of `GroundingSupport` structs. Each object details how specific segments of the
  /// model's response are supported by the `groundingChunks`.
  /// </summary>
  public IReadOnlyList<GroundingSupport> GroundingSupports {
    get {
      return _groundingSupports ?? new List<GroundingSupport>();
    }
  }

  /// <summary>
  /// Google Search entry point for web searches.
  /// This contains an HTML/CSS snippet that **must** be embedded in an app to display a Google
  /// Search entry point for follow-up web searches related to the model's "Grounded Response".
  /// </summary>
  public SearchEntryPoint? SearchEntryPoint { get; }

  private GroundingMetadata(List<string> webSearchQueries, List<GroundingChunk> groundingChunks,
                            List<GroundingSupport> groundingSupports, SearchEntryPoint? searchEntryPoint) {
    _webSearchQueries = webSearchQueries;
    _groundingChunks = groundingChunks;
    _groundingSupports = groundingSupports;
    SearchEntryPoint = searchEntryPoint;
  }

  internal static GroundingMetadata FromJson(Dictionary<string, object> jsonDict) {
    List<GroundingSupport> supports = null;
    if (jsonDict.TryParseValue("groundingSupports", out List<object> supportListRaw))
    {
      supports = supportListRaw
          .OfType<Dictionary<string, object>>()
          .Where(d => d.ContainsKey("segment")) // Filter out if segment is missing
          .Select(GroundingSupport.FromJson)
          .ToList();
    }

    return new GroundingMetadata(
      jsonDict.ParseStringList("webSearchQueries"),
      jsonDict.ParseObjectList("groundingChunks", GroundingChunk.FromJson),
      supports,
      jsonDict.ParseNullableObject("searchEntryPoint", Firebase.AI.SearchEntryPoint.FromJson)
    );
  }
}

/// <summary>
/// A struct representing the Google Search entry point.
/// </summary>
public readonly struct SearchEntryPoint {
  /// <summary>
  /// An HTML/CSS snippet that can be embedded in your app.
  ///
  /// To ensure proper rendering, it's recommended to display this content within a web view component.
  /// </summary>
  public string RenderedContent { get; }

  private SearchEntryPoint(string renderedContent) {
    RenderedContent = renderedContent;
  }

  internal static SearchEntryPoint FromJson(Dictionary<string, object> jsonDict) {
    return new SearchEntryPoint(
      jsonDict.ParseValue<string>("renderedContent", JsonParseOptions.ThrowEverything)
    );
  }
}

/// <summary>
/// Represents a chunk of retrieved data that supports a claim in the model's response. This is
/// part of the grounding information provided when grounding is enabled.
/// </summary>
public readonly struct GroundingChunk {
  /// <summary>
  /// Contains details if the grounding chunk is from a web source.
  /// </summary>
  public WebGroundingChunk? Web { get; }

  private GroundingChunk(WebGroundingChunk? web) {
    Web = web;
  }

  internal static GroundingChunk FromJson(Dictionary<string, object> jsonDict) {
    return new GroundingChunk(
      jsonDict.ParseNullableObject("web", WebGroundingChunk.FromJson)
    );
  }
}

/// <summary>
/// A grounding chunk sourced from the web.
/// </summary>
public readonly struct WebGroundingChunk {
  /// <summary>
  /// The URI of the retrieved web page.
  /// </summary>
  public System.Uri Uri { get; }
  /// <summary>
  /// The title of the retrieved web page.
  /// </summary>
  public string Title { get; }
  /// <summary>
  /// The domain of the original URI from which the content was retrieved.
  ///
  /// This field is only populated when using the Vertex AI Gemini API.
  /// </summary>
  public string Domain { get; }

  private WebGroundingChunk(System.Uri uri, string title, string domain) {
    Uri = uri;
    Title = title;
    Domain = domain;
  }

  internal static WebGroundingChunk FromJson(Dictionary<string, object> jsonDict) {
    Uri uri = null;
    if (jsonDict.TryParseValue("uri", out string uriString)) {
      uri = new Uri(uriString);
    }

    return new WebGroundingChunk(
      uri,
      jsonDict.ParseValue<string>("title"),
      jsonDict.ParseValue<string>("domain")
    );
  }
}

/// <summary>
/// Provides information about how a specific segment of the model's response is supported by the
/// retrieved grounding chunks.
/// </summary>
public readonly struct GroundingSupport {
  private readonly IReadOnlyList<int> _groundingChunkIndices;

  /// <summary>
  /// Specifies the segment of the model's response content that this grounding support pertains
  /// to.
  /// </summary>
  public Segment Segment { get; }

  /// <summary>
  /// A list of indices that refer to specific `GroundingChunk` structs within the
  /// `GroundingMetadata.GroundingChunks` array. These referenced chunks are the sources that
  /// support the claim made in the associated `segment` of the response. For example, an array
  /// `[1, 3, 4]`
  /// means that `groundingChunks[1]`, `groundingChunks[3]`, `groundingChunks[4]` are the
  /// retrieved content supporting this part of the response.
  /// </summary>
  public IReadOnlyList<int> GroundingChunkIndices {
    get {
      return _groundingChunkIndices ?? new List<int>();
    }
  }

  private GroundingSupport(Segment segment, List<int> groundingChunkIndices) {
    Segment = segment;
    _groundingChunkIndices = groundingChunkIndices;
  }

  internal static GroundingSupport FromJson(Dictionary<string, object> jsonDict) {
    List<int> indices = new List<int>();
    if (jsonDict.TryParseValue("groundingChunkIndices", out List<object> indicesRaw)) {
      indices = indicesRaw.OfType<long>().Select(l => (int)l).ToList();
    }

    return new GroundingSupport(
      jsonDict.ParseObject("segment", Segment.FromJson, JsonParseOptions.ThrowEverything),
      indices
    );
  }
}

/// <summary>
/// Represents a specific segment within a `ModelContent` struct, often used to pinpoint the
/// exact location of text or data that grounding information refers to.
/// </summary>
public readonly struct Segment {
  /// <summary>
  /// The zero-based index of the `Part` object within the `parts` array of its parent
  /// `ModelContent` object. This identifies which part of the content the segment belongs to.
  /// </summary>
  public int PartIndex { get; }
  /// <summary>
  /// The zero-based start index of the segment within the specified `Part`, measured in UTF-8
  /// bytes. This offset is inclusive, starting from 0 at the beginning of the part's content.
  /// </summary>
  public int StartIndex { get; }
  /// <summary>
  /// The zero-based end index of the segment within the specified `Part`, measured in UTF-8
  /// bytes. This offset is exclusive, meaning the character at this index is not included in the
  /// segment.
  /// </summary>
  public int EndIndex { get; }
  /// <summary>
  /// The text corresponding to the segment from the response.
  /// </summary>
  public string Text { get; }

  private Segment(int partIndex, int startIndex, int endIndex, string text) {
    PartIndex = partIndex;
    StartIndex = startIndex;
    EndIndex = endIndex;
    Text = text;
  }

  internal static Segment FromJson(Dictionary<string, object> jsonDict) {
    return new Segment(
      jsonDict.ParseValue<int>("partIndex"),
      jsonDict.ParseValue<int>("startIndex"),
      jsonDict.ParseValue<int>("endIndex"),
      jsonDict.ParseValue<string>("text")
    );
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
  /// The number of tokens used by the model's internal "thinking" process.
  ///
  /// For models that support thinking (like Gemini 2.5 Pro and Flash), this represents the actual
  /// number of tokens consumed for reasoning before the model generated a response. For models
  /// that do not support thinking, this value will be `0`.
  ///
  /// When thinking is used, this count will be less than or equal to the `thinkingBudget` set in
  /// the `ThinkingConfig`.
  /// </summary>
  public int ThoughtsTokenCount { get; }
  /// <summary>
  /// The total number of tokens in both the request and response.
  /// </summary>
  public int TotalTokenCount { get; }

  private readonly IReadOnlyList<ModalityTokenCount> _promptTokensDetails;
  public IReadOnlyList<ModalityTokenCount> PromptTokensDetails {
    get {
      return _promptTokensDetails ?? new List<ModalityTokenCount>();
    }
  }

  private readonly IReadOnlyList<ModalityTokenCount> _candidatesTokensDetails;
  public IReadOnlyList<ModalityTokenCount> CandidatesTokensDetails {
    get {
      return _candidatesTokensDetails ?? new List<ModalityTokenCount>();
    }
  }

  // Hidden constructor, users don't need to make this.
  private UsageMetadata(int promptTC, int candidatesTC, int thoughtsTC, int totalTC,
                        List<ModalityTokenCount> promptDetails, List<ModalityTokenCount> candidateDetails) {
    PromptTokenCount = promptTC;
    CandidatesTokenCount = candidatesTC;
    ThoughtsTokenCount = thoughtsTC;
    TotalTokenCount = totalTC;
    _promptTokensDetails = promptDetails;
    _candidatesTokensDetails = candidateDetails;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static UsageMetadata FromJson(Dictionary<string, object> jsonDict) {
    return new UsageMetadata(
      jsonDict.ParseValue<int>("promptTokenCount"),
      jsonDict.ParseValue<int>("candidatesTokenCount"),
      jsonDict.ParseValue<int>("thoughtsTokenCount"),
      jsonDict.ParseValue<int>("totalTokenCount"),
      jsonDict.ParseObjectList("promptTokensDetails", ModalityTokenCount.FromJson),
      jsonDict.ParseObjectList("candidatesTokensDetails", ModalityTokenCount.FromJson));
  }
}

}
