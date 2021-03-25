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

/// @brief Android Parameters.
public class AndroidParameters {
  /// Creates an Android Parameters object with the given package name.
  ///
  /// @param packageName The package name of the Android app to use to open
  /// the link. The app must be connected to your project from the Overview
  /// page of the Firebase console.
  public AndroidParameters(string packageName) {
    PackageName = packageName;
  }

  /// The package name of the Android app to use to open the link. The app
  /// must be connected to your project from the Overview page of the
  /// Firebase console.
  public string PackageName { get; set; }
  /// The link to open when the app isn't installed.
  ///
  /// Specify this to do something other than install your app from the
  /// Play Store when the app isn't installed, such as open the mobile web
  /// version of the content, or display a promotional page for your app.
  public System.Uri FallbackUrl { get; set; }
  /// The versionCode of the minimum version of your app that can open the
  /// link.
  public int MinimumVersion { get; set; }
}

}
