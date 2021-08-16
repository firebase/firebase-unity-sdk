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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Firestore {
  /// <summary>
  /// A batch of write operations, used to perform multiple writes as a single atomic unit.
  /// </summary>
  /// <remarks>
  /// <para>A <c>WriteBatch</c> object can be acquired by calling
  /// <see cref="FirebaseFirestore.StartBatch"/>. It provides methods for adding writes to the write
  /// batch. None of the writes will be committed (or visible locally) until
  /// <see cref="CommitAsync"/> is called.</para>
  ///
  /// <para>Unlike transactions, write batches are persisted offline and therefore are preferable
  /// when you don't need to condition your writes on read data.</para>
  /// </remarks>
  public sealed class WriteBatch {
    private readonly WriteBatchProxy _proxy;

    internal WriteBatch(WriteBatchProxy proxy) {
      _proxy = Util.NotNull(proxy);
    }

    /// <summary>
    /// Deletes the document referenced by the provided <c>DocumentReference</c>.
    /// </summary>
    /// <param name="documentReference">The document to delete. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Delete(DocumentReference documentReference) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      _proxy.Delete(documentReference.Proxy);
      return this;
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields can contain dots
    /// to reference nested fields in the document.  Fields not present in this dictionary are not
    /// updated. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(DocumentReference documentReference, IDictionary<string, object> updates) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(updates, nameof(updates));

      var fieldValue = ConvertToFieldValue(updates);
      FirestoreCpp.WriteBatchUpdate(_proxy, documentReference.Proxy, fieldValue);

      return this;
    }

    private FieldValueProxy ConvertToFieldValue(IDictionary<string, object> updates) {
      FieldValueProxy data = ValueSerializer.Serialize(SerializationContext.Default, updates);

      Util.HardAssert(data.is_map(), "The serialized type should be a map but isn't. " +
          "This should not happen.");

      return data;
    }

    /// <summary>
    /// Updates the field in the document referred to by the provided <c>DocumentReference</c>. If
    /// no document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="field">The dot-separated name of the field to update. Must not be <c>null</c>.
    /// </param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(DocumentReference documentReference, string field, object value) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNullOrEmpty(field, nameof(field));

      var data = new FieldToValueMap();
      data.Insert(field, ValueSerializer.Serialize(SerializationContext.Default, value));
      FirestoreCpp.WriteBatchUpdate(_proxy, documentReference.Proxy, data);

      return this;
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields not present in
    /// this dictionary are not updated. Must not be <c>null</c>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Update(DocumentReference documentReference, IDictionary<FieldPath, object> updates) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(updates, nameof(updates));

      var data = new FieldPathToValueMap();
      foreach (KeyValuePair<FieldPath, object> key_value in updates) {
        FieldPathProxy key = key_value.Key.ConvertToProxy();
        FieldValueProxy value = ValueSerializer.Serialize(SerializationContext.Default, key_value.Value);
        data.Insert(key, value);
      }
      FirestoreCpp.WriteBatchUpdate(_proxy, documentReference.Proxy, data);

      return this;
    }

    /// <summary>
    /// Writes to the document referred to by the provided <c>DocumentReference</c>. If the document
    /// does not yet exist, it will be created. If you pass <paramref name="options"/>, the provided
    /// data can be merged into an existing document.
    /// </summary>
    /// <param name="documentReference">The document in which to set the data. Must not be
    /// <c>null</c>.</param>
    /// <param name="documentData">The data for the document. Must not be <c>null</c>.</param>
    /// <param name="options">The options to use when updating the document. May be <c>null</c>,
    /// which is equivalent to <see cref="SetOptions.Overwrite"/>.</param>
    /// <returns>This batch, for the purposes of method chaining.</returns>
    public WriteBatch Set(DocumentReference documentReference, object documentData, SetOptions options = null) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(documentData, nameof(documentData));

      FieldValueProxy data = ValueSerializer.Serialize(SerializationContext.Default, documentData);
      if (!data.is_map()) {
        throw new ArgumentException("documentData must be either an IDictionary or a POCO. Instead we get " +
                                    documentData.GetType().FullName);
      }
      if (options == null) {
        options = SetOptions.Overwrite;
      }

      FirestoreCpp.WriteBatchSet(_proxy, documentReference.Proxy, data, options.Proxy);
      return this;
    }

    /// <summary>
    /// Commits all of the writes in this write batch as a single atomic unit.
    /// </summary>
    /// <returns>The write result of the server operation. The task will not complete while the
    /// client is offline, though local changes will be visible immediately.</returns>
    public Task CommitAsync() => _proxy.CommitAsync();
  }
}
