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
using System.Collections.Generic;
using Firebase.Database.Internal;
using Firebase.Internal;
using Firebase.Platform;

namespace Firebase.Database {
  /// <summary>The entry point for accessing a Firebase Database.</summary>
  /// <remarks>
  ///   The entry point for accessing a Firebase Database.  You can get an instance by calling
  ///   <see cref="DefaultInstance" />
  ///   . To access a location in the database and read or
  ///   write data, use
  ///   <see cref="GetReference()" />
  /// </remarks>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class FirebaseDatabase {
    private static readonly object LiveLock = new object();
    private static readonly HashSet<FirebaseDatabase> LiveInstances = new HashSet<FirebaseDatabase>();
    private static readonly FirebaseInstanceCache<FirebaseDatabase> Instances =
        new FirebaseInstanceCache<FirebaseDatabase>(db => db.DisposeInternal());

    static FirebaseDatabase() {
      FirebaseInstanceCache.RegisterResetHook(ClearLiveSet);
    }

    private readonly DatabaseRepo _repo;

    private FirebaseDatabase(FirebaseApp app, ParsedDatabaseUrl parsedUrl) {
      App = app;
      _repo = new DatabaseRepo(app, this, parsedUrl);
      lock (LiveLock) {
        LiveInstances.Add(this);
      }
    }

    internal DatabaseRepo Repo => _repo;

    /// <summary>
    ///   Returns the Firebase.FirebaseApp instance to which this FirebaseDatabase belongs.
    /// </summary>
    /// <returns>The Firebase.FirebaseApp instance to which this FirebaseDatabase belongs.</returns>
    public FirebaseApp App { get; private set; }

    /// <summary>Gets the instance of FirebaseDatabase for the default Firebase.App.</summary>
    /// <value>A FirebaseDatabase instance.</value>
    public static FirebaseDatabase DefaultInstance {
      get {
        FirebaseApp instance = FirebaseApp.DefaultInstance;
        if (instance == null) {
          // This *should* never happen.
          throw new DatabaseException("FirebaseApp could not be initialized.");
        }
        return GetInstance(instance);
      }
    }

    /// <summary>Gets an instance of FirebaseDatabase for a specific Firebase.FirebaseApp.</summary>
    /// <param name="app">The Firebase.FirebaseApp to get a FirebaseDatabase for.</param>
    /// <returns>A FirebaseDatabase instance.</returns>
    public static FirebaseDatabase GetInstance(FirebaseApp app) {
      if (app == null) {
        throw new ArgumentNullException(nameof(app));
      }
      string configuredUrl = null;
      if (Services.AppConfig != null && app.AppPlatform != null) {
        configuredUrl = Services.AppConfig.GetDatabaseUrl(app.AppPlatform);
      }
      if (string.IsNullOrEmpty(configuredUrl)) {
        configuredUrl = app.Options?.DatabaseUrl?.ToString();
      }
      return GetInstance(app, configuredUrl);
    }

    /// <summary>Gets an instance of FirebaseDatabase for the specified URL.</summary>
    /// <param name="url">The URL to the Firebase Database instance you want to access.</param>
    /// <returns>A FirebaseDatabase instance.</returns>
    public static FirebaseDatabase GetInstance(string url) {
      FirebaseApp instance = FirebaseApp.DefaultInstance;
      if (instance == null) {
        // This *should* never happen.
        throw new DatabaseException("FirebaseApp could not be initialized.");
      }
      return GetInstance(instance, url);
    }

    /// <summary>Gets a FirebaseDatabase instance for the specified URL, using the specified
    /// FirebsaeApp.</summary>
    /// <param name="app">The Firebase.FirebaseApp to get a FirebaseDatabase for.</param>
    /// <param name="url">The URL to the Firebase Database instance you want to access.</param>
    /// <returns>A FirebaseDatabase instance.</returns>
    public static FirebaseDatabase GetInstance(FirebaseApp app, string url) {
      if (app == null) {
        throw new ArgumentNullException(nameof(app));
      }
      if (string.IsNullOrEmpty(url)) {
        throw new DatabaseException(
            "Failed to get FirebaseDatabase instance: Specify DatabaseURL within "
            + "FirebaseApp or from your GetInstance() call.");
      }
      ParsedDatabaseUrl parsedUrl = ParsedDatabaseUrl.Parse(url, app);
      if (!parsedUrl.Path.IsEmpty) {
        throw new DatabaseException(
            $"Specified Database URL '{url}' must point to the root of a Firebase Database "
            + "(use GetReferenceFromUrl() to reference a child path).");
      }
      return Instances.GetOrCreate(
          app,
          () => new FirebaseDatabase(app, parsedUrl),
          parsedUrl.CacheKey);
    }

