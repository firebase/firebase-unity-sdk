// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Analytics {

public static partial class FirebaseAnalytics {

  /// <summary>
  /// Game achievement ID (String).
  /// </summary>
  public static string ParameterAchievementID = "achievement_id";

  /// <summary>
  /// The ad format (e.g. Banner, Interstitial, Rewarded, Native, Rewarded
  /// Interstitial, Instream). (String).
  /// </summary>
  public static string ParameterAdFormat = "ad_format";

  /// <summary>
  /// Ad Network Click ID (String). Used for network-specific click IDs
  /// which vary in format.
  /// </summary>
  public static string ParameterAdNetworkClickID = "aclid";

  /// <summary>
  /// The ad platform (e.g. MoPub, IronSource) (String).
  /// </summary>
  public static string ParameterAdPlatform = "ad_platform";

  /// <summary>
  /// The ad source (e.g. AdColony) (String).
  /// </summary>
  public static string ParameterAdSource = "ad_source";

  /// <summary>
  /// The ad unit name (e.g. Banner_03) (String).
  /// </summary>
  public static string ParameterAdUnitName = "ad_unit_name";

  /// <summary>
  /// A product affiliation to designate a supplying company or brick and
  /// mortar store location
  /// (String). @code
  /// let params = [
  /// AnalyticsParameterAffiliation : "Google Store",
  /// // ...
  /// ]
  /// </summary>
  public static string ParameterAffiliation = "affiliation";

  /// <summary>
  /// Campaign custom parameter (String). Used as a method of capturing
  /// custom data in a campaign. Use varies by network.
  /// </summary>
  public static string ParameterCP1 = "cp1";

  /// <summary>
  /// The individual campaign name, slogan, promo code, etc. Some networks
  /// have pre-defined macro to capture campaign information, otherwise can
  /// be populated by developer. Highly Recommended (String).
  /// </summary>
  public static string ParameterCampaign = "campaign";

  /// <summary>
  /// Campaign ID (String). Used for keyword analysis to identify a specific
  /// product promotion or strategic campaign. This is a required key for
  /// GA4 data import.
  /// </summary>
  public static string ParameterCampaignID = "campaign_id";

  /// <summary>
  /// Character used in game (String).
  /// </summary>
  public static string ParameterCharacter = "character";

  /// <summary>
  /// Campaign content (String).
  /// </summary>
  public static string ParameterContent = "content";

  /// <summary>
  /// Type of content selected (String).
  /// </summary>
  public static string ParameterContentType = "content_type";

  /// <summary>
  /// Coupon code used for a purchase (String).
  /// </summary>
  public static string ParameterCoupon = "coupon";

  /// <summary>
  /// Creative Format (String). Used to identify the high-level
  /// classification of the type of ad served by a specific campaign.
  /// </summary>
  public static string ParameterCreativeFormat = "creative_format";

  /// <summary>
  /// The name of a creative used in a promotional spot (String).
  /// </summary>
  public static string ParameterCreativeName = "creative_name";

  /// <summary>
  /// The name of a creative slot (String).
  /// </summary>
  public static string ParameterCreativeSlot = "creative_slot";

  /// <summary>
  /// Currency of the purchase or items associated with the event, in
  /// 3-letter
  /// ISO_4217 (http://en.wikipedia.org/wiki/ISO_4217#Active_codes) format (String).
  /// </summary>
  public static string ParameterCurrency = "currency";

  /// <summary>
  /// Flight or Travel destination (String).
  /// </summary>
  public static string ParameterDestination = "destination";

  /// <summary>
  /// Monetary value of discount associated with a purchase (Double).
  /// </summary>
  public static string ParameterDiscount = "discount";

  /// <summary>
  /// The arrival date, check-out date or rental end date for the item. This
  /// should be in YYYY-MM-DD format (String).
  /// </summary>
  public static string ParameterEndDate = "end_date";

  /// <summary>
  /// Indicates that the associated event should either extend the current
  /// session or start a new session if no session was active when the event
  /// was logged. Specify 1 to extend the current session or to start a new
  /// session; any other value will not extend or start a session.
  /// </summary>
  public static string ParameterExtendSession = "extend_session";

  /// <summary>
  /// Flight number for travel events (String).
  /// </summary>
  public static string ParameterFlightNumber = "flight_number";

  /// <summary>
  /// Indicates if the user is on a free trial of a subscription. Specify 1
  /// to indicate true and 0 to indicate false (Int).
  /// </summary>
  public static string ParameterFreeTrial = "free_trial";

