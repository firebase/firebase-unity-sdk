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
using System.Collections.Generic;
using System.Reflection;
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
    // Dictionary of FirebaseStorage instances indexed by a key FirebaseStorageInternal.InstanceKey.
    private static readonly Dictionary<string, FirebaseStorage> storageByInstanceKey =
        new Dictionary<string, FirebaseStorage>();

    // Proxy for the C++ firebase::storage::Storage object.
    private FirebaseStorageInternal storageInternal;
    // Proxy for the C++ firebase::app:App object.
    private readonly FirebaseApp firebaseApp;
    // Key of this instance within storageByInstanceKey.
    private string instanceKey;

    // Logger for this module.
    private static readonly ModuleLogger logger = new ModuleLogger { Tag = "Cloud Storage" };

    /// <summary>
    /// Construct an instance associated with the specified app and bucket.
    /// </summary>
    /// <param name="storage">C# proxy for firebase::storage::Storage.</param>
    /// <param name="app">App the C# proxy storageInternal was created from.</param>
    private FirebaseStorage(FirebaseStorageInternal storage, FirebaseApp app) {
      firebaseApp = app;
      firebaseApp.AppDisposed += OnAppDisposed;
      storageInternal = storage;
      Logger = new ModuleLogger(parentLogger: logger) {
        Tag = String.Format("{0} {1}:", logger.Tag, storageInternal.Url),
        // TODO(smiles): Remove when FirebaseApp logger is changed to ModuleLogger.
        Level = FirebaseApp.LogLevel
      };
      // As we know there is only one reference to the C++ firebase::storage::Storage object here
      // we'll let the proxy object take ownership so the C++ object can be deleted when the
      // proxy's Dispose() method is executed.
      storageInternal.SetSwigCMemOwn(true);
      instanceKey = storageInternal.InstanceKey;

      Logger.LogMessage(LogLevel.Debug, String.Format("Created {0}", instanceKey));
    }

    // Dispose of this object.
    private static void DisposeObject(object objectToDispose) {
      ((FirebaseStorage)objectToDispose).Dispose();
    }

    /// <summary>
    /// Remove the reference to this object from the storageByInstanceKey dictionary.
    /// </summary>
    ~FirebaseStorage() {
      Dispose();
      Logger.LogMessage(LogLevel.Debug, String.Format("Finalized {0}", instanceKey));
    }

    // Remove the reference to this instance from storageByInstanceKey and dispose the proxy.
    private void Dispose() {
      System.GC.SuppressFinalize(this);
      lock (storageByInstanceKey) {
        if (storageInternal != null &&
            FirebaseStorageInternal.getCPtr(storageInternal).Handle != IntPtr.Zero) {
          firebaseApp.AppDisposed -= OnAppDisposed;
          storageByInstanceKey.Remove(instanceKey);
          storageInternal.Dispose();
          storageInternal = null;
        }
      }
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Warning, "FirebaseStorage.OnAppDisposed()");
      Dispose();
    }

    // Throw a NullReferenceException if this proxy references a deleted object.
    private void ThrowIfNull() {
      if (storageInternal == null ||
          FirebaseStorageInternal.getCPtr(storageInternal).Handle == System.IntPtr.Zero) {
        throw new System.NullReferenceException();
      }
    }

    // Logger for this instance.
    internal ModuleLogger Logger { get; private set; }

    private static FieldInfo pathFieldInfo;
    private static FieldInfo cachedToString;

    // HACK:  This hack is specific to firebase storage, and fixes an issue where
    // System.Uri decodes "%2F" into slashes, (against the RFC spec) and ignores
    // the "dontFormat" flag.  (Because it has been deprecated.)
    // This hack relies on the fact that paths in Firebase Storage do not have
    // slashes after the object indicator "/o/<path to object>", so it's safe to
    // convert all slashes past the "/o/" back into "%2F"s.
    // The input should be a completely escaped/formatted URL, because it will
    // still be returned via Uri.ToString() and Uri.OriginalString().
    internal static Uri ConstructFormattedUri(string formattedUrl) {
      Uri uri = new Uri(formattedUrl);
      string paq = uri.PathAndQuery;  // ensure that "path" has been calculated.

      // Set up reflection fields and cache them for additional calls.
      if (pathFieldInfo == null) {
        pathFieldInfo = typeof(Uri).GetField("path",
                      BindingFlags.Instance | BindingFlags.NonPublic);
        if (pathFieldInfo == null) {
          FirebaseStorage.DefaultInstance.Logger.LogMessage(LogLevel.Debug, String.Format(
              "{0}: Unable to reflect Uri field to fix slashes.  Storage operations will only work on"
              + " the root path of your storage bucket", uri.ToString()));
          return uri;
        }
      }
      if (cachedToString == null) {
        // cachedToString is not required, but will fix instances of ToString in addition to the
        // regular fix to PathAndQuery.  This is important for returning a proper DownloadUrl.
        cachedToString = typeof(Uri).GetField("cachedToString",
                  BindingFlags.Instance | BindingFlags.NonPublic);
        if (cachedToString == null) {
          FirebaseStorage.DefaultInstance.Logger.LogMessage(LogLevel.Debug, String.Format(
              "{0}: Unable to reflect Uri field cachedToString.  Storage operations may only work on"
              + " the root path of your storage bucket", uri.ToString()));
          // Continue to try to fix PathAndQuery if we can, since that controls uploads/download operations.
        }
      }

      try {
        string path = (string) pathFieldInfo.GetValue(uri);
        if (path == null) {
          return uri;
        }
        int index = path.LastIndexOf("/o/");
        if (index == -1) {
          return uri;
        }
        if (index + 3 == path.Length) {
          return uri;
        }
        string fixedSuffix = path.Substring(index + 3).Replace("/", "%2F");
        path = path.Substring(0, index + 3) + fixedSuffix;
        pathFieldInfo.SetValue(uri, path);
        if (cachedToString != null) {
          cachedToString.SetValue(uri, formattedUrl);
        }
      } catch (FieldAccessException ) {
        FirebaseStorage.DefaultInstance.Logger.LogMessage(LogLevel.Debug, String.Format(
            "{0}: FieldAccessException reflecting to fix canonicalization", uri.ToString()));
      } catch (TargetException ) {
        FirebaseStorage.DefaultInstance.Logger.LogMessage(LogLevel.Debug, String.Format(
            "{0}: TargetException reflecting to fix canonicalization", uri.ToString()));
      } catch (ArgumentException ) {
        FirebaseStorage.DefaultInstance.Logger.LogMessage(LogLevel.Debug, String.Format(
            "{0}: ArgumentException reflecting to fix canonicalization", uri.ToString()));
      }
      GC.KeepAlive(paq); // Just in case, don't optimize me away.
      return uri;
    }

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
    public static FirebaseStorage DefaultInstance {
      get { return GetInstance(FirebaseApp.DefaultInstance); }
    }

    /// <summary>Returns the maximum time to retry a download if a failure occurs.</summary>
    /// <value>
    ///   maximum time which defaults to 10 minutes (600,000 milliseconds).
    /// </value>
    public TimeSpan MaxDownloadRetryTime {
      get {
        ThrowIfNull();
        return TimeSpan.FromSeconds(storageInternal.MaxDownloadRetryTime);
      }
      set {
        ThrowIfNull();
        storageInternal.MaxDownloadRetryTime = value.TotalSeconds;
      }
    }

    /// <summary>Returns the maximum time to retry an upload if a failure occurs.</summary>
    /// <returns>
    ///   the maximum time which defaults to 10 minutes (600,000 milliseconds).
    /// </returns>
    public TimeSpan MaxUploadRetryTime {
      get {
        ThrowIfNull();
        return TimeSpan.FromSeconds(storageInternal.MaxUploadRetryTime);
      }
      set {
        ThrowIfNull();
        storageInternal.MaxUploadRetryTime = value.TotalSeconds;
      }
    }

    /// <summary>
    ///   Sets the maximum time to retry operations other than upload and download if a
    ///   failure occurs.
    /// </summary>
    public TimeSpan MaxOperationRetryTime {
      get {
        ThrowIfNull();
        return TimeSpan.FromSeconds(storageInternal.MaxOperationRetryTime);
      }
      set {
        ThrowIfNull();
        storageInternal.MaxOperationRetryTime = value.TotalSeconds;
      }
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
        ThrowIfNull();
        return new StorageReference(this, storageInternal.GetReference());
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
      get { return logger.Level; }
      set {
        logger.Level = value;
        if (FirebaseApp.LogLevel > value) FirebaseApp.LogLevel = value;
      }
    }

    /// <summary>
    ///   The
    ///   <see cref="Firebase.FirebaseApp" />
    ///   associated with this
    ///   <see cref="FirebaseStorage" />
    ///   instance.
    /// </summary>
    public FirebaseApp App { get { return firebaseApp; } }

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
      return GetInstanceInternal(app,
                                 !String.IsNullOrEmpty(url) ? url :
                                 !String.IsNullOrEmpty(app.Options.StorageBucket) ?
                                 String.Format("gs://{0}", app.Options.StorageBucket) : null);
    }

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
    public static FirebaseStorage GetInstance(string url) {
      return GetInstance(FirebaseApp.DefaultInstance, url);
    }

    /// <summary>
    /// Find a previously created FirebaseStorage by FirebaseStorageInternal.InstanceKey.
    /// </summary>
    /// <param name="instanceKey">Key to search for.</param>
    /// <returns>Storage instance if successful, null otherwise.</returns>
    private static FirebaseStorage FindByKey(string instanceKey) {
      lock (storageByInstanceKey) {
        FirebaseStorage storage = null;
        if (storageByInstanceKey.TryGetValue(instanceKey, out storage)) {
          if (storage != null) return storage;
          storageByInstanceKey.Remove(instanceKey);
        }
      }
      return null;
    }

    /// <summary>
    /// Get or create an instance from the specified all and bucket URL.
    /// </summary>
    /// <remarks>
    /// Even though the C++ layer will return only one instance for each app / bucket tuple, this
    /// method prevents creation of multiple FirebaseStorageInternal proxy objects that each point
    /// at the same underlying C++ object such that for each app / bucket tuple we'll always have
    /// the unique set of FirebaseStorage --> FirebaseStorageInternal --> firebase::storage::Storage
    /// objects.
    /// </remarks>
    /// <param name="app">App the storage instance is associated with.</param>
    /// <param name="bucketUrl">URL of the storage bucket associated with the instance.</param>
    /// <returns>FirebaseStorage instance that is unique for the app / bucketUrl tuple.</returns>
    private static FirebaseStorage GetInstanceInternal(FirebaseApp app, string bucketUrl) {
      app = app ?? FirebaseApp.DefaultInstance;
      InitResult initResult;
      FirebaseStorageInternal storageInternal = null;
      try {
        storageInternal =
            FirebaseStorageInternal.GetInstanceInternal(app, bucketUrl, out initResult);
      } catch (ApplicationException exception) {
        // The original C# implementation threw an ArgumentException when failing to create a
        // storage instance so mirror the behavior here.
        throw new ArgumentException(exception.Message);
      }
      if (initResult != InitResult.Success) {
        // The original C# implementation threw an ArgumentException when failing to create a
        // storage instance so mirror the behavior here.
        // TODO(b/77966715): Better way to communicate url parse error to C#
        // After introducing InitResult, Storage::GetInstance() in C++ no longer trigger exception
        // through assert.  However, InitResult may not be success due to various reason, ex.
        // invalid url or fail to create Java/Object-C object. Throw ArgumentException
        // for all scenario for now but need to have better way to handle different error in the
        // future.
        throw new ArgumentException(String.Format(
          "Unable to initialize FirebaseStorage for bucket URL '{0}'", bucketUrl ?? ""));
      } else if (storageInternal == null) {
        logger.LogMessage(LogLevel.Error,
                          String.Format("Unable to create FirebaseStorage for bucket " +
                                        "URL '{0}'", bucketUrl ?? ""));
        return null;
      }
      FirebaseStorage storage = null;
      lock (storageByInstanceKey) {
        var storageInternalInstanceKey = storageInternal.InstanceKey;
        storage = FindByKey(storageInternalInstanceKey);
        if (storage != null) return storage;
        storage = new FirebaseStorage(storageInternal, app);
        storageByInstanceKey[storageInternalInstanceKey] = storage;
      }
      return storage;
    }

    /// <summary>
    /// Throw an ArgumentException if the StorageReference is invalid.
    /// </summary>
    /// <param name="reference">Reference to validate.</param>
    /// <param name="message">Message used to construct the exception if reference is
    /// invalid.</param>
    /// <returns>reference</returns>
    /// <throws>ArgumentException if reference is invalid.</param>
    private StorageReferenceInternal ValidateStorageReferenceInternal(
        StorageReferenceInternal reference, string message) {
      if (reference == null || !reference.IsValid) throw new ArgumentException(message);
      return reference;
    }

    /// <summary>
    ///   Returns a gs:// url to the Cloud Storage Bucket, or an empty string if this Storage was
    ///   created with default parameters.
    /// </summary>
    public string Url() {
      ThrowIfNull();
      return storageInternal.Url;
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
      try {
        ThrowIfNull();
        return new StorageReference(
          this, ValidateStorageReferenceInternal(
            storageInternal.GetReferenceFromUrl(fullUrl),
            String.Format("URL {0} does not match the storage URL {1}", fullUrl,
                          storageInternal.Url)));
      } catch (ApplicationException exception) {
        // The original C# implementation threw an ArgumentException when failing to create a
        // storage instance so mirror the behavior here.
        throw new ArgumentException(exception.Message);
      }
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
      try {
        ThrowIfNull();
        return new StorageReference(
          this, ValidateStorageReferenceInternal(
            storageInternal.GetReference(location),
            String.Format("Path {0} is invalid for storage URL {1}", location,
                          storageInternal.Url)));
      } catch (ApplicationException exception) {
        // The original C# implementation threw an ArgumentException when failing to create a
        // storage instance so mirror the behavior here.
        throw new ArgumentException(exception.Message);
      }
    }
  }
}
