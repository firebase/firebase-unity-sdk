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
using System.Globalization;
using Google.MiniJSON;

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
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0,
                                                              DateTimeKind.Utc);

    private string _bucket = "";
    private string _generation = "";
    private string _metadataGeneration = "";
    private string _name = "";
    private string _path = "";
    private long _sizeBytes = 0;
    private DateTime _creationTimeMillis = UnixEpoch;
    private DateTime _updatedTimeMillis = UnixEpoch;
    private string _md5Hash = "";
    private string _cacheControl = "";
    private string _contentDisposition = "";
    private string _contentEncoding = "";
    private string _contentLanguage = "";
    private string _contentType = "";
    private Dictionary<string, string> _customMetadata = new Dictionary<string, string>();
    private string _downloadTokens = "";
    private readonly StorageReference _storageReference;

    private bool _hasCacheControl;
    private bool _hasContentDisposition;
    private bool _hasContentEncoding;
    private bool _hasContentLanguage;
    private bool _hasContentType;
    private bool _hasCustomMetadata;

    /// <summary>
    ///   Creates a
    ///   <see cref="StorageMetadata" />
    ///   object to hold metadata for a
    ///   <see cref="StorageReference" />
    /// </summary>
    public StorageMetadata() {
      _storageReference = null;
    }

    internal StorageMetadata(StorageReference reference) {
      _storageReference = reference;
      if (reference != null) {
        _bucket = reference.Bucket ?? "";
        _path = (reference.Path ?? "").TrimStart('/');
        _name = reference.Name ?? "";
      }
    }

    internal StorageMetadata(StorageMetadata metadataToCopy)
        : this(metadataToCopy != null ? metadataToCopy._storageReference : null, metadataToCopy) {
    }

    internal StorageMetadata(StorageReference reference, StorageMetadata metadataToCopy) {
      _storageReference = reference ?? (metadataToCopy != null ? metadataToCopy._storageReference : null);
      if (metadataToCopy != null) {
        _bucket = metadataToCopy._bucket ?? "";
        _generation = metadataToCopy._generation ?? "";
        _metadataGeneration = metadataToCopy._metadataGeneration ?? "";
        _name = metadataToCopy._name ?? "";
        _path = metadataToCopy._path ?? "";
        _sizeBytes = metadataToCopy._sizeBytes;
        _creationTimeMillis = metadataToCopy._creationTimeMillis;
        _updatedTimeMillis = metadataToCopy._updatedTimeMillis;
        _md5Hash = metadataToCopy._md5Hash ?? "";
        // Null is preserved for the user-settable fields: a MetadataChange that sets a field to
        // null requests that the server clear it.
        _cacheControl = metadataToCopy._cacheControl;
        _contentDisposition = metadataToCopy._contentDisposition;
        _contentEncoding = metadataToCopy._contentEncoding;
        _contentLanguage = metadataToCopy._contentLanguage;
        _contentType = metadataToCopy._contentType;
        _customMetadata = metadataToCopy._customMetadata != null
            ? new Dictionary<string, string>(metadataToCopy._customMetadata)
            : new Dictionary<string, string>();
        _downloadTokens = metadataToCopy._downloadTokens ?? "";
        _hasCacheControl = metadataToCopy._hasCacheControl;
        _hasContentDisposition = metadataToCopy._hasContentDisposition;
        _hasContentEncoding = metadataToCopy._hasContentEncoding;
        _hasContentLanguage = metadataToCopy._hasContentLanguage;
        _hasContentType = metadataToCopy._hasContentType;
        _hasCustomMetadata = metadataToCopy._hasCustomMetadata;
      }
      if (_storageReference != null) {
        if (string.IsNullOrEmpty(_bucket)) _bucket = _storageReference.Bucket ?? "";
        if (string.IsNullOrEmpty(_path)) _path = (_storageReference.Path ?? "").TrimStart('/');
        if (string.IsNullOrEmpty(_name)) _name = _storageReference.Name ?? "";
      }
    }

    internal StorageMetadata(StorageReference reference, MetadataChange change)
        : this(reference, MetadataChange.Build(change)) {
    }

    /// <returns>
    ///   the content type of the
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public string ContentType {
      get { return _contentType; }
    }

    /// <returns>the keys for custom metadata.</returns>
    public IEnumerable<string> CustomMetadataKeys {
      get {
        return _customMetadata != null
            ? (IEnumerable<string>)new List<string>(_customMetadata.Keys)
            : new List<string>();
      }
    }

    /// <returns>
    ///   the path of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Path { get { return _path; } }

    /// <returns>
    ///   a simple name of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Name { get { return _name; } }

    /// <returns>
    ///   the owning Google Cloud Storage bucket for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string Bucket { get { return _bucket; } }

    /// <returns>
    ///   a version String indicating what version of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string Generation { get { return _generation; } }

    /// <returns>
    ///   a version String indicating the version of this
    ///   <see cref="StorageMetadata" />
    /// </returns>
    public string MetadataGeneration {
      get { return _metadataGeneration; }
    }

    /// <returns>
    ///   the time the
    ///   <see cref="StorageReference" />
    ///   was created.
    /// </returns>
    public DateTime CreationTimeMillis {
      get { return _creationTimeMillis; }
    }

    /// <returns>
    ///   the time the
    ///   <see cref="StorageReference" />
    ///   was last updated.
    /// </returns>
    public DateTime UpdatedTimeMillis {
      get { return _updatedTimeMillis; }
    }

    /// <returns>
    ///   the stored Size in bytes of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public long SizeBytes { get { return _sizeBytes; } }

    /// <returns>
    ///   the MD5Hash of the
    ///   <see cref="StorageReference" />
    ///   object
    /// </returns>
    public string Md5Hash { get { return _md5Hash; } }

    /// <returns>
    ///   the Cache Control setting of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string CacheControl { get { return _cacheControl; } }

    /// <returns>
    ///   the content disposition of the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentDisposition { get { return _contentDisposition; } }

    /// <returns>
    ///   the content encoding for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentEncoding { get { return _contentEncoding; } }

    /// <returns>
    ///   the content language for the
    ///   <see cref="StorageReference" />
    /// </returns>
    public string ContentLanguage { get { return _contentLanguage; } }

    /// <returns>
    ///   the associated
    ///   <see cref="StorageReference" />
    ///   for which this metadata belongs to.
    /// </returns>
    public StorageReference Reference { get { return _storageReference; } }

    /// <summary>
    ///   Returns custom metadata for a
    ///   <see cref="StorageReference" />
    /// </summary>
    /// <param name="key">The key for which the metadata should be returned</param>
    /// <returns>the metadata stored in the object the given key.</returns>
    public string GetCustomMetadata(string key) {
      if (_customMetadata == null || key == null) {
        return null;
      }
      string metadataValue;
      if (!_customMetadata.TryGetValue(key, out metadataValue)) {
        return null;
      }
      return metadataValue;
    }

    internal string DownloadTokens {
      get { return _downloadTokens; }
    }

    internal string GetFirstDownloadToken() {
      if (string.IsNullOrEmpty(_downloadTokens)) {
        return null;
      }
      string[] tokens = _downloadTokens.Split(',');
      for (int i = 0; i < tokens.Length; ++i) {
        if (!string.IsNullOrEmpty(tokens[i])) {
          return tokens[i];
        }
      }
      return null;
    }

    // Setting any of the following to null requests that the server clear the field; the value
    // is serialized as JSON null by ToJsonDict().
    internal void SetCacheControl(string value) {
      _cacheControl = value;
      _hasCacheControl = true;
    }

    internal void SetContentDisposition(string value) {
      _contentDisposition = value;
      _hasContentDisposition = true;
    }

    internal void SetContentEncoding(string value) {
      _contentEncoding = value;
      _hasContentEncoding = true;
    }

    internal void SetContentLanguage(string value) {
      _contentLanguage = value;
      _hasContentLanguage = true;
    }

    internal void SetContentType(string value) {
      _contentType = value;
      _hasContentType = true;
    }

    // A null value for a key requests that the server delete that custom metadata entry.
    internal void SetCustomMetadata(IDictionary<string, string> customMetadata) {
      _customMetadata = new Dictionary<string, string>();
      if (customMetadata != null) {
        foreach (var kv in customMetadata) {
          if (kv.Key != null) {
            _customMetadata[kv.Key] = kv.Value;
          }
        }
      }
      _hasCustomMetadata = true;
    }

    internal IDictionary<string, string> GetCustomMetadataCopy() {
      return _customMetadata != null
          ? new Dictionary<string, string>(_customMetadata)
          : new Dictionary<string, string>();
    }

    /// <summary>
    /// Build StorageMetadata from a MetadataChange.
    /// </summary>
    /// <param name="metadataChange">Metadata change to construct a StorageMetadata from.</param>
    /// <returns>StorageMetadata if the specified instance is not null, null otherwise.</returns>
    internal static StorageMetadata BuildMetadata(MetadataChange metadataChange) {
      return MetadataChange.Build(metadataChange);
    }

    internal static StorageMetadata FromJson(string json, StorageReference reference = null) {
      if (string.IsNullOrEmpty(json)) {
        return new StorageMetadata(reference);
      }
      var dict = Json.Deserialize(json) as Dictionary<string, object>;
      if (dict == null) {
        throw new StorageException(
            StorageException.ErrorUnknown, 0,
            "The server did not return a valid JSON response. Contact Firebase support if this issue persists.");
      }
      return FromJsonDict(dict, reference);
    }

    internal static StorageMetadata FromJsonDict(Dictionary<string, object> dict,
                                                 StorageReference reference = null) {
      var metadata = new StorageMetadata(reference);
      if (dict == null) {
        return metadata;
      }

      if (dict.TryGetValue("bucket", out var bucketObj) && bucketObj != null) {
        metadata._bucket = Convert.ToString(bucketObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("generation", out var generationObj) && generationObj != null) {
        metadata._generation = Convert.ToString(generationObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("metageneration", out var metaGenerationObj) && metaGenerationObj != null) {
        metadata._metadataGeneration =
            Convert.ToString(metaGenerationObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("name", out var nameObj) && nameObj != null) {
        string rawPath = Convert.ToString(nameObj, CultureInfo.InvariantCulture) ?? "";
        metadata._path = rawPath;
        string trimmed = rawPath.TrimEnd('/');
        int lastSlash = trimmed.LastIndexOf('/');
        metadata._name = lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
      }

      if (dict.TryGetValue("size", out var sizeObj) && sizeObj != null) {
        if (sizeObj is long l) {
          metadata._sizeBytes = l;
        } else if (sizeObj is int i) {
          metadata._sizeBytes = i;
        } else if (sizeObj is double d) {
          metadata._sizeBytes = (long)d;
        } else {
          string sizeStr = Convert.ToString(sizeObj, CultureInfo.InvariantCulture);
          if (long.TryParse(sizeStr, NumberStyles.Integer, CultureInfo.InvariantCulture,
                            out var parsedSize)) {
            metadata._sizeBytes = parsedSize;
          }
        }
      }

      if (dict.TryGetValue("timeCreated", out var timeCreatedObj) && timeCreatedObj != null) {
        string timeCreatedStr = Convert.ToString(timeCreatedObj, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(timeCreatedStr) &&
            DateTimeOffset.TryParse(
                timeCreatedStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var createdDto)) {
          metadata._creationTimeMillis = createdDto.UtcDateTime;
        }
      }

      if (dict.TryGetValue("updated", out var updatedObj) && updatedObj != null) {
        string updatedStr = Convert.ToString(updatedObj, CultureInfo.InvariantCulture);
        if (!string.IsNullOrEmpty(updatedStr) &&
            DateTimeOffset.TryParse(
                updatedStr, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var updatedDto)) {
          metadata._updatedTimeMillis = updatedDto.UtcDateTime;
        }
      }

      if (dict.TryGetValue("md5Hash", out var md5Obj) && md5Obj != null) {
        metadata._md5Hash = Convert.ToString(md5Obj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("cacheControl", out var cacheControlObj) && cacheControlObj != null) {
        metadata._cacheControl =
            Convert.ToString(cacheControlObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("contentDisposition", out var contentDispositionObj) &&
          contentDispositionObj != null) {
        metadata._contentDisposition =
            Convert.ToString(contentDispositionObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("contentEncoding", out var contentEncodingObj) &&
          contentEncodingObj != null) {
        metadata._contentEncoding =
            Convert.ToString(contentEncodingObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("contentLanguage", out var contentLanguageObj) &&
          contentLanguageObj != null) {
        metadata._contentLanguage =
            Convert.ToString(contentLanguageObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("contentType", out var contentTypeObj) && contentTypeObj != null) {
        metadata._contentType =
            Convert.ToString(contentTypeObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("downloadTokens", out var downloadTokensObj) &&
          downloadTokensObj != null) {
        metadata._downloadTokens =
            Convert.ToString(downloadTokensObj, CultureInfo.InvariantCulture) ?? "";
      }

      if (dict.TryGetValue("metadata", out var customMetaObj) &&
          customMetaObj is IDictionary<string, object> customMetaDict) {
        metadata._customMetadata = new Dictionary<string, string>();
        foreach (var kv in customMetaDict) {
          if (kv.Key != null) {
            metadata._customMetadata[kv.Key] = kv.Value != null
                ? Convert.ToString(kv.Value, CultureInfo.InvariantCulture) ?? ""
                : "";
          }
        }
      }

      return metadata;
    }

    internal Dictionary<string, object> ToJsonDict() {
      var dict = new Dictionary<string, object>();
      // Explicitly-set fields are always emitted, including null, which the REST API treats
      // as "clear this field" (JSON merge-patch semantics).
      if (_hasCacheControl || !string.IsNullOrEmpty(_cacheControl)) {
        dict["cacheControl"] = _cacheControl;
      }
      if (_hasContentDisposition || !string.IsNullOrEmpty(_contentDisposition)) {
        dict["contentDisposition"] = _contentDisposition;
      }
      if (_hasContentEncoding || !string.IsNullOrEmpty(_contentEncoding)) {
        dict["contentEncoding"] = _contentEncoding;
      }
      if (_hasContentLanguage || !string.IsNullOrEmpty(_contentLanguage)) {
        dict["contentLanguage"] = _contentLanguage;
      }
      if (_hasContentType || !string.IsNullOrEmpty(_contentType)) {
        dict["contentType"] = _contentType;
      }
      if (_hasCustomMetadata || (_customMetadata != null && _customMetadata.Count > 0)) {
        var customMetaDict = new Dictionary<string, object>();
        if (_customMetadata != null) {
          foreach (var kv in _customMetadata) {
            customMetaDict[kv.Key] = kv.Value;
          }
        }
        dict["metadata"] = customMetaDict;
      }
      return dict;
    }

    internal string ToJson() {
      return Json.Serialize(ToJsonDict());
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
        {"Reference", Reference != null ? Reference.Path : ""},
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
  }
}
