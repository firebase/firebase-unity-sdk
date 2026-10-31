/*
 * Copyright 2026 Google LLC
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

namespace Firebase.Storage.Internal {
  /// <summary>
  /// Represents a normalized Google Cloud Storage bucket and object path.
  /// </summary>
  internal sealed class StoragePath : IEquatable<StoragePath> {
    private const string DefaultBaseUrl = "https://firebasestorage.googleapis.com/v0";

    /// <summary>
    /// Gets the <see cref="FirebaseStorage" /> instance associated with this path.
    /// </summary>
    public FirebaseStorage Storage { get; }

    /// <summary>
    /// Gets the Google Cloud Storage bucket name.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// Gets the normalized object path without leading or trailing slashes (or empty string at root).
    /// </summary>
    public string ObjectPath { get; }

    /// <summary>
    /// Gets the full path starting with a leading slash (e.g. "/folder/file.txt" or "/" at root).
    /// </summary>
    public string FullPath => "/" + ObjectPath;

    /// <summary>
    /// Gets the short name (last segment) of the object path, or empty string at root.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a value indicating whether this path points to the root of the bucket.
    /// </summary>
    public bool IsRoot => string.IsNullOrEmpty(ObjectPath);

    /// <summary>
    /// Initializes a new instance of the <see cref="StoragePath" /> class.
    /// </summary>
    /// <param name="storage">The owning <see cref="FirebaseStorage" /> instance.</param>
    /// <param name="bucket">The Google Cloud Storage bucket name.</param>
    /// <param name="objectPath">The relative object path within the bucket.</param>
    public StoragePath(FirebaseStorage storage, string bucket, string objectPath = null) {
      Storage = storage;
      Bucket = bucket ?? string.Empty;
      ObjectPath = NormalizePath(objectPath);
      if (string.IsNullOrEmpty(ObjectPath)) {
        Name = string.Empty;
      } else {
        int lastSlash = ObjectPath.LastIndexOf('/');
        Name = lastSlash >= 0 ? ObjectPath.Substring(lastSlash + 1) : ObjectPath;
      }
    }

    /// <summary>
    /// Normalizes a path by collapsing consecutive slashes and trimming leading/trailing slashes.
    /// </summary>
    public static string NormalizePath(string path) {
      if (string.IsNullOrEmpty(path)) {
        return string.Empty;
      }
      string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      return segments.Length == 0 ? string.Empty : string.Join("/", segments);
    }

    /// <summary>
    /// Returns the parent <see cref="StoragePath" />, or null if this path is at the root.
    /// </summary>
    public StoragePath GetParent() {
      if (IsRoot) {
        return null;
      }
      int lastSlash = ObjectPath.LastIndexOf('/');
      if (lastSlash >= 0) {
        return new StoragePath(Storage, Bucket, ObjectPath.Substring(0, lastSlash));
      }
      return new StoragePath(Storage, Bucket, string.Empty);
    }

    /// <summary>
    /// Returns the root <see cref="StoragePath" /> for the same bucket.
    /// </summary>
    public StoragePath GetRoot() {
      return new StoragePath(Storage, Bucket, string.Empty);
    }

    /// <summary>
    /// Returns a child <see cref="StoragePath" /> relative to this path.
    /// </summary>
    public StoragePath GetChild(string childPath) {
      string normalizedChild = NormalizePath(childPath);
      if (string.IsNullOrEmpty(normalizedChild)) {
        return this;
      }
      if (IsRoot) {
        return new StoragePath(Storage, Bucket, normalizedChild);
      }
      return new StoragePath(Storage, Bucket, ObjectPath + "/" + normalizedChild);
    }

    /// <summary>
    /// Escapes an object path for Firebase Storage REST URLs.
    /// </summary>
    public static string EscapePath(string path) {
      return Uri.EscapeDataString(path ?? string.Empty).Replace("+", "%2B");
    }

    /// <summary>
    /// Returns the base REST API URL for Firebase Storage (or the configured emulator).
    /// </summary>
    public static string GetBaseUrl(string emulatorHost = null, int emulatorPort = 0) {
      if (!string.IsNullOrEmpty(emulatorHost) && emulatorPort > 0) {
        return $"http://{emulatorHost}:{emulatorPort}/v0";
      }
      return DefaultBaseUrl;
    }

    /// <summary>
    /// Returns the REST API URL for the bucket's objects collection.
    /// </summary>
    public string GetHttpBucketObjectsUrl(string emulatorHost = null, int emulatorPort = 0) {
      return $"{GetBaseUrl(emulatorHost, emulatorPort)}/b/{Bucket}/o";
    }

    /// <summary>
    /// Returns the REST API URL for this specific object (or the bucket objects endpoint if at root).
    /// </summary>
    public string GetHttpObjectUrl(string emulatorHost = null, int emulatorPort = 0) {
      if (IsRoot) {
        return GetHttpBucketObjectsUrl(emulatorHost, emulatorPort);
      }
      return $"{GetHttpBucketObjectsUrl(emulatorHost, emulatorPort)}/{EscapePath(ObjectPath)}";
    }

    /// <inheritdoc />
    public bool Equals(StoragePath other) {
      if (ReferenceEquals(null, other)) return false;
      if (ReferenceEquals(this, other)) return true;
      return string.Equals(Bucket, other.Bucket, StringComparison.Ordinal) &&
             string.Equals(ObjectPath, other.ObjectPath, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) {
      return obj is StoragePath other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode() {
      unchecked {
        int hash = 17;
        hash = hash * 31 + (Bucket != null ? StringComparer.Ordinal.GetHashCode(Bucket) : 0);
        hash = hash * 31 + (ObjectPath != null ? StringComparer.Ordinal.GetHashCode(ObjectPath) : 0);
        return hash;
      }
    }

    /// <inheritdoc />
    public override string ToString() {
      return $"gs://{Bucket}{FullPath}";
    }
  }
}
