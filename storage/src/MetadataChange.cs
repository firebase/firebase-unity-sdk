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
  /// MetadataChange is a set of new metadata values used during object upload
  /// or when modifying the metadata of an object.
  /// A MetadataChange can be created from an existing <see cref="StorageMetadata"/>
  /// or it can be created from scratch.
  /// </summary>
  public class MetadataChange {
    private readonly StorageMetadata metadata;

    /// <summary>Creates an empty set of metadata.</summary>
    public MetadataChange() {
      metadata = new StorageMetadata();
    }

    /// <summary>Used to create a modified version of the original set of metadata.</summary>
    /// <param name="original">The source of the metadata to build from.</param>
    public MetadataChange(StorageMetadata original) {
      metadata = new StorageMetadata(original);
    }

    /// <summary>
    /// Build StorageMetadata from this class.
    /// </summary>
    internal StorageMetadata Build() {
      return new StorageMetadata(metadata);
    }

    /// <summary>
    /// Build StorageMetadata from the specified class.
    /// </summary>
    /// <param name="metadataChange"></param>
    /// <returns>StorageMetadata instance if the specified MetadataChange is not null, null
    /// otherwise.</returns>
    internal static StorageMetadata Build(MetadataChange metadataChange) {
      return metadataChange != null ? metadataChange.Build() : null;
    }

    /// <summary>
    ///   Gets or sets the content language for the
    ///   <see cref="StorageReference" />.
    ///   This must be an
    ///   <see href="https://www.loc.gov/standards/iso639-2/php/code_list.php">ISO 639-1</see>
    ///   two-letter language code. E.g. "zh", "es", "en".
    /// </summary>
    public string ContentLanguage {
      get { return metadata.ContentLanguage; }
      set { metadata.Internal.ContentLanguage = value; }
    }

    /// <summary>
    ///   Gets or sets the content encoding for the
    ///   <see cref="StorageReference" />.
    /// </summary>
    public string ContentEncoding {
      get { return metadata.ContentEncoding; }
      set { metadata.Internal.ContentEncoding = value; }
    }

    /// <summary>
    ///   Gets or sets the content disposition for the
    ///   <see cref="StorageReference" />.
    /// </summary>
    public string ContentDisposition {
      get { return metadata.ContentDisposition; }
      set { metadata.Internal.ContentDisposition = value; }
    }

    /// <summary>
    ///   Gets or sets the Cache Control for the
    ///   <see cref="StorageReference" />.
    /// </summary>
    public string CacheControl {
      get { return metadata.CacheControl; }
      set { metadata.Internal.CacheControl = value; }
    }

    /// <summary>
    /// Gets or sets custom metadata.
    /// To use this in an object initalizer, you may use the form:
    /// var change = new MetadataChange
    ///           {
    ///               CustomMetadata = new Dictionary<string, string> {
    ///                 {"customkey1", "customValue1"},
    ///                 {"customkey2", "customValue2"}
    ///               }
    ///           }
    /// </summary>
    public IDictionary<string, string> CustomMetadata {
     set {
       var metadataMap = new StringStringMap();
       foreach (var kv in value) metadataMap[kv.Key] = kv.Value;
       metadata.Internal.CustomMetadata = metadataMap;
     }

     get {
       var metadataMap = new Dictionary<string, string>();
       foreach (var kv in metadata.Internal.CustomMetadata) metadataMap[kv.Key] = kv.Value;
       return metadataMap;
     }
    }

    /// <summary>Sets custom metadata</summary>
    /// <param name="key">the key of the new value</param>
    /// <param name="value">the value to set.</param>
    private void SetCustomMetadata(string key, string value) {
      var modifiedMetadata = CustomMetadata;
      modifiedMetadata[key] = value;
      CustomMetadata = modifiedMetadata;
    }

    /// <summary>
    ///   Gets or sets the Content Type of this associated
    ///   <see cref="StorageReference" />.
    /// </summary>
    public string ContentType {
      get { return metadata.ContentType; }
      set { metadata.Internal.ContentType = value; }
    }
  }
}
