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

using System;
using System.Collections.Generic;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore {
  /// <summary>
  /// An immutable snapshot of the data for a document.
  /// </summary>
  /// <remarks>
  /// <para>A <c>DocumentSnapshot</c> contains data read from a document in your Cloud Firestore
  /// database. The data can be extracted with the
  /// <see cref="DocumentSnapshot.ToDictionary(ServerTimestampBehavior)"/>
  /// or <see cref="M:DocumentSnapshot.GetValue`1{T}(string, ServerTimestampBehavior)"/> methods.
  /// </para>
  ///
  /// <para>If the <c>DocumentSnapshot</c> points to a non-existing document, <c>ToDictionary</c>
  /// will return <c>null</c>. You can always explicitly check for a document's existence by
  /// checking <see cref="DocumentSnapshot.Exists"/>.</para>
  /// </remarks>
  public sealed class DocumentSnapshot {
    private readonly DocumentReference _reference;
    private readonly Dictionary<string, FirestoreValue> _fields;
    private readonly bool _exists;
    private readonly SnapshotMetadata _metadata;

    internal Dictionary<string, FirestoreValue> InternalFields => _fields;

    internal DocumentSnapshot(
        DocumentReference reference,
        Dictionary<string, FirestoreValue> fields,
        bool exists,
        SnapshotMetadata metadata) {
      _reference = Preconditions.CheckNotNull(reference, nameof(reference));
      _exists = exists;
      _fields = exists
          ? (fields ?? new Dictionary<string, FirestoreValue>(StringComparer.Ordinal))
          : null;
      _metadata = metadata ?? new SnapshotMetadata(false, false);
    }

    /// <summary>
    /// The full reference to the document.
    /// </summary>
    public DocumentReference Reference => _reference;

    /// <summary>
    /// The ID of the document.
    /// </summary>
    public string Id => _reference.Id;

    /// <summary>
    /// Whether or not the document exists.
    /// </summary>
    public bool Exists => _exists;

    /// <summary>
    /// The metadata for this <c>DocumentSnapshot</c>.
    /// </summary>
    public SnapshotMetadata Metadata => _metadata;

    /// <summary>
    /// Returns the document data as a <see cref="Dictionary{String, Object}"/>.
    /// </summary>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <returns>A <see cref="Dictionary{String, Object}"/> containing the document data or
    /// <c>null</c> if this is a nonexistent document.</returns>
    public Dictionary<string, object> ToDictionary(
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) =>
        ConvertTo<Dictionary<string, object>>(serverTimestampBehavior);

    /// <summary>
    /// Deserializes the document data as the specified type.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the document data as.</typeparam>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <returns>The deserialized data or <c>default(T)</c> if this is a nonexistent document.
    /// </returns>
    public T ConvertTo<T>(
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) {
      // For reference types, it makes more sense to return a non-existent document as a null
      // value. This matches Android behavior.
      BclType targetType = typeof(T);
      if (!_exists) {
        BclType underlyingType = Nullable.GetUnderlyingType(targetType);
        if (!targetType.IsValueType || underlyingType != null) {
          return default;
        }
        throw new ArgumentException(String.Format(
            "Unable to convert a non-existent document to {0}", targetType.FullName));
      }

      FirestoreValue mapVal = FirestoreValue.FromMap(_fields)
          .ResolveServerTimestamp(serverTimestampBehavior);
      var context = new DeserializationContext(this);
      return (T)ValueDeserializer.Deserialize(context, mapVal, targetType);
    }

    /// <summary>
    /// Fetches a field value from the document, throwing an exception if the field does not exist.
    /// </summary>
    /// <param name="path">The dot-separated field path to fetch. Must not be <c>null</c> or
    /// empty.</param>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <exception cref="InvalidOperationException">The field does not exist in the document data.
    /// </exception>
    /// <returns>The deserialized value.</returns>
    public T GetValue<T>(
        string path,
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      return GetValue<T>(FieldPath.FromDotSeparatedString(path), serverTimestampBehavior);
    }

    /// <summary>
    /// Attempts to fetch the given field value from the document, returning whether or not it was
    /// found.
    /// </summary>
    /// <remarks>
    /// This method does not throw an exception if the field is not found, but does throw an
    /// exception if the field was found but cannot be deserialized.
    /// </remarks>
    /// <param name="path">The dot-separated field path to fetch. Must not be <c>null</c> or empty.
    /// </param>
    /// <param name="value">When this method returns, contains the deserialized value if the field
    /// was found, or the default value of <typeparamref name="T"/> otherwise.</param>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <returns><c>true</c> if the field was found in the document; <c>false</c> otherwise.
    /// </returns>
    public bool TryGetValue<T>(
        string path,
        out T value,
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      return TryGetValue(
          FieldPath.FromDotSeparatedString(path), out value, serverTimestampBehavior);
    }

    /// <summary>
    /// Fetches a field value from the document, throwing an exception if the field does not exist.
    /// </summary>
    /// <param name="path">The field path to fetch. Must not be <c>null</c> or empty.</param>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <exception cref="InvalidOperationException">The field does not exist in the document data.
    /// </exception>
    /// <returns>The deserialized value.</returns>
    public T GetValue<T>(
        FieldPath path,
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) {
      Preconditions.CheckNotNull(path, nameof(path));
      if (TryGetValue(path, out T value, serverTimestampBehavior)) {
        return value;
      }
      throw new InvalidOperationException("Field " + path + " not found in document");
    }

    /// <summary>
    /// Attempts to fetch the given field value from the document, returning whether or not it was
    /// found.
    /// </summary>
    /// <remarks>
    /// This method does not throw an exception if the field is not found, but does throw an
    /// exception if the field was found but cannot be deserialized.
    /// </remarks>
    /// <param name="path">The field path to fetch. Must not be <c>null</c> or empty.</param>
    /// <param name="value">When this method returns, contains the deserialized value if the field
    /// was found, or the default value of <typeparamref name="T"/> otherwise.</param>
    /// <param name="serverTimestampBehavior">Configures the behavior for server timestamps that
    /// have not yet been set to their final value.</param>
    /// <returns><c>true</c> if the field was found in the document; <c>false</c> otherwise.
    /// </returns>
    public bool TryGetValue<T>(
        FieldPath path,
        out T value,
        ServerTimestampBehavior serverTimestampBehavior = ServerTimestampBehavior.None) {
      Preconditions.CheckNotNull(path, nameof(path));
      value = default;
      if (!_exists) return false;
      if (path.IsDocumentId) {
        var context = new DeserializationContext(this);
        FirestoreValue refFv = FirestoreValue.FromReference(new DocumentReferenceValue(
            _reference.Firestore.ProjectId,
            _reference.Firestore.DatabaseId,
            _reference.ResourcePath));
        value = (T)ValueDeserializer.Deserialize(context, refFv, typeof(T));
        return true;
      }
      if (!FirestoreValue.TryGetFieldByPath(_fields, path, out FirestoreValue raw)) {
        return false;
      }
      FirestoreValue resolved = raw.ResolveServerTimestamp(serverTimestampBehavior);
      var deserCtx = new DeserializationContext(this);
      value = (T)ValueDeserializer.Deserialize(deserCtx, resolved, typeof(T));
      return true;
    }

    /// <summary>
    /// Determines whether or not the given field path is present in the document. If this snapshot
    /// represents a missing document, this method will always return <c>false</c>.
    /// </summary>
    /// <param name="path">The dot-separated field path to check. Must not be <c>null</c> or empty.
    /// </param>
    /// <returns><c>true</c> if the specified path represents a field in the document; <c>false</c>
    /// otherwise.</returns>
    public bool ContainsField(string path) {
      Preconditions.CheckNotNullOrEmpty(path, nameof(path));
      return ContainsField(FieldPath.FromDotSeparatedString(path));
    }

    /// <summary>
    /// Determines whether or not the given field path is present in the document. If this snapshot
    /// represents a missing document, this method will always return <c>false</c>.
    /// </summary>
    /// <param name="path">The field path to check. Must not be <c>null</c>.</param>
    /// <returns><c>true</c> if the specified path represents a field in the document; <c>false</c>
    /// otherwise.  </returns>
    public bool ContainsField(FieldPath path) {
      Preconditions.CheckNotNull(path, nameof(path));
      if (!_exists) return false;
      return FirestoreValue.TryGetFieldByPath(_fields, path, out _);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as DocumentSnapshot);

    /// <inheritdoc />
    public bool Equals(DocumentSnapshot other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null) return false;
      if (!_reference.Equals(other._reference) ||
          _exists != other._exists ||
          !_metadata.Equals(other._metadata)) {
        return false;
      }
      if (!_exists) return true;
      return FirestoreClientCore.MapsEqualExact(_fields, other._fields);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      int h = _reference.GetHashCode();
      h = h * 31 + (_exists ? 1 : 0);
      h = h * 31 + _metadata.GetHashCode();
      if (_exists && _fields != null) {
        h = h * 31 + FirestoreValue.FromMap(_fields).GetHashCode();
      }
      return h;
    }
  }
}
