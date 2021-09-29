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
using System.Runtime.InteropServices;
using System.Text;

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

    /// <summary>
    /// The length of the blob, in bytes.
    /// </summary>
    public int Length => _bytes.Length;

    /// <summary>
    /// Returns the byte at index <paramref name="index"/>.
    /// </summary>
    /// <param name="index">The index in the blob to return. Must be greater than or equal to 0, and
    /// less than <see cref="Length"/>.</param>
    /// <returns>The byte at index <paramref name="index"/>.</returns>
    public byte this[int index] => _bytes[index];

    /// <summary>
    /// Returns a copy of this Blob as a byte[].
    /// </summary>
    public byte[] ToBytes() => (byte[])_bytes.Clone();

    /// <summary>
    /// Constructs a new <see cref="Blob"/> by copying the current content of
    /// <paramref name="bytes"/>.
    /// </summary>
    /// <param name="bytes">Byte array to copy.</param>
    /// <returns>A new blob containing a copy of <paramref name="bytes"/>.</returns>
    public static Blob CopyFrom(byte[] bytes) => new Blob(bytes);

    /// <inheritdoc />
    public override bool Equals(object obj) => obj is Blob && Equals((Blob)obj);

    /// <inheritdoc />
    public override int GetHashCode() {
      // The traditional algorithm for hash protobuf ByteString.
      if (_hash != 0) {
        return _hash;
      }
      int result = Length;
      foreach (byte b in _bytes) {
        result = result * 31 + (int)b;
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
      if (Length != other.Length ||
          (_hash != 0 && other._hash != 0 && _hash != other._hash)) {
        return false;
      }
      for (int i = 0; i < Length; ++i) {
        if (_bytes[i] != other._bytes[i]) {
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
      StringBuilder builder = new StringBuilder("Blob: Length=");
      builder.Append(_bytes.Length);
      builder.Append(_bytes.Length > 32 ? "; Hex (first 32 bytes only)=" : "; Hex=");
      for (int i = 0; i < Math.Min(_bytes.Length, 32); i++) {
        if (i != 0) {
          builder.Append(" ");
        }
        builder.Append(_bytes[i].ToString("X2"));
      }
      return builder.ToString();
    }

    private Blob(byte[] bytes) {
      _bytes = (byte[])bytes.Clone();
      _hash = 0;
    }
  }
}
