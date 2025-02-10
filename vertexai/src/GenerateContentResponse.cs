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

namespace Firebase.VertexAI {

public readonly struct GenerateContentResponse {
  public IEnumerable<Candidate> Candidates { get; }
  public PromptFeedback? PromptFeedback { get; }
  public UsageMetadata? UsageMetadata { get; }

  // Helper properties
  // The response's content as text, if it exists
  public string Text { get; }

  // Returns function calls found in any Parts of the first candidate of the response, if any.
  public IEnumerable<ModelContent.FunctionCallPart> FunctionCalls { get; }
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
