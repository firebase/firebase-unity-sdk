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
using Firebase.Platform;
using Firebase.Database.Internal;

namespace Firebase.Database {
  /// <summary>The entry point for accessing a Firebase Database.</summary>
  /// <remarks>
  ///   The entry point for accessing a Firebase Database.  You can get an instance by calling
  ///   <see cref="DefaultInstance" />
  ///   . To access a location in the database and read or
  ///   write data, use
  ///   <see cref="GetReference()" />
  /// </remarks>
  public sealed class FirebaseDatabase {
    private const string SFirebaseSdkVersion = "1.0.0.0";
    private const string SDefaultName = "__DEFAULT__";

    private InternalFirebaseDatabase internalDatabase;

    // Name of this instance in the databases dictionary.
    private string name;

    internal FirebaseDatabase(FirebaseApp app, InternalFirebaseDatabase internalDB) {
      App = app;
      internalDatabase = internalDB;
      App.AppDisposed += OnAppDisposed;
    }

    // Dispose of this object.
    private static void DisposeObject(object objectToDispose) {
      ((FirebaseDatabase)objectToDispose).Dispose();
    }

    ~FirebaseDatabase() {
      Dispose();
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      Dispose();
    }

    // Destroy the proxy to the C++ Database object.
    private void Dispose() {
      System.GC.SuppressFinalize(this);
      lock(this) {
        if (internalDatabase == null) return;

        lock(databases) {
          databases.Remove(name);
        }

        App.AppDisposed -= OnAppDisposed;
        App = null;

        internalDatabase.Dispose();
        internalDatabase = null;
      }
    }

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

    /// <summary>A static map FirebaseApp.name to FirebaseDatabase instance.</summary>
    /// <remarks>
    ///   This prevents creating two different C# FirebaseDatabase instances for
    ///   the same underlying C++ instance. It uses weak references to avoid keeping
    ///   the FirebaseDatabase alive forever.
    /// </remarks>
    private static Dictionary<string, FirebaseDatabase> databases =
        new Dictionary<string, FirebaseDatabase>();

    /// <summary>Gets an instance of FirebaseDatabase for a specific Firebase.FirebaseApp.</summary>
    /// <param name="app">The Firebase.FirebaseApp to get a FirebaseDatabase for.</param>
    /// <returns>A FirebaseDatabase instance.</returns>
    public static FirebaseDatabase GetInstance(FirebaseApp app) {
      return GetInstance(app, Services.AppConfig.GetDatabaseUrl(app.AppPlatform));
    }

    // This returns a database instance that is currently live, or the default instance
    // if there is no live instance.
    // This is used for the GoOnline / GoOffline methods that are static in almost any
    // Firebase SDK (including this one), except the C++ SDK.
    // It's not ideal to use an arbitrary instance, but it should work.
    internal static FirebaseDatabase AnyInstance {
      get {
        lock(databases) {
          var e = databases.GetEnumerator();
          while (e.MoveNext()) {
            FirebaseDatabase db = e.Current.Value;
            if (db != null) return db;
          }
        }
        // no existing databases, try to create a default instance
        return DefaultInstance;
      }
    }

    /// <summary>Gets an instance of FirebaseDatabase for the specified URL.</summary>
    /// <param name="url">The URL to the Firebase Database instance you want to access.</param>
    /// <returns>A FirebaseDatabase instance.</returns>
    public static FirebaseDatabase GetInstance(String url) {
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
    public static FirebaseDatabase GetInstance(FirebaseApp app, String url) {
      if (String.IsNullOrEmpty(url)) {
        throw new DatabaseException(
          "Failed to get FirebaseDatabase instance: Specify DatabaseURL within "
              + "FirebaseApp or from your GetInstance() call.");
      }
      FirebaseDatabase db = null;
      string name = String.Format("({0}, {1})", app.Name, url);
      lock (databases) {
        if (!databases.TryGetValue(name, out db)) {
          InitResult initResult;
          var internalDB = InternalFirebaseDatabase.GetInstanceInternal(app, url, out initResult);
          if (internalDB == null || initResult != InitResult.Success) {
            throw new DatabaseException("Failed to get FirebaseDatabase instance. "
                + "Please check the log for more information.");
          }
          db = new FirebaseDatabase(app, internalDB);
          db.name = name;
          databases[name] = db;
        }
      }
      return db;
    }

    /// <summary>Gets a DatabaseReference for the root location of this FirebaseDatabase.</summary>
    /// <value>A DatabaseReference instance.</value>
    public DatabaseReference RootReference {
      get { return new DatabaseReference(internalDatabase.GetReference(), this); }
    }

    /// <summary>Gets a DatabaseReference for the provided path.</summary>
    /// <param name="path">Path to a location in your FirebaseDatabase.</param>
    /// <returns>A DatabaseReference pointing to the specified path.</returns>
    public DatabaseReference GetReference(string path) {
      return new DatabaseReference(internalDatabase.GetReference(path), this);
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
      return new DatabaseReference(internalDatabase.GetReferenceFromUrl(url), this);
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
    public void PurgeOutstandingWrites() {
      internalDatabase.PurgeOutstandingWrites();
    }

    /// <summary>
    ///   Resumes our connection to the Firebase Database backend after a previous
    ///   <see cref="GoOffline()" />
    ///   Call.
    /// </summary>
    public void GoOnline() {
      internalDatabase.GoOnline();
    }

    /// <summary>
    ///   Shuts down our connection to the Firebase Database backend until
    ///   <see cref="GoOnline()" />
    ///   is called.
    /// </summary>
    public void GoOffline() {
      internalDatabase.GoOffline();
    }

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
    public void SetPersistenceEnabled(bool enabled) {
      internalDatabase.set_persistence_enabled(enabled);
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
    /// <remarks>
    /// On Android this can only be set before any operations have been performed with the object.
    /// </remarks>
    /// <value>The desired minimum log level</value>
    public LogLevel LogLevel {
      get { return internalDatabase.log_level(); }
      set {
          internalDatabase.set_log_level(value);
      }
    }
  }
}
