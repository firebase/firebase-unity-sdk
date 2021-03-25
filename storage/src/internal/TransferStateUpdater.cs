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
  /// Creates a MonitorController and updates a progress handler with UploadState as an upload
  /// proceeds.
  /// </summary>
  internal class TransferStateUpdater<T> {
    // Handler which is updated with the upload state as the upload proceeds.
    private IProgress<T> handler;
    // Backing store for ProgressState.
    private TransferState transferState;

    /// <summary>
    /// If a progressHandler is specified:
    /// * Create a MonitorController to monitor an upload.
    /// * Register an event to monitor the transfer providing updates to progressHandler.
    /// </summary>
    /// <param name="storageReference">Storage reference we're uploading to.</param>
    /// <param name="progressHandler">Handler to be notified of the upload state or null to
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
      MonitorController = MonitorControllerInternal.Create(storageReference.Internal);
      if (handler != null) {
        MonitorController.Progress += (sender, eventArgs) => {
          // Propagate forward progress to the transfer state.
          var bytesTransferred = MonitorController.BytesTransferred;
          var totalByteCount = MonitorController.TotalByteCount;
          if (bytesTransferred > transferState.BytesTransferred) {
            transferState.BytesTransferred = bytesTransferred;
          }
          if (totalByteCount > transferState.TotalByteCount) {
            transferState.TotalByteCount = totalByteCount;
          }
          transferState.UploadSessionUri = null;  // TODO(smiles): b/68320317
          handler.Report(ProgressState);
        };
      }
    }

    /// <summary>
    /// Report metadata written to remote storage to the UploadState and report progress.
    /// </summary>
    public void SetMetadata(StorageMetadata metadata) {
      if (handler != null) {
        transferState.Metadata = metadata;
        handler.Report(ProgressState);
      }
    }

    /// <summary>
    /// Get the transfer state object instanced by this class.
    /// This will not be created if this object was created with no progress handler.
    /// </summary>
    public T ProgressState { get; private set; }

    /// <summary>
    /// MonitorControllerInternal instance used to control the transfer.
    /// </summary>
    public MonitorControllerInternal MonitorController { get; private set; }
  }
}
