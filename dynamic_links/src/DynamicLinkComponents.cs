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

using System;

namespace Firebase.DynamicLinks {

/// @brief The information needed to generate a Dynamic Link.
public class DynamicLinkComponents {
  // Keep a reference to FirebaseApp as it initializes this SDK.
  private Firebase.FirebaseApp app;

  /// Creates a new Dynamic Link Components object with the given link
  /// and dynamic link domain.
  ///
  /// @param link The base link that your app will open.
  /// @param uriPrefix The URI prefix (of the form "https://xyz.app.goo.gl") to
  /// use for this Dynamic Link. You can find this value in the Dynamic Links
  /// section of the Firebase console. If you have set up custom domains on your
  /// project, set this to your project's custom domain as listed in the
  /// Firebase console. Note: If you do not specify "https://" as the URI
  /// scheme, it will be added.
  public DynamicLinkComponents(System.Uri link, string uriPrefix) {
    app = Firebase.FirebaseApp.DefaultInstance;
    Link = link;
    if (!uriPrefix.StartsWith("https://")) {
      uriPrefix = "https://" + uriPrefix;
    }
    DomainUriPrefix = uriPrefix;
  }

  /// The long Dynamic Link, generated from the other parameters.
  public System.Uri LongDynamicLink {
    get {
      return ShortDynamicLink.ConvertFromInternal(
        FirebaseDynamicLinks.GetLongLinkInternal(ConvertToInternal())).Url;
    }
  }

  /// The link your app will open. You can specify any URL your app can
  /// handle, such as a link to your app's content, or a URL that initiates
  /// some app-specific logic such as crediting the user with a coupon, or
  /// displaying a specific welcome screen. This link must be a well-formatted
  /// URL, be properly URL-encoded, and use the HTTP or HTTPS scheme.
  public System.Uri Link { get; set; }
  /// The domain (of the form "https://xyz.app.goo.gl") to use for this Dynamic
  /// Link. You can find this value in the Dynamic Links section of the Firebase
  /// console.
  ///
  /// If you have set up custom domains on your project, set this to your
  /// project's custom domain as listed in the Firebase console.
  ///
  /// Only https:// links are supported.
  public string DomainUriPrefix { get; set; }
  /// The Google Analytics parameters.
  public GoogleAnalyticsParameters GoogleAnalyticsParameters { get; set; }
  /// The iOS parameters.
  public IOSParameters IOSParameters { get; set; }
  /// The iTunes Connect App Analytics parameters.
  public ITunesConnectAnalyticsParameters ITunesConnectAnalyticsParameters { get; set; }
  /// The Android parameters.
  public AndroidParameters AndroidParameters { get; set; }
  /// The social meta-tag parameters.
  public SocialMetaTagParameters SocialMetaTagParameters { get; set; }

  // Convert this object to DynamicLinkComponentsInternal.
  internal DynamicLinkComponentsInternal ConvertToInternal() {
    return new DynamicLinkComponentsInternal {
      link = Firebase.FirebaseApp.UriToUrlString(Link),
      domain_uri_prefix = DomainUriPrefix,
      google_analytics_parameters = GoogleAnalyticsParameters == null ?
          null : new GoogleAnalyticsParametersInternal {
        source = GoogleAnalyticsParameters.Source,
        medium = GoogleAnalyticsParameters.Medium,
        campaign = GoogleAnalyticsParameters.Campaign,
        term = GoogleAnalyticsParameters.Term,
        content = GoogleAnalyticsParameters.Content
      },
      ios_parameters = IOSParameters == null ? null : new IOSParametersInternal {
        bundle_id = IOSParameters.BundleId,
        fallback_url = Firebase.FirebaseApp.UriToUrlString(IOSParameters.FallbackUrl),
        custom_scheme = IOSParameters.CustomScheme,
        ipad_fallback_url = Firebase.FirebaseApp.UriToUrlString(IOSParameters.IPadFallbackUrl),
        ipad_bundle_id = IOSParameters.IPadBundleId,
        app_store_id = IOSParameters.AppStoreId,
        minimum_version = IOSParameters.MinimumVersion
      },
      itunes_connect_analytics_parameters = ITunesConnectAnalyticsParameters == null ?
          null : new ITunesConnectAnalyticsParametersInternal {
        provider_token = ITunesConnectAnalyticsParameters.ProviderToken,
        affiliate_token = ITunesConnectAnalyticsParameters.AffiliateToken,
        campaign_token = ITunesConnectAnalyticsParameters.CampaignToken
      },
      android_parameters = AndroidParameters == null ?
          null : new AndroidParametersInternal {
        package_name = AndroidParameters.PackageName,
        fallback_url = Firebase.FirebaseApp.UriToUrlString(AndroidParameters.FallbackUrl),
        minimum_version = AndroidParameters.MinimumVersion
      },
      social_meta_tag_parameters = SocialMetaTagParameters == null ?
          null : new SocialMetaTagParametersInternal {
        title = SocialMetaTagParameters.Title,
        description = SocialMetaTagParameters.Description,
        image_url = Firebase.FirebaseApp.UriToUrlString(SocialMetaTagParameters.ImageUrl),
      }
    };
  }
}

}