    /// <summary>Gets a DatabaseReference for the root location of this FirebaseDatabase.</summary>
    /// <value>A DatabaseReference instance.</value>
    public DatabaseReference RootReference => new DatabaseReference(this, RtdbPath.Empty);

    /// <summary>Gets a DatabaseReference for the provided path.</summary>
    /// <param name="path">Path to a location in your FirebaseDatabase.</param>
    /// <returns>A DatabaseReference pointing to the specified path.</returns>
    public DatabaseReference GetReference(string path) {
      RtdbPath parsed = RtdbPath.ParseAndValidate(path, nameof(path), allowMetaKeys: path != null && path.StartsWith(".info", StringComparison.Ordinal));
      return new DatabaseReference(this, parsed);
    }

    /// <summary>Gets a DatabaseReference for the provided URL.</summary>
    /// <remarks>
    ///   Gets a DatabaseReference for the provided URL.  The URL must be a URL to a path
    ///   within this FirebaseDatabase.  To create a DatabaseReference to a different database,
    ///   create a
    ///   <see cref="Firebase.FirebaseApp" />
    ///   with a
    ///   <see>
    ///     <cref>Firebase.FirebaseOptions</cref>
    ///   </see>
    ///   object configured with
    ///   the appropriate database URL.
    /// </remarks>
    /// <param name="url">A URL to a path within your database.</param>
    /// <returns>A DatabaseReference for the provided URL.</returns>
    public DatabaseReference GetReferenceFromUrl(Uri url) {
      if (url == null) {
        throw new ArgumentNullException(nameof(url));
      }
      return GetReferenceFromUrl(url.ToString());
    }

    /// <summary>Gets a DatabaseReference for the provided URL.</summary>
    /// <remarks>
    ///   Gets a DatabaseReference for the provided URL.  The URL must be a URL to a path
    ///   within this FirebaseDatabase.  To create a DatabaseReference to a different database,
    ///   create a
    ///   <see cref="Firebase.FirebaseApp" />
    ///   with a
    ///   <see>
    ///     <cref>Firebase.FirebaseOptions</cref>
    ///   </see>
    ///   object configured with
    ///   the appropriate database URL.
    /// </remarks>
    /// <param name="url">A URL to a path within your database.</param>
    /// <returns>A DatabaseReference for the provided URL.</returns>
    public DatabaseReference GetReferenceFromUrl(string url) {
      if (string.IsNullOrWhiteSpace(url)) {
        throw new ArgumentException("Database URL cannot be null or empty.", nameof(url));
      }
      ParsedDatabaseUrl target = ParsedDatabaseUrl.Parse(url, App);
      bool hostMatches = string.Equals(target.Host, _repo.ParsedUrl.Host, StringComparison.OrdinalIgnoreCase);
      bool nsMatches = !target.HasExplicitNamespace ||
          string.Equals(target.Namespace, _repo.ParsedUrl.Namespace, StringComparison.Ordinal);
      if (!hostMatches || !nsMatches) {
        throw new DatabaseException(
            $"Invalid URL '{url}' passed to GetReferenceFromUrl(); expected host '{_repo.BaseUrl}'.");
      }
      return new DatabaseReference(this, target.Path);
    }

