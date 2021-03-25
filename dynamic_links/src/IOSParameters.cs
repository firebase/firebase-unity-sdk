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

/// @brief iOS Parameters.
public class IOSParameters {
  /// Constructs an IOS Parameters object with the given bundle id.
  ///
  /// @param bundleId The bundle ID of the iOS app to use to open the link.
  /// The app must be connected to your project from the Overview page of the
  /// Firebase console.
  public IOSParameters(string bundleId) {
    BundleId = bundleId;
  }

  /// The parameters ID of the iOS app to use to open the link. The app must
  /// be connected to your project from the Overview page of the Firebase
  /// console.
  public string BundleId { get; set; }
  /// The link to open on iOS if the app is not installed.
  ///
  /// Specify this to do something other than install your app from the
  /// App Store when the app isn't installed, such as open the mobile
  /// web version of the content, or display a promotional page for your app.
  public System.Uri FallbackUrl { get; set; }
  /// The app's custom URL scheme, if defined to be something other than your
  /// app's parameters ID.
  public string CustomScheme { get; set; }
  /// The link to open on iPad if the app is not installed.
  ///
  /// Overrides fallback_url when on iPad.
  public System.Uri IPadFallbackUrl { get; set; }
  /// The iPad parameters ID of the app.
  public string IPadBundleId { get; set; }
  /// The App Store ID, used to send users to the App Store when the app
  /// isn't installed.
  public string AppStoreId { get; set; }
  /// The minimum version of your app that can open the link.
  public string MinimumVersion { get; set; }
}

}
