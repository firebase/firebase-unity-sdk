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

using System.Collections;
using System.Collections.Generic;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  /// <summary>
  /// Base class for types that always serialize to a list value. This type handles serialization
  /// by assuming the type implements <see cref="IEnumerable"/> and simply serializing each element;
  /// derived types need to perform deserialization.
  /// </summary>
  internal abstract class EnumerableConverterBase : ConverterBase {
    internal EnumerableConverterBase(BclType targetType) : base(targetType) { }

    public  // TODO(b/141568173): Significant newline.
        override FirestoreValue Serialize(SerializationContext context, object value) {
      if (value is ICollection col) {
        if (col.Count == 0) return FirestoreValue.FromArray(System.Array.Empty<FirestoreValue>());
        var arr = new FirestoreValue[col.Count];
        int idx = 0;
        foreach (object element in col) {
          arr[idx++] = ValueSerializer.Serialize(context, element);
        }
        return FirestoreValue.FromArray(arr);
      }
      var list = new List<FirestoreValue>();
      foreach (object element in (IEnumerable)value) {
        list.Add(ValueSerializer.Serialize(context, element));
      }
      return FirestoreValue.FromArray(list.ToArray());
    }
  }
}
