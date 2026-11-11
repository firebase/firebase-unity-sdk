// Copyright 2026 Google LLC
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
using System.Security.Cryptography;

namespace Firebase.Firestore.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class ResourcePath : IEquatable<ResourcePath>, IComparable<ResourcePath> {
    private const string AutoIdAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    internal static readonly ResourcePath Empty = new ResourcePath(Array.Empty<string>());
    private static readonly RandomNumberGenerator Rng = RandomNumberGenerator.Create();

    internal string[] Segments { get; }
    private string _canonical;

    internal ResourcePath(string[] segments) {
      Segments = segments ?? Array.Empty<string>();
    }

    internal int Count => Segments.Length;
    internal bool IsEmpty => Segments.Length == 0;
    internal bool IsCollection => Segments.Length % 2 == 1;
    internal bool IsDocument => Segments.Length > 0 && Segments.Length % 2 == 0;
    internal string Id => Segments.Length == 0 ? string.Empty : Segments[Segments.Length - 1];

    internal string CanonicalString =>
        _canonical ?? (_canonical = Segments.Length == 0 ? string.Empty : string.Join("/", Segments));

    internal ResourcePath Parent {
      get {
        if (Segments.Length == 0) return null;
        if (Segments.Length == 1) return Empty;
        var copy = new string[Segments.Length - 1];
        Array.Copy(Segments, 0, copy, 0, copy.Length);
        return new ResourcePath(copy);
      }
    }

    internal ResourcePath Child(string segment) {
      var copy = new string[Segments.Length + 1];
      Array.Copy(Segments, 0, copy, 0, Segments.Length);
      copy[Segments.Length] = segment;
      return new ResourcePath(copy);
    }

    internal ResourcePath Append(ResourcePath suffix) {
      if (suffix == null || suffix.Count == 0) return this;
      if (Count == 0) return suffix;
      var copy = new string[Count + suffix.Count];
      Array.Copy(Segments, 0, copy, 0, Count);
      Array.Copy(suffix.Segments, 0, copy, Count, suffix.Count);
      return new ResourcePath(copy);
    }

    internal bool IsImmediateParentOf(ResourcePath child) {
      if (child == null || child.Count != Count + 1) return false;
      for (int i = 0; i < Count; i++) {
        if (!string.Equals(Segments[i], child.Segments[i], StringComparison.Ordinal)) {
          return false;
        }
      }
      return true;
    }

    internal bool IsPrefixOf(ResourcePath other) {
      if (other == null || other.Count < Count) return false;
      for (int i = 0; i < Count; i++) {
        if (!string.Equals(Segments[i], other.Segments[i], StringComparison.Ordinal)) {
          return false;
        }
      }
      return true;
    }

    internal static ResourcePath ParseRelative(string rawPath, string paramName) {
      Preconditions.CheckNotNull(rawPath, paramName);
      if (rawPath.IndexOf("//", StringComparison.Ordinal) >= 0) {
        throw new ArgumentException(
            $"Invalid path ({rawPath}). Paths must not contain // in them.", paramName);
      }
      var list = new List<string>();
      int start = 0;
      for (int i = 0; i <= rawPath.Length; i++) {
        if (i == rawPath.Length || rawPath[i] == '/') {
          if (i > start) {
            list.Add(rawPath.Substring(start, i - start));
          }
          start = i + 1;
        }
      }
      if (list.Count == 0) {
        throw new ArgumentException("Invalid path (empty).", paramName);
      }
      return new ResourcePath(list.ToArray());
    }

    internal static ResourcePath ParseUntrusted(string rawPath) {
      if (string.IsNullOrEmpty(rawPath)) return Empty;
      string[] parts = rawPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
      return parts.Length == 0 ? Empty : new ResourcePath(parts);
    }

    /// <summary>
    /// Parses a full Firestore resource name:
    /// <c>projects/{projectId}/databases/{databaseId}/documents/{docPath}</c>.
    /// </summary>
    internal static bool TryParseResourceName(
        string fullName, out string projectId, out string databaseId, out ResourcePath docPath) {
      projectId = null;
      databaseId = null;
      docPath = Empty;
      if (string.IsNullOrEmpty(fullName)) return false;
      string[] parts = fullName.Split('/');
      // Minimum: projects/{p}/databases/{db}/documents (5 parts) + doc segments
      if (parts.Length < 5 ||
          !string.Equals(parts[0], "projects", StringComparison.Ordinal) ||
          !string.Equals(parts[2], "databases", StringComparison.Ordinal) ||
          !string.Equals(parts[4], "documents", StringComparison.Ordinal)) {
        return false;
      }
      projectId = parts[1];
      databaseId = parts[3];
      if (parts.Length == 5) {
        docPath = Empty;
        return true;
      }
      var segs = new string[parts.Length - 5];
      Array.Copy(parts, 5, segs, 0, segs.Length);
      docPath = new ResourcePath(segs);
      return true;
    }

    internal string ToFullResourceName(string projectId, string databaseId) {
      return Count == 0
          ? $"projects/{projectId}/databases/{databaseId}/documents"
          : $"projects/{projectId}/databases/{databaseId}/documents/{CanonicalString}";
    }

    /// <summary>
    /// Checks backend segment constraints (reserved <c>__.*__</c>, <c>.</c>, <c>..</c>).
    /// Reference creation allows these segments, while read/write/listen RPCs reject them with
    /// <see cref="FirestoreError.InvalidArgument"/>.
    /// </summary>
    internal FirestoreException ValidateForBackend() {
      for (int i = 0; i < Segments.Length; i++) {
        string seg = Segments[i];
        if (string.IsNullOrEmpty(seg) || seg == "." || seg == ".." || IsReservedSegment(seg)) {
          return new FirestoreException(
              FirestoreError.InvalidArgument,
              $"Invalid resource path ({CanonicalString}): segment '{seg}' is not valid.");
        }
      }
      return null;
    }

    internal static bool IsReservedSegment(string segment) =>
        segment != null &&
        segment.Length >= 4 &&
        segment.StartsWith("__", StringComparison.Ordinal) &&
        segment.EndsWith("__", StringComparison.Ordinal);

    internal static string AutoId() {
      Span<byte> randomBytes = stackalloc byte[20];
      lock (Rng) {
        Rng.GetBytes(randomBytes);
      }
      Span<char> chars = stackalloc char[20];
      for (int i = 0; i < 20; i++) {
        // Reject >= 248 (62 * 4 = 248) only rarely; fallback modulo keeps single-call speed.
        chars[i] = AutoIdAlphabet[randomBytes[i] % AutoIdAlphabet.Length];
      }
      return new string(chars);
    }

    public int CompareTo(ResourcePath other) {
      if (ReferenceEquals(this, other)) return 0;
      if (other == null) return 1;
      int min = Math.Min(Count, other.Count);
      for (int i = 0; i < min; i++) {
        int cmp = FirestoreValue.CompareUtf8Strings(Segments[i], other.Segments[i]);
        if (cmp != 0) return cmp;
      }
      return Count.CompareTo(other.Count);
    }

    public bool Equals(ResourcePath other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null || Count != other.Count) return false;
      for (int i = 0; i < Count; i++) {
        if (!string.Equals(Segments[i], other.Segments[i], StringComparison.Ordinal)) {
          return false;
        }
      }
      return true;
    }

    public override bool Equals(object obj) => Equals(obj as ResourcePath);

    public override int GetHashCode() {
      int hash = 17;
      for (int i = 0; i < Segments.Length; i++) {
        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Segments[i]);
      }
      return hash;
    }

    public override string ToString() => CanonicalString;
  }
}
