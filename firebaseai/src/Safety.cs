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
using Firebase.AI.Internal;

namespace Firebase.AI {

/// <summary>
/// Categories describing the potential harm a piece of content may pose.
/// </summary>
public enum HarmCategory {
  /// <summary>
  /// A new and not yet supported value.
  /// </summary>
  Unknown = 0,
  /// <summary>
  /// Harassment content.
  /// </summary>
  Harassment,
  /// <summary>
  /// Negative or harmful comments targeting identity and/or protected attributes.
  /// </summary>
  HateSpeech,
  /// <summary>
  /// Contains references to sexual acts or other lewd content.
  /// </summary>
  SexuallyExplicit,
  /// <summary>
  /// Promotes or enables access to harmful goods, services, or activities.
  /// </summary>
  DangerousContent,
  /// <summary>
  /// Content that may be used to harm civic integrity.
  /// </summary>
  CivicIntegrity,
}

/// <summary>
/// A type used to specify a threshold for harmful content, beyond which the model will return a
/// fallback response instead of generated content.
/// </summary>
public readonly struct SafetySetting {

  /// <summary>
  /// Block at and beyond a specified threshold.
  /// </summary>
  public enum HarmBlockThreshold {
    /// <summary>
    /// Content with negligible harm is allowed.
    /// </summary>
    LowAndAbove,
    /// <summary>
    /// Content with negligible to low harm is allowed.
    /// </summary>
    MediumAndAbove,
    /// <summary>
    /// Content with negligible to medium harm is allowed.
    /// </summary>
    OnlyHigh,
    /// <summary>
    /// All content is allowed regardless of harm.
    /// </summary>
    None,
    /// <summary>
    /// All content is allowed regardless of harm, and metadata will not be included in the response.
    /// </summary>
    Off,
  }

  /// <summary>
  /// The method of computing whether the threshold has been exceeded.
  /// </summary>
  public enum HarmBlockMethod {
    /// <summary>
    /// Use only the probability score.
    /// </summary>
    Probability,
    /// <summary>
    /// Use both probability and severity scores.
    /// </summary>
    Severity,
  }

  private readonly HarmCategory _category;
  private readonly HarmBlockThreshold _threshold;
  private readonly HarmBlockMethod? _method;

  /// <summary>
  /// Initializes a new safety setting with the given category and threshold.
  /// </summary>
  /// <param name="category">The category this safety setting should be applied to.</param>
  /// <param name="threshold">The threshold describing what content should be blocked.</param>
  /// <param name="method">The method of computing whether the threshold has been exceeded; if not specified,
  ///     the default method is `Severity` for most models. This parameter is unused in the GoogleAI backend.</param>
  public SafetySetting(HarmCategory category, HarmBlockThreshold threshold,
      HarmBlockMethod? method = null) {
    _category = category;
    _threshold = threshold;
    _method = method;
  }

  private string ConvertCategory(HarmCategory category) {
    return category switch {
      HarmCategory.Unknown => "UNKNOWN",
      HarmCategory.Harassment => "HARM_CATEGORY_HARASSMENT",
      HarmCategory.HateSpeech => "HARM_CATEGORY_HATE_SPEECH",
      HarmCategory.SexuallyExplicit => "HARM_CATEGORY_SEXUALLY_EXPLICIT",
      HarmCategory.DangerousContent => "HARM_CATEGORY_DANGEROUS_CONTENT",
      HarmCategory.CivicIntegrity => "HARM_CATEGORY_CIVIC_INTEGRITY",
      _ => category.ToString(), // Fallback
    };
  }

  private string ConvertThreshold(HarmBlockThreshold threshold) {
    return threshold switch {
      HarmBlockThreshold.LowAndAbove => "BLOCK_LOW_AND_ABOVE",
      HarmBlockThreshold.MediumAndAbove => "BLOCK_MEDIUM_AND_ABOVE",
      HarmBlockThreshold.OnlyHigh => "BLOCK_ONLY_HIGH",
      HarmBlockThreshold.None => "BLOCK_NONE",
      HarmBlockThreshold.Off => "OFF",
      _ => threshold.ToString(), // Fallback
    };
  }

  private string ConvertMethod(HarmBlockMethod method) {
    return method switch {
      HarmBlockMethod.Probability => "PROBABILITY",
      HarmBlockMethod.Severity => "SEVERITY",
      _ => method.ToString(), // Fallback
    };
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for serializing the object to JSON for the API request.
  /// </summary>
  internal Dictionary<string, object> ToJson(FirebaseAI.Backend.InternalProvider backend) {
    Dictionary<string, object> jsonDict = new () {
      ["category"] = ConvertCategory(_category),
      ["threshold"] = ConvertThreshold(_threshold),
    };
    // GoogleAI doesn't support HarmBlockMethod.
    if (backend != FirebaseAI.Backend.InternalProvider.GoogleAI) {
      if (_method.HasValue) jsonDict["method"] = ConvertMethod(_method.Value);
    }
    return jsonDict;
  }
}

/// <summary>
/// A type defining potentially harmful media categories and their model-assigned ratings. A value
/// of this type may be assigned to a category for every model-generated response, not just
/// responses that exceed a certain threshold.
/// </summary>
public readonly struct SafetyRating {

  /// <summary>
  /// The probability that a given model output falls under a harmful content category.
  ///
  /// > Note: This does not indicate the severity of harm for a piece of content.
  /// </summary>
  public enum HarmProbability {
    /// <summary>
    /// A new and not yet supported value.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// The probability is zero or close to zero.
    ///
    /// For benign content, the probability across all categories will be this value.
    /// </summary>
    Negligible,
    /// <summary>
    /// The probability is small but non-zero.
    /// </summary>
    Low,
    /// <summary>
    /// The probability is moderate.
    /// </summary>
    Medium,
    /// <summary>
    /// The probability is high.
    ///
    /// The content described is very likely harmful.
    /// </summary>
    High,
  }

  /// <summary>
  /// The magnitude of how harmful a model response might be for the respective `HarmCategory`.
  /// </summary>
  public enum HarmSeverity {
    /// <summary>
    /// A new and not yet supported value.
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// Negligible level of harm severity.
    /// </summary>
    Negligible,
    /// <summary>
    /// Low level of harm severity.
    /// </summary>
    Low,
    /// <summary>
    /// Medium level of harm severity.
    /// </summary>
    Medium,
    /// <summary>
    /// High level of harm severity.
    /// </summary>
    High,
  }

  /// <summary>
  /// The category describing the potential harm a piece of content may pose.
  /// </summary>
  public HarmCategory Category { get; }
  /// <summary>
  /// The model-generated probability that the content falls under the specified HarmCategory.
  ///
  /// This is a discretized representation of the `ProbabilityScore`.
  ///
  /// > Important: This does not indicate the severity of harm for a piece of content.
  /// </summary>
  public HarmProbability Probability { get; }
  /// <summary>
  /// The confidence score that the response is associated with the corresponding HarmCategory.
  ///
  /// The probability safety score is a confidence score between 0.0 and 1.0, rounded to one decimal
  /// place; it is discretized into a `HarmProbability` in `Probability`. See [probability
  /// scores](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal/configure-safety-filters#comparison_of_probability_scores_and_severity_scores)
  /// in the Google Cloud documentation for more details.
  /// </summary>
  public float ProbabilityScore { get; }
  /// <summary>
  /// If true, the response was blocked.
  /// </summary>
  public bool Blocked { get; }
  /// <summary>
  /// The severity reflects the magnitude of how harmful a model response might be.
  ///
  /// This is a discretized representation of the `SeverityScore`.
  /// </summary>
  public HarmSeverity Severity { get; }
  /// <summary>
  /// The severity score is the magnitude of how harmful a model response might be.
  ///
  /// The severity score ranges from 0.0 to 1.0, rounded to one decimal place; it is discretized
  /// into a `HarmSeverity` in `Severity`. See [severity scores](https://cloud.google.com/vertex-ai/generative-ai/docs/multimodal/configure-safety-filters#comparison_of_probability_scores_and_severity_scores)
  /// in the Google Cloud documentation for more details.
  /// </summary>
  public float SeverityScore { get; }

  // Hidden constructor, users don't need to make this.
  private SafetyRating(HarmCategory category, HarmProbability probability,
      float probabilityScore, bool blocked, HarmSeverity severity, float severityScore) {
    Category = category;
    Probability = probability;
    ProbabilityScore = probabilityScore;
    Blocked = blocked;
    Severity = severity;
    SeverityScore = severityScore;
  }

  private static HarmCategory ParseCategory(string str) {
    return str switch {
      "HARM_CATEGORY_HARASSMENT" => HarmCategory.Harassment,
      "HARM_CATEGORY_HATE_SPEECH" => HarmCategory.HateSpeech,
      "HARM_CATEGORY_SEXUALLY_EXPLICIT" => HarmCategory.SexuallyExplicit,
      "HARM_CATEGORY_DANGEROUS_CONTENT" => HarmCategory.DangerousContent,
      "HARM_CATEGORY_CIVIC_INTEGRITY" => HarmCategory.CivicIntegrity,
      _ => HarmCategory.Unknown,
    };
  }

  private static HarmProbability ParseProbability(string str) {
    return str switch {
      "NEGLIGIBLE" => HarmProbability.Negligible,
      "LOW" => HarmProbability.Low,
      "MEDIUM" => HarmProbability.Medium,
      "HIGH" => HarmProbability.High,
      _ => HarmProbability.Unknown,
    };
  }

  private static HarmSeverity ParseSeverity(string str) {
    return str switch {
      "HARM_SEVERITY_NEGLIGIBLE" => HarmSeverity.Negligible,
      "HARM_SEVERITY_LOW" => HarmSeverity.Low,
      "HARM_SEVERITY_MEDIUM" => HarmSeverity.Medium,
      "HARM_SEVERITY_HIGH" => HarmSeverity.High,
      _ => HarmSeverity.Unknown,
    };
  }

  /// <summary>
  /// Intended for internal use only.
  /// This method is used for deserializing JSON responses and should not be called directly.
  /// </summary>
  internal static SafetyRating FromJson(Dictionary<string, object> jsonDict) {
    return new SafetyRating(
      jsonDict.ParseEnum("category", ParseCategory),
      jsonDict.ParseEnum("probability", ParseProbability),
      jsonDict.ParseValue<float>("probabilityScore"),
      jsonDict.ParseValue<bool>("blocked"),
      jsonDict.ParseEnum("severity", ParseSeverity),
      jsonDict.ParseValue<float>("severityScore")
    );
  }
}

}
