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
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  ///   UploadState contains information for an upload in progress.
  /// </summary>
  public class UploadState {
    /// Construct an instance associated with the specified storage reference.
    /// <param name="reference">Storage reference associated with this transfer.</param>
    /// <param name="totalByteCount">Total number of bytes to transfer or -1 if the size of the
    /// upload is unknown.</param>
   internal UploadState(StorageReference reference, long totalByteCount) {
     State = new TransferState(reference) { TotalByteCount = totalByteCount };
    }

    /// <summary>
    /// The total number of bytes uploaded so far.
    /// </summary>
    /// <returns>the total number of bytes uploaded so far.</returns>
    public long BytesTransferred { get { return State.BytesTransferred; } }

    /// <summary>
    /// The total number of bytes to upload.
    /// </summary>
    /// <returns>the total number of bytes to upload or -1 if the size is unknown.</returns>
    public long TotalByteCount { get { return State.TotalByteCount; } }

    /// <returns>
    ///   the session Uri, valid for approximately one week, which can be used to
    ///   resume an upload later by passing this value into an upload.
    /// </returns>
    public Uri UploadSessionUri { get { return State.UploadSessionUri; } }

    /// <returns>
    ///   the metadata for the object.  After uploading, this will return the resulting
    ///   final Metadata which will include the upload URL.
    /// </returns>
    public StorageMetadata Metadata { get { return State.Metadata; } }

    /// <summary>
    ///   Returns the <see cref="StorageReference" /> associated with this upload.
    /// </summary>
    /// <returns>the <see cref="StorageReference" /> associated with this upload.</returns>
    public StorageReference Reference { get { return State.Reference; } }

    /// <summary>
    /// Backing store for UploadState properties.
    /// </summary>
    internal TransferState State { get; private set; }
  }
}
