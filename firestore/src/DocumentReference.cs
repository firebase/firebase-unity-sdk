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

using Firebase.Firestore.Internal;
using Firebase.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Firestore {
  using DocumentSnapshotCallbackMap =
      ListenerRegistrationMap<Action<DocumentSnapshotProxy, FirestoreError, string>>;

  /// <summary>
  /// A <c>DocumentReference</c> refers to a document location in a Cloud Firestore database and can
  /// be used to write, read, or listen to the location. There may or may not exist a document at
  /// the referenced location. A <c>DocumentReference</c> can also be used to create a
  /// <see cref="CollectionReference"/> to a subcollection.
  /// </summary>
  public sealed class DocumentReference : IEquatable<DocumentReference> {
    private readonly DocumentReferenceProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    internal DocumentReferenceProxy Proxy {
      get {
        return _proxy;
      }
    }

    internal DocumentReference(DocumentReferenceProxy proxy, FirebaseFirestore firestore) {
      Util.HardAssert(Util.NotNull(proxy).is_valid(),
          "Cannot create DocumentReference for invalid DocumentReferenceProxy");
      _proxy = proxy;
      _firestore = Util.NotNull(firestore);
    }

    internal static void ClearCallbacksForOwner(FirebaseFirestore owner) {
      snapshotListenerCallbacks.ClearCallbacksForOwner(owner);
    }

    /// <summary>
    /// The database which contains the document.
    /// </summary>
    public FirebaseFirestore Firestore {
      get {
        return _firestore;
      }
    }

    /// <summary>
    /// The final part of the complete document path; this is the identity of the document relative
    /// to its parent collection.
    /// </summary>
    public string Id => _proxy.id();

    /// <summary>
    /// The complete document path, not including project and database ID.
    /// </summary>
    public string Path => _proxy.path();

    /// <summary>
    /// The parent collection. Never <c>null</c>.
    /// </summary>
    public CollectionReference Parent => new CollectionReference(_proxy.Parent(), Firestore);

    /// <summary>
    /// Creates a <see cref="CollectionReference"/> for a child collection of this document.
    /// </summary>
    /// <param name="path">The path to the collection, relative to this document. Must not be
    /// <c>null</c>, and must contain an odd number of slash-separated path elements.</param>
    /// <returns>A <see cref="CollectionReference"/> for the specified collection.</returns>
    public CollectionReference Collection(string path) {
      Preconditions.CheckNotNull(path, nameof(path));
      return new CollectionReference(_proxy.Collection(path), Firestore);
    }

    /// <inheritdoc />
    public override int GetHashCode() => Firestore.GetHashCode() * 31 + Path.GetHashCode();

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DocumentReference);

    /// <inheritdoc />
    public bool Equals(DocumentReference other) => other != null && Path == other.Path && Firestore == other.Firestore;

    /// <inheritdoc />
    public override string ToString() {
      AppOptionsInternal options = Util.NotNull(Firestore.App.options());
      string projectId = Util.NotNull(options.ProjectId);
      // TODO(zxu): Find a way to programmatically get database id.
      return "/projects/" + projectId + "/databases/(default)/" + Path;
    }

    /// <summary>
    /// Asynchronously deletes the document referred to by this <c>DocumentReference</c>.
    /// </summary>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task DeleteAsync() => _proxy.DeleteAsync();

    /// <summary>
    /// Asynchronously performs a set of updates on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="updates">The updates to perform on the document, keyed by the field path to
    /// update. Fields not present in this dictionary are not updated. Must not be <c>null</c>.
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(IDictionary<string, object> updates) {
      Preconditions.CheckNotNull(updates, nameof(updates));
      FieldValueProxy fieldValue = ValueSerializer.Serialize(SerializationContext.Default, updates);
      return FirestoreCpp.DocumentReferenceUpdateAsync(_proxy, fieldValue);
    }

    /// <summary>
    /// Asynchronously performs a single field update on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="field">The dot-separated name of the field to update. Must not be
    /// <c>null</c>.</param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(string field, object value) {
      Preconditions.CheckNotNull(field, nameof(field));
      FieldValueProxy proxyValue = ValueSerializer.Serialize(SerializationContext.Default, value);
      return UpdateAsync(new Dictionary<string, object>{
        {field, proxyValue},
      });
    }

    /// <summary>
    /// Asynchronously performs a set of updates on the document referred to by this
    /// <c>DocumentReference</c>.
    /// </summary>
    /// <param name="updates">The updates to perform on the document, keyed by the field path to
    /// update. Fields not present in this dictionary are not updated. Must not be <c>null</c>.
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task UpdateAsync(IDictionary<FieldPath, object> updates) {
      Preconditions.CheckNotNull(updates, nameof(updates));
      var data = new FieldPathToValueMap();
      foreach (KeyValuePair<FieldPath, object> kv in updates) {
        FieldPathProxy key = kv.Key.ConvertToProxy();
        FieldValueProxy value = ValueSerializer.Serialize(SerializationContext.Default, kv.Value);
        data.Insert(key, value);
      }
      return FirestoreCpp.DocumentReferenceUpdateAsync(_proxy, data);
    }

    /// <summary>
    /// Asynchronously sets data in the document, either replacing it completely or merging fields.
    /// </summary>
    /// <param name="documentData">The data to store in the document. Must not be <c>null</c>.
    /// </param>
    /// <param name="options">
    /// <para>The options to use when updating the document. May be <c>null</c>, which is equivalent
    /// to <see cref="SetOptions.Overwrite"/>.</para>
    /// </param>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task SetAsync(object documentData, SetOptions options = null) {
      Preconditions.CheckNotNull(documentData, nameof(documentData));

      if (options == null) {
        options = SetOptions.Overwrite;
      }

      FieldValueProxy fieldValue = ValueSerializer.Serialize(SerializationContext.Default, documentData);
      if (!fieldValue.is_map()) {
        throw new ArgumentException("documentData must be either an IDictionary or a POCO." +
                                    " Instead we got " + documentData.GetType().FullName);
      }

      return FirestoreCpp.DocumentReferenceSetAsync(_proxy, fieldValue, options.Proxy);
    }

    /// <summary>
    /// Asynchronously fetches a snapshot of the document.
    /// </summary>
    /// <remarks>
    /// <para>By default, <c>GetSnapshotAsync</c> attempts to provide up-to-date data when possible
    /// by waiting for data from the server, but it may return cached data or fail if you are
    /// offline and the server cannot be reached. This behavior can be altered via the <c>source</c>
    /// parameter.</para>
    /// </remarks>
    /// <param name="source">indicates whether the results should be fetched from the cache only
    /// (<c>Source.Cache</c>), the server only (<c>Source.Server</c>), or to attempt the server and
    /// fall back to the cache (<c>Source.Default</c>).</param>
    /// <returns>A snapshot of the document. The snapshot may represent a missing document.</returns>
    public Task<DocumentSnapshot> GetSnapshotAsync(Source source = Source.Default) {
      var sourceProxy = Enums.Convert(source);
      return Util.MapResult(_proxy.GetAsync(sourceProxy), taskResult => {
        return new DocumentSnapshot(taskResult, Firestore);
      });
    }

    /// <summary>
    /// Starts listening to changes to the document referenced by this <c>DocumentReference</c>.
    /// </summary>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(Action<DocumentSnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      return Listen(MetadataChanges.Exclude, callback);
    }

    /// <summary>
    /// Starts listening to changes to the document referenced by this <c>DocumentReference</c>.
    /// </summary>
    /// <param name="metadataChanges">Indicates whether metadata-only changes (i.e. only
    /// <c>DocumentSnapshot.Metadata</c> changed) should trigger snapshot events.</param>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(MetadataChanges metadataChanges, Action<DocumentSnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      var tcs = new TaskCompletionSource<object>();
      int uid = snapshotListenerCallbacks.Register(Firestore, (snapshotProxy, errorCode,
                                                               errorMessage) => {
        if (errorCode != FirestoreError.Ok) {
          tcs.SetException(new FirestoreException(errorCode, errorMessage));
        } else {
          FirebaseHandler.RunOnMainThread<object>(() => {
            callback(new DocumentSnapshot(snapshotProxy, Firestore));
            return null;
          });
        }
      });

      var metadataChangesProxy = Enums.Convert(metadataChanges);
      var listener = FirestoreCpp.AddDocumentSnapshotListener(_proxy, metadataChangesProxy, uid,
                                                              documentSnapshotsHandler);

      return new ListenerRegistration(snapshotListenerCallbacks, uid, tcs, listener);
    }

    private static DocumentSnapshotCallbackMap snapshotListenerCallbacks = new DocumentSnapshotCallbackMap();
    internal delegate void ListenerDelegate(int callbackId, IntPtr snapshotPtr,
                                            FirestoreError errorCode, string errorMessage);
    private static ListenerDelegate documentSnapshotsHandler = new ListenerDelegate(DocumentSnapshotsHandler);


    [MonoPInvokeCallback(typeof(ListenerDelegate))]
    private static void DocumentSnapshotsHandler(int callbackId, IntPtr snapshotPtr,
                                                 FirestoreError errorCode, string errorMessage) {
      try {
        // Create the proxy object _before_ doing anything else to ensure that the C++ object's
        // memory does not get leaked (https://github.com/firebase/firebase-unity-sdk/issues/49).
        var documentSnapshotProxy = new DocumentSnapshotProxy(snapshotPtr, /*cMemoryOwn=*/true);

        Action<DocumentSnapshotProxy, FirestoreError, string> callback;
        if (snapshotListenerCallbacks.TryGetCallback(callbackId, out callback)) {
          callback(documentSnapshotProxy, errorCode, errorMessage);
        }

      } catch (Exception e) {
        Util.OnPInvokeManagedException(e, nameof(DocumentSnapshotsHandler));
      }
    }
  }
}
