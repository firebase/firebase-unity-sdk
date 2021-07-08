/*
 * Copyright 2018 Google LLC
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

using UnityEngine;

namespace Firebase.Crashlytics.Editor {

  /// <summary>
  /// Base class for all Crashlytics Unity Editor Plugin Tests
  /// </summary>
  public class CrashlyticsEditorTestBase {

    /// <summary>
    /// Default setup for all Crashlytics Unity Editor Plugin Tests
    ///
    /// This is required for tests using UnityEngine to work
    /// </summary>
    public CrashlyticsEditorTestBase() {
      Debug.logger.logEnabled = false;
    }
  }
}
