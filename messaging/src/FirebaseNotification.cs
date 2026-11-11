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

using System;
using System.Collections.Generic;

namespace Firebase.Messaging {

/// @brief Data structure for parameters that are unique to the Android
/// implementation.
public sealed class AndroidNotificationParams {
  public AndroidNotificationParams() { }

  /// The channel id that was provided when the message was sent.
  public string ChannelId { get; internal set; } = string.Empty;
}

/// Used for messages that display a notification.
///
/// On android, this requires that the app is using the Play Services client
/// library.
public sealed class FirebaseNotification {
  public FirebaseNotification() { }

  /// Android-specific data to show.
  public AndroidNotificationParams Android { get; internal set; }

  /// Indicates the badge on the client app home icon. iOS and tvOS only.
  public string Badge { get; internal set; } = string.Empty;

  /// Indicates notification body text.
  public string Body { get; internal set; } = string.Empty;

  /// Indicates the string value to replace format specifiers in body string
  /// for localization.
  ///
  /// On iOS and tvOS, this corresponds to "loc-args" in APNS payload.
  ///
  /// On Android, these are the format arguments for the string resource. For
  /// more information, see [Formatting strings][1].
  ///
  /// [1]:
  /// https://developer.android.com/guide/topics/resources/string-resource.html#FormattingAndStyling
  public IEnumerable<string> BodyLocalizationArgs { get; internal set; } = Array.Empty<string>();

  /// Indicates the key to the body string for localization.
  ///
  /// On iOS and tvOS, this corresponds to "loc-key" in APNS payload.
  ///
  /// On Android, use the key in the app's string resources when populating this
  /// value.
  public string BodyLocalizationKey { get; internal set; } = string.Empty;

  /// The action associated with a user click on the notification.
  ///
  /// On Android, if this is set, an activity with a matching intent filter is
  /// launched when user clicks the notification.
  ///
  /// If set on iOS or tvOS, corresponds to category in APNS payload.
  public string ClickAction { get; internal set; } = string.Empty;

  /// Indicates color of the icon, expressed in \#rrggbb format. Android only.
  public string Color { get; internal set; } = string.Empty;

  /// Indicates notification icon. Sets value to myicon for drawable resource
  /// myicon.
  public string Icon { get; internal set; } = string.Empty;

  /// Indicates a sound to play when the device receives the notification.
  /// Supports default, or the filename of a sound resource bundled in the
  /// app.
  ///
  /// Android sound files must reside in /res/raw/, while tvOS and iOS sound
  /// files can be in the main bundle of the client app or in the
  /// Library/Sounds folder of the app’s data container.
  public string Sound { get; internal set; } = string.Empty;

  /// Indicates whether each notification results in a new entry in the
  /// notification drawer on Android. If not set, each request creates a new
  /// notification. If set, and a notification with the same tag is already
  /// being shown, the new notification replaces the existing one in the
  /// notification drawer.
  public string Tag { get; internal set; } = string.Empty;

  /// Indicates notification title. This field is not visible on tvOS, iOS
  /// phones and tablets.
  public string Title { get; internal set; } = string.Empty;

  /// Indicates the string value to replace format specifiers in title string
  /// for localization.
  ///
  /// On iOS and tvOS, this corresponds to "title-loc-args" in APNS payload.
  ///
  /// On Android, these are the format arguments for the string resource. For
  /// more information, see [Formatting strings][1].
  ///
  /// [1]:
  /// https://developer.android.com/guide/topics/resources/string-resource.html#FormattingAndStyling
  public IEnumerable<string> TitleLocalizationArgs { get; internal set; } = Array.Empty<string>();

  /// Indicates the key to the title string for localization.
  ///
  /// On iOS and tvOS, this corresponds to "title-loc-key" in APNS payload.
  ///
  /// On Android, use the key in the app's string resources when populating this
  /// value.
  public string TitleLocalizationKey { get; internal set; } = string.Empty;
}

}  // namespace Firebase.Messaging
