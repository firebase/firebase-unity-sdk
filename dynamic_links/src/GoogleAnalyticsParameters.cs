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

/// @brief Google Analytics Parameters.
public class GoogleAnalyticsParameters {
  /// The campaign source; used to identify a search engine, newsletter,
  /// or other source.
  public string Source { get; set; }
  /// The campaign medium; used to identify a medium such as email or
  /// cost-per-click (cpc).
  public string Medium { get; set; }
  /// The campaign name; The individual campaign name, slogan, promo code,
  /// etc. for a product.
  public string Campaign { get; set; }
  /// The campaign term; used with paid search to supply the keywords for ads.
  public string Term { get; set; }
  /// The campaign content; used for A/B testing and content-targeted ads to
  /// differentiate ads or links that point to the same URL.
  public string Content { get; set; }
}

}
