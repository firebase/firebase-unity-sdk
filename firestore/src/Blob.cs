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
using System.Text;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// An immutable sequence of bytes.
  /// </summary>
  /// <remarks>
  /// Although this is a struct, it's effectively just a wrapper around a byte[].
  /// </remarks>
  public struct Blob : IEquatable<Blob> {
    private readonly byte[] _bytes;
    private int _hash;

    internal byte[] InternalBytes => _bytes ?? Array.Empty<byte>();

    /// <summary>
    /// The length of the blob, in bytes.
    /// </summary>
    public int Length => _bytes == null ? 0 : _bytes.Length;

    /// <summary>
    /// Returns the byte at index <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The index in the blob to return. Must be greater than or equal to 0, and
    /// less than <see cref="Length"/>.</param>
    /// <returns>The byte at index <paramref name="index"/>.</returns>
    public byte this[int index] {
      get {
        if (_bytes == null) throw new IndexOutOfRangeException();
        return _bytes[index];
      }
    }

    /// <summary>
    /// Returns a copy of this Blob as a byte[].
    /// </summary>
    public byte[] ToBytes() => _bytes == null ? Array.Empty<byte>() : (byte[])_bytes.Clone();

    /// <summary>
    /// Constructs a new <see cref="Blob"/> by copying the current content of
    /// <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">Byte array to copy.</param>
    /// <returns>A new blob containing a copy of <paramref name="bytes"/>.</returns>
    public static Blob CopyFrom(byte[] bytes) {
      Preconditions.CheckNotNull(bytes, nameof(bytes));
      return new Blob(bytes, copy: true);
    }

    internal static Blob FromTrusted(byte[] bytes) =>
        new Blob(bytes ?? Array.Empty<byte>(), copy: false);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is Blob && Equals((Blob)obj);

    /// <inheritdoc />
    public override int GetHashCode() {
      // The traditional algorithm for hash protobuf ByteString.
      if (_hash != 0) {
        return _hash;
      }
      byte[] arr = InternalBytes;
      int result = arr.Length;
      for (int i = 0; i < arr.Length; i++) {
        result = result * 31 + arr[i];
      }
      if (result == 0) {
        result = 1;
      }
      _hash = result;
      return result;
    }

    /// <inheritdoc />
    public bool Equals(Blob other) {
      // SequenceEqual is not available. So just do this in naive way.
      byte[] a = InternalBytes;
      byte[] b = other.InternalBytes;
      if (a.Length != b.Length || (_hash != 0 && other._hash != 0 && _hash != other._hash)) {
        return false;
      }
      for (int i = 0; i < a.Length; ++i) {
        if (a[i] != b[i]) {
          return false;
        }
      }
      return true;
    }

    /// <summary>
    /// Operator overload to compare two Blob values for equality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns>true if <paramref name="lhs"/> is equal to <paramref name="rhs"/>; otherwise
    /// false.</returns>
    public static bool operator ==(Blob lhs, Blob rhs) => lhs.Equals(rhs);

    /// <summary>
    /// Operator overload to compare two Blob values for inequality.
    /// </summary>
    /// <param name="lhs">Left value to compare</param>
    /// <param name="rhs">Right value to compare</param>
    /// <returns>false if <paramref name="lhs"/> is equal to <paramref name="rhs"/>; otherwise
    /// true.</returns>
    public static bool operator !=(Blob lhs, Blob rhs) => !lhs.Equals(rhs);

    /// <inheritdoc />
    public override string ToString() {
      byte[] arr = InternalBytes;
      var builder = new StringBuilder("Blob: Length=");
      builder.Append(arr.Length);
      builder.Append(arr.Length > 32 ? "; Hex (first 32 bytes only)=" : "; Hex=");
      for (int i = 0; i < Math.Min(arr.Length, 32); i++) {
        if (i != 0) {
          builder.Append(" ");
        }
        builder.Append(arr[i].ToString("X2"));
      }
      return builder.ToString();
    }

    private Blob(byte[] bytes, bool copy) {
      _bytes = copy ? (byte[])bytes.Clone() : bytes;
      _hash = 0;
    }
  }
}
