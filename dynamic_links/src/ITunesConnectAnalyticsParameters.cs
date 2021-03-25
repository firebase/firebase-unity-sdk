/*
 * Copyright 2017 Google LLC
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

namespace Firebase.DynamicLinks {

/// @brief iTunes Connect App Analytics Parameters.
public class ITunesConnectAnalyticsParameters {
  /// The provider token that enables analytics for Dynamic Links from
  /// within iTunes Connect.
  public string ProviderToken { get; set; }
  /// The affiliate token used to create affiliate-coded links.
  public string AffiliateToken { get; set; }
  /// The campaign token that developers can add to any link in order to
  /// track sales from a specific marketing campaign.
  public string CampaignToken { get; set; }
}

}
