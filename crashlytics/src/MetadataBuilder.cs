/*
 * Copyright 2021 Google LLC
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

namespace Firebase.Crashlytics {
  using System;
  using UnityEngine;

  /// <summary>
  /// Automatically collects metadata and serializes to JSON in a space-efficient way.
  /// </summary>
  internal class MetadataBuilder {
    public static string METADATA_KEY = "com.crashlytics.metadata.unity";

    public static string GenerateMetadataJSON() {
      try {
        Metadata metadata = new Metadata();
        return JsonUtility.ToJson(metadata);
      } catch (Exception e) {
        UnityEngine.Debug.LogError(
            "Failed to generate Unity-specific metadata for Crashlytics due to: " + e.ToString());
        return "";
      }
    }
  }
}
