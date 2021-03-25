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

using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  /// DownloadState contains information for a download in progress.
  /// It is sent to a <see cref="StorageProgress" /> handler during the operation.
  /// </summary>
  public sealed class DownloadState {
    /// Construct an instance associated with the specified storage reference.
    /// <param name="reference">Storage reference associated with this transfer.</param>
    /// <param name="totalByteCount">Total number of bytes to transfer or -1 if the size of the
    /// download is unknown.</param>
    internal DownloadState(StorageReference reference, long totalByteCount) {
      State = new TransferState(reference) { TotalByteCount = totalByteCount };
    }

    /// <summary>
    /// The total number of bytes downloaded so far.
    /// </summary>
    /// <returns>the total number of bytes downloaded so far.</returns>
    public long BytesTransferred { get { return State.BytesTransferred; } }

    /// <summary>
    /// The total number of bytes to download.
    /// </summary>
    /// <returns>The total number of bytes to download or -1 if the size is unknown.</returns>
    public long TotalByteCount { get { return State.TotalByteCount; } }

    /// <summary>
    ///   Returns the <see cref="StorageReference" /> associated with this download.
    /// </summary>
    /// <returns>the <see cref="StorageReference" /> associated with this download.</returns>
    public StorageReference Reference { get { return State.Reference; } }

    /// <summary>
    /// Backing store for DownloadState properties.
    /// </summary>
    internal TransferState State { get; private set; }
  }
}
