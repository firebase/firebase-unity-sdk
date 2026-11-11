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

using System;
using Firebase.Auth.Internal;

namespace Firebase.Auth {
  /// @brief Interface implemented by each identity provider.
  public interface IUserInfo {
    /// Gets the display name associated with the user, if any.
    string DisplayName { get; }
    /// Gets email associated with the user, if any.
    string Email { get; }
    /// Gets the photo url associated with the user, if any.
    Uri PhotoUrl { get; }
    /// Gets the provider ID for the user (For example, "Facebook").
    string ProviderId { get; }
    /// Gets the unique user ID for the user.
    ///
    /// @note The user's ID, unique to the Firebase project.
    /// Do NOT use this value to authenticate with your backend server, if you
    /// have one. Use User.Token() instead.
    string UserId { get; }
  }

  /// <summary>Represents profile information for a user from a specific identity provider.</summary>
  public class UserInfoInterface : IUserInfo, IDisposable {
    private readonly AuthProviderInfoSnapshot _info;
    private readonly Uri _photoUri;

    public UserInfoInterface() => _info = new AuthProviderInfoSnapshot();

    internal UserInfoInterface(AuthProviderInfoSnapshot info) {
      _info = info ?? new AuthProviderInfoSnapshot();
      _photoUri = AuthUrlHelper.StringToUri(_info.PhotoUrl);
    }

    internal virtual string GetDisplayNameCore() => _info?.DisplayName ?? string.Empty;
    internal virtual string GetEmailCore() => _info?.Email ?? string.Empty;
    internal virtual string GetPhoneNumberCore() => _info?.PhoneNumber ?? string.Empty;
    internal virtual Uri GetPhotoUrlCore() => _photoUri;
    internal virtual string GetProviderIdCore() => _info?.ProviderId ?? string.Empty;
    internal virtual string GetUserIdCore() => _info?.UserId ?? string.Empty;

    /// Gets the display name associated with the user, if any.
    public string DisplayName => GetDisplayNameCore();

    /// Gets email associated with the user, if any.
    public string Email => GetEmailCore();

    /// Gets the phone number for the user, in E.164 format.
    public string PhoneNumber => GetPhoneNumberCore();

    /// Gets the photo url associated with the user, if any.
    public Uri PhotoUrl => GetPhotoUrlCore();

    /// Gets the provider ID for the user (For example, "Facebook").
    public string ProviderId => GetProviderIdCore();

    /// Gets the unique Firebase user ID for the user.
    ///
    /// @note The user's ID, unique to the Firebase project.
    /// Do NOT use this value to authenticate with your backend server, if you
    /// have one.
    /// Use FirebaseUser.TokenAsync instead.
    public string UserId => GetUserIdCore();

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>Metadata associated with a <see cref="FirebaseUser" /> account.</summary>
  public class UserMetadata : IDisposable {
    private static readonly DateTime UnixEpochUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly DateTime _creationTimestamp;
    private readonly DateTime _lastSignInTimestamp;

    public UserMetadata() {
      _creationTimestamp = UnixEpochUtc;
      _lastSignInTimestamp = UnixEpochUtc;
    }

    internal UserMetadata(long creationTimestampMs, long lastSignInTimestampMs) {
      _creationTimestamp = creationTimestampMs > 0 ? UnixEpochUtc.AddMilliseconds(creationTimestampMs) : UnixEpochUtc;
      _lastSignInTimestamp = lastSignInTimestampMs > 0 ? UnixEpochUtc.AddMilliseconds(lastSignInTimestampMs) : _creationTimestamp;
    }

    /// The Firebase user creation UTC timestamp.
    public DateTime CreationTimestamp => _creationTimestamp;

    /// The last sign in UTC timestamp.
    public DateTime LastSignInTimestamp => _lastSignInTimestamp;

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public virtual void Dispose(bool disposing) { }
  }

  /// <summary>Profile properties that can be updated via <see cref="FirebaseUser.UpdateUserProfileAsync" />.</summary>
  /// <remarks>
  /// Unset properties are not modified, while setting a property to <c>""</c> or <c>null</c> resets it.
  /// </remarks>
  public sealed class UserProfile : IDisposable {
    private string _displayName = string.Empty;
    private string _photoUrl = string.Empty;

    internal bool HasDisplayName { get; private set; }
    internal bool HasPhotoUrl { get; private set; }
    internal string PhotoUrlString => _photoUrl;

    /// Construct a UserProfile with no display name or photo URL.
    public UserProfile() { }

    /// Gets or sets the display name associated with the user.
    public string DisplayName {
      get => _displayName;
      set {
        HasDisplayName = true;
        _displayName = value ?? string.Empty;
      }
    }

    /// User photo URI.
    /// The photo url associated with the user, if any.
    public Uri PhotoUrl {
      get => AuthUrlHelper.StringToUri(_photoUrl);
      set {
        HasPhotoUrl = true;
        _photoUrl = AuthUrlHelper.UriToString(value);
      }
    }

    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    public void Dispose(bool disposing) { }
  }
}  // namespace Firebase.Auth

