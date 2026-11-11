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
  /// Ad Impression event. This event signifies when a user sees an ad
  /// impression. Note: If you supply the <c>AnalyticsParameterValue</c>
  /// parameter, you must also supply the <c>AnalyticsParameterCurrency</c>
  /// parameter so that revenue metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterAdPlatform</c> (String) (optional)
  /// <c>AnalyticsParameterAdFormat</c> (String) (optional)
  /// <c>AnalyticsParameterAdSource</c> (String) (optional)
  /// <c>AnalyticsParameterAdUnitName</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventAdImpression = "ad_impression";

  /// <summary>
  /// Add Payment Info event. This event signifies that a user has submitted
  /// their payment information. Note: If you supply the @c
  /// AnalyticsParameterValue parameter, you must also supply the @c
  /// AnalyticsParameterCurrency parameter so that revenue metrics can be
  /// computed accurately. Params:
  /// <c>AnalyticsParameterCoupon</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterPaymentType</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventAddPaymentInfo = "add_payment_info";

  /// <summary>
  /// Add Shipping Info event. This event signifies that a user has
  /// submitted their shipping information. Note: If you supply the @c
  /// AnalyticsParameterValue parameter, you must also supply the @c
  /// AnalyticsParameterCurrency parameter so that revenue metrics can be
  /// computed accurately. Params:
  /// <c>AnalyticsParameterCoupon</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterShippingTier</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventAddShippingInfo = "add_shipping_info";

  /// <summary>
  /// E-Commerce Add To Cart event. This event signifies that an item(s) was
  /// added to a cart for purchase. Add this event to a funnel with @c
  /// AnalyticsEventPurchase to gauge the effectiveness of your
  /// checParameter(kout, If you supply the <c>AnalyticsParameterValue</c>
  /// parameter), you must also supply the <c>AnalyticsParameterCurrency</c>
  /// parameter so that revenue metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventAddToCart = "add_to_cart";

  /// <summary>
  /// E-Commerce Add To Wishlist event. This event signifies that an item
  /// was added to a wishlist. Use this event to identify popular gift
  /// items. Note: If you supply the <c>AnalyticsParameterValue</c> parameter,
  /// you must also supply the <c>AnalyticsParameterCurrency</c> parameter so
  /// that revenue metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventAddToWishlist = "add_to_wishlist";

  /// <summary>
  /// App Open event. By logging this event when an App becomes active,
  /// developers can understand how often users leave and return during the
  /// course of a Session. Although Sessions are automatically reported,
  /// this event can provide further clarification around the continuous
  /// engagement of app-users.
  /// </summary>
  public static string EventAppOpen = "app_open";

  /// <summary>
  /// E-Commerce Begin Checkout event. This event signifies that a user has
  /// begun the process of checking out. Add this event to a funnel with
  /// your <c>AnalyticsEventPurchase</c> event to gauge the effectiveness of
  /// your checkout process. Note: If you supply the @c
  /// AnalyticsParameterValue parameter, you must also supply the @c
  /// AnalyticsParameterCurrency parameter so that revenue metrics can be
  /// computed accurately. Params:
  /// <c>AnalyticsParameterCoupon</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventBeginCheckout = "begin_checkout";

  /// <summary>
  /// Campaign Detail event. Log this event to supply the referral details
  /// of a re-engagement campaign. Note: you must supply at least one of the
  /// required parameters AnalyticsParameterSource, AnalyticsParameterMedium
  /// or AnalyticsParameterCampaign. Params:
  /// <c>AnalyticsParameterSource</c> (String)
  /// <c>AnalyticsParameterMedium</c> (String)
  /// <c>AnalyticsParameterCampaign</c> (String)
  /// <c>AnalyticsParameterTerm</c> (String) (optional)
  /// <c>AnalyticsParameterContent</c> (String) (optional)
  /// <c>AnalyticsParameterAdNetworkClickID</c> (String) (optional)
  /// <c>AnalyticsParameterCP1</c> (String) (optional)
  /// <c>AnalyticsParameterCampaignID</c> (String) (optional)
  /// <c>AnalyticsParameterCreativeFormat</c> (String) (optional)
  /// <c>AnalyticsParameterMarketingTactic</c> (String) (optional)
  /// <c>AnalyticsParameterSourcePlatform</c> (String) (optional)
  /// </summary>
  public static string EventCampaignDetails = "campaign_details";

  /// <summary>
  /// Earn Virtual Currency event. This event tracks the awarding of virtual
  /// currency in your app. Log this along with @c
  /// AnalyticsEventSpendVirtualCurrency to better understand your virtual
  /// economy. Params:
  /// <c>AnalyticsParameterVirtualCurrencyName</c> (String)
  /// <c>AnalyticsParameterValue</c> (Int or Double)
  /// </summary>
  public static string EventEarnVirtualCurrency = "earn_virtual_currency";

  /// <summary>
  /// Generate Lead event. Log this event when a lead has been generated in
  /// the app to understand the efficacy of your install and re-engagement
  /// campaigns. Note: If you supply the <c>AnalyticsParameterValue</c>
  /// parameter, you must also supply the <c>AnalyticsParameterCurrency</c>
  /// parameter so that revenue metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventGenerateLead = "generate_lead";

  /// <summary>
  /// In-App Purchase event. This event signifies that extra content or a
  /// subscription was purchased by a user inside an app. Note: This is
  /// different from the ecommerce purchase event. Note: If you supply the
  /// <c>AnalyticsParameterValue</c> parameter, you must also supply the @c
  /// AnalyticsParameterCurrency parameter so that revenue metrics can be
  /// computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String)
  /// <c>AnalyticsParameterFreeTrial</c> (Int) (optional)
  /// <c>AnalyticsParameterPrice</c> (Double) (optional)
  /// <c>AnalyticsParameterPriceIsDiscounted</c> (Int) (optional)
  /// <c>AnalyticsParameterProductID</c> (String) (optional)
  /// <c>AnalyticsParameterProductName</c> (String) (optional)
  /// <c>AnalyticsParameterQuantity</c> (Int) (optional)
  /// <c>AnalyticsParameterSubscription</c> (Int) (optional)
  /// <c>AnalyticsParameterValue</c> (Double)
  /// </summary>
  public static string EventInAppPurchase = "in_app_purchase";

  /// <summary>
  /// Join Group event. Log this event when a user joins a group such as a
  /// guild, team or family. Use this event to analyze how popular certain
  /// groups or social features are in your app. Params:
  /// <c>AnalyticsParameterGroupID</c> (String)
  /// </summary>
  public static string EventJoinGroup = "join_group";

  /// <summary>
  /// Level End event. Log this event when the user finishes a level.
  /// Params:
  /// <c>AnalyticsParameterLevelName</c> (String)
  /// <c>AnalyticsParameterSuccess</c> (String)
  /// </summary>
  public static string EventLevelEnd = "level_end";

  /// <summary>
  /// Level Start event. Log this event when the user starts a new level.
  /// Params:
  /// <c>AnalyticsParameterLevelName</c> (String)
  /// </summary>
  public static string EventLevelStart = "level_start";

  /// <summary>
  /// Level Up event. This event signifies that a player has leveled up in
  /// your gaming app. It can help you gauge the level distribution of your
  /// userbase and help you identify certain levels that are difficult to
  /// pass. Params:
  /// <c>AnalyticsParameterLevel</c> (Int)
  /// <c>AnalyticsParameterCharacter</c> (String) (optional)
  /// </summary>
  public static string EventLevelUp = "level_up";

  /// <summary>
  /// Login event. Apps with a login feature can report this event to
  /// signify that a user has logged in.
  /// </summary>
  public static string EventLogin = "login";

  /// <summary>
  /// Post Score event. Log this event when the user posts a score in your
  /// gaming app. This event can help you understand how users are actually
  /// performing in your game and it can help you correlate high scores with
  /// certain audiences or behaviors. Params:
  /// <c>AnalyticsParameterScore</c> (Int)
  /// <c>AnalyticsParameterLevel</c> (Int) (optional)
  /// <c>AnalyticsParameterCharacter</c> (String) (optional)
  /// </summary>
  public static string EventPostScore = "post_score";

  /// <summary>
  /// E-Commerce Purchase event. This event signifies that an item(s) was
  /// purchased by a user. Note: This is different from the in-app purchase
  /// event, which is reported automatically for App Store-based apps. Note:
  /// If you supply the <c>AnalyticsParameterValue</c> parameter, you must also
  /// supply the <c>AnalyticsParameterCurrency</c> parameter so that revenue
  /// metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterAffiliation</c> (String) (optional)
  /// <c>AnalyticsParameterCoupon</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterEndDate</c> (String) (optional)
  /// <c>AnalyticsParameterItemID</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterShipping</c> (Double) (optional)
  /// <c>AnalyticsParameterStartDate</c> (String) (optional)
  /// <c>AnalyticsParameterTax</c> (Double) (optional)
  /// <c>AnalyticsParameterTransactionID</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventPurchase = "purchase";

  /// <summary>
  /// E-Commerce Refund event. This event signifies that a refund was
  /// issued. Note: If you supply the <c>AnalyticsParameterValue</c> parameter,
  /// you must also supply the <c>AnalyticsParameterCurrency</c> parameter so
  /// that revenue metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterAffiliation</c> (String) (optional)
  /// <c>AnalyticsParameterCoupon</c> (String) (optional)
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterShipping</c> (Double) (optional)
  /// <c>AnalyticsParameterTax</c> (Double) (optional)
  /// <c>AnalyticsParameterTransactionID</c> (String) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventRefund = "refund";

  /// <summary>
  /// E-Commerce Remove from Cart event. This event signifies that an
  /// item(s) was removed from a cart. Note: If you supply the @c
  /// AnalyticsParameterValue parameter, you must also supply the @c
  /// AnalyticsParameterCurrency parameter so that revenue metrics can be
  /// computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventRemoveFromCart = "remove_from_cart";

  /// <summary>
  /// Screen View event. This event signifies a screen view. Use this when a
  /// screen transition occurs. This event can be logged irrespective of
  /// whether automatic screen tracking is enabled. Params:
  /// <c>AnalyticsParameterScreenClass</c> (String) (optional)
  /// <c>AnalyticsParameterScreenName</c> (String) (optional)
  /// </summary>
  public static string EventScreenView = "screen_view";

  /// <summary>
  /// Search event. Apps that support search features can use this event to
  /// contextualize search operations by supplying the appropriate,
  /// corresponding parameters. This event can help you identify the most
  /// popular content in your app. Params:
  /// <c>AnalyticsParameterSearchTerm</c> (String)
  /// <c>AnalyticsParameterStartDate</c> (String) (optional)
  /// <c>AnalyticsParameterEndDate</c> (String) (optional)
  /// <c>AnalyticsParameterNumberOfNights</c> (Int) (optional) for hotel bookings
  /// <c>AnalyticsParameterNumberOfRooms</c> (Int) (optional) for hotel bookings
  /// <c>AnalyticsParameterNumberOfPassengers</c> (Int) (optional) for travel bookings
  /// <c>AnalyticsParameterOrigin</c> (String) (optional)
  /// <c>AnalyticsParameterDestination</c> (String) (optional)
  /// <c>AnalyticsParameterTravelClass</c> (String) (optional) for travel bookings
  /// </summary>
  public static string EventSearch = "search";

  /// <summary>
  /// Select Content event. This general purpose event signifies that a user
  /// has selected some content of a certain type in an app. The content can
  /// be any object in your app. This event can help you identify popular
  /// content and categories of content in your app. Params:
  /// <c>AnalyticsParameterContentType</c> (String)
  /// <c>AnalyticsParameterItemID</c> (String)
  /// </summary>
  public static string EventSelectContent = "select_content";

  /// <summary>
  /// Select Item event. This event signifies that an item was selected by a
  /// user from a list. Use the appropriate parameters to contextualize the
  /// event. Use this event to discover the most popular items selected.
  /// Params:
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterItemListID</c> (String) (optional)
  /// <c>AnalyticsParameterItemListName</c> (String) (optional)
  /// </summary>
  public static string EventSelectItem = "select_item";

  /// <summary>
  /// Select promotion event. This event signifies that a user has selected
  /// a promotion offer. Use the appropriate parameters to contextualize the
  /// event, such as the item(s) for which the promotion applies. Params:
  /// <c>AnalyticsParameterCreativeName</c> (String) (optional)
  /// <c>AnalyticsParameterCreativeSlot</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterLocationID</c> (String) (optional)
  /// <c>AnalyticsParameterPromotionID</c> (String) (optional)
  /// <c>AnalyticsParameterPromotionName</c> (String) (optional)
  /// </summary>
  public static string EventSelectPromotion = "select_promotion";

  /// <summary>
  /// Share event. Apps with social features can log the Share event to
  /// identify the most viral content. Params:
  /// <c>AnalyticsParameterContentType</c> (String)
  /// <c>AnalyticsParameterItemID</c> (String)
  /// </summary>
  public static string EventShare = "share";

  /// <summary>
  /// Sign Up event. This event indicates that a user has signed up for an
  /// account in your app. The parameter signifies the method by which the
  /// user signed up. Use this event to understand the different behaviors
  /// between logged in and logged out users. Params:
  /// <c>AnalyticsParameterMethod</c> (String)
  /// </summary>
  public static string EventSignUp = "sign_up";

  /// <summary>
  /// Spend Virtual Currency event. This event tracks the sale of virtual
  /// goods in your app and can help you identify which virtual goods are
  /// the most popular objects of purchase. Params:
  /// <c>AnalyticsParameterItemName</c> (String)
  /// <c>AnalyticsParameterVirtualCurrencyName</c> (String)
  /// <c>AnalyticsParameterValue</c> (Int or Double)
  /// </summary>
  public static string EventSpendVirtualCurrency = "spend_virtual_currency";

  /// <summary>
  /// Tutorial Begin event. This event signifies the start of the
  /// on-boarding process in your app. Use this in a funnel with @c
  /// AnalyticsEventTutorialComplete to understand how many users complete
  /// this process and move on to the full app experience.
  /// </summary>
  public static string EventTutorialBegin = "tutorial_begin";

  /// <summary>
  /// Tutorial End event. Use this event to signify the user's completion of
  /// your app's on-boarding process. Add this to a funnel with @c
  /// AnalyticsEventTutorialBegin to gauge the completion rate of your
  /// on-boarding process.
  /// </summary>
  public static string EventTutorialComplete = "tutorial_complete";

  /// <summary>
  /// Unlock Achievement event. Log this event when the user has unlocked an
  /// achievement in your game. Since achievements generally represent the
  /// breadth of a gaming experience, this event can help you understand how
  /// many users are experiencing all that your game has to offer. Params:
  /// <c>AnalyticsParameterAchievementID</c> (String)
  /// </summary>
  public static string EventUnlockAchievement = "unlock_achievement";

  /// <summary>
  /// E-commerce View Cart event. This event signifies that a user has
  /// viewed their cart. Use this to analyze your purchase funnel. Note: If
  /// you supply the <c>AnalyticsParameterValue</c> parameter, you must also
  /// supply the <c>AnalyticsParameterCurrency</c> parameter so that revenue
  /// metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventViewCart = "view_cart";

  /// <summary>
  /// View Item event. This event signifies that a user has viewed an item.
  /// Use the appropriate parameters to contextualize the event. Use this
  /// event to discover the most popular items viewed in your app. Note: If
  /// you supply the <c>AnalyticsParameterValue</c> parameter, you must also
  /// supply the <c>AnalyticsParameterCurrency</c> parameter so that revenue
  /// metrics can be computed accurately. Params:
  /// <c>AnalyticsParameterCurrency</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterValue</c> (Double) (optional)
  /// </summary>
  public static string EventViewItem = "view_item";

  /// <summary>
  /// View Item List event. Log this event when a user sees a list of items
  /// or offerings. Params:
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterItemListID</c> (String) (optional)
  /// <c>AnalyticsParameterItemListName</c> (String) (optional)
  /// </summary>
  public static string EventViewItemList = "view_item_list";

  /// <summary>
  /// View Promotion event. This event signifies that a promotion was shown
  /// to a user. Add this event to a funnel with the @c
  /// AnalyticsEventAddToCart and <c>AnalyticsEventPurchase</c> to gauge your
  /// conversion process. Params:
  /// <c>AnalyticsParameterCreativeName</c> (String) (optional)
  /// <c>AnalyticsParameterCreativeSlot</c> (String) (optional)
  /// <c>AnalyticsParameterItems</c> ([[String: Any]]) (optional)
  /// <c>AnalyticsParameterLocationID</c> (String) (optional)
  /// <c>AnalyticsParameterPromotionID</c> (String) (optional)
  /// <c>AnalyticsParameterPromotionName</c> (String) (optional)
  /// </summary>
  public static string EventViewPromotion = "view_promotion";

  /// <summary>
  /// View Search Results event. Log this event when the user has been
  /// presented with the results of a search. Params:
  /// <c>AnalyticsParameterSearchTerm</c> (String)
  /// </summary>
  public static string EventViewSearchResults = "view_search_results";
}

}
