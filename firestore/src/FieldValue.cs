// Copyright 2018 Google LLC
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

namespace Firebase.Firestore {
  /// <summary>
  /// A static class providing properties and methods to represent sentinel values.
  /// </summary>
  /// <remarks>
  /// <para>Sentinel values are special values where the client-side value is not part of the
  /// document modification sent to the server. A property decorated with
  /// <see cref="FirestorePropertyAttribute"/> can specify an additional attribute to indicate
  /// that it's a sentinel value, such as a <see cref="ServerTimestampAttribute"/>, or the
  /// sentinel values returned by the members of this class can be used directly as values to be
  /// serialized (for example, in anonymous types), and they will be handled directly by the
  /// serialization mechanism.</para>
  /// </remarks>
  public static class FieldValue {
    /// <summary>
    /// Sentinel value indicating that the field should be set to the timestamp of the commit
    /// that creates or modifies the document.
    /// </summary>
    public static object ServerTimestamp { get; } = FieldValueProxy.ServerTimestamp();

    /// <summary>
    /// Sentinel value indicating that the field should be deleted from the document.
    /// </summary>
    public static object Delete { get; } = FieldValueProxy.Delete();

    /// <summary>
    /// Returns a special value that can be used with <c>SetAsync()</c> or <c>UpdateAsync()</c> that tells the
    /// server to union the given elements with any array value that already exists on the server. Each
    /// specified element that doesn't already exist in the array will be added to the end. If the
    /// field being modified is not already an array, it will be overwritten with an array containing
    /// exactly the specified elements.
    /// </summary>
    ///
    /// <param name="elements">The elements to union into the array.</param>
    /// <returns>The <c>FieldValue</c> sentinel for use in a call to <c>SetAsync()</c> or <c>UpdateAsync()</c>.
    /// </returns>
    public static object ArrayUnion(params object[] elements) {
      Preconditions.CheckNotNull(elements, nameof(elements));
      var fieldValue = ValueSerializer.Serialize(SerializationContext.Default, elements);
      var array = FirestoreCpp.ConvertFieldValueToVector(fieldValue);
      return FirestoreCpp.FieldValueArrayUnion(array);
    }

    /// <summary>
    /// Returns a special value that can be used with <c>SetAsync()</c> or <c>UpdateAsync()</c> that tells the
    /// server to remove the given elements from any array value that already exists on the server. All
    /// instances of each element specified will be removed from the array. If the field being modified
    /// is not already an array, it will be overwritten with an empty array.
    /// </summary>
    ///
    /// <param name="elements">The elements to remove from the array.</param>
    /// <returns>The <c>FieldValue</c> sentinel for use in a call to <c>SetAsync()</c> or <c>UpdateAsync()</c>.
    /// </returns>
    public static object ArrayRemove(params object[] elements) {
      Preconditions.CheckNotNull(elements, nameof(elements));
      var fieldValue = ValueSerializer.Serialize(SerializationContext.Default, elements);
      var array = FirestoreCpp.ConvertFieldValueToVector(fieldValue);
      return FirestoreCpp.FieldValueArrayRemove(array);
    }

    /// <summary>
    /// Returns a special value that can be used with <c>SetAsync()</c> or <c>UpdateAsync()</c> that tells the server to
    /// increment the field's current value by the given value.
    /// </summary>
    /// <remarks>
    /// <para>If the current field value is an integer, possible integer overflows are resolved to
    /// <see cref="System.Int64.MinValue"/> or <see cref="System.Int64.MaxValue"/>. If the current
    /// field value is a double, both values will be interpreted as doubles and the arithmetic will
    /// follow IEEE 754 semantics.
    /// </para>
    /// <para>If the current field is not an integer or double, or if the field does not yet exist, the
    /// transformation will set the field to the given value.
    /// </para>
    /// </remarks>
    /// <returns>The <c>FieldValue</c> sentinel for use in a call to <c>SetAsync()</c> or <c>UpdateAsync()</c>.</returns>
    ///
    public static object Increment(long value) => FieldValueProxy.IntegerIncrement(value);

    /// <summary>
    /// Returns a special value that can be used with <c>SetAsync()</c> or <c>UpdateAsync()</c> that tells the server to
    /// increment the field's current value by the given value.
    /// </summary>
    /// <remarks>
    /// <para>If the current value is an integer or a double, both the current and the given value will be
    /// interpreted as doubles and all arithmetic will follow IEEE 754 semantics. Otherwise, the
    /// transformation will set the field to the given value.
    /// </para>
    /// </remarks>
    /// <returns>The <c>FieldValue</c> sentinel for use in a call to <c>SetAsync()</c> or <c>UpdateAsync()</c>.</returns>
    ///
    public static object Increment(double value) => FieldValueProxy.DoubleIncrement(value);
  }
}
