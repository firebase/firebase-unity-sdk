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
using System.Linq;
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// A struct used to configure speech generation settings.
/// </summary>
public readonly struct SpeechConfig {
  internal readonly string voice;

  private SpeechConfig(string voice) {
    this.voice = voice;
  }

  /// <summary>
  /// See https://cloud.google.com/text-to-speech/docs/chirp3-hd for the list of available voices.
  /// </summary>
  /// <param name="voice"></param>
  /// <returns></returns>
  public static SpeechConfig UsePrebuiltVoice(string voice) {
    return new SpeechConfig(voice);
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    Dictionary<string, object> dict = new();

    if (!string.IsNullOrWhiteSpace(voice)) {
      dict["voiceConfig"] = new Dictionary<string, object>() {
        { "prebuiltVoiceConfig" , new Dictionary<string, object>() {
          { "voiceName", voice }
        } }
      };
    }

    return dict;
  }
}

/// <summary>
/// A struct defining model parameters to be used when generating live session content.
/// </summary>
public readonly struct LiveGenerationConfig {
  private readonly SpeechConfig? _speechConfig;
  private readonly List<ResponseModality> _responseModalities;
  private readonly float? _temperature;
  private readonly float? _topP;
  private readonly float? _topK;
  private readonly int? _maxOutputTokens;
  private readonly float? _presencePenalty;
  private readonly float? _frequencyPenalty;

  /// <summary>
  /// Creates a new `LiveGenerationConfig` value.
  ///
  /// See the
  /// [Configure model parameters](https://firebase.google.com/docs/vertex-ai/model-parameters)
  /// guide and the
  /// [Cloud documentation](https://cloud.google.com/vertex-ai/generative-ai/docs/model-reference/inference#generationconfig)
  /// for more details.
  /// </summary>
  /// 
  /// <param name="speechConfig">The speech configuration to use if generating audio output.</param>
  /// 
  /// <param name="responseModalities">A list of response types to receive from the model.
  /// Note: Currently only supports being provided one type, despite being a list.</param>
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
  public LiveGenerationConfig(
      SpeechConfig? speechConfig = null,
      IEnumerable<ResponseModality> responseModalities = null,
      float? temperature = null,
      float? topP = null,
      float? topK = null,
      int? maxOutputTokens = null,
      float? presencePenalty = null,
      float? frequencyPenalty = null) {
    _speechConfig = speechConfig;
    _responseModalities = responseModalities != null ?
        new List<ResponseModality>(responseModalities) : new List<ResponseModality>();
    _temperature = temperature;
    _topP = topP;
    _topK = topK;
    _maxOutputTokens = maxOutputTokens;
    _presencePenalty = presencePenalty;
    _frequencyPenalty = frequencyPenalty;
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson() {
    Dictionary<string, object> jsonDict = new();
    if (_speechConfig.HasValue) jsonDict["speechConfig"] = _speechConfig?.ToJson();
    if (_responseModalities != null && _responseModalities.Any()) {
      jsonDict["responseModalities"] =
          _responseModalities.Select(EnumConverters.ResponseModalityToString).ToList();
    }
    if (_temperature.HasValue) jsonDict["temperature"] = _temperature.Value;
    if (_topP.HasValue) jsonDict["topP"] = _topP.Value;
    if (_topK.HasValue) jsonDict["topK"] = _topK.Value;
    if (_maxOutputTokens.HasValue) jsonDict["maxOutputTokens"] = _maxOutputTokens.Value;
    if (_presencePenalty.HasValue) jsonDict["presencePenalty"] = _presencePenalty.Value;
    if (_frequencyPenalty.HasValue) jsonDict["frequencyPenalty"] = _frequencyPenalty.Value;

    return jsonDict;
  }
}

}
