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

    /// <summary>
    /// Creates a new instance of the class.
    /// </summary>
    /// <param name="hasPendingWrites">Indicates whether this snapshot has pending writes.</param>
    /// <param name="isFromCache">Indicates whether this snapshot is from the cache.</param>
    public SnapshotMetadata(bool hasPendingWrites, bool isFromCache) {
      HasPendingWrites = hasPendingWrites;
      IsFromCache = isFromCache;
    }

    internal SnapshotMetadataProxy ConvertToProxy() {
      return new SnapshotMetadataProxy(HasPendingWrites, IsFromCache);
    }

    internal static SnapshotMetadata ConvertFromProxy(SnapshotMetadataProxy metadata) {
      return new SnapshotMetadata(metadata.has_pending_writes(), metadata.is_from_cache());
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      return HasPendingWrites.GetHashCode() * 31 + IsFromCache.GetHashCode() * 23;
    }

    /// <inheritdoc />
    public override bool Equals(object obj) {
      return Equals(obj as SnapshotMetadata);
    }

    /// <summary>
    /// Compares this snapshot metadata with another for equality.
    /// </summary>
    /// <param name="other">The snapshot metadata to compare this one with.</param>
    /// <returns><c>true</c> if this snapshot metadata is equal to <paramref name="other"/>;
    /// <c>false</c> otherwise.</returns>
    public bool Equals(SnapshotMetadata other) {
      return other != null && HasPendingWrites == other.HasPendingWrites &&
             IsFromCache == other.IsFromCache;
    }
  }
}
