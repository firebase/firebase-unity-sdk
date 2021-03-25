/*
 * Copyright 2021 Google LLC
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
using System.Threading.Tasks;

namespace Firebase.RemoteConfig {

  /// @brief Entry point for the old version of the Firebase C# SDK for Remote Config.
  public sealed class FirebaseRemoteConfigDeprecated {

    /// @brief The default cache expiration used by FetchAsync(), equal to 12 hours.
    [Obsolete("Use the property FirebaseRemoteConfig.DefaultCacheExpiration instead.")]
    public static TimeSpan DefaultCacheExpiration {
      get { return FirebaseRemoteConfig.DefaultCacheExpiration; }
    }

    /// @brief The default timeout used when Fetching, equal to 30 seconds,
    /// in milliseconds.
    [Obsolete("Use the property FirebaseRemoteConfig.DefaultTimeoutInMilliseconds instead.")]
    public static ulong kDefaultTimeoutInMilliseconds {
      get { return FirebaseRemoteConfig.DefaultTimeoutInMilliseconds; }
    }

    /// @brief Returns information about the last fetch request, in the form
    /// of a @ref ConfigInfo struct.
    [Obsolete("Use the instance property Info instead.")]
    public static ConfigInfo Info {
      get { return FirebaseRemoteConfig.DefaultInstance.Info; }
    }

    /// @brief Gets the set of all Remote Config parameter keys.
    [Obsolete("Use the instance property Keys instead.")]
    public static IEnumerable<string> Keys {
      get { return FirebaseRemoteConfig.DefaultInstance.Keys; }
    }

    /// @brief Gets and sets the configuration settings for operations.
    ///
    /// Get returns a copy of the current settings; to change the settings
    /// a ConfigSettings with the new settings needs to be set.
    [Obsolete(
        "Use the instance property ConfigSettings and method SetConfigSettingsAsync instead.")]
    public static ConfigSettings Settings {
      get { return FirebaseRemoteConfig.DefaultInstance.ConfigSettings; }
      set { FirebaseRemoteConfig.DefaultInstance.SetConfigSettingsAsync(value); }
    }

    /// @brief Applies the most recently fetched data, so that its values can be
    /// accessed.
    ///
    /// Calls to @ref GetLong(), @ref GetDouble(), @ref GetString() and
    /// @ref GetData() will not reflect the new data retrieved by @ref Fetch()
    /// until @ref ActivateFetched() is called.  This gives the developer control
    /// over when newly fetched data is visible to their application.
    ///
    /// @return true if a previously fetch configuration was activated, false
    /// if a fetched configuration wasn't found or the configuration was previously
    /// activated.
    [Obsolete("Use the instance method ActivateAsync instead.")]
    public static bool ActivateFetched() {
      LogUtil.LogMessage(LogLevel.Warning,
                         "Deprecated ActivateFetched called. This method is now asynchronous. " +
                             "Use the instance method ActivateAsync for better control.");
      FirebaseRemoteConfig.DefaultInstance.ActivateAsync();
      return false;
    }

    /// @brief Fetches config data from the server.
    ///
    /// @note This does not actually apply the data or make it accessible,
    /// it merely retrieves it and caches it.  To accept and access the newly
    /// retrieved values, you must call @ref ActivateFetched().
    /// Note that this function is asynchronous, and will normally take an
    /// unspecified amount of time before completion.
    ///
    /// @returns A Task which can be used to determine when the fetch is
    /// complete.
    [Obsolete("Use the instance method FetchAsync instead.")]
    public static Task FetchAsync() {
      return FirebaseRemoteConfig.DefaultInstance.FetchAsync();
    }

    /// @brief Fetches config data from the server.
    ///
    /// @note This does not actually apply the data or make it accessible,
    /// it merely retrieves it and caches it.  To accept and access the newly
    /// retrieved values, you must call @ref ActivateFetched().
    /// Note that this function is asynchronous, and will normally take an
    /// unspecified amount of time before completion.
    ///
    /// @param[in] cacheExpiration The time to keep previously fetch data
    /// available.  If cached data is available that is newer than
    /// cacheExpiration, then the function returns immediately and does not
    /// fetch any data. A cacheExpiration of zero seconds will always
    /// cause a fetch.
    ///
    /// @returns A Task which can be used to determine when the fetch is
    /// complete.
    [Obsolete("Use the instance method FetchAsync instead.")]
    public static Task FetchAsync(System.TimeSpan cacheExpiration) {
      return FirebaseRemoteConfig.DefaultInstance.FetchAsync(cacheExpiration);
    }

    /// @brief The set of all Remote Config parameter keys in the
    /// default namespace with prefix.
    [Obsolete("Use the instance method GetKeysByPrefix instead.")]
    public static IEnumerable<string> GetKeysByPrefix(string prefix) {
      return FirebaseRemoteConfig.DefaultInstance.GetKeysByPrefix(prefix);
    }

    /// @brief Gets the ConfigValue corresponding to the specified key.
    ///
    /// @param[in] key Key of the value to be retrieved.
    ///
    /// @returns The ConfigValue associated with the specified key.
    [Obsolete("Use the instance method GetValue instead.")]
    public static ConfigValue GetValue(string key) {
      return FirebaseRemoteConfig.DefaultInstance.GetValue(key);
    }

    /// @brief Sets the default values based on a string dictionary.
    ///
    /// @note This completely overrides all previous values.
    ///
    /// @param defaults IDictionary of string keys to values, representing the new
    /// set of defaults to apply. If the same key is specified multiple times, the
    /// value associated with the last duplicate key is applied.
    [Obsolete("Use the instance method SetDefaultsAsync instead.")]
    public static void SetDefaults(IDictionary<string, object> defaults) {
      LogUtil.LogMessage(LogLevel.Warning,
                         "Deprecated SetDefaults called. This method is now asynchronous. " +
                             "Use the instance method SetDefaultsAsync for better control.");
      FirebaseRemoteConfig.DefaultInstance.SetDefaultsAsync(defaults);
    }
  }

}  // namespace Firebase.RemoteConfig
