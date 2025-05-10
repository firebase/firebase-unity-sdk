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

namespace Firebase.AI {

/// <summary>
/// The response type the model should return with.
/// </summary>
public enum ResponseModality {
  /// <summary>
  /// Specifies that the model should generate textual content.
  ///
  /// Use this modality when you need the model to produce written language, such as answers to
  /// questions, summaries, creative writing, code snippets, or structured data formats like JSON.
  /// </summary>
  Text,
  /// <summary>
  /// **Public Experimental**: Specifies that the model should generate image data.
  ///
  /// Use this modality when you want the model to create visual content based on the provided input
  /// or prompts. The response might contain one or more generated images. See the [image
  /// generation](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal-response-generation#image-generation)
  /// documentation for more details.
  ///
  /// > Warning: Image generation using Gemini 2.0 Flash is a **Public Experimental** feature, which
  /// > means that it is not subject to any SLA or deprecation policy and could change in
  /// > backwards-incompatible ways.
  /// </summary>
  Image,
  /// <summary>
  /// **Public Experimental**: Specifies that the model should generate audio data.
  /// 
  /// Use this modality with a `LiveGenerationConfig` to create audio content based on the
  /// provided input or prompts with a `LiveGenerativeModel`.
  /// </summary>
  Audio,
}

}
