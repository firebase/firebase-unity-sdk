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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>Represents a reference to a Google Cloud Storage object.</summary>
  /// <remarks>
  ///   Represents a reference to a Google Cloud Storage object. Developers can upload and download
  ///   objects, get/set object metadata, and delete an object at a specified path.
  ///   (see <a href="https://cloud.google.com/storage/">Google Cloud Storage</a>)
  /// </remarks>
  public sealed class StorageReference {
    // Storage object this reference was created from.
    private readonly FirebaseStorage firebaseStorage;
    // Normalized bucket and object path for this reference.
    private readonly StoragePath path;

    /// <summary>
    /// Construct a StorageReference associated with the specified FirebaseStorage and StoragePath.
    /// </summary>
    internal StorageReference(FirebaseStorage storage, StoragePath storagePath) {
      firebaseStorage = Preconditions.CheckNotNull(storage, nameof(storage));
      path = Preconditions.CheckNotNull(storagePath, nameof(storagePath));
      Logger = new ModuleLogger(parentLogger: firebaseStorage.Logger) {
        Tag = String.Format("StorageReference {0}", ToString())
      };
    }

    /// <summary>
    /// Logger for this reference.
    /// </summary>
    internal ModuleLogger Logger { get; private set; }

    /// <summary>
    /// Internal StoragePath backing this reference.
    /// </summary>
    internal StoragePath InternalPath => path;

    /// <summary>
    ///   Returns a new instance of
    ///   <see cref="StorageReference" />
    ///   pointing to the parent location or null
    ///   if this instance references the root location.
    ///   For example:
    ///   <pre>
    ///     <c>
    ///       path = foo/bar/baz   parent = foo/bar
    ///       path = foo           parent = (root)
    ///       path = (root)        parent = (null)
    ///     </c>
    ///   </pre>
    /// </summary>
    /// <returns>
    ///   the parent
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public StorageReference Parent =>
        path.GetParent() is StoragePath parentPath
            ? new StorageReference(firebaseStorage, parentPath)
            : null;

    /// <summary>
    ///   Returns a new instance of
    ///   <see cref="StorageReference" />
    ///   pointing to the root location.
    /// </summary>
    /// <returns>
    ///   the root
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public StorageReference Root => new StorageReference(firebaseStorage, path.GetRoot());

    /// <summary>Returns the short name of this object.</summary>
    /// <returns>the name.</returns>
    public string Name => path.Name;

    /// <summary>
    ///   Returns the full path to this object, not including the Google Cloud Storage bucket.
    /// </summary>
    /// <returns>the path.</returns>
    public string Path => path.FullPath;

    /// <summary>Return the Google Cloud Storage bucket that holds this object.</summary>
    /// <returns>the bucket.</returns>
    public string Bucket => path.Bucket;

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseStorage" />
    ///   service which created this reference.
    /// </summary>
    /// <returns>
    ///   The
    ///   <see cref="FirebaseStorage" />
    ///   service.
    /// </returns>
    public FirebaseStorage Storage => firebaseStorage;

    /// <summary>
    ///   Returns a new instance of
    ///   <see cref="StorageReference" />
    ///   pointing to a child location of the current
    ///   reference. All leading and trailing slashes will be removed, and consecutive slashes will
    ///   be compressed to single slashes. For example:
    ///   <pre>
    ///     <c>
    ///       child = /foo/bar     path = foo/bar
    ///       child = foo/bar/     path = foo/bar
    ///       child = foo///bar    path = foo/bar
    ///     </c>
    ///   </pre>
    /// </summary>
    /// <param name="pathString">The relative path from this reference.</param>
    /// <returns>
    ///   the child
    ///   <see cref="StorageReference" />
    ///   .
    /// </returns>
    public StorageReference Child(string pathString) {
      return new StorageReference(firebaseStorage, path.GetChild(pathString));
    }

    /// <summary>
    ///   Uploads byte data to this
    ///   <see cref="StorageReference" />
    ///   .
    ///   This is not recommended for large files. Instead upload a file via
    ///   <see>
    ///     <cref>PutFile</cref>
    ///   </see>
    ///   or a Stream via
    ///   <see>
    ///     <cref>PutStream</cref>
    ///   </see>
    ///   .
    /// </summary>
    /// <param name="bytes">The byte[] to upload.</param>
    /// <param name="customMetadata">
    ///   <see cref="MetadataChange" />
    ///   containing additional information (MIME type, etc.)
    ///   about the object being uploaded.
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <param name="previousSessionUri">A Uri previously obtained by
    ///  <see cref="UploadState.UploadSessionUri"/> that can be used to resume
    ///  a previously interrupted upload.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the upload.
    /// </returns>
    public Task<StorageMetadata> PutBytesAsync(
        byte[] bytes,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default(CancellationToken),
        Uri previousSessionUri = null) {
      return ResumableUploadTask.PutBytesAsync(
          this, bytes, customMetadata, progressHandler, cancelToken, previousSessionUri, path);
    }

    /// <summary>
    ///   Uploads from a content URI to this
    ///   <see cref="StorageReference" />
    ///   .
    /// </summary>
    /// <param name="filePath">
    ///   The source of the upload.
    ///   This should be a file system URI representing the path the object
    ///   should be uploaded from.
    /// </param>
    /// <param name="customMetadata">
    ///   <see cref="MetadataChange" />
    ///   containing additional information (MIME
    ///   type, etc.) about the object being uploaded.
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <param name="previousSessionUri">A Uri previously obtained by
    ///  <see cref="UploadState.UploadSessionUri"/> that can be used to resume
    ///  a previously interrupted upload.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the upload.
    /// </returns>
    public Task<StorageMetadata> PutFileAsync(
        string filePath,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default(CancellationToken),
        Uri previousSessionUri = null) {
      return ResumableUploadTask.PutFileAsync(
          this, filePath, customMetadata, progressHandler, cancelToken, previousSessionUri, path);
    }

    /// <summary>
    ///   Uploads a stream of data to this
    ///   <see cref="StorageReference" />
    ///   .
    ///   The stream will remain open at the end of the upload.
    /// </summary>
    /// <param name="stream">
    ///   The
    ///   <see cref="Stream" />
    ///   to upload.
    /// </param>
    /// <param name="customMetadata">
    ///   <see cref="MetadataChange" />
    ///   containing additional information (MIME type, etc.)
    ///   about the object being uploaded.
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <param name="previousSessionUri">A Uri previously obtained by
    ///  <see cref="UploadState.UploadSessionUri"/> that can be used to resume
    ///  a previously interrupted upload.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the upload.
    /// </returns>
    public Task<StorageMetadata> PutStreamAsync(
        Stream stream,
        MetadataChange customMetadata = null,
        IProgress<UploadState> progressHandler = null,
        CancellationToken cancelToken = default(CancellationToken),
        Uri previousSessionUri = null) {
      return ResumableUploadTask.PutStreamAsync(
          this, stream, customMetadata, progressHandler, cancelToken, previousSessionUri, path);
    }

    /// <summary>
    ///   Retrieves metadata associated with an object at this
    ///   <see cref="StorageReference" />
    ///   .
    /// </summary>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task<StorageMetadata> GetMetadataAsync() {
      return StorageHttpClient.GetMetadataAsync(this, path);
    }

    /// <summary>
    ///   Retrieves a list of prefixes and items under this
    ///   <see cref="StorageReference" />
    ///   .
    /// </summary>
    /// <param name="maxResults">The maximum number of items to return.</param>
    /// <param name="pageToken">A page token to resume from.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task<StorageListResult> ListAsync(int maxResults = 1000, string pageToken = null) {
      return StorageHttpClient.ListAsync(this, path, maxResults, pageToken);
    }

    internal async Task<StorageListResult> ListAllAsync() {
      var prefixes = new List<StorageReference>();
      var items = new List<StorageReference>();
      string pageToken = null;
      do {
        StorageListResult page = await ListAsync(1000, pageToken).ConfigureAwait(false);
        if (page?.Prefixes != null) {
          prefixes.AddRange(page.Prefixes);
        }
        if (page?.Items != null) {
          items.AddRange(page.Items);
        }
        pageToken = page?.NextPageToken;
      } while (!string.IsNullOrEmpty(pageToken));
      return new StorageListResult(prefixes, items, string.Empty);
    }

    /// <summary>
    ///   Retrieves a long lived download URL with a revokable token.
    /// </summary>
    /// <remarks>
    ///   Retrieves a long lived download URL with a revokable token.
    ///   This can be used to share the file with others, but can be revoked by a developer
    ///   in the Firebase Console if desired.
    /// </remarks>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task<Uri> GetDownloadUrlAsync() {
      return StorageHttpClient.GetDownloadUrlAsync(this, path);
    }

    /// <summary>
    ///   Updates the metadata associated with this
    ///   <see cref="StorageReference" />
    ///   .
    /// </summary>
    /// <param name="metadata">
    ///   A
    ///   <see cref="MetadataChange" />
    ///   object with the metadata to update.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="System.Threading.Tasks.Task" />
    ///   that will return the final
    ///   <see cref="StorageMetadata" />
    ///   once the operation
    ///   is complete.
    /// </returns>
    public Task<StorageMetadata> UpdateMetadataAsync(MetadataChange metadata) {
      Preconditions.CheckNotNull(metadata);
      return StorageHttpClient.UpdateMetadataAsync(this, path, metadata);
    }

    /// <summary>
    ///   Downloads the object from this
    ///   <see cref="StorageReference" />
    ///   A byte array will be allocated large enough to hold the entire file in memory.
    ///   Therefore, using this method will impact memory usage of your process. If you are
    ///   downloading many large files,
    ///   <see>
    ///     <cref>GetStream(StreamDownloadTask.StreamProcessor)</cref>
    ///   </see>
    ///   may be a better option.
    /// </summary>
    /// <param name="maxDownloadSizeBytes">
    ///   The maximum allowed size in bytes that will be allocated.
    ///   Set this parameter to prevent out of memory conditions from
    ///   occurring. If the download exceeds this limit, the task will
    ///   fail and an
    ///   <see cref="System.IndexOutOfRangeException" />
    ///   will be returned.
    /// </param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task<byte[]> GetBytesAsync(long maxDownloadSizeBytes) {
      return GetBytesAsync(maxDownloadSizeBytes, progressHandler: null,
                           cancelToken: default(CancellationToken));
    }

    /// <summary>
    ///   Downloads the object from this
    ///   <see cref="StorageReference" />
    ///   A byte array will be allocated large enough to hold the entire file in memory.
    ///   Therefore, using this method will impact memory usage of your process. If you are
    ///   downloading many large files,
    ///   <see>
    ///     <cref>GetStream(StreamDownloadTask.StreamProcessor)</cref>
    ///   </see>
    ///   may be a better option.
    /// </summary>
    /// <param name="maxDownloadSizeBytes">
    ///   The maximum allowed size in bytes that will be allocated.
    ///   Set this parameter to prevent out of memory conditions from
    ///   occurring. If the download exceeds this limit, the task will
    ///   fail and an
    ///   <see cref="System.IndexOutOfRangeException" />
    ///   will be returned.
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task<byte[]> GetBytesAsync(long maxDownloadSizeBytes,
                                      IProgress<DownloadState> progressHandler,
                                      CancellationToken cancelToken = default(CancellationToken)) {
      return ResumableDownloadTask.GetBytesAsync(
          this, maxDownloadSizeBytes, progressHandler, cancelToken, path);
    }

    /// <summary>
    ///   Downloads the object at this
    ///   <see cref="StorageReference" />
    ///   to a specified system
    ///   filepath.
    /// </summary>
    /// <param name="destinationFilePath">
    ///   A file system URI representing the path the object should be
    ///   downloaded to.
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task GetFileAsync(string destinationFilePath,
                             IProgress<DownloadState> progressHandler = null,
                             CancellationToken cancelToken = default(CancellationToken)) {
      return ResumableDownloadTask.GetFileAsync(
          this, destinationFilePath, progressHandler, cancelToken, path);
    }

    /// <summary>
    ///   Downloads the object at this
    ///   <see cref="StorageReference" />
    ///   via a
    ///   <see cref="Stream" />
    ///   .
    ///   The resulting InputStream should be not be accessed on the main thread
    ///   because calling into it may block the calling thread.
    /// </summary>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation.
    /// </returns>
    public Task<Stream> GetStreamAsync() {
      return ResumableDownloadTask.GetStreamAsync(this, default(CancellationToken), path);
    }

    /// <summary>
    ///   Downloads the object at this
    ///   <see cref="StorageReference" />
    ///   via a
    ///   <see cref="Stream" />
    ///   .
    /// </summary>
    /// <param name="streamProcessor">
    ///   A delegate
    ///   that is responsible for
    ///   reading data from the
    ///   <see cref="Stream" />
    ///   .
    ///   The delegate
    ///   is called on a background
    ///   thread and exceptions thrown from this object will be returned as
    ///   a failure to the Task
    /// </param>
    /// <param name="progressHandler">
    ///  usually an instance of <see cref="StorageProgress"/> that will
    ///  receive periodic updates during the operation. This value can
    ///  be null.</param>
    /// <param name="cancelToken">A CancellationToken to control the operation
    ///  and possibly later cancel it.  This value may be CancellationToken.None
    ///  to indicate no value.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task GetStreamAsync(Action<Stream> streamProcessor,
                               IProgress<DownloadState> progressHandler = null,
                               CancellationToken cancelToken = default(CancellationToken)) {
      return ResumableDownloadTask.GetStreamAsync(
          this, streamProcessor, progressHandler, cancelToken, path);
    }

    /// <summary>
    ///   Deletes the object at this
    ///   <see cref="StorageReference" />
    ///   .
    /// </summary>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   which can be used to monitor the operation and obtain the result.
    /// </returns>
    public Task DeleteAsync() {
      return StorageHttpClient.DeleteAsync(this, path);
    }

    /// <returns>
    ///   This object in URI form, which can then be shared and passed into
    ///   <see cref="FirebaseStorage.GetReferenceFromUrl" />
    ///   .
    /// </returns>
    public override string ToString() {
      return String.Format("gs://{0}{1}", Bucket, Path);
    }

    /// <summary>
    /// Compares two storage reference URIs.
    /// </summary>
    /// <returns>true if two references point to the same path, false otherwise.</returns>
    public override bool Equals(object other) {
      if (!(other is StorageReference)) {
        return false;
      }
      var otherStorage = (StorageReference) other;
      return otherStorage.ToString().Equals(ToString());
    }

    /// <summary>
    /// Create a hash of the URI string used by this reference.
    /// </summary>
    /// <returns>Hash of this reference's URI.</returns>
    public override int GetHashCode() {
      return ToString().GetHashCode();
    }
  }
}
