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
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;
using Firebase.RemoteConfig.Internal;

namespace Firebase.RemoteConfig {
  /// @brief Entry point for the Firebase C# SDK for Remote Config.
  public sealed class FirebaseRemoteConfig {
#if UNITY_6000_6_OR_NEWER
    [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    private static readonly FirebaseInstanceCache<FirebaseRemoteConfig> Instances =
        new FirebaseInstanceCache<FirebaseRemoteConfig>(rc => rc.Dispose());

    private readonly FirebaseApp _app;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();
    private readonly RemoteConfigStore _store;
    private readonly RemoteConfigFetchClient _fetchClient;
    private readonly RealtimeStreamClient _realtimeClient;
    private int _disposed;

    private FirebaseRemoteConfig(FirebaseApp app, AtomicFileStore fileStore = null) {
      _app = app ?? throw new ArgumentNullException(nameof(app));
      _store = new RemoteConfigStore(app.Name, RemoteConfigFetchClient.ResolveProjectIdentifier(app) ?? app.Options?.AppId ?? string.Empty, fileStore);
      _fetchClient = new RemoteConfigFetchClient(app, _store);
      _realtimeClient = new RealtimeStreamClient(this, app, _store, _fetchClient, _disposeCts.Token);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorReloadHook() {
      UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
      UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
    }
    private static void OnBeforeAssemblyReload() => Instances.ClearForReload();
#endif

    internal static FirebaseRemoteConfig CreateForTesting(FirebaseApp app, AtomicFileStore fileStore) => new FirebaseRemoteConfig(app, fileStore);
    internal void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) { _disposeCts.Cancel(); _realtimeClient.Dispose(); } }
    internal RemoteConfigStore Store => _store;
    internal RealtimeStreamClient RealtimeClient => _realtimeClient;
    internal IReadOnlyList<string> LastActivatedUpdatedKeys => _store.LastActivatedUpdatedKeys;

    /// @brief App object associated with this FirebaseRemoteConfig.
    public FirebaseApp App => _app;
    /// @brief Returns the FirebaseRemoteConfig initialized with the default FirebaseApp.
    public static FirebaseRemoteConfig DefaultInstance => GetInstance(FirebaseApp.DefaultInstance);
    /// @brief The default cache expiration used by FetchAsync(), equal to 12 hours.
    public static TimeSpan DefaultCacheExpiration => TimeSpan.FromMilliseconds(RemoteConfigStore.DefaultMinimumFetchIntervalMs);
    /// The default timeout used by FetchAsync(), equal to 30 seconds,
    /// in milliseconds.
    public static ulong DefaultTimeoutInMilliseconds => RemoteConfigStore.DefaultFetchTimeoutMs;

    // EventHandlers that will be used as ConfigUpdateListeners. The first listener added will open
    // the stream connection and the last removed will close the stream.
    public event EventHandler<ConfigUpdateEventArgs> OnConfigUpdateListener {
      add { ThrowIfDisposed(); _realtimeClient.AddListener(value); }
      remove { if (Volatile.Read(ref _disposed) == 0) _realtimeClient.RemoveListener(value); }
    }

    /// @brief Returns a FirebaseRemoteConfig, initialized with a custom Firebase App.
    ///
    /// @params app The customer FirebaseApp used for initialization.
    ///
    /// @returns A FirebaseRemoteConfig instance.
    public static FirebaseRemoteConfig GetInstance(FirebaseApp app) {
      FirebaseApp targetApp = app ?? FirebaseApp.DefaultInstance;
      return Instances.GetOrCreate(targetApp, () => new FirebaseRemoteConfig(targetApp));
    }

    /// @brief Returns a Task that contains ConfigInfo representing the
    /// initialization status of this Firebase Remote Config instance.
    /// Use this method to ensure Set/Get call not being blocked.
    ///
    /// @returns A Task contains ConfigInfo.
    public Task<ConfigInfo> EnsureInitializedAsync() { ThrowIfDisposed(); return Task.FromResult(_store.GetInfo()); }