    /// <summary>
    ///   The Firebase Database client automatically queues writes and sends them to the server at
    ///   the earliest opportunity, depending on network connectivity.
    /// </summary>
    /// <remarks>
    ///   The Firebase Database client automatically queues writes and sends them to the server at
    ///   the earliest opportunity, depending on network connectivity.  In some cases (e.g.
    ///   offline usage) there may be a large number of writes waiting to be sent. Calling this
    ///   method will purge all outstanding writes so they are abandoned.
    ///   All writes will be purged, including transactions and
    ///   <see cref="DatabaseReference.OnDisconnect()" />
    ///   writes.  The writes will
    ///   be rolled back locally, perhaps triggering events for affected event listeners, and the
    ///   client will not (re-)send them to the Firebase backend.
    /// </remarks>
    public void PurgeOutstandingWrites() => _repo.PurgeOutstandingWrites();

    /// <summary>
    ///   Resumes our connection to the Firebase Database backend after a previous
    ///   <see cref="GoOffline()" />
    ///   Call.
    /// </summary>
    public void GoOnline() => _repo.GoOnline();

    /// <summary>
    ///   Shuts down our connection to the Firebase Database backend until
    ///   <see cref="GoOnline()" />
    ///   is called.
    /// </summary>
    public void GoOffline() => _repo.GoOffline();

    /// <summary>
    ///   Sets whether pending write data will persist between application exits.
    /// </summary>
    /// <remarks>
    ///   The Firebase Database client will cache synchronized data and keep track of all writes
    ///   you've initiated while your application is running. It seamlessly handles intermittent
    ///   network connections and re-sends write operations when the network connection is restored.
    ///   However by default your write operations and cached data are only stored in-memory and will
    ///   be lost when your app restarts. By setting this value to true, the data will be persisted to
    ///   on-device (disk) storage and will thus be available again when the app is restarted (even
    ///   when there is no network connectivity at that time).
    ///
    ///   Note:SetPersistenceEnabled should be called before creating any instances of
    ///   DatabaseReference, and only needs to be called once per application.
    /// </remarks>
    /// <param name="enabled">
    ///   Set this to true to persist write data to on-device (disk) storage, or false to discard
    ///   pending writes when the app exists.
    /// </param>
    public void SetPersistenceEnabled(bool enabled) => _repo.SetPersistenceEnabled(enabled);

    /// <summary>Gets or sets the maximum disk cache size in bytes when persistence is enabled (default 10 MB).</summary>
    public long PersistenceCacheSizeBytes {
      get => _repo.PersistenceCacheSizeBytes;
      set => _repo.PersistenceCacheSizeBytes = value;
    }

    /// <summary>
    ///   By default, this is set to
    ///   <see cref="Firebase.LogLevel.Info">Info</see>
    ///   .
    ///   This includes any internal errors (
    ///   <see cref="Firebase.LogLevel.Error">Error</see>
    ///   )
    ///   and any security debug messages (
    ///   <see cref="Firebase.LogLevel.Info">Info</see>
    ///   )
    ///   that the client receives. Set to
    ///   <see cref="Firebase.LogLevel.Debug">Debug</see>
    ///   to turn on the diagnostic logging.
    /// </summary>
    /// <value>The desired minimum log level</value>
    public LogLevel LogLevel {
      get => _repo.LogLevel;
      set => _repo.LogLevel = value;
    }

    internal static void GoOfflineAll() {
      FirebaseDatabase[] snapshot;
      lock (LiveLock) {
        snapshot = new FirebaseDatabase[LiveInstances.Count];
        LiveInstances.CopyTo(snapshot);
      }
      if (snapshot.Length == 0) {
        DefaultInstance.GoOffline();
        return;
      }
      for (int i = 0; i < snapshot.Length; i++) {
        snapshot[i].GoOffline();
      }
    }

    internal static void GoOnlineAll() {
      FirebaseDatabase[] snapshot;
      lock (LiveLock) {
        snapshot = new FirebaseDatabase[LiveInstances.Count];
        LiveInstances.CopyTo(snapshot);
      }
      if (snapshot.Length == 0) {
        DefaultInstance.GoOnline();
        return;
      }
      for (int i = 0; i < snapshot.Length; i++) {
        snapshot[i].GoOnline();
      }
    }

    private static void ClearLiveSet() {
      lock (LiveLock) {
        LiveInstances.Clear();
      }
    }

    private void DisposeInternal() {
      lock (LiveLock) {
        LiveInstances.Remove(this);
      }
      _repo.Dispose();
    }
  }
}
