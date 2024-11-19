/*
 * Copyright 2024 Google LLC
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

namespace Firebase.Analytics {

/// @brief The type of consent to set.
///
/// Supported consent types are mapped to corresponding constants in the Android
/// and iOS SDKs. Omitting a type retains its previous status.
public enum ConsentType {
  AdStorage = 0,
  AnalyticsStorage,
  AdUserData,
  AdPersonalization
}

/// @brief The status value of the consent type.
///
/// Supported statuses are ConsentStatus.Granted and ConsentStatus.Denied.
public enum ConsentStatus {
  Granted = 0,
  Denied
}

}
