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

  /// @brief Entry point for the Firebase C# SDK for Remote Config.
  public sealed class FirebaseRemoteConfig {
    // Dictionary of FirebaseRemoteConfig instances indexed by a key
    // FirebaseRemoteConfigInternal.InstanceKey.
    private static readonly Dictionary<string, FirebaseRemoteConfig> remoteConfigByInstanceKey =
        new Dictionary<string, FirebaseRemoteConfig>();

    // Proxy for the C++ firebase::remote_config::remote_config object.
    private FirebaseRemoteConfigInternal remoteConfigInternal;
    // Proxy for the C++ firebase::app::App object.
    private readonly FirebaseApp firebaseApp;
    // Key of this instance within remoteConfigByInstanceKey.
    private string instanceKey;

    // Function for C++ to call when the config is updated.
    private static RemoteConfigUtil.ConfigUpdateDelegate configUpdateDelegate =
      new RemoteConfigUtil.ConfigUpdateDelegate(ConfigUpdateMethod);

    /// @brief App object associated with this FirebaseRemoteConfig.
    public FirebaseApp App {
      get { return firebaseApp; }
    }

    private event EventHandler<ConfigUpdateEventArgs> ConfigUpdateListenerImpl;
    // EventHandlers that will be used as ConfigUpdateListeners. The first listener added will open 
    // the stream connection and the last removed will close the stream.
    public event EventHandler<ConfigUpdateEventArgs> OnConfigUpdateListener {
      add {
        ThrowIfNull();
        // If this is the first listener, hook into C++.
        if (ConfigUpdateListenerImpl == null ||
            ConfigUpdateListenerImpl.GetInvocationList().Length == 0) {
          RemoteConfigUtil.SetConfigUpdateCallback(remoteConfigInternal, configUpdateDelegate);
        }

        ConfigUpdateListenerImpl += value;
      }
      remove {
        ThrowIfNull();
        ConfigUpdateListenerImpl -= value;

        // If that was the last listener, remove the C++ hooks.
        if (ConfigUpdateListenerImpl == null ||
            ConfigUpdateListenerImpl.GetInvocationList().Length == 0) {
          RemoteConfigUtil.SetConfigUpdateCallback(remoteConfigInternal, null);
        }
      }
    }

    private FirebaseRemoteConfig(FirebaseRemoteConfigInternal remoteConfig, FirebaseApp app) {
      firebaseApp = app;
      firebaseApp.AppDisposed += OnAppDisposed;
      remoteConfigInternal = remoteConfig;

      // As we know there is only one reference to the C++ internal object here
      // we'll let the proxy object take ownership so the C++ object can be deleted when the
      // proxy's Dispose() method is executed.
      remoteConfigInternal.SetSwigCMemOwn(true);
      instanceKey = remoteConfigInternal.InstanceKey;
    }

    // Dispose of this object.
    private static void DisposeObject(object objectToDispose) {
      ((FirebaseRemoteConfig)objectToDispose).Dispose();
    }

    /// <summary>
    /// Remove the reference to this object from the remoteConfigByInstanceKey dictionary.
    /// </summary>
    ~FirebaseRemoteConfig() {
      Dispose();
    }

    // Remove the reference to this instance from remoteConfigByInstanceKey and dispose the proxy.
    private void Dispose() {
      System.GC.SuppressFinalize(this);
      lock (remoteConfigByInstanceKey) {
        if (remoteConfigInternal != null &&
            FirebaseRemoteConfigInternal.getCPtr(remoteConfigInternal).Handle != IntPtr.Zero) {
          firebaseApp.AppDisposed -= OnAppDisposed;
          remoteConfigByInstanceKey.Remove(instanceKey);
          remoteConfigInternal.Dispose();
          remoteConfigInternal = null;
        }
      }
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      LogUtil.LogMessage(Firebase.LogLevel.Warning, "FirebaseRemoteConfig.OnAppDisposed()");
      Dispose();
    }

    // Throw a NullReferenceException if this proxy references a deleted object.
    private void ThrowIfNull() {
      if (remoteConfigInternal == null ||
          FirebaseRemoteConfigInternal.getCPtr(remoteConfigInternal).Handle == System.IntPtr.Zero) {
        throw new System.NullReferenceException();
      }
    }

    /// @brief Returns a FirebaseRemoteConfig, initialized with a custom Firebase App.
    ///
    /// @params app The customer FirebaseApp used for initialization.
    ///
    /// @returns A FirebaseRemoteConfig instance.
    public static FirebaseRemoteConfig GetInstance(FirebaseApp app) {
      app = app ?? FirebaseApp.DefaultInstance;
      FirebaseRemoteConfigInternal remoteConfigInternal =
          FirebaseRemoteConfigInternal.GetInstanceInternal(app);

      if (remoteConfigInternal == null) {
        LogUtil.LogMessage(LogLevel.Error,
                           "Unable to create FirebaseRemoteConfig for app " + app.Name);
        return null;
      }
      FirebaseRemoteConfig remoteConfig = null;
      lock (remoteConfigByInstanceKey) {
        var instanceKey = remoteConfigInternal.InstanceKey;
        remoteConfig = FindByKey(instanceKey);
        if (remoteConfig != null)
          return remoteConfig;
        remoteConfig = new FirebaseRemoteConfig(remoteConfigInternal, app);
        remoteConfigByInstanceKey[instanceKey] = remoteConfig;
      }
      return remoteConfig;
    }

    private static FirebaseRemoteConfig FindByKey(string instanceKey) {
      lock (remoteConfigByInstanceKey) {
        FirebaseRemoteConfig remoteConfig = null;
        if (remoteConfigByInstanceKey.TryGetValue(instanceKey, out remoteConfig)) {
          if (remoteConfig != null)
            return remoteConfig;
          remoteConfigByInstanceKey.Remove(instanceKey);
        }
      }
      return null;
    }

    /// @brief Returns the FirebaseRemoteConfig initialized with the default FirebaseApp.
    public static FirebaseRemoteConfig DefaultInstance {
      get { return GetInstance(FirebaseApp.DefaultInstance); }
    }

    /// @brief Returns a Task that contains ConfigInfo representing the
    /// initialization status of this Firebase Remote Config instance.
    /// Use this method to ensure Set/Get call not being blocked.
    ///
    /// @returns A Task contains ConfigInfo.
    public System.Threading.Tasks.Task<ConfigInfo> EnsureInitializedAsync() {
      ThrowIfNull();
      return remoteConfigInternal.EnsureInitializedAsync();
    }

    /// @brief Asynchronously activates the most recently fetched configs,
    /// so that the fetched key value pairs take effect.
    ///
    /// @returns A Task that contains true if fetched configs were activated.
    /// The Task will contain false if the configs were already activated.
    public System.Threading.Tasks.Task<bool> ActivateAsync() {
      ThrowIfNull();
      return remoteConfigInternal.ActivateAsync();
    }

    /// @brief Asynchronously fetches and then activates the fetched configs.
    ///
    /// If the time elapsed since the last fetch from the Firebase Remote Config
    /// backend is more than the default minimum fetch interval, configs are
    /// fetched from the backend.
    ///
    /// After the fetch is complete, the configs are activated so that the fetched
    /// key value pairs take effect.
    ///
    /// @returns A Task that contains true if the call activated the fetched configs.
    /// The Task will contain false if the fetch failed, or the configs were already
    /// activated.
    public Task<bool> FetchAndActivateAsync() {
      ThrowIfNull();
      return remoteConfigInternal.FetchAndActivateAsync();
    }

    /// @brief Fetches config data from the server.
    ///
    /// @note This does not actually apply the data or make it accessible,
    /// it merely retrieves it and caches it.  To accept and access the newly
    /// retrieved values, you must call @ref ActivateAsync().
    /// Note that this function is asynchronous, and will normally take an
    /// unspecified amount of time before completion.
    ///
    /// @returns A Task which can be used to determine with the fetch is complete.
    public Task FetchAsync() {
      ThrowIfNull();
      return remoteConfigInternal.FetchAsync();
    }

    /// @brief Fetches config data from the server.
    ///
    /// @note This does not actually apply the data or make it accessible,
    /// it merely retrieves it and caches it.  To accept and access the newly
    /// retrieved values, you must call @ref ActivateAsync().
    /// Note that this function is asynchronous, and will normally take an
    /// unspecified amount of time before completion.
    ///
    /// @param[in] cacheExpiration The amount of time to keep
    /// previously fetch data available.  If cached data is available that is
    /// newer than cacheExpiration, then the function returns
    /// immediately and does not fetch any data. A cacheExpiration of
    /// zero will always cause a fetch.
    ///
    /// @returns A Task which can be used to determine with the fetch is complete.
    public System.Threading.Tasks.Task FetchAsync(TimeSpan cacheExpiration) {
      ThrowIfNull();
      return remoteConfigInternal.FetchAsync((ulong)cacheExpiration.TotalSeconds);
    }

    /// @brief Sets the default values based on a string to object dictionary.
    ///
    /// @note This completely overrides all previous values.
    ///
    /// @param defaults IDictionary of string keys to values, representing the new
    /// set of defaults to apply. If the same key is specified multiple times, the
    /// value associated with the last duplicate key is applied.
    ///
    /// @return A Task which can be used to determine when the operation is
    /// complete.
    public Task SetDefaultsAsync(IDictionary<string, object> defaults) {
      ThrowIfNull();
      return remoteConfigInternal.SetDefaultsInternalAsync(
          RemoteConfigUtil.ConvertDictionaryToMap(defaults));
    }

    /// @brief Asynchronously changes the settings for this Remote Config
    /// instance.
    ///
    /// @param settings The new settings to be applied.
    ///
    /// @return a Task which can be used to determine when the operation is
    /// complete.
    public Task SetConfigSettingsAsync(ConfigSettings settings) {
      ThrowIfNull();
      return remoteConfigInternal.SetConfigSettingsAsync(ConfigSettings.ToInternal(settings));
    }

    /// @brief Gets the current settings of the RemoteConfig object.
    public ConfigSettings ConfigSettings {
      get {
        ThrowIfNull();
        return ConfigSettings.FromInternal(remoteConfigInternal.GetConfigSettings());
      }
    }

    /// @brief Gets the @ref ConfigValue corresponding to the key.
    ///
    /// @param key Key of the value to be retrieved.
    /// @returns The @ref ConfigValue associated with the key.
    public ConfigValue GetValue(string key) {
      ThrowIfNull();
      ConfigValueInternal valueInternal = remoteConfigInternal.GetValueInternal(key);
      byte[] array = new byte[valueInternal.data.Count];
      valueInternal.data.CopyTo(array);
      ConfigValue result = new ConfigValue(array, valueInternal.source);
      valueInternal.Dispose();
      return result;
    }

    /// @brief Gets the set of all Remote Config parameter keys.
    public IEnumerable<string> Keys {
      get {
        ThrowIfNull();
        return remoteConfigInternal.GetKeys();
      }
    }

    /// @brief Gets the set of keys that start with the given prefix.
    ///
    /// @param[in] prefix The key prefix to look for. If empty or null, this
    /// method will return all keys.
    ///
    /// @return Set of Remote Config parameter keys that start with the specified
    /// prefix. Will return an empty set if there are no keys with the given
    /// prefix.
    public IEnumerable<string> GetKeysByPrefix(string prefix) {
      ThrowIfNull();
      return remoteConfigInternal.GetKeysByPrefix(prefix);
    }

    /// @brief Returns a Dictionary of Firebase Remote Config key value pairs.
    ///
    /// Evaluates the values of the parameters in the following order:
    /// The activated value, if the last successful @ref ActivateAsync() contained the
    /// key. The default value, if the key was set with @ref SetDefaultsAsync().
    public IDictionary<string, ConfigValue> AllValues {
      get {
        ThrowIfNull();
        Dictionary<string, ConfigValue> result = new Dictionary<string, ConfigValue>();
        foreach (string key in Keys) {
          result[key] = GetValue(key);
        }
        return result;
      }
    }

    /// @brief Returns information about the last fetch request, in the form
    /// of a @ref ConfigInfo struct.
    public ConfigInfo Info {
      get {
        ThrowIfNull();
        return remoteConfigInternal.GetInfo();
      }
    }

    /// @brief The default cache expiration used by FetchAsync(), equal to 12 hours.
    public static TimeSpan DefaultCacheExpiration {
      get {
        return System.TimeSpan.FromMilliseconds((long)RemoteConfigUtil.kDefaultCacheExpiration);
      }
    }

    /// The default timeout used by FetchAsync(), equal to 30 seconds,
    /// in milliseconds.
    public static ulong DefaultTimeoutInMilliseconds {
      get { return RemoteConfigUtil.kDefaultTimeoutInMilliseconds; }
    }

    internal void OnConfigUpdate(ConfigUpdateInternal configUpdate, RemoteConfigError error) {
      EventHandler<ConfigUpdateEventArgs> handler = ConfigUpdateListenerImpl;
      if (handler != null) {
        // Make a copy of the list, to not rely on the Swig list
        List<string> updatedKeys = new List<string>(configUpdate.updated_keys);
        handler(this, new ConfigUpdateEventArgs {
          UpdatedKeys = updatedKeys,
          Error = error
        });
      }
    }

    [MonoPInvokeCallback(typeof(RemoteConfigUtil.ConfigUpdateDelegate))]
    private static void ConfigUpdateMethod(string appName, System.IntPtr configUpdatePtr, 
        int error) {
      FirebaseRemoteConfig rc;
      if (remoteConfigByInstanceKey.TryGetValue(appName, out rc)) {
        // Create a ConfigUpdateInternal with the given pointer
        ConfigUpdateInternal configUpdate = new ConfigUpdateInternal(configUpdatePtr, false);
        // convert error to RemoteConfigError
        RemoteConfigError errorInternal = (RemoteConfigError)error;

        rc.OnConfigUpdate(configUpdate, errorInternal);
      }
    }
  }
}  // namespace Firebase.RemoteConfig
