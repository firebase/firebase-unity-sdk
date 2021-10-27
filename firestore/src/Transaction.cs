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
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Firestore {
  /// <summary>
  /// A transaction, as created by
  /// <see cref="FirebaseFirestore.RunTransactionAsync{T}(System.Func{Transaction, Task{T}})"/>
  /// (and overloads) and passed to user code.
  /// </summary>
  public sealed class Transaction {
    private readonly TransactionCallbackProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    internal Transaction(TransactionCallbackProxy proxy, FirebaseFirestore firestore) {
      _proxy = Util.NotNull(proxy);
      _firestore = Util.NotNull(firestore);
    }

    /// <summary>
    /// The database for this transaction.
    /// </summary>
    public FirebaseFirestore Firestore {
      get {
        return _firestore;
      }
    }

    /// <summary>
    /// Read a snapshot of the document specified by <paramref name="documentReference"/>, with
    /// respect to this transaction. This method cannot be called after any write operations have
    /// been created.
    /// </summary>
    /// <param name="documentReference">The document reference to read. Must not be <c>null</c>.
    /// </param>
    /// <returns>A snapshot of the given document with respect to this transaction.</returns>
    public Task<DocumentSnapshot> GetSnapshotAsync(DocumentReference documentReference) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      // C++ and native client SDK only provides sync API. Here we convert it to async API.
      return Task.Factory.StartNew(() => {
        TransactionResultOfGetProxy result = _proxy.Get(documentReference.Proxy);
        if (!result.is_valid()) {
          throw new InvalidOperationException("Transaction has ended");
        } else if (Enums.Convert(result.error_code()) == FirestoreError.Ok) {
          return new DocumentSnapshot(result.TakeSnapshot(), Firestore);
        } else {
          throw new FirestoreException(Enums.Convert(result.error_code()), result.error_message());
        }
      });
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
    public void Set(DocumentReference documentReference, object documentData, SetOptions options =
        null) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(documentData, nameof(documentData));

      FieldValueProxy data = ValueSerializer.Serialize(SerializationContext.Default, documentData);
      if (!data.is_map()) {
        throw new ArgumentException("documentData must be either an IDictionary or a POCO. Instead we get " + documentData.GetType().FullName);
      }
      if (options == null) {
        options = SetOptions.Overwrite;
      }

      bool successful = _proxy.Set(documentReference.Proxy, data, options.Proxy);
      if (!successful) {
        throw new InvalidOperationException("Transaction has ended");
      }
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields can contain dots
    /// to reference nested fields in the document.  Fields not present in this dictionary are not
    /// updated. Must not be <c>null</c>.</param>
    public void Update(DocumentReference documentReference, IDictionary<string, object> updates) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(updates, nameof(updates));

      FieldValueProxy data = ValueSerializer.Serialize(SerializationContext.Default, updates);
      if (!data.is_map()) {
        // TODO(zxu): More likely to be an internal error since the type is already checked.
        throw new ArgumentException("updates must be Dictionary<string, object>. Instead we get " + updates.GetType().FullName);
      }

      bool successful = _proxy.Update(documentReference.Proxy, data);
      if (!successful) {
        throw new InvalidOperationException("Transaction has ended");
      }
    }

    /// <summary>
    /// Updates the field in the document referred to by the provided <c>DocumentReference</c>. If
    /// no document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="field">The dot-separated name of the field to update. Must not be <c>null</c>.
    /// </param>
    /// <param name="value">The new value for the field. May be <c>null</c>.</param>
    public void Update(DocumentReference documentReference, string field, object value) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNullOrEmpty(field, nameof(field));

      var data = new FieldToValueMap();
      data.Insert(field, ValueSerializer.Serialize(SerializationContext.Default, value));

      bool successful = _proxy.Update(documentReference.Proxy, data);
      if (!successful) {
        throw new InvalidOperationException("Transaction has ended");
      }
    }

    /// <summary>
    /// Updates fields in the document referred to by the provided <c>DocumentReference</c>. If no
    /// document exists yet, the update will fail.
    /// </summary>
    /// <param name="documentReference">The document to update. Must not be <c>null</c>.</param>
    /// <param name="updates">A dictionary of field / value pairs to update. Fields not present in
    /// this dictionary are not updated. Must not be <c>null</c>.</param>
    public void Update(DocumentReference documentReference, IDictionary<FieldPath, object> updates) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      Preconditions.CheckNotNull(updates, nameof(updates));

      var data = new FieldPathToValueMap();
      foreach (KeyValuePair<FieldPath, object> key_value in updates) {
        FieldPathProxy key = key_value.Key.ConvertToProxy();
        FieldValueProxy value = ValueSerializer.Serialize(SerializationContext.Default, key_value.Value);
        data.Insert(key, value);
      }

      bool successful = _proxy.Update(documentReference.Proxy, data);
      if (!successful) {
        throw new InvalidOperationException("Transaction has ended");
      }
    }

    /// <summary>
    /// Deletes the document referenced by the provided <c>DocumentReference</c>.
    /// </summary>
    /// <param name="documentReference">The document to delete. Must not be <c>null</c>.</param>
    public void Delete(DocumentReference documentReference) {
      Preconditions.CheckNotNull(documentReference, nameof(documentReference));
      bool successful = _proxy.Delete(documentReference.Proxy);
      if (!successful) {
        throw new InvalidOperationException("Transaction has ended");
      }
    }
  }
}
