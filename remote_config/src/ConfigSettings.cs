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

namespace Firebase.RemoteConfig {

  /// @brief Settings for FirebaseRemoteConfig operations.
  public struct ConfigSettings {
    /// The timeout specifies how long the client should wait for a connection to
    /// the Firebase Remote Config servers.
    ///
    /// @note A fetch call will fail if it takes longer than the specified timeout
    /// to connect to the Remote Config servers. Default is 60 seconds.
    public ulong FetchTimeoutInMilliseconds { get; set; }

    /// The minimum interval between successive fetch calls.
    ///
    /// @note Fetches less than duration seconds after the last fetch from the
    /// Firebase Remote Config server would use values returned during the last
    /// fetch. Default is 12 hours.
    public ulong MinimumFetchInternalInMilliseconds { get; set; }

    /// @brief Enable / disable developer mode.
    ///
    /// During app development, you might want to refresh the cache very
    /// frequently (many times per hour) to let you rapidly iterate as you
    /// develop and test your app. To accommodate rapid iteration on a project
    /// with up to 10 developers, you can set isDeveloperModeEnabled to true,
    /// changing the caching settings of the FirebaseRemoteConfig object.
    [System.Obsolete("This property is no longer used")]
    public bool IsDeveloperMode { get; set; }

    internal static ConfigSettings FromInternal(ConfigSettingsInternal csInternal) {
      return new ConfigSettings {
        FetchTimeoutInMilliseconds = csInternal.fetch_timeout_in_milliseconds,
        MinimumFetchInternalInMilliseconds = csInternal.minimum_fetch_interval_in_milliseconds
      };
    }

    internal static ConfigSettingsInternal ToInternal(ConfigSettings cs) {
      ConfigSettingsInternal csInternal = new ConfigSettingsInternal();
      csInternal.fetch_timeout_in_milliseconds = cs.FetchTimeoutInMilliseconds;
      csInternal.minimum_fetch_interval_in_milliseconds = cs.MinimumFetchInternalInMilliseconds;
      return csInternal;
    }
  }
}
