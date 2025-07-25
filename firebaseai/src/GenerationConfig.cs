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
using Firebase.AI.Internal;

namespace Firebase.AI {

  /// <summary>
  /// A struct defining model parameters to be used when sending generative AI
  /// requests to the backend model.
  /// </summary>
  public readonly struct GenerationConfig {
    private readonly float? _temperature;
    private readonly float? _topP;
    private readonly float? _topK;
    private readonly int? _candidateCount;
    private readonly int? _maxOutputTokens;
    private readonly float? _presencePenalty;
    private readonly float? _frequencyPenalty;
    private readonly string[] _stopSequences;
    private readonly string _responseMimeType;
    private readonly Schema _responseSchema;
    private readonly List<ResponseModality> _responseModalities;
    private readonly ThinkingConfig? _thinkingConfig;

    /// <summary>
    /// Creates a new `GenerationConfig` value.
    ///
    /// See the
    /// [Configure model parameters](https://firebase.google.com/docs/vertex-ai/model-parameters)
    /// guide and the
    /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// for more details.
    /// </summary>
    /// 
    /// <param name="temperature">Controls the randomness of the language model's output. Higher values (for
    /// example, 1.0) make the text more random and creative, while lower values (for example,
    /// 0.1) make it more focused and deterministic.
    ///
    /// > Note: A temperature of 0 means that the highest probability tokens are always selected.
    /// > In this case, responses for a given prompt are mostly deterministic, but a small amount
    /// > of variation is still possible.
    ///
    /// > Important: The range of supported temperature values depends on the model; see the
    /// > [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// > for more details.</param>
    /// 
    /// <param name="topP">Controls diversity of generated text. Higher values (e.g., 0.9) produce more diverse
    /// text, while lower values (e.g., 0.5) make the output more focused.
    ///
    /// The supported range is 0.0 to 1.0.
    ///
    /// > Important: The default `topP` value depends on the model; see the
    /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// for more details.</param>
    /// 
    /// <param name="topK">Limits the number of highest probability words the model considers when generating
    /// text. For example, a topK of 40 means only the 40 most likely words are considered for the
    /// next token. A higher value increases diversity, while a lower value makes the output more
    /// deterministic.
    ///
    /// The supported range is 1 to 40.
    ///
    /// > Important: Support for `topK` and the default value depends on the model; see the
    /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// for more details.</param>
    /// 
    /// <param name="candidateCount">The number of response variations to return; defaults to 1 if not set.
    /// Support for multiple candidates depends on the model; see the
    /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// for more details.</param>
    /// 
    /// <param name="maxOutputTokens">Maximum number of tokens that can be generated in the response.
    /// See the configure model parameters [documentation](https://firebase.google.com/docs/vertex-ai/model-parameters?platform=ios#max-output-tokens)
    /// for more details.</param>
    /// 
    /// <param name="presencePenalty">Controls the likelihood of repeating the same words or phrases already
    /// generated in the text. Higher values increase the penalty of repetition, resulting in more
    /// diverse output.
    ///
    /// > Note: While both `presencePenalty` and `frequencyPenalty` discourage repetition,
    /// > `presencePenalty` applies the same penalty regardless of how many times the word/phrase
    /// > has already appeared, whereas `frequencyPenalty` increases the penalty for *each*
    /// > repetition of a word/phrase.
    ///
    /// > Important: The range of supported `presencePenalty` values depends on the model; see the
    /// > [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// > for more details.</param>
    /// 
    /// <param name="frequencyPenalty">Controls the likelihood of repeating words or phrases, with the penalty
    /// increasing for each repetition. Higher values increase the penalty of repetition,
    /// resulting in more diverse output.
    ///
    /// > Note: While both `frequencyPenalty` and `presencePenalty` discourage repetition,
    /// > `frequencyPenalty` increases the penalty for *each* repetition of a word/phrase, whereas
    /// > `presencePenalty` applies the same penalty regardless of how many times the word/phrase
    /// > has already appeared.
    ///
    /// > Important: The range of supported `frequencyPenalty` values depends on the model; see
    /// > the
    /// > [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// > for more details.</param>
    /// 
    /// <param name="stopSequences">A set of up to 5 `String`s that will stop output generation. If specified,
    /// the API will stop at the first appearance of a stop sequence. The stop sequence will not
    /// be included as part of the response. See the
    /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
    /// for more details.</param>
    /// 
    /// <param name="responseMimeType">Output response MIME type of the generated candidate text.
    ///
    /// Supported MIME types:
    /// - `text/plain`: Text output; the default behavior if unspecified.
    /// - `application/json`: JSON response in the candidates.
    /// - `text/x.enum`: For classification tasks, output an enum value as defined in the
    /// `responseSchema`.</param>
    /// 
    /// <param name="responseSchema">Output schema of the generated candidate text. If set, a compatible
    /// `responseMimeType` must also be set.
    ///
    /// Compatible MIME types:
    /// - `application/json`: Schema for JSON response.
    ///
    /// Refer to the
    /// [Control generated output](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal/control-generated-output)
    /// guide for more details.</param>
    /// 
    /// <param name="responseModalities">
    /// The data types (modalities) that may be returned in model responses.
    ///
    /// See the [multimodal
    /// responses](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal-response-generation)
    /// documentation for more details.
    /// </param>
    /// 
    /// <param name="thinkingConfig">
    /// Configuration for controlling the "thinking" behavior of compatible Gemini
    /// models; see `ThinkingConfig` for more details.
    ///
    /// An error will be returned if this field is set for models that don't
    /// support thinking.
    /// </param>
    public GenerationConfig(
        float? temperature = null,
        float? topP = null,
        float? topK = null,
        int? candidateCount = null,
        int? maxOutputTokens = null,
        float? presencePenalty = null,
        float? frequencyPenalty = null,
        string[] stopSequences = null,
        string responseMimeType = null,
        Schema responseSchema = null,
        IEnumerable<ResponseModality> responseModalities = null,
        ThinkingConfig? thinkingConfig = null) {
      _temperature = temperature;
      _topP = topP;
      _topK = topK;
      _candidateCount = candidateCount;
      _maxOutputTokens = maxOutputTokens;
      _presencePenalty = presencePenalty;
      _frequencyPenalty = frequencyPenalty;
      _stopSequences = stopSequences;
      _responseMimeType = responseMimeType;
      _responseSchema = responseSchema;
      _responseModalities = responseModalities != null ?
          new List<ResponseModality>(responseModalities) : null;
      _thinkingConfig = thinkingConfig;
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson() {
      Dictionary<string, object> jsonDict = new();
      if (_temperature.HasValue) jsonDict["temperature"] = _temperature.Value;
      if (_topP.HasValue) jsonDict["topP"] = _topP.Value;
      if (_topK.HasValue) jsonDict["topK"] = _topK.Value;
      if (_candidateCount.HasValue) jsonDict["candidateCount"] = _candidateCount.Value;
      if (_maxOutputTokens.HasValue) jsonDict["maxOutputTokens"] = _maxOutputTokens.Value;
      if (_presencePenalty.HasValue) jsonDict["presencePenalty"] = _presencePenalty.Value;
      if (_frequencyPenalty.HasValue) jsonDict["frequencyPenalty"] = _frequencyPenalty.Value;
      if (_stopSequences != null && _stopSequences.Length > 0) jsonDict["stopSequences"] = _stopSequences;
      if (!string.IsNullOrWhiteSpace(_responseMimeType)) jsonDict["responseMimeType"] = _responseMimeType;
      if (_responseSchema != null) jsonDict["responseSchema"] = _responseSchema.ToJson();
      if (_responseModalities != null && _responseModalities.Count > 0) {
        jsonDict["responseModalities"] =
            _responseModalities.Select(EnumConverters.ResponseModalityToString).ToList();
      }
      if (_thinkingConfig != null) jsonDict["thinkingConfig"] = _thinkingConfig?.ToJson();

      return jsonDict;
    }
  }

  /// <summary>
  /// Configuration options for Thinking features.
  /// </summary>
  public readonly struct ThinkingConfig {
#if !DOXYGEN
    public readonly int? ThinkingBudget { get; }
#endif

    /// <summary>
    /// Initializes configuration options for Thinking features.
    /// </summary>
    /// <param name="thinkingBudget">The token budget for the model's thinking process.</param>
    public ThinkingConfig(int? thinkingBudget = null) {
      ThinkingBudget = thinkingBudget;
    }

    /// <summary>
    /// Intended for internal use only.
    /// This method is used for serializing the object to JSON for the API request.
    /// </summary>
    internal Dictionary<string, object> ToJson() {
      Dictionary<string, object> jsonDict = new();
      if (ThinkingBudget.HasValue) {
        jsonDict["thinkingBudget"] = ThinkingBudget.Value;
      }
      return jsonDict;
    }
  }


}
