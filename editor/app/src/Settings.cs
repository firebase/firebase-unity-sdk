/*
 * Copyright 2020 Google LLC
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

using Google;

namespace Firebase.Editor {

/// <summary>
/// Editor settings storage for Firebase components.
/// </summary>
internal class Settings {
    // Construct settings.
    private static ProjectSettings settings = new ProjectSettings("Google.Firebase.");

    /// <summary>
    /// Get the current settings.
    /// </summary>
    internal static ProjectSettings Instance { get { return settings; } }

    /// <summary>
    /// Reset settings to default values.
    /// </summary>
    internal static void RestoreDefaultSettings() {
        // When more settings are added delete the keys here using settings.DeleteKeys().
        Measurement.analytics.RestoreDefaultSettings();
    }
}

}
