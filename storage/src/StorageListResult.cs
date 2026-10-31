/*
 * Copyright 2026 Google LLC
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Firebase.Storage.Internal;
using Google.MiniJSON;

namespace Firebase.Storage {
  /// <summary>
  /// Represents the result of a List operation.
  /// </summary>
  public sealed class StorageListResult {
    private readonly List<StorageReference> prefixes;
    private readonly List<StorageReference> items;
    private readonly string nextPageToken;

    internal StorageListResult(IEnumerable<StorageReference> prefixes,
                               IEnumerable<StorageReference> items,
                               string nextPageToken) {
      this.prefixes = prefixes != null
          ? new List<StorageReference>(prefixes)
          : new List<StorageReference>();
      this.items = items != null
          ? new List<StorageReference>(items)
          : new List<StorageReference>();
      this.nextPageToken = nextPageToken ?? string.Empty;
    }

    internal static StorageListResult FromJson(string json, FirebaseStorage storage, string bucket) {
      if (string.IsNullOrEmpty(json)) {
        return new StorageListResult(null, null, string.Empty);
      }
      var dict = Json.Deserialize(json) as Dictionary<string, object>;
      if (dict == null) {
        throw new StorageException(
            StorageException.ErrorUnknown, 0,
            "The server did not return a valid JSON response. Contact Firebase support if this issue persists.");
      }
      return FromJsonDict(dict, storage, bucket);
    }

    internal static StorageListResult FromJsonDict(Dictionary<string, object> dict,
                                                   FirebaseStorage storage,
                                                   string bucket) {
      var parsedPrefixes = new List<StorageReference>();
      var parsedItems = new List<StorageReference>();
      string parsedNextPageToken = string.Empty;

      if (dict != null) {
        if (dict.TryGetValue("prefixes", out var prefixesObj) && prefixesObj is IList prefixesList) {
          foreach (var prefixEntry in prefixesList) {
            if (prefixEntry == null) continue;
            string prefixStr = Convert.ToString(prefixEntry, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(prefixStr)) {
              parsedPrefixes.Add(
                  new StorageReference(storage, new StoragePath(storage, bucket, prefixStr)));
            }
          }
        }

        if (dict.TryGetValue("items", out var itemsObj) && itemsObj is IList itemsList) {
          foreach (var itemEntry in itemsList) {
            if (itemEntry == null) continue;
            if (itemEntry is string itemStr) {
              if (!string.IsNullOrEmpty(itemStr)) {
                parsedItems.Add(
                    new StorageReference(storage, new StoragePath(storage, bucket, itemStr)));
              }
            } else if (itemEntry is IDictionary<string, object> itemDict) {
              if (itemDict.TryGetValue("name", out var nameObj) && nameObj != null) {
                string itemName = Convert.ToString(nameObj, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(itemName)) {
                  parsedItems.Add(
                      new StorageReference(storage, new StoragePath(storage, bucket, itemName)));
                }
              }
            }
          }
        }

        if (dict.TryGetValue("nextPageToken", out var nextPageTokenObj) &&
            nextPageTokenObj != null) {
          parsedNextPageToken =
              Convert.ToString(nextPageTokenObj, CultureInfo.InvariantCulture) ?? string.Empty;
        }
      }

      return new StorageListResult(parsedPrefixes, parsedItems, parsedNextPageToken);
    }

    /// <summary>
    /// Gets the list of prefixes (folders) returned by the List operation.
    /// </summary>
    public IReadOnlyList<StorageReference> Prefixes { get { return prefixes; } }

    /// <summary>
    /// Gets the list of items (files) returned by the List operation.
    /// </summary>
    public IReadOnlyList<StorageReference> Items { get { return items; } }

    /// <summary>
    /// Gets a page token that can be used to resume the List operation, or an empty string if there are no more results.
    /// </summary>
    public string NextPageToken { get { return nextPageToken; } }

    internal string PageToken { get { return nextPageToken; } }
  }
}
