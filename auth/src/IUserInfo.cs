/*
 * Copyright 2016 Google LLC
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

namespace Firebase.Auth {

// This interface is used for the C# Api, instead of using the generated
// UserInfoInterface class, which cannot be an interface itself because of the
// generated UserInfoInterfaceList needing to construct new ones.
// UserInfoInterface instead implements this, and other classes that would
// implement UserInfoInterface, such as User, should instead use this.

/// @brief Interface implemented by each identity provider.
public interface IUserInfo {
  /// Gets the display name associated with the user, if any.
  string DisplayName { get; }
  /// Gets email associated with the user, if any.
  string Email { get; }
  /// Gets the photo url associated with the user, if any.
  System.Uri PhotoUrl { get; }
  /// Gets the provider ID for the user (For example, "Facebook").
  string ProviderId { get; }
  /// Gets the unique user ID for the user.
  ///
  /// @note The user's ID, unique to the Firebase project.
  /// Do NOT use this value to authenticate with your backend server, if you
  /// have one. Use User.Token() instead.
  string UserId { get; }
}

}  // namespace Firebase.Auth

