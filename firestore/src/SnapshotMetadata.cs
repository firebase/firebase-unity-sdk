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

namespace Firebase.Firestore {
  /// <summary>
  /// Metadata about a snapshot, describing the state of the snapshot.
  /// </summary>
  /// <remarks>
  /// Note for EAP: <c>SnapshotMetadata</c> is not yet available on on QuerySnapshots but will be
  /// added in a later release.
  /// </remarks>
  public sealed class SnapshotMetadata {
    /// <summary>
    /// <c>true</c> if the snapshot contains the result of local writes (e.g. <c>SetAsync</c> or
    /// <c>UpdateAsync</c> calls) that have not yet been committed to the backend.  If your listener
    /// has opted into metadata updates (via <c>MetadataChanges.Include</c>) you will receive
    /// another snapshot with <c>HasPendingWrites</c> equal to <c>false</c> once the writes have
    /// been committed to the backend.
    /// </summary>
    public bool HasPendingWrites { get; private set; }

    /// <summary>
    /// <c>true</c> if the snapshot was created from cached data rather than guaranteed up-to-date
    /// server data. If your listener has opted into metadata updates (via
    /// <c>MetadataChanges.Include</c>) you will receive another snapshot with <c>IsFromCache</c>
    /// equal to <c>false</c> once the client has received up-to-date data from the backend.
    /// </summary>
    public bool IsFromCache { get; private set; }

    internal SnapshotMetadata(bool hasPendingWrites, bool isFromCache) {
      HasPendingWrites = hasPendingWrites;
      IsFromCache = isFromCache;
    }

    internal SnapshotMetadataProxy ConvertToProxy() {
      return new SnapshotMetadataProxy(HasPendingWrites, IsFromCache);
    }

    internal static SnapshotMetadata ConvertFromProxy(SnapshotMetadataProxy metadata) {
      return new SnapshotMetadata(metadata.has_pending_writes(), metadata.is_from_cache());
    }
  }
}
