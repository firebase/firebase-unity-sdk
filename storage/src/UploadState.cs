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

namespace Firebase.Storage {
  /// <summary>
  ///   UploadState contains information for an upload in progress.
  /// </summary>
  public class UploadState {
    /// Construct an instance associated with the specified storage reference.
    /// <param name="reference">Storage reference associated with this transfer.</param>
    /// <param name="totalByteCount">Total number of bytes to transfer or -1 if the size of the
    /// upload is unknown.</param>
    /// <param name="uploadSessionUri">Optional resumable upload session URI.</param>
    internal UploadState(StorageReference reference, long totalByteCount,
                         Uri uploadSessionUri = null) {
      Reference = reference;
      TotalByteCount = totalByteCount;
      UploadSessionUri = uploadSessionUri;
    }

    /// <summary>
    /// The total number of bytes uploaded so far.
    /// </summary>
    /// <returns>the total number of bytes uploaded so far.</returns>
    public long BytesTransferred { get; internal set; }

    /// <summary>
    /// The total number of bytes to upload.
    /// </summary>
    /// <returns>the total number of bytes to upload or -1 if the size is unknown.</returns>
    public long TotalByteCount { get; internal set; }

    /// <returns>
    ///   the session Uri, valid for approximately one week, which can be used to
    ///   resume an upload later by passing this value into an upload.
    /// </returns>
    public Uri UploadSessionUri { get; internal set; }

    /// <returns>
    ///   the metadata for the object.  After uploading, this will return the resulting
    ///   final Metadata which will include the upload URL.
    /// </returns>
    public StorageMetadata Metadata { get; internal set; }

    /// <summary>
    ///   Returns the <see cref="StorageReference" /> associated with this upload.
    /// </summary>
    /// <returns>the <see cref="StorageReference" /> associated with this upload.</returns>
    public StorageReference Reference { get; }
  }
}
