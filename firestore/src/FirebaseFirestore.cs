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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Platform;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  using SnapshotsInSyncCallbackMap = ListenerRegistrationMap<Action>;
  using LoadBundleProgressCallbackMap = ListenerRegistrationMap<Action<LoadBundleTaskProgress>>;

  /// <summary>
  /// Represents a Cloud Firestore database and is the entry point for all Cloud Firestore
  /// operations.
  /// </summary>
  public sealed class FirebaseFirestore {
    // This lock and boolean pair are used to ensure that the mapping from Firebase app to Firestore
    // instance is removed atomically from the C++ instance cache and the C# instance cache. Any
    // call to a C++ method that removes the mapping from the C++ instance cache must do so while
    // holding this lock and must then set the boolean flag to false while still holding the lock.
    // Abiding by this contract ensures that the two caches are kept in sync.
    private readonly object _isInCppInstanceCacheLock = new object();
    private bool _isInCppInstanceCache = true;

    private readonly ReaderWriterLock _disposeLock = new ReaderWriterLock();
    private FirestoreProxy _proxy;

    private readonly FirebaseFirestoreSettings _settings;
    private readonly TransactionManager _transactionManager;

    private static readonly IDictionary<FirebaseApp, FirebaseFirestore> _instanceCache =
        new Dictionary<FirebaseApp, FirebaseFirestore>();

    // We rely on e.g. firestore.Document("a/b").Firestore returning the original Firestore
    // instance so it's important the constructor remains private and we only create one
    // FirebaseFirestore instance per FirebaseApp instance.
    private FirebaseFirestore(FirestoreProxy proxy, FirebaseApp app) {
      _proxy = Util.NotNull(proxy);
      App = app;
      app.AppDisposed += OnAppDisposed;

      // This call to InitializeConverterCache() exists to make sure that AOT works.
      Firebase.Firestore.Converters.ConverterCache.InitializeConverterCache();

      string dotnetVersion = EnvironmentVersion.GetEnvironmentVersion();
      ApiHeaders.SetClientLanguage(String.Format("gl-dotnet/{0}", dotnetVersion));

      _settings = new FirebaseFirestoreSettings(proxy);
      _transactionManager = new TransactionManager(this, proxy);
    }

    /// <summary>
    /// Destroys this object and frees all resources it has acquired.
    /// </summary>
    ~FirebaseFirestore() {
      Dispose();
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      Dispose();
    }

    // TODO(b/201097848) Make `Dispose()` public and implement `IDisposable`.
    private void Dispose() {
      _disposeLock.AcquireWriterLock(Int32.MaxValue);
      try {
        if (_proxy == null) {
          return;
        }

        snapshotsInSyncCallbacks.ClearCallbacksForOwner(this);
        loadBundleProgressCallbackMap.ClearCallbacksForOwner(this);
        DocumentReference.ClearCallbacksForOwner(this);
        Query.ClearCallbacksForOwner(this);

        _transactionManager.Dispose();
        _settings.Dispose();

        App.AppDisposed -= OnAppDisposed;

        lock (_isInCppInstanceCacheLock) {
          _proxy.Dispose();
          _isInCppInstanceCache = false;
          RemoveSelfFromInstanceCache();
        }

        _proxy = null;
        App = null;
      } finally {
        _disposeLock.ReleaseWriterLock();
      }

      System.GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns the <c>FirebaseApp</c> instance to which this <c>FirebaseFirestore</c> belongs.
    /// </summary>
    public FirebaseApp App { get; private set; }

    /// <summary>
    /// Gets the instance of <c>FirebaseFirestore</c> for the default <c>FirebaseApp</c>.
    /// </summary>
    /// <value>A <c>FirebaseFirestore</c> instance.</value>
    public static FirebaseFirestore DefaultInstance {
      get {
        FirebaseApp app = Util.NotNull(FirebaseApp.DefaultInstance);
        return GetInstance(app);
      }
    }

    /// <summary>
    /// Gets an instance of <c>FirebaseFirestore</c> for a specific <c>FirebaseApp</c>.
    /// </summary>
    /// <param name="app">The <c>FirebaseApp</c> for which to get a <c>FirebaseFirestore</c>
    /// instance.</param>
    /// <returns>A <c>FirebaseFirestore</c> instance.</returns>
    public static FirebaseFirestore GetInstance(FirebaseApp app) {
      Preconditions.CheckNotNull(app, nameof(app));

      while (true) {
        FirebaseFirestore firestore;

        // Acquire the lock on `_instanceCache` to see if the given `FirebaseApp` is in
        // `_instanceCache`; if it isn't then create the `FirebaseFirestore` instance, put it in the
        // cache, and return it.
        lock (_instanceCache) {
          if (!_instanceCache.TryGetValue(app, out firestore)) {
            // NOTE: FirestoreProxy.GetInstance() returns an *owning* reference (see the %newobject
            // directive in firestore.SWIG); therefore, we must make sure that we *never* call
            // FirestoreProxy.GetInstance() when it would return a proxy for a C++ object that it
            // previously returned. Otherwise, if it did, then that C++ object would be deleted
            // twice, causing a crash.
            FirestoreProxy firestoreProxy = Util.NotNull(FirestoreProxy.GetInstance(app));
            firestore = new FirebaseFirestore(firestoreProxy, app);
            _instanceCache[app] = firestore;
            return firestore;
          }
        }

        // Acquire the lock on the specific `FirebaseFirestore` instance's `_isInCppInstanceCache`
        // and check if the mapping is also present in the C++ instance cache. If it is, then go
        // ahead and return the `FirebaseFirestore` object. If it isn't, then there must have been a
        // concurrent call to `TerminateAsync` or `Dispose` that removed it from the C++ instance
        // cache, in which case we just try the whole process over again. That's why this logic is
        // in "while" loop as opposed to a single "if" statement.
        lock (firestore._isInCppInstanceCacheLock) {
          if (firestore._isInCppInstanceCache) {
            return firestore;
          }
        }
      }
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
    public FirebaseFirestoreSettings Settings {
      get {
        return _settings;
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
      return WithFirestoreProxy(proxy => new CollectionReference(proxy.Collection(path), this));
    }

    /// <summary>
    /// Creates a local <see cref="DocumentReference"/> for the given path, which must include an
    /// even number of slash-separated identifiers. This does not perform any remote operations.
    /// </summary>
    /// <param name="path">The document path, e.g. <c>col1/doc1/col2/doc2</c>.</param>
    /// <returns>A document reference.</returns>
    public DocumentReference Document(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      return WithFirestoreProxy(proxy => new DocumentReference(proxy.Document(path), this));
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
      return WithFirestoreProxy(proxy => new Query(proxy.CollectionGroup(collectionId), this));
    }

    /// <summary>
    /// Creates a write batch, which can be used to commit multiple mutations atomically.
    /// </summary>
    /// <returns>A write batch for this database.</returns>
    public WriteBatch StartBatch() {
      return WithFirestoreProxy(proxy => new WriteBatch(proxy.batch()));
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
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails to
    /// commit after 5 attempts, the transaction will fail.</para>
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
    /// Runs a transaction asynchronously, with an asynchronous callback that returns a value.
    /// The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails to
    /// commit after the maximum number of attempts specified in the given <c>TransactionOptions</c>
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
    public Task<T> RunTransactionAsync<T>(TransactionOptions options, Func<Transaction, Task<T>> callback) {
      Preconditions.CheckNotNull(options, nameof(options));
      Preconditions.CheckNotNull(callback, nameof(callback));
      return WithFirestoreProxy(proxy => _transactionManager.RunTransactionAsync(options, callback));
    }

    /// <summary>
    /// Runs a transaction asynchronously, with an asynchronous callback that doesn't return a
    /// value. The specified callback is executed for a newly-created transaction.
    /// </summary>
    /// <remarks>
    /// <para><c>RunTransactionAsync</c> executes the given callback on the main thread and then
    /// attempts to commit the changes applied within the transaction. If any document read within
    /// the transaction has changed, the <paramref name="callback"/> will be retried. If it fails to
    /// commit after the maximum number of attempts specified in the given <c>TransactionOptions</c>
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
      // Just pass through to the overload where the callback returns a Task<T>.
      return RunTransactionAsync(options, transaction => Util.MapResult<object>(callback(transaction), null));
    }

    private static SnapshotsInSyncCallbackMap snapshotsInSyncCallbacks = new SnapshotsInSyncCallbackMap();
    internal delegate void SnapshotsInSyncDelegate(int callbackId);
    private static SnapshotsInSyncDelegate snapshotsInSyncHandler = new SnapshotsInSyncDelegate(SnapshotsInSyncHandler);


    [MonoPInvokeCallback(typeof(SnapshotsInSyncDelegate))]
    private static void SnapshotsInSyncHandler(int callbackId) {
      try {
        Action callback;

        if (snapshotsInSyncCallbacks.TryGetCallback(callbackId, out callback)) {
          FirebaseHandler.RunOnMainThread<object>(() => {
            callback();
            return null;
          });
        }

      } catch (Exception e) {
        Util.OnPInvokeManagedException(e, nameof(SnapshotsInSyncHandler));
      }
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
      var tcs = new TaskCompletionSource<object>();
      int uid = -1;
      ListenerRegistrationProxy listenerProxy = WithFirestoreProxy(proxy => {
        uid = snapshotsInSyncCallbacks.Register(this, callback);
        return FirestoreCpp.AddSnapshotsInSyncListener(proxy, uid, snapshotsInSyncHandler);
      });
      return new ListenerRegistration(snapshotsInSyncCallbacks, uid, tcs, listenerProxy);
    }

    private static LoadBundleProgressCallbackMap loadBundleProgressCallbackMap =
        new LoadBundleProgressCallbackMap();
    internal delegate void LoadBundleTaskProgressDelegate(int callbackId, IntPtr progress);
    private static LoadBundleTaskProgressDelegate handler =
        new LoadBundleTaskProgressDelegate(LoadBundleTaskProgressHandler);

    [MonoPInvokeCallback(typeof(LoadBundleTaskProgressDelegate))]
    private static void LoadBundleTaskProgressHandler(int callbackId, IntPtr progressPtr) {
      try {
        // Create the proxy object _before_ doing anything else to ensure that the C++ object's
        // memory does not get leaked (https://github.com/firebase/firebase-unity-sdk/issues/49).
        var progressProxy = new LoadBundleTaskProgressProxy(progressPtr, /*cMemoryOwn=*/true);

        Action<LoadBundleTaskProgress> callback;
        if (loadBundleProgressCallbackMap.TryGetCallback(callbackId, out callback)) {
          callback(new LoadBundleTaskProgress(progressProxy));
        }
      } catch (Exception e) {
        Util.OnPInvokeManagedException(e, nameof(LoadBundleTaskProgressHandler));
      }
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
      return WithFirestoreProxy(
          proxy => Util.MapResult(FirestoreCpp.LoadBundleAsync(proxy, bundleData),
                                  progressProxy => new LoadBundleTaskProgress(progressProxy)));
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
        string bundleData, System.EventHandler<LoadBundleTaskProgress> progressHandler) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      Preconditions.CheckNotNull(progressHandler, nameof(progressHandler));

      Action<LoadBundleTaskProgress> action = (LoadBundleTaskProgress progress) => {
        FirebaseHandler.RunOnMainThread<object>(() => {
          progressHandler(this, progress);
          return null;
        });
      };

      return WithFirestoreProxy(proxy => {
        int callbackId = loadBundleProgressCallbackMap.Register(this, action);
        return Util.MapResult(FirestoreCpp.LoadBundleAsync(proxy, bundleData, callbackId, handler),
                              progressProxy => new LoadBundleTaskProgress(progressProxy));
      });
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
      // TODO(b/190626833): Consider expose a C++ interface taking byte array, to avoid encoding
      // here.
      return LoadBundleAsync(Encoding.UTF8.GetString(bundleData));
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
        byte[] bundleData, System.EventHandler<LoadBundleTaskProgress> progressHandler) {
      Preconditions.CheckNotNull(bundleData, nameof(bundleData));
      Preconditions.CheckNotNull(progressHandler, nameof(progressHandler));
      return LoadBundleAsync(Encoding.UTF8.GetString(bundleData), progressHandler);
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
      return WithFirestoreProxy(proxy => {
        return proxy.NamedQueryAsync(queryName).ContinueWith<Query>((queryProxy) => {
          if (queryProxy.IsFaulted) {
            return null;
          }

          return new Query(queryProxy.Result, this);
        });
      });
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
      return WithFirestoreProxy(proxy => proxy.DisableNetworkAsync());
    }

    /// <summary>
    /// Re-enables network usage for this instance after a prior call to
    /// <see cref="FirebaseFirestore.DisableNetworkAsync"/>.
    /// </summary>
    /// <returns>A task which completes once networking is enabled.</returns>
    public Task EnableNetworkAsync() {
      return WithFirestoreProxy(proxy => proxy.EnableNetworkAsync());
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
      return WithFirestoreProxy(proxy => proxy.WaitForPendingWritesAsync());
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
    /// <c>GetInstance()</c> or <c>GetInstance(FirebaseApp)</c>.
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
      return WithFirestoreProxy(proxy => {
        lock (_isInCppInstanceCacheLock) {
          Task terminateTask = proxy.TerminateAsync();
          _isInCppInstanceCache = false;
          RemoveSelfFromInstanceCache();
          return terminateTask;
        }
      });
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
      return WithFirestoreProxy(proxy => proxy.ClearPersistenceAsync());
    }

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
      set {
        // This is thread-safe as far as the underlying one is thread-safe.
        FirestoreProxy.set_log_level(value);
      }
    }

    /// <summary>
    /// Ensure that the `FirestoreProxy` has had its settings applied, and then call the given
    /// `Function` with that `FirestoreProxy` instance, returning whatever it returns. If the
    /// `FirestoreProxy` has been disposed, then a `FirestoreException` is thrown.
    /// </summary>
    private T WithFirestoreProxy<T>(Func<FirestoreProxy, T> func) {
      _disposeLock.AcquireReaderLock(Int32.MaxValue);
      try {
        if (_proxy == null) {
          throw new InvalidOperationException("Firestore instance has been disposed");
        }

        _settings.EnsureAppliedToFirestoreProxy();

        return func(_proxy);
      } finally {
        _disposeLock.ReleaseReaderLock();
      }
    }

    /// <summary>
    /// Ensure that the `FirestoreProxy` has had its settings applied, and then call the given
    /// `Action` with that `FirestoreProxy` instance. If the `FirestoreProxy` has been disposed,
    /// then a `FirestoreException` is thrown.
    /// </summary>
    private void WithFirestoreProxy(Action<FirestoreProxy> action) {
      WithFirestoreProxy<object>(proxy => {
        action(proxy);
        return null;
      });
    }

    /// <summary>
    /// Removes the mapping from the instance cache for this object.
    /// </summary>
    private void RemoveSelfFromInstanceCache() {
      lock (_instanceCache) {
        FirebaseFirestore cachedFirestore;
        if (_instanceCache.TryGetValue(App, out cachedFirestore) && cachedFirestore == this) {
          _instanceCache.Remove(App);
        }
      }
    }
  }
}
