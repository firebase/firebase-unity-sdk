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
using System.Collections.Generic;
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  /// Represents the result of a List operation.
  /// </summary>
  public sealed class StorageListResult {
    private readonly StorageListResultInternal internalResult;
    private readonly List<StorageReference> prefixes;
    private readonly List<StorageReference> items;

    internal StorageListResult(FirebaseStorage storage, StorageListResultInternal internalResult) {
      this.internalResult = internalResult;

      prefixes = new List<StorageReference>();
      var prefixesCount = internalResult.prefixes_count();
      for (uint i = 0; i < prefixesCount; i++) {
        prefixes.Add(new StorageReference(storage, internalResult.prefixes_get(i)));
      }

      items = new List<StorageReference>();
      var itemsCount = internalResult.items_count();
      for (uint i = 0; i < itemsCount; i++) {
        items.Add(new StorageReference(storage, internalResult.items_get(i)));
      }
    }

    /// <summary>
    /// Gets the list of prefixes (folders) returned by the List operation.
    /// </summary>
    public IEnumerable<StorageReference> Prefixes { get { return prefixes; } }

    /// <summary>
    /// Gets the list of items (files) returned by the List operation.
    /// </summary>
    public IEnumerable<StorageReference> Items { get { return items; } }

    /// <summary>
    /// Gets a page token that can be used to resume the List operation, or an empty string if there are no more results.
    /// </summary>
    public string NextPageToken { get { return internalResult.next_page_token(); } }
  }
}
