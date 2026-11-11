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

namespace Firebase {

/// @brief Options that control the creation of a Firebase App.
/// @if swig_examples
/// @see FirebaseApp
/// @endif
public sealed class AppOptions : global::System.IDisposable {

  /// @brief Create `AppOptions`.
  ///
  /// To create a FirebaseApp object, the Firebase application identifier and
  /// API key should be set using AppId and ApiKey respectively.
  ///
  /// @see FirebaseApp.Create().
  public AppOptions() { }

  // To prevent an API change, we need to keep this function.
  public void Dispose() { }

  /// The database root URL, e.g. @"http://abc-xyz-123.firebaseio.com".
  public System.Uri DatabaseUrl { get; set; }

  /// Gets or sets the App Id.
  ///
  /// This is the mobilesdk_app_id in the Android google-services.json config
  /// file or GOOGLE_APP_ID in the GoogleService-Info.plist.
  ///
  /// This only needs to be specified if your application does not include
  /// google-services.json or GoogleService-Info.plist in its resources.
  public string AppId { get; set; }

  /// Gets or sets the API key used to authenticate requests from your app.
  ///
  /// For example, "AIzaSyDdVgKwhZl0sTTTLZ7iTmt1r3N2cJLnaDk" used to identify
  /// your app to Google servers.
  ///
  /// This only needs to be specified if your application does not include
  /// google-services.json or GoogleService-Info.plist in its resources.
  public string ApiKey { get; set; }

  /// Gets or sets the messaging sender Id.
  ///
  /// This only needs to be specified if your application does not include
  /// google-services.json or GoogleService-Info.plist in its resources.
  public string MessageSenderId { get; set; }

  /// Gets or sets the Google Cloud Storage bucket name, e.g.
  /// @"abc-xyz-123.storage.firebase.com".
  public string StorageBucket { get; set; }

  /// Gets or sets the Google Cloud project ID.
  ///
  /// This is the project_id in the Android google-services.json config
  /// file or PROJECT_ID in the GoogleService-Info.plist.
  public string ProjectId { get; set; }

  /// Gets or sets the Android, iOS or tvOS client project name.
  ///
  /// This is the project_name in the Android google-services.json config
  /// file or BUNDLE_ID in the GoogleService-Info.plist.
  internal string PackageName { get; set; }

  /// @brief Load options from a JSON string.
  ///
  /// @param json_config JSON string to read options from.
  ///
  /// @returns Returns an AppOptions instance if successful, null otherwise.
  public static AppOptions LoadFromJsonConfig(string json_config) =>
      AppConfigLoader.LoadFromJsonConfig(json_config);

  internal static AppOptions LoadFromPlistConfig(string plistConfig) =>
      AppConfigLoader.LoadFromPlistConfig(plistConfig);

  internal AppOptions Clone() => new AppOptions {
    DatabaseUrl = DatabaseUrl, AppId = AppId, ApiKey = ApiKey,
    MessageSenderId = MessageSenderId, StorageBucket = StorageBucket,
    ProjectId = ProjectId, PackageName = PackageName,
  };
}

}  // namespace Firebase