  /// <summary>
  /// Group/clan/guild ID (String).
  /// </summary>
  public static string ParameterGroupID = "group_id";

  /// <summary>
  /// The index of the item in a list (Int).
  /// </summary>
  public static string ParameterIndex = "index";

  /// <summary>
  /// Item brand (String).
  /// </summary>
  public static string ParameterItemBrand = "item_brand";

  /// <summary>
  /// Item category (context-specific) (String).
  /// </summary>
  public static string ParameterItemCategory = "item_category";

  /// <summary>
  /// Item Category (context-specific) (String).
  /// </summary>
  public static string ParameterItemCategory2 = "item_category2";

  /// <summary>
  /// Item Category (context-specific) (String).
  /// </summary>
  public static string ParameterItemCategory3 = "item_category3";

  /// <summary>
  /// Item Category (context-specific) (String).
  /// </summary>
  public static string ParameterItemCategory4 = "item_category4";

  /// <summary>
  /// Item Category (context-specific) (String).
  /// </summary>
  public static string ParameterItemCategory5 = "item_category5";

  /// <summary>
  /// Item ID (context-specific) (String).
  /// </summary>
  public static string ParameterItemID = "item_id";

  /// <summary>
  /// The ID of the list in which the item was presented to the
  /// user (String).
  /// </summary>
  public static string ParameterItemListID = "item_list_id";

  /// <summary>
  /// The name of the list in which the item was presented to the user
  /// (String).
  /// </summary>
  public static string ParameterItemListName = "item_list_name";

  /// <summary>
  /// Item Name (context-specific) (String).
  /// </summary>
  public static string ParameterItemName = "item_name";

  /// <summary>
  /// Item variant (String).
  /// </summary>
  public static string ParameterItemVariant = "item_variant";

  /// <summary>
  /// The list of items involved in the transaction expressed as `[[String:
  /// Any]]`.
  /// </summary>
  public static string ParameterItems = "items";

  /// <summary>
  /// Level in game (Int).
  /// </summary>
  public static string ParameterLevel = "level";

  /// <summary>
  /// The name of a level in a game (String).
  /// </summary>
  public static string ParameterLevelName = "level_name";

  /// <summary>
  /// Location (String). The Google &lt;a href="https://developers.google.com/places/place-id"&gt;Place ID
  /// &lt;/a&gt; that corresponds to the associated event. Alternatively, you can supply your own custom
  /// Location ID.
  /// </summary>
  public static string ParameterLocation = "location";

  /// <summary>
  /// The location associated with the event. Preferred to be the Google
  /// Place ID (https://developers.google.com/places/place-id) that corresponds to the
  /// associated item but could be overridden to a custom location ID
  /// string.(String).
  /// </summary>
  public static string ParameterLocationID = "location_id";

  /// <summary>
  /// Marketing Tactic (String). Used to identify the targeting criteria
  /// applied to a specific campaign.
  /// </summary>
  public static string ParameterMarketingTactic = "marketing_tactic";

  /// <summary>
  /// The advertising or marParameter(keting, cpc, banner, email), push.
  /// Highly recommended (String).
  /// </summary>
  public static string ParameterMedium = "medium";

  /// <summary>
  /// A particular approach used in an operation; for example, "facebook" or
  /// "email" in the context of a sign_up or login event. (String).
  /// </summary>
  public static string ParameterMethod = "method";

  /// <summary>
  /// Number of nights staying at hotel (Int).
  /// </summary>
  public static string ParameterNumberOfNights = "number_of_nights";

  /// <summary>
  /// Number of passengers traveling (Int).
  /// </summary>
  public static string ParameterNumberOfPassengers = "number_of_passengers";

  /// <summary>
  /// Number of rooms for travel events (Int).
  /// </summary>
  public static string ParameterNumberOfRooms = "number_of_rooms";

  /// <summary>
  /// Flight or Travel origin (String).
  /// </summary>
  public static string ParameterOrigin = "origin";

  /// <summary>
  /// The chosen method of payment (String).
  /// </summary>
  public static string ParameterPaymentType = "payment_type";

  /// <summary>
  /// Purchase price (Double).
  /// </summary>
  public static string ParameterPrice = "price";

  /// <summary>
  /// Indicates if an item's price is discounted. Specify 1 to indicate true
  /// and 0 to indicate false (Int).
  /// </summary>
  public static string ParameterPriceIsDiscounted = "price_is_discounted";

