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
using Google.MiniJSON;
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// The model's response to a count tokens request.
/// </summary>
public readonly struct CountTokensResponse {
  /// <summary>
  /// The total number of tokens in the input given to the model as a prompt.
  /// </summary>
  public int TotalTokens { get; }
  /// <summary>
  /// The total number of billable characters in the text input given to the model as a prompt.
  ///
  /// > Important: This does not include billable image, video or other non-text input. See
  /// [Firebase AI pricing](https://firebase.google.com/docs/vertex-ai/pricing) for details.
  /// </summary>
  public int? TotalBillableCharacters { get; }

  private readonly ReadOnlyCollection<ModalityTokenCount> _promptTokensDetails;
  /// <summary>
  /// The breakdown, by modality, of how many tokens are consumed by the prompt.
  /// </summary>
  public IEnumerable<ModalityTokenCount> PromptTokensDetails {
    get {
      return _promptTokensDetails ?? new ReadOnlyCollection<ModalityTokenCount>(new List<ModalityTokenCount>());
    }
  }

  // Hidden constructor, users don't need to make this
  private CountTokensResponse(int totalTokens,
                              int? totalBillableCharacters = null,
                              List<ModalityTokenCount> promptTokensDetails = null) {
    TotalTokens = totalTokens;
    TotalBillableCharacters = totalBillableCharacters;
    _promptTokensDetails =
        new ReadOnlyCollection<ModalityTokenCount>(promptTokensDetails ?? new List<ModalityTokenCount>());
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static CountTokensResponse FromJson(string jsonString) {
    return FromJson(Json.Deserialize(jsonString) as Dictionary<string, object>);
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static CountTokensResponse FromJson(Dictionary<string, object> jsonDict) {
    return new CountTokensResponse(
      jsonDict.ParseValue<int>("totalTokens"),
      jsonDict.ParseNullableValue<int>("totalBillableCharacters"),
      jsonDict.ParseObjectList("promptTokensDetails", ModalityTokenCount.FromJson));
  }
}

}
