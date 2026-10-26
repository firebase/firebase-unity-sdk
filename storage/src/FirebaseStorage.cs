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
using System.Collections.Concurrent;
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
    // Thread-safe dictionary of FirebaseStorage instances indexed by "{app.Name}/{url}".
    private static readonly ConcurrentDictionary<string, FirebaseStorage> storageByInstanceKey =
        new ConcurrentDictionary<string, FirebaseStorage>();

    // The FirebaseApp instance associated with this FirebaseStorage instance.
    private readonly FirebaseApp firebaseApp;
    // Key of this instance within storageByInstanceKey.
    private readonly string instanceKey;
    // Tracks whether the associated FirebaseApp has been disposed.
    private bool _disposed;

    // Logger for this module.
    private static readonly ModuleLogger logger = new ModuleLogger { Tag = "Cloud Storage" };

    /// <summary>
    /// Construct an instance associated with the specified app and bucket.
    /// </summary>
    /// <param name="app">App this FirebaseStorage instance is associated with.</param>
    /// <param name="bucket">Cloud Storage bucket name.</param>
    /// <param name="key">Instance cache key.</param>
    private FirebaseStorage(FirebaseApp app, string bucket, string key) {
      firebaseApp = app;
      Bucket = bucket ?? string.Empty;
      instanceKey = key;
      firebaseApp.AppDisposed += OnAppDisposed;
      logger.LogMessage(LogLevel.Debug, String.Format("Created {0}", instanceKey));
    }

    private void OnAppDisposed(object sender, EventArgs eventArgs) {
      Dispose();
    }

    // Remove the reference to this instance from storageByInstanceKey and unsubscribe from AppDisposed.
    private void Dispose() {
      if (_disposed) {
        return;
      }
      _disposed = true;
      firebaseApp.AppDisposed -= OnAppDisposed;
      if (storageByInstanceKey.TryGetValue(instanceKey, out FirebaseStorage cached) &&
          ReferenceEquals(cached, this)) {
        storageByInstanceKey.TryRemove(instanceKey, out _);
      }
    }

    // Logger for this instance.
    internal ModuleLogger Logger => logger;

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
      get => logger.Level;
      set => logger.Level = value;
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
            null, resolvedUrl, logger, allowObjectPath: false);
        bucket = parsedPath.Bucket;
        resolvedUrl = "gs://" + bucket;
      }

      string key = $"{app.Name}/{resolvedUrl ?? ""}";
      return storageByInstanceKey.AddOrUpdate(
          key,
          k => new FirebaseStorage(app, bucket, k),
          (k, existing) => (ReferenceEquals(existing.firebaseApp, app) && !existing._disposed)
              ? existing
              : new FirebaseStorage(app, bucket, k));
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
          this, fullUrl, logger, allowObjectPath: true);
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