  /// <summary>
  /// The ID of a product (String).
  /// </summary>
  public static string ParameterProductID = "product_id";

  /// <summary>
  /// The name of a product (String).
  /// </summary>
  public static string ParameterProductName = "product_name";

  /// <summary>
  /// The ID of a product promotion (String).
  /// </summary>
  public static string ParameterPromotionID = "promotion_id";

  /// <summary>
  /// The name of a product promotion (String).
  /// </summary>
  public static string ParameterPromotionName = "promotion_name";

  /// <summary>
  /// Purchase quantity (Int).
  /// </summary>
  public static string ParameterQuantity = "quantity";

  /// <summary>
  /// Score in game (Int).
  /// </summary>
  public static string ParameterScore = "score";

  /// <summary>
  /// Current screen class, such as the class name of the UIViewController,
  /// logged with screen_view event and added to every event (String).
  /// </summary>
  public static string ParameterScreenClass = "screen_class";

  /// <summary>
  /// Current screen name, such as the name of the UIViewController, logged
  /// with screen_view event and added to every event (String).
  /// </summary>
  public static string ParameterScreenName = "screen_name";

  /// <summary>
  /// The search string/keywords used (String).
  /// </summary>
  public static string ParameterSearchTerm = "search_term";

  /// <summary>
  /// Shipping cost associated with a transaction (Double).
  /// </summary>
  public static string ParameterShipping = "shipping";

  /// <summary>
  /// The shipping tier (e.g. Ground, Air, Next-day) selected for delivery
  /// of the purchased item (String).
  /// </summary>
  public static string ParameterShippingTier = "shipping_tier";

  /// <summary>
  /// The origin of your traffic, such as an Ad network (for example,
  /// google) or partner (urban airship). Identify the advertiser, site,
  /// publication, etc. that is sending traffic to your property. Highly
  /// recommended (String).
  /// </summary>
  public static string ParameterSource = "source";

  /// <summary>
  /// Source Platform (String). Used to identify the platform responsible
  /// for directing traffic to a given Analytics property (e.g., a buying
  /// platform where budgets, targeting criteria, etc. are set, a platform
  /// for managing organic traffic data, etc.).
  /// </summary>
  public static string ParameterSourcePlatform = "source_platform";

  /// <summary>
  /// The departure date, check-in date or rental start date for the item.
  /// This should be in YYYY-MM-DD format (String).
  /// </summary>
  public static string ParameterStartDate = "start_date";

  /// <summary>
  /// Indicates if the purchase is a subscription. Specify 1 to indicate
  /// true and 0 to indicate false (Int).
  /// </summary>
  public static string ParameterSubscription = "subscription";

  /// <summary>
  /// The result of an operation. Specify 1 to indicate success and 0 to
  /// indicate failure (Int).
  /// </summary>
  public static string ParameterSuccess = "success";

  /// <summary>
  /// Tax cost associated with a transaction (Double).
  /// </summary>
  public static string ParameterTax = "tax";

  /// <summary>
  /// If you're manually tagging keyword campaigns, you should use utm_term
  /// to specify the keyword (String).
  /// </summary>
  public static string ParameterTerm = "term";

  /// <summary>
  /// The unique identifier of a transaction (String).
  /// </summary>
  public static string ParameterTransactionID = "transaction_id";

  /// <summary>
  /// Travel class (String).
  /// </summary>
  public static string ParameterTravelClass = "travel_class";

  /// <summary>
  /// A context-specific numeric value which is accumulated automatically
  /// for each event type. This is a general purpose parameter that is
  /// useful for accumulating a key metric that pertains to an event.
  /// Examples include revenue, distance, time and points. Value should be
  /// specified as Int or Double. Notes: Values for pre-defined
  /// currency-related events (such as <c>AnalyticsEventAddToCart</c>) should be
  /// supplied using Double and must be accompanied by a @c
  /// AnalyticsParameterCurrency parameter. The valid range of accumulated
  /// values is [-9,223,372,036,854.77, 9,223,372,036,854.77]. Supplying a
  /// non-numeric value, omitting the corresponding @c
  /// AnalyticsParameterCurrency parameter, or supplying an invalid
  /// currency code (https://goo.gl/qqX3J2) for conversion events will cause that
  /// conversion to be omitted from reporting.
  /// </summary>
  public static string ParameterValue = "value";

  /// <summary>
  /// Name of virtual currency type (String).
  /// </summary>
  public static string ParameterVirtualCurrencyName = "virtual_currency_name";
}

}
