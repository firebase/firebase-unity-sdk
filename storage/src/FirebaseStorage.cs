/*
 * Copyright 2017 Google LLC
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
using System.Threading;
using Firebase.Internal;
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  ///   FirebaseStorage is a service that supports uploading and downloading large objects to Google
  ///   Cloud Storage.
  /// </summary>
  /// <remarks>
  ///   FirebaseStorage is a service that supports uploading and downloading large objects to Google
  ///   Cloud Storage. Pass a custom instance of
  ///   <see cref="Firebase.FirebaseApp" />
  ///   to
  ///   <see cref="GetInstance" />
  ///   which will initialize it with a storage
  ///   location (bucket) specified via
  ///   <see cref="AppOptions.StorageBucket" />
  ///   .
  ///   <p />
  ///   Otherwise, if you call
  ///   <see cref="DefaultInstance" />
  ///   without a FirebaseApp, the
  ///   FirebaseStorage instance will initialize with the default
  ///   <see cref="Firebase.FirebaseApp" />
  ///   obtainable from
  ///   <see cref="FirebaseApp.DefaultInstance" />.
  ///   The storage location in this case will come the JSON
  ///   configuration file downloaded from the web.
  /// </remarks>
  public sealed class FirebaseStorage {
#if UNITY_6000_6_OR_NEWER
    [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    private static readonly FirebaseInstanceCache<FirebaseStorage> _instances =
        new FirebaseInstanceCache<FirebaseStorage>(s => s.Dispose());

    // The FirebaseApp instance associated with this FirebaseStorage instance.
    private readonly FirebaseApp firebaseApp;
    // Cancelled when this instance is disposed so in-flight transfers are aborted.
    private readonly CancellationTokenSource disposeCts = new CancellationTokenSource();
    private readonly CancellationToken disposeToken;

    // Logger for this module; parent of every instance's Logger.
#if UNITY_6000_6_OR_NEWER
    [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    private static readonly ModuleLogger moduleLogger =
        new ModuleLogger { Tag = "Cloud Storage" };

    /// <summary>
    /// Construct an instance associated with the specified app and bucket.
    /// </summary>
    /// <param name="app">App this FirebaseStorage instance is associated with.</param>
    /// <param name="bucket">Cloud Storage bucket name.</param>
    private FirebaseStorage(FirebaseApp app, string bucket) {
      firebaseApp = app;
      Bucket = bucket ?? string.Empty;
      disposeToken = disposeCts.Token;
      Logger = new ModuleLogger(parentLogger: moduleLogger) {
        Tag = $"{moduleLogger.Tag} {Url()}:",
      };
    }

    // Cancel any in-flight transfers started from this instance when evicted by AppDisposed/reload.
    private void Dispose() {
      // The source is intentionally never disposed: transfers started after Dispose() still
      // register on DisposeToken, which would throw ObjectDisposedException on Mono.
      disposeCts.Cancel();
    }

    // Cancelled when this instance (or its FirebaseApp) is disposed.
    internal CancellationToken DisposeToken => disposeToken;

    // Logger for this instance; messages are prefixed with the instance URL.
    internal ModuleLogger Logger { get; }

    // Cloud Storage bucket name associated with this instance.
    internal string Bucket { get; }

    // Configured storage emulator host, if any.
    internal string EmulatorHost { get; private set; }

    // Configured storage emulator port, if any.
    internal int EmulatorPort { get; private set; }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseStorage" />
    ///   , initialized with the default
    ///   <see cref="Firebase.FirebaseApp" />
    ///   .
    /// </summary>
    /// <value>
    ///   a
    ///   <see cref="FirebaseStorage" />
    ///   instance.
    /// </value>
    public static FirebaseStorage DefaultInstance => GetInstance(FirebaseApp.DefaultInstance);

    /// <summary>Returns the maximum time to retry a download if a failure occurs.</summary>
    /// <value>
    ///   maximum time which defaults to 10 minutes (600,000 milliseconds).
    /// </value>
    public TimeSpan MaxDownloadRetryTime { get; set; } = TimeSpan.FromMinutes(10.0);

    /// <summary>Returns the maximum time to retry an upload if a failure occurs.</summary>
    /// <returns>
    ///   the maximum time which defaults to 10 minutes (600,000 milliseconds).
    /// </returns>
    public TimeSpan MaxUploadRetryTime { get; set; } = TimeSpan.FromMinutes(10.0);

    /// <summary>
    ///   Sets the maximum time to retry operations other than upload and download if a
    ///   failure occurs.
    /// </summary>
    public TimeSpan MaxOperationRetryTime { get; set; } = TimeSpan.FromMinutes(2.0);

    /// <summary>
    ///   Configures the Storage SDK to use an emulated backend instead of
    ///   the default remote backend. This method should be called before invoking
    ///   any other methods on a new instance of Storage
    /// </summary>
    /// <param name="host">
    ///   The host that the storage emulator is running on.
    /// </param>
    /// <param name="port">
    ///   The port that the storage emulator is running on.
    /// </param>
    public void UseEmulator(string host, int port) {
      Preconditions.CheckNotEmpty(host, "host cannot be null or empty");
      Preconditions.CheckArgument(port > 0, "port must be greater than 0");
      EmulatorHost = host;
      EmulatorPort = port;
    }

    /// <summary>
    ///   Creates a new
    ///   <see cref="StorageReference" />
    ///   initialized at the root Cloud Storage location.
    /// </summary>
    /// <returns>
    ///   An instance of
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public StorageReference RootReference {
      get {
        return new StorageReference(this, new StoragePath(this, Bucket, string.Empty));
      }
    }

    /// <summary>
    ///   Sets or Gets how verbose Cloud Storage Logging will be.
    ///   To obtain more debug information, set this to
    ///   <see>
    ///     <cref>LogLevel.Verbose</cref>
    ///   </see>
    /// </summary>
    public LogLevel LogLevel {
      get => Logger.Level;
      set {
        Logger.Level = value;
        // FirebaseLogger drops anything more verbose than the global level, so lower it too.
        if (FirebaseApp.LogLevel > value) {
          FirebaseApp.LogLevel = value;
        }
      }
    }

    /// <summary>
    ///   The
    ///   <see cref="Firebase.FirebaseApp" />
    ///   associated with this
    ///   <see cref="FirebaseStorage" />
    ///   instance.
    /// </summary>
    public FirebaseApp App => firebaseApp;

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseStorage" />
    ///   , initialized with the default <see cref="Firebase.FirebaseApp" />
    ///   and a custom Storage Bucket
    /// </summary>
    /// <param name="url">
    ///   The gs:// url to your Cloud Storage Bucket.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="FirebaseStorage" />
    ///   instance.
    /// </returns>
    public static FirebaseStorage GetInstance(string url) =>
        GetInstance(FirebaseApp.DefaultInstance, url);

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseStorage" />
    ///   , initialized with a custom
    ///   <see cref="Firebase.FirebaseApp" />
    /// </summary>
    /// <param name="app">
    ///   The custom
    ///   <see cref="Firebase.FirebaseApp" />
    ///   used for initialization.
    /// </param>
    /// <param name="url">
    ///   The gs:// url to your Cloud Storage Bucket.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="FirebaseStorage" />
    ///   instance.
    /// </returns>
    public static FirebaseStorage GetInstance(FirebaseApp app, string url = null) {
      app = app ?? FirebaseApp.DefaultInstance;
      Preconditions.CheckNotNull(app, "app");

      string resolvedUrl = url;
      if (string.IsNullOrEmpty(resolvedUrl)) {
        string defaultBucket = app.Options?.StorageBucket;
        if (!string.IsNullOrEmpty(defaultBucket)) {
          resolvedUrl = defaultBucket.IndexOf("://", StringComparison.Ordinal) >= 0
              ? defaultBucket
              : "gs://" + defaultBucket;
        }
      }

      string bucket = string.Empty;
      if (!string.IsNullOrEmpty(resolvedUrl)) {
        StoragePath parsedPath = StorageUriParser.UriToComponents(
            null, resolvedUrl, moduleLogger, allowObjectPath: false);
        bucket = parsedPath.Bucket;
        resolvedUrl = "gs://" + bucket;
      }

      return _instances.GetOrCreate(
          app,
          () => {
            var created = new FirebaseStorage(app, bucket);
            created.Logger.LogMessage(LogLevel.Debug, String.Format("Created {0}/{1}", app.Name, resolvedUrl ?? ""));
            return created;
          },
          resolvedUrl ?? string.Empty);
    }

    /// <summary>
    ///   Returns a gs:// url to the Cloud Storage Bucket, or an empty string if this Storage was
    ///   created with default parameters.
    /// </summary>
    public string Url() {
      return string.IsNullOrEmpty(Bucket) ? string.Empty : $"gs://{Bucket}";
    }

    /// <summary>
    ///   Creates a
    ///   <see cref="StorageReference" />
    ///   given a gs:// or https:// URL pointing to a Firebase
    ///   Storage location.
    /// </summary>
    /// <param name="fullUrl">
    ///   A gs:// or http[s]:// URL used to initialize the reference.
    ///   For example, you can pass in a download URL retrieved from
    ///   <see cref="StorageReference.GetDownloadUrlAsync" />
    ///   or the uri retrieved from
    ///   <see cref="StorageReference.ToString()" />
    ///   An error is thrown if fullUrl is not associated with the
    ///   <see cref="Firebase.FirebaseApp" />
    ///   used to initialize this
    ///   <see cref="FirebaseStorage" />
    ///   .
    /// </param>
    public StorageReference GetReferenceFromUrl(string fullUrl) {
      StoragePath path = StorageUriParser.UriToComponents(
          this, fullUrl, Logger, allowObjectPath: true);
      if (path == null) {
        throw new ArgumentException($"Invalid storage URL: {fullUrl}", nameof(fullUrl));
      }
      if (!string.IsNullOrEmpty(Bucket) &&
          !string.Equals(path.Bucket, Bucket, StringComparison.Ordinal)) {
        throw new ArgumentException(
            $"Provided URL {fullUrl} is for bucket {path.Bucket}, expected {Bucket}");
      }
      return new StorageReference(this, path);
    }

    /// <summary>
    ///   Creates a new
    ///   <see cref="StorageReference" />
    ///   initialized with a child Cloud Storage location.
    /// </summary>
    /// <param name="location">
    ///   A relative path from the root to initialize the reference with, for instance
    ///   "path/to/object"
    /// </param>
    /// <returns>
    ///   An instance of
    ///   <see cref="StorageReference" />
    ///   at the given child path.
    /// </returns>
    public StorageReference GetReference(string location) {
      if (string.IsNullOrEmpty(location)) {
        return RootReference;
      }
      return RootReference.Child(location);
    }
  }
}
