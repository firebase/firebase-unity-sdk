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

namespace Firebase.VertexAI {

public enum HarmCategory {
  Unknown,
  Harassment,
  HateSpeech,
  SexuallyExplicit,
  DangerousContent,
  CivicIntegrity,
}

public readonly struct SafetySetting {
  public enum HarmBlockThreshold {
    LowAndAbove,
    MediumAndAbove,
    OnlyHigh,
    None,
  }

  public enum HarmBlockMethod {
    Probability,
    Severity,
  }

  public SafetySetting(HarmCategory category, HarmBlockThreshold threshold,
      HarmBlockMethod? method = null) { throw new NotImplementedException(); }
}

public readonly struct SafetyRating {
  public enum HarmProbability {
    Unknown,
    Negligible,
    Low,
    Medium,
    High,
  }

  public enum HarmSeverity {
    Unknown,
    Negligible,
    Low,
    Medium,
    High,
  }

  public HarmCategory Category { get; }
  public HarmProbability Probability { get; }
  public float ProbabilityScore { get; }
  public bool Blocked { get; }
  public HarmSeverity Severity { get; }
  public float SeverityScore { get; }

  // Hidden constructor, users don't need to make this
}

}
