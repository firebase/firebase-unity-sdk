// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Firebase.Firestore.Converters;
using Firebase.Firestore.Internal;
using Firebase.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// Represents a Cloud Firestore database and is the entry point for all Cloud Firestore
  /// operations.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class FirebaseFirestore {
    private const string DefaultDatabase = "(default)";

    private static readonly object CacheLock = new object();
    private static readonly Dictionary<(string AppName, string Database), FirebaseFirestore> InstanceCache =
        new Dictionary<(string, string), FirebaseFirestore>();
    private static volatile LogLevel s_logLevel = LogLevel.Info;

    static FirebaseFirestore() {
      FirebaseInstanceCache.RegisterResetHook(ClearCacheForReload);
    }

    private static void ClearCacheForReload() {
      List<FirebaseFirestore> evicted;
      lock (CacheLock) {
        if (InstanceCache.Count == 0) return;
        evicted = new List<FirebaseFirestore>(InstanceCache.Values);
        InstanceCache.Clear();
      }
      for (int i = 0; i < evicted.Count; i++) {
        evicted[i].DisposeInternal(fromReload: true);
      }
    }

    private readonly object _disposeLock = new object();
    private readonly string _appName;
    private readonly string _projectId;
    private readonly string _databaseId;
    private readonly FirebaseFirestoreSettings _settings;
    private readonly FirestoreClientCore _core;
    private bool _inCache = true;
    private volatile bool _disposed;

    internal FirestoreClientCore Core => _core;
    internal string ProjectId => _projectId;
    internal string DatabaseId => _databaseId;
    internal bool IsDisposed => _disposed;

    // We rely on e.g. firestore.Document("a/b").Firestore returning the original Firestore
    // instance so it's important the constructor remains private.
    private FirebaseFirestore(FirebaseApp app, string database) {
      App = app;
      _appName = app.Name;
      _projectId = app.Options?.ProjectId ?? string.Empty;
      _databaseId = string.IsNullOrEmpty(database) ? DefaultDatabase : database;
      _settings = new FirebaseFirestoreSettings();
      _core = new FirestoreClientCore(this, app, _databaseId);
      app.AppDisposed += OnAppDisposed;

      // This call to InitializeConverterCache() exists to make sure that AOT works.
      ConverterCache.InitializeConverterCache();
    }

    /// <summary>
    /// Destroys this object and frees all resources it has acquired.
    /// </summary>
    ~FirebaseFirestore() {
      DisposeInternal(fromReload: false);
    }

    private void OnAppDisposed(object sender, EventArgs eventArgs) {
      DisposeInternal(fromReload: false);
    }

    // TODO(b/201097848) Make `Dispose()` public and implement `IDisposable`.
    private void DisposeInternal(bool fromReload) {
      FirebaseApp capturedApp;
      lock (_disposeLock) {
        if (_disposed) return;
        _disposed = true;
        capturedApp = App;
        App = null;
      }

      _settings.MarkDisposed();
      EvictSelfFromCache();

      if (capturedApp != null) {
        try {
          capturedApp.AppDisposed -= OnAppDisposed;
        } catch (Exception) {
          // Ignore errors if FirebaseApp is already torn down during domain reload.
        }
      }

      _core.Dispose();
      if (!fromReload) {
        GC.SuppressFinalize(this);
      }
    }

    /// <summary>
    /// Removes the mapping from the instance cache for this object.
    /// </summary>
    private void EvictSelfFromCache() {
      lock (CacheLock) {
        if (!_inCache) return;
        _inCache = false;
        (string, string) key = (_appName, _databaseId);
        if (InstanceCache.TryGetValue(key, out FirebaseFirestore cur) && ReferenceEquals(cur, this)) {
          InstanceCache.Remove(key);
        }
      }
    }

    /// <summary>
    /// Returns the <c>FirebaseApp</c> instance to which this <c>FirebaseFirestore</c> belongs.
    /// </summary>
    public FirebaseApp App { get; private set; }

    /// <summary>
    /// Gets the instance of <c>FirebaseFirestore</c> for the default <c>FirebaseApp</c> with the default <c>database name</c>.
    /// </summary>
    /// <value>A <c>FirebaseFirestore</c> instance.</value>
    public static FirebaseFirestore DefaultInstance {
      get {
        FirebaseApp app = Util.NotNull(FirebaseApp.DefaultInstance);
        return GetInstance(app, DefaultDatabase);
      }
    }

    /// <summary>
    /// Gets an instance of <c>FirebaseFirestore</c> for a specific <c>FirebaseApp</c> with the default <c>database name</c>.
    /// </summary>
    /// <param name="app">The <c>FirebaseApp</c> for which to get a <c>FirebaseFirestore</c>
    /// instance.</param>
    /// <returns>A <c>FirebaseFirestore</c> instance.</returns>
    public static FirebaseFirestore GetInstance(FirebaseApp app) {
      return GetInstance(app, DefaultDatabase);
    }

    /// <summary>
    /// Gets an instance of <c>FirebaseFirestore</c> for the default <c>FirebaseApp</c> with a spesific <c>database name</c>.
    /// </summary>
    /// <param name="database">The customized name for the <c>database</c>.
    /// instance.</param>
    /// <returns>A <c>FirebaseFirestore</c> instance.</returns>
    public static FirebaseFirestore GetInstance(string database) {
      Preconditions.CheckNotNull(database, nameof(database));
      FirebaseApp app = Util.NotNull(FirebaseApp.DefaultInstance);
      return GetInstance(app, database);
    }

    /// <summary>
    /// Gets an instance of <c>FirebaseFirestore</c> for a specific <c>FirebaseApp</c> with a spesific <c>database name</c>.
    /// </summary>
    /// <param name="app">The <c>FirebaseApp</c> for which to get a <c>FirebaseFirestore</c>
    /// <param name="database">The customized name for the <c>database</c>.
    /// instance.</param>
    /// <returns>A <c>FirebaseFirestore</c> instance.</returns>
    public static FirebaseFirestore GetInstance(FirebaseApp app, string database) {
      Preconditions.CheckNotNull(app, nameof(app));
      Preconditions.CheckNotNull(database, nameof(database));

      string appName;
      try {
        appName = app.Name;
        if (string.IsNullOrEmpty(appName) || !ReferenceEquals(FirebaseApp.GetInstance(appName), app)) {
          throw new ArgumentException("FirebaseApp instance has already been disposed.", nameof(app));
        }
      } catch (ArgumentException) {
        throw;
      } catch (Exception ex) {
        throw new ArgumentException("FirebaseApp instance is invalid or disposed.", nameof(app), ex);
      }

      string normalizedDb = database.Length == 0 ? DefaultDatabase : database;
      (string, string) key = (appName, normalizedDb);

      lock (CacheLock) {
        if (InstanceCache.TryGetValue(key, out FirebaseFirestore existing) &&
            existing._inCache &&
            !existing._disposed &&
            ReferenceEquals(existing.App, app)) {
          return existing;
        }
        var created = new FirebaseFirestore(app, normalizedDb);
        InstanceCache[key] = created;
        return created;
      }
    }

    internal FirebaseFirestore GetDatabaseInstanceForReference(string projectId, string databaseId) {
      FirebaseApp app = App;
      if (app != null &&
          string.Equals(projectId, _projectId, StringComparison.Ordinal) &&
          !string.Equals(databaseId, _databaseId, StringComparison.Ordinal)) {
        try {
          return GetInstance(app, databaseId);
        } catch (Exception) {
          return this;
        }
      }
      return this;
    }

    /// <summary>
    /// The settings of this <c>FirebaseFirestore</c> object.
    /// </summary>
    /// <remarks>
    /// <para>To change the settings used by this <c>FirebaseFirestore</c> instance, simply change
    /// the values of the properties on this <c>FirebaseFirestoreSettings</c> object.</para>
    /// <para>Invoking any non-static method on this <c>FirebaseFirestore</c> instance will "lock
    /// in" the settings. No changes are allowed to be made to the settings once they are locked in.
    /// Attempting to do so will result in an exception being thrown by the property setter.
    /// </para>
    /// </remarks>
    public FirebaseFirestoreSettings Settings => _settings;

    /// <summary>
    ///  Sets the log verbosity of all Firestore instances.
    ///
    ///  The default verbosity level is
    ///  <see cref="Firebase.LogLevel.Info">Info</see>
    ///  .
    ///  Set to
    ///  <see cref="Firebase.LogLevel.Debug">Debug</see>
    ///  to turn on the diagnostic logging.
    /// </summary>
    /// <value>The desired verbosity.</value>
    public static LogLevel LogLevel {
      set => s_logLevel = value;
    }

    internal static LogLevel CurrentLogLevel => s_logLevel;

    private void EnsureNotDisposed() {
      if (_disposed) {
        throw new InvalidOperationException("This FirebaseFirestore instance has been disposed.");
      }
    }

    /// <summary>
    /// Creates a local <see cref="CollectionReference"/> for the given path, which must include an
    /// odd number of slash-separated identifiers. This does not perform any remote operations.
    /// </summary>
    /// <param name="path">The collection path, e.g. <c>col1/doc1/col2</c>.</param>
    /// <returns>A collection reference.</returns>
    public CollectionReference Collection(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      EnsureNotDisposed();
      if (!_core.IsTerminated) _core.EnsureStarted();
      ResourcePath rp = ResourcePath.ParseRelative(path, nameof(path));
      if (!rp.IsCollection) {
        throw new ArgumentException(
            $"Invalid collection reference. Collection references must have an odd number of segments, but {path} has {rp.Count}.",
            nameof(path));
      }
      return new CollectionReference(rp, this);
    }

    /// <summary>
    /// Creates a local <see cref="DocumentReference"/> for the given path, which must include an
    /// even number of slash-separated identifiers. This does not perform any remote operations.
    /// </summary>
    /// <param name="path">The document path, e.g. <c>col1/doc1/col2/doc2</c>.</param>
    /// <returns>A document reference.</returns>
    public DocumentReference Document(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      EnsureNotDisposed();
      if (!_core.IsTerminated) _core.EnsureStarted();
      ResourcePath rp = ResourcePath.ParseRelative(path, nameof(path));
      if (!rp.IsDocument) {
        throw new ArgumentException(
            $"Invalid document reference. Document references must have an even number of segments, but {path} has {rp.Count}.",
            nameof(path));
      }
      return new DocumentReference(rp, this);
    }

    /// <summary>
    /// Creates and returns a new <see cref="Query"/> that includes all documents in the
    /// database that are contained in a collection or subcollection with the
    /// given collection ID.
    /// </summary>
    /// <param name="collectionId">Identifies the collections to query over.
    /// Every collection or subcollection with this ID as the last segment
    /// of its path will be included. Must not contain a slash.</param>
    /// <returns>The created <see cref="Query"/>.</returns>
    public Query CollectionGroup(string collectionId) {
      Preconditions.CheckNotNullOrEmpty(collectionId, nameof(collectionId));
      if (collectionId.IndexOf('/') >= 0) {
        throw new ArgumentException(
            $"Invalid collectionId '{collectionId}'. Collection IDs must not contain '/'.",
            nameof(collectionId));
      }
      EnsureNotDisposed();
      if (!_core.IsTerminated) _core.EnsureStarted();
      return new Query(new QuerySpec(ResourcePath.Empty, collectionId), this);
    }

    /// <summary>
    /// Creates a write batch, which can be used to commit multiple mutations atomically.
    /// </summary>
    /// <returns>A write batch for this database.</returns>
    public WriteBatch StartBatch() {
      EnsureNotDisposed();
      if (!_core.IsTerminated) _core.EnsureStarted();
      return new WriteBatch(this);
    }

    /// <summary>
    /// Runs a transaction asynchronously, with an asynchronous callback that doesn't return a
    /// value. The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails
    /// to commit after 5 attempts, the transaction will fail.</para>
    ///
    /// <para>The maximum number of writes allowed in a single transaction is 500, but note that
    /// each usage of <see cref="FieldValue.ServerTimestamp"/>, <c>FieldValue.ArrayUnion</c>,
    /// <c>FieldValue.ArrayRemove</c>, or <c>FieldValue.Increment</c> inside a transaction counts as
    /// an additional write.</para>
    /// </remarks>
    /// <param name="callback">The callback to execute. Must not be <c>null</c>. The callback will
    /// be invoked on the main thread.</param>
    /// <returns>A task which completes when the transaction has committed.</returns>
    public Task RunTransactionAsync(Func<Transaction, Task> callback) {
      return RunTransactionAsync(new TransactionOptions(), callback);
    }

    /// <summary>
    /// Runs a transaction asynchronously, with an asynchronous callback that returns a value.
    /// The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails
    /// to commit after 5 attempts, the transaction will fail.</para>
    ///
    /// <para>The maximum number of writes allowed in a single transaction is 500, but note that
    /// each usage of <see cref="FieldValue.ServerTimestamp"/>, <c>FieldValue.ArrayUnion</c>,
    /// <c>FieldValue.ArrayRemove</c>, or <c>FieldValue.Increment</c> inside a transaction counts as
    /// an additional write.</para>
    /// </remarks>
    ///
    /// <typeparam name="T">The result type of the callback.</typeparam>
    /// <param name="callback">The callback to execute. Must not be <c>null</c>. The callback will
    /// be invoked on the main thread.</param>
    /// <returns>A task which completes when the transaction has committed. The result of the task
    /// then contains the result of the callback.</returns>
    public Task<T> RunTransactionAsync<T>(Func<Transaction, Task<T>> callback) {
      return RunTransactionAsync(new TransactionOptions(), callback);
    }

    /// <summary>
    /// Runs a transaction asynchronously, with an asynchronous callback that doesn't return a
    /// value. The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails
    /// to commit after the maximum number of attempts specified in the given <c>TransactionOptions</c>
    /// object, the transaction will fail.</para>
    ///
    /// <para>The maximum number of writes allowed in a single transaction is 500, but note that
    /// each usage of <see cref="FieldValue.ServerTimestamp"/>, <c>FieldValue.ArrayUnion</c>,
    /// <c>FieldValue.ArrayRemove</c>, or <c>FieldValue.Increment</c> inside a transaction counts as
    /// an additional write.</para>
    /// </remarks>
    /// <param name="options">The transaction options for controlling execution. Must not be
    /// <c>null</c>.</param>
    /// <param name="callback">The callback to execute. Must not be <c>null</c>. The callback will
    /// be invoked on the main thread.</param>
    /// <returns>A task which completes when the transaction has committed.</returns>
    public Task RunTransactionAsync(TransactionOptions options, Func<Transaction, Task> callback) {
      Preconditions.CheckNotNull(options, nameof(options));
      Preconditions.CheckNotNull(callback, nameof(callback));
      EnsureNotDisposed();
      return _core.RunTransactionAsync<object>(options, async tx => {
        Task inner = callback(tx);
        if (inner == null) {
          throw new InvalidOperationException("Transaction callback returned a null Task.");
        }
        await inner.ConfigureAwait(false);
        return null;
      });
    }

    /// <summary>
    /// Runs a transaction asynchronously, with an asynchronous callback that returns a value.
    /// The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails
    /// to commit after the maximum number of attempts specified in the given <c>TransactionOptions</c>
    /// object, the transaction will fail.</para>
    ///
    /// <para>The maximum number of writes allowed in a single transaction is 500, but note that
    /// each usage of <see cref="FieldValue.ServerTimestamp"/>, <c>FieldValue.ArrayUnion</c>,
    /// <c>FieldValue.ArrayRemove</c>, or <c>FieldValue.Increment</c> inside a transaction counts as
    /// an additional write.</para>
    /// </remarks>
    ///
    /// <typeparam name="T">The result type of the callback.</typeparam>
    /// <param name="options">The transaction options for controlling execution. Must not be
    /// <c>null</c>.</param>
    /// <param name="callback">The callback to execute. Must not be <c>null</c>. The callback will
    /// be invoked on the main thread.</param>
    /// <returns>A task which completes when the transaction has committed. The result of the task
    /// then contains the result of the callback.</returns>
    public Task<T> RunTransactionAsync<T>(
        TransactionOptions options, Func<Transaction, Task<T>> callback) {
      Preconditions.CheckNotNull(options, nameof(options));
      Preconditions.CheckNotNull(callback, nameof(callback));
      EnsureNotDisposed();
      return _core.RunTransactionAsync(options, callback);
    }

    /// <summary>
    /// Disables network access for this instance.
    /// </summary>
    /// <remarks>
    /// While the network is disabled, any snapshot listeners or <c>GetSnapshotAsync</c> calls will
    /// return results from cache, and any write operations will be queued until network usage is
    /// re-enabled via a call to <see cref="FirebaseFirestore.EnableNetworkAsync"/>.
    /// </remarks>
    /// <returns>A task which completes once networking is disabled.</returns>
    public Task DisableNetworkAsync() {
      EnsureNotDisposed();
      return _core.DisableNetworkAsync();
    }

    /// <summary>
    /// Re-enables network usage for this instance after a prior call to
    /// <see cref="FirebaseFirestore.DisableNetworkAsync"/>.
    /// </summary>
    /// <returns>A task which completes once networking is enabled.</returns>
    public Task EnableNetworkAsync() {
      EnsureNotDisposed();
      return _core.EnableNetworkAsync();
    }

    /// <summary>
    /// Waits until all currently pending writes for the active user have been acknowledged by the
    /// backend.
    /// </summary>
    ///
    /// <remarks>
    /// The returned Task completes immediately if there are no outstanding writes. Otherwise, the
    /// Task waits for all previously issued writes (including those written in a previous app
    /// session), but it does not wait for writes that were added after the method is called. If you
    /// wish to wait for additional writes, you have to call `WaitForPendingWritesAsync()` again.
    ///
    /// Any outstanding `WaitForPendingWritesAsync()` Tasks are cancelled during user changes.
    /// </remarks>
    ///
    /// <returns> A Task which completes when all currently pending writes have been acknowledged
    /// by the backend.</returns>
    public Task WaitForPendingWritesAsync() {
      EnsureNotDisposed();
      return _core.WaitForPendingWritesAsync();
    }

    /// <summary>
    /// Terminates this <c>FirebaseFirestore</c> instance.
    /// </summary>
    ///
    /// <remarks>
    /// After calling <c>Terminate()</c>, only the <c>ClearPersistenceAsync()</c> method may be
    /// used. Calling any other method will result in an error.
    ///
    /// To restart after termination, simply create a new instance of <c>FirebaseFirestore</c> with
    /// <c>GetInstance()</c> methods.
    ///
    /// <c>Terminate()</c> does not cancel any pending writes, and any tasks that are awaiting a
    /// response from the server will not be resolved. The next time you start this instance, it
    /// will resume attempting to send these writes to the server.
    ///
    /// Note: under normal circumstances, calling <c>Terminate()</c> is not required. This method
    /// is useful only when you want to force this instance to release all of its resources or in
    /// combination with <c>ClearPersistenceAsync</c> to ensure that all local state is destroyed
    /// between test runs.
    /// </remarks>
    ///
    /// <returns>
    /// A Task which completes when the instance has been successfully terminated.
    /// </returns>
    public Task TerminateAsync() {
      EnsureNotDisposed();
      // Synchronously evict from InstanceCache so subsequent GetInstance() calls immediately
      // construct a fresh FirebaseFirestore instance!
      EvictSelfFromCache();
      return _core.TerminateAsync();
    }

    /// <summary>
    /// Clears the persistent storage. This includes pending writes and cached documents.
    /// </summary>
    ///
    /// <remarks>
    /// Must be called while the Firestore instance is not started (after the app is shut down or
    /// when the app is first initialized). On startup, this method must be called before other
    /// methods (other than getting or setting `FirebaseFirestoreSettings`). If the Firestore
    /// instance is still running, the task will complete with an error code of
    /// `FailedPrecondition`.
    ///
    /// Note: `ClearPersistenceAsync()` is primarily intended to help write reliable tests that use
    /// Firestore. It uses the most efficient mechanism possible for dropping existing data but does
    /// not attempt to securely overwrite or otherwise make cached data unrecoverable. For
    /// applications that are sensitive to the disclosure of cache data in between user sessions we
    /// strongly recommend not to enable persistence in the first place.
    /// </remarks>
    ///
    /// <returns>A Task which completes when the clear persistence operation has completed.
    /// </returns>
    public Task ClearPersistenceAsync() {
      EnsureNotDisposed();
      return _core.ClearPersistenceAsync();
    }

    /// <summary>
    /// Attaches a listener for a snapshots-in-sync event. The snapshots-in-sync event indicates
    /// that all listeners affected by a given change have fired, even if a single
    /// server-generated change affects multiple listeners.
    /// </summary>
    /// <remarks>
    /// NOTE: The snapshots-in-sync event only indicates that listeners are in sync with each other,
    /// but does not relate to whether those snapshots are in sync with the server. Use
    /// SnapshotMetadata in the individual listeners to determine if a snapshot is from the cache or
    /// the server.
    /// </remarks>
    /// <param name="callback">A callback to be called every time all snapshot listeners are in
    /// sync with each other. The callback will be invoked on the main thread.</param>
    /// <returns>A registration object that can be used to remove the listener.</returns>
    public ListenerRegistration ListenForSnapshotsInSync(Action callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      EnsureNotDisposed();
      return _core.ListenSnapshotsInSync(callback);
    }

    /// <summary>
    /// Loads a Firestore bundle into the local cache.
    /// </summary>
    /// <param name="bundleData">The bundle to be loaded.</param>
    /// <returns>A task that is completed when the loading is completed. The result of
    /// the task then contains the final progress of the loading operation.
    /// </returns>
    public Task<LoadBundleTaskProgress> LoadBundleAsync(string bundleData) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      EnsureNotDisposed();
      return _core.LoadBundleAsync(Encoding.UTF8.GetBytes(bundleData), null);
    }

    /// <summary>
    /// Loads a Firestore bundle into the local cache.
    /// </summary>
    /// <param name="bundleData">The bundle to be loaded, as a UTF-8 encoded byte array.</param>
    /// <returns>A task that is completed when the loading is completed. The result of
    /// the task then contains the final progress of the loading operation.
    /// </returns>
    public Task<LoadBundleTaskProgress> LoadBundleAsync(byte[] bundleData) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      EnsureNotDisposed();
      return _core.LoadBundleAsync(bundleData, null);
    }

    /// <summary>
    /// Loads a Firestore bundle into the local cache, taking an <see cref="System.EventHandler"/>
    /// to monitor loading progress.
    /// </summary>
    /// <param name="bundleData">The bundle to be loaded.</param>
    /// <param name="progressHandler">A <see cref="System.EventHandler"/> that is notified with
    /// progress updates, and completion or error updates. The handler will be invoked on the
    /// main thread.</param>
    /// <returns>A task that is completed when the loading is completed. The result of the task
    /// then contains the final progress of the loading operation.
    /// </returns>
    public Task<LoadBundleTaskProgress> LoadBundleAsync(
        string bundleData, EventHandler<LoadBundleTaskProgress> progressHandler) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      Preconditions.CheckNotNull(progressHandler, nameof(progressHandler));
      EnsureNotDisposed();
      return _core.LoadBundleAsync(Encoding.UTF8.GetBytes(bundleData), progressHandler);
    }

    /// <summary>
    /// Loads a Firestore bundle into the local cache, taking an <see cref="System.EventHandler"/>
    /// to monitor loading progress.
    /// </summary>
    /// <param name="bundleData">The bundle to be loaded, as a UTF8 encoded byte array.</param>
    /// <param name="progressHandler">An <see cref="System.EventHandler"/> that is notified with
    /// progress updates, and completion or error updates. The handler will be invoked on the main
    /// thread.</param>
    /// <returns>A task that is completed when the loading is completed. The result of the task
    /// then contains the final progress of the loading operation.
    /// </returns>
    public Task<LoadBundleTaskProgress> LoadBundleAsync(
        byte[] bundleData, EventHandler<LoadBundleTaskProgress> progressHandler) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      Preconditions.CheckNotNull(progressHandler, nameof(progressHandler));
      EnsureNotDisposed();
      return _core.LoadBundleAsync(bundleData, progressHandler);
    }

    /// <summary>
    /// Reads a Firestore <see cref="Query"/> from the local cache, identified by the given
    /// name.
    /// </summary>
    /// <remarks>
    /// Named queries are packaged into bundles on the server side (along with the
    /// resulting documents) and loaded into local cache using <see
    /// cref="FirebaseFirestore.LoadBundleAsync(string)"/>. Once in the local cache, you can use
    /// this method to extract a query by name.
    /// </remarks>
    /// <param name="queryName">The name of the query to read from saved bundles.</param>
    /// <returns>A task that is completed with the query associated with the given name. The result
    /// of the returned task is set to null if no queries can be found.
    /// </returns>
    public Task<Query> GetNamedQueryAsync(string queryName) {
      Preconditions.CheckNotNull(queryName, nameof(queryName));
      EnsureNotDisposed();
      return _core.GetNamedQueryAsync(queryName);
    }
  }
}
