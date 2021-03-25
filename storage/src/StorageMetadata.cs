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

namespace Firebase.Storage {
  /// <summary>
  ///   Metadata for a
  ///   <see cref="StorageReference" />
  ///   .
  ///   Metadata stores default attributes such as size and content type.
  ///   You may also store custom metadata key value pairs.
  ///   Metadata values may be used to authorize operations using declarative validation rules.
  ///   This class is readonly.  To create or change metadata, use
  ///   <see cref="MetadataChange"/>.
  /// </summary>
  public class StorageMetadata {
    // TODO(smiles): Move to app?
    // Other instances of this constant:
    // * Firebase.Platform.Security.ServiceAccountCredential.UnixEpoch
    // * Firebase.Database.Internal.Logging.DefaultLogger.BuildLogMessage() - contains a copy.
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0,
                                                              DateTimeKind.Utc);

    // Storage reference this metadata was retrieved from.
    private readonly StorageReference storageReference;

    /// <summary>
    ///   Creates a
    ///   <see cref="StorageMetadata" />
    ///   object to hold metadata for a
    ///   <see cref="StorageReference" />
    /// </summary>
    public StorageMetadata() {
      storageReference = null;
      Internal = new MetadataInternal(FirebaseStorage.DefaultInstance);
    }

    internal StorageMetadata(StorageReference reference, MetadataInternal metadata) {
      storageReference = reference;
      Internal = metadata;
    }

    internal StorageMetadata(StorageMetadata metadataToCopy) {
      storageReference = metadataToCopy.storageReference;
      Internal = metadataToCopy.Internal.Copy();
    }

    /// <returns>
    ///   the content type of the
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public string ContentType {
      get { return Internal.ContentType; }
    }

    /// <returns>the keys for custom metadata.</returns>
    public IEnumerable<string> CustomMetadataKeys {
      get {
          var customMetadata = Internal.CustomMetadata;
          return customMetadata != null ? customMetadata.Keys : new List<string>();
      }
    }

    /// <returns>
    ///   the path of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Path { get { return Internal.Path; } }

    /// <returns>
    ///   a simple name of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Name { get { return Internal.Name; } }

    /// <returns>
    ///   the owning Google Cloud Storage bucket for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string Bucket { get { return Internal.Bucket; } }

    /// <returns>
    ///   a version String indicating what version of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string Generation { get { return Internal.Generation.ToString(); } }

    /// <returns>
    ///   a version String indicating the version of this
    ///   <see cref="StorageMetadata" />
    /// </returns>
    public string MetadataGeneration {
      get { return Internal.MetadataGeneration.ToString(); }
    }

    /// <returns>
    ///   the time the
    ///   <see cref="StorageReference" />
    ///   was created.
    /// </returns>
    public DateTime CreationTimeMillis {
      get { return UnixEpoch.AddMilliseconds(Internal.CreationTime); }
    }

    /// <returns>
    ///   the time the
    ///   <see cref="StorageReference" />
    ///   was last updated.
    /// </returns>
    public DateTime UpdatedTimeMillis {
      get { return UnixEpoch.AddMilliseconds(Internal.UpdatedTime); }
    }

    /// <returns>
    ///   the stored Size in bytes of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public long SizeBytes { get { return Internal.SizeBytes; } }

    /// <returns>
    ///   the MD5Hash of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Md5Hash { get { return Internal.Md5Hash; } }

    /// <returns>
    ///   the Cache Control setting of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string CacheControl { get { return Internal.CacheControl; } }

    /// <returns>
    ///   the content disposition of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentDisposition { get { return Internal.ContentDisposition; } }

    /// <returns>
    ///   the content encoding for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentEncoding { get { return Internal.ContentEncoding; } }

    /// <returns>
    ///   the content language for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentLanguage { get { return Internal.ContentLanguage; } }

    /// <returns>
    ///   the associated
    ///   <see cref="StorageReference" />
    ///   for which this metadata belongs to.
    /// </returns>
    public StorageReference Reference { get { return storageReference; } }

    /// <summary>
    ///   Returns custom metadata for a
    ///   <see cref="StorageReference" />
    /// </summary>
    /// <param name="key">The key for which the metadata should be returned</param>
    /// <returns>the metadata stored in the object the given key.</returns>
    public string GetCustomMetadata(string key) {
      var customMetadata = Internal.CustomMetadata;
      string metadataValue = null;
      if (customMetadata == null ||
          !customMetadata.TryGetValue(key, out metadataValue)) {
          return null;
      }
      return metadataValue;
    }

    /// <summary>
    /// Write all fields of this object to a string.
    /// </summary>
    /// NOTE: This does not override ToString() to avoid polluting the public API.
    internal string AsString() {
      Dictionary<string, object> fieldsAndValues = new Dictionary<string, object> {
        {"ContentType", ContentType},
        {"Path", Path},
        {"Name", Name},
        {"Bucket", Bucket},
        {"Generation", Generation},
        {"MetadataGeneration", MetadataGeneration},
        {"CreationTimeMillis", CreationTimeMillis},
        {"UpdatedTimeMillis", UpdatedTimeMillis},
        {"SizeBytes", SizeBytes},
        {"Md5Hash", Md5Hash},
        {"CacheControl", CacheControl},
        {"ContentDisposition", ContentDisposition},
        {"ContentEncoding", ContentEncoding},
        {"ContentLanguage", ContentLanguage},
        {"Reference", Reference.Path},
      };
      var metadataKeys = new List<string>(CustomMetadataKeys);
      for (int i = 0; i < metadataKeys.Count; ++i) {
        var key = metadataKeys[i];
        fieldsAndValues[String.Format("CustomMetadata[{0}]", key)] = GetCustomMetadata(key);
      }
      var fieldAndValueStrings = new List<string>();
      foreach (var kv in fieldsAndValues) {
        fieldAndValueStrings.Add(String.Format("{0}={1}", kv.Key, kv.Value));
      }
      return String.Join(", ", fieldAndValueStrings.ToArray());
    }

    /// <summary>
    /// C# proxy to firebase::storage::Metadata.
    /// </summary>
    internal MetadataInternal Internal { get; private set; }

    /// <summary>
    /// Get MetadataInternal from the specified StorageMetata.
    /// </summary>
    /// <param name="metadata">Metadata to query.</param>
    /// <returns>MetadataInternal if the specified instance is not null, null otherwise.</returns>
    internal static MetadataInternal GetMetadataInternal(StorageMetadata metadata) {
      return metadata != null ? metadata.Internal : null;
    }

    /// <summary>
    /// Build MetadataInternal from a MetadataChange.
    /// </summary>
    /// <param name="metadataChange">Metadata change to construct a MetadataInternal from.</param>
    /// <returns>MetadataInternal if the specified instance is not null, null otherwise.</returns>
    internal static MetadataInternal BuildMetadataInternal(MetadataChange metadataChange) {
      return GetMetadataInternal(MetadataChange.Build(metadataChange));
    }
  }
}
