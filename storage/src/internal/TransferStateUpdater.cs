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
using Firebase.Storage;

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Updates a progress handler with TransferState (UploadState or DownloadState) as a transfer
  /// proceeds.
  /// </summary>
  internal class TransferStateUpdater<T> {
    // Handler which is updated with the transfer state as the transfer proceeds.
    private readonly IProgress<T> handler;
    // Backing store for ProgressState.
    private readonly TransferState transferState;

    /// <summary>
    /// Construct a TransferStateUpdater to report progress updates to progressHandler.
    /// </summary>
    /// <param name="storageReference">Storage reference being transferred.</param>
    /// <param name="progressHandler">Handler to be notified of the transfer state or null to
    /// receive no updates.</param>
    /// <param name="progressState">State passed to progressHandler to report progress.</param>
    /// <param name="progressStateBackingStore">Instance of a TransferState retrieved from
    /// progressState.</param>
    public TransferStateUpdater(StorageReference storageReference,
                                IProgress<T> progressHandler,
                                T progressState, TransferState progressStateBackingStore) {
      handler = progressHandler;
      ProgressState = progressState;
      transferState = progressStateBackingStore;
      if (transferState != null && transferState.Reference == null) {
        transferState.Reference = storageReference;
      }
    }

    /// <summary>
    /// Propagate forward progress to the transfer state and notify the progress handler.
    /// </summary>
    public void ReportProgress(long bytesTransferred, long totalByteCount,
                               Uri uploadSessionUri = null) {
      if (transferState != null) {
        if (bytesTransferred > transferState.BytesTransferred) {
          transferState.BytesTransferred = bytesTransferred;
        }
        if (totalByteCount > transferState.TotalByteCount) {
          transferState.TotalByteCount = totalByteCount;
        }
        if (uploadSessionUri != null) {
          transferState.UploadSessionUri = uploadSessionUri;
        }
      }
      if (handler != null) {
        ExceptionAggregator.Wrap(() => { handler.Report(ProgressState); });
      }
    }

    /// <summary>
    /// Alias for ReportProgress.
    /// </summary>
    public void UpdateProgress(long bytesTransferred, long totalByteCount,
                               Uri uploadSessionUri = null) {
      ReportProgress(bytesTransferred, totalByteCount, uploadSessionUri);
    }

    /// <summary>
    /// Set the resumable upload session URI on the backing TransferState.
    /// </summary>
    public void SetUploadSessionUri(Uri uploadSessionUri) {
      if (transferState != null) {
        transferState.UploadSessionUri = uploadSessionUri;
      }
    }

    /// <summary>
    /// Report metadata written to remote storage to the UploadState and report progress.
    /// </summary>
    public void SetMetadata(StorageMetadata metadata) {
      if (transferState != null) {
        transferState.Metadata = metadata;
      }
      if (handler != null) {
        ExceptionAggregator.Wrap(() => { handler.Report(ProgressState); });
      }
    }

    /// <summary>
    /// Get the transfer state object instanced by this class.
    /// </summary>
    public T ProgressState { get; private set; }

    /// <summary>
    /// Backing TransferState instance.
    /// </summary>
    public TransferState TransferState {
      get { return transferState; }
    }
  }
}