    /// @brief Asynchronously activates the most recently fetched configs,
    /// so that the fetched key value pairs take effect.
    ///
    /// @returns A Task that contains true if fetched configs were activated.
    /// The Task will contain false if the configs were already activated.
    public Task<bool> ActivateAsync() { ThrowIfDisposed(); return _store.ActivateAsync(); }

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
    public async Task<bool> FetchAndActivateAsync() {
      ThrowIfDisposed();
      await FetchAsync().ConfigureAwait(false);
      return await ActivateAsync().ConfigureAwait(false);
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
      ThrowIfDisposed();
      ulong minMs = _store.GetSettings().MinimumFetchIntervalInMilliseconds;
      return _fetchClient.FetchAsync(minMs >= (ulong)TimeSpan.MaxValue.TotalMilliseconds ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(minMs), _disposeCts.Token);
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
    public Task FetchAsync(TimeSpan cacheExpiration) { ThrowIfDisposed(); return _fetchClient.FetchAsync(cacheExpiration, _disposeCts.Token); }

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
      ThrowIfDisposed();
      if (defaults == null) throw new ArgumentNullException(nameof(defaults));
      return _store.SetDefaultsAsync(defaults);
    }

    /// @brief Sets the custom signals values based on the input dictionary.
    ///
    /// @note This upserts the custom signals, i.e., inserts a new signal if none exists
    /// or updates the signal if it does.
    ///
    /// @param customSignals IDictionary of string keys to object values, representing the
    /// set of custom signals to apply. Supported value types are string, long (and integer types),
    /// double (and float types), or null to remove a signal.
    /// If the same key is specified multiple times, the value associated with the last
    /// duplicate key is applied.
    ///
    /// @return A Task which can be used to determine when the operation is
    /// complete.
    public Task SetCustomSignalsAsync(IDictionary<string, object> customSignals) {
      ThrowIfDisposed();
      if (customSignals == null) throw new ArgumentNullException(nameof(customSignals));
      foreach (KeyValuePair<string, object> pair in customSignals) if (pair.Key == null) throw new ArgumentException("Key cannot be null", nameof(customSignals));
      try { return _store.SetCustomSignalsAsync(customSignals); }
      catch (FirebaseException ex) { return Task.FromException(ex); }
    }

    /// @brief Asynchronously changes the settings for this Remote Config
    /// instance.
    ///
    /// @param settings The new settings to be applied.
    ///
    /// @return a Task which can be used to determine when the operation is
    /// complete.
    public Task SetConfigSettingsAsync(ConfigSettings settings) { ThrowIfDisposed(); return _store.SetSettingsAsync(settings); }

    /// @brief Gets the current settings of the RemoteConfig object.
    public ConfigSettings ConfigSettings { get { ThrowIfDisposed(); return _store.GetSettings(); } }

    /// @brief Gets the @ref ConfigValue corresponding to the key.
    ///
    /// @param key Key of the value to be retrieved.
    /// @returns The @ref ConfigValue associated with the key.
    public ConfigValue GetValue(string key) { ThrowIfDisposed(); return _store.GetValue(key); }

    /// @brief Gets the set of all Remote Config parameter keys.
    public IEnumerable<string> Keys { get { ThrowIfDisposed(); return _store.GetKeys(); } }

    /// @brief Gets the set of keys that start with the given prefix.
    ///
    /// @param[in] prefix The key prefix to look for. If empty or null, this
    /// method will return all keys.
    ///
    /// @return Set of Remote Config parameter keys that start with the specified
    /// prefix. Will return an empty set if there are no keys with the given
    /// prefix.
    public IEnumerable<string> GetKeysByPrefix(string prefix) { ThrowIfDisposed(); return _store.GetKeysByPrefix(prefix); }

    /// @brief Returns a Dictionary of Firebase Remote Config key value pairs.
    ///
    /// Evaluates the values of the parameters in the following order:
    /// The activated value, if the last successful @ref ActivateAsync() contained the
    /// key. The default value, if the key was set with @ref SetDefaultsAsync().
    public IDictionary<string, ConfigValue> AllValues { get { ThrowIfDisposed(); return _store.GetAllValues(); } }

    /// @brief Returns information about the last fetch request, in the form
    /// of a @ref ConfigInfo object.
    public ConfigInfo Info { get { ThrowIfDisposed(); return _store.GetInfo(); } }

    private void ThrowIfDisposed() {
      if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(FirebaseRemoteConfig));
    }
  }
}  // namespace Firebase.RemoteConfig
