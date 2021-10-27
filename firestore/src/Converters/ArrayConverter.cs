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
using System;
using System.Collections.Generic;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  /// <summary>
  /// Converter for array types. Serialization is handled by the base class; only custom deserialization is required.
  /// </summary>
  internal sealed class ArrayConverter : EnumerableConverterBase {
    private readonly BclType _elementType;

    internal ArrayConverter(BclType elementType) : base(elementType.MakeArrayType()) {
      _elementType = elementType;
    }

    protected override object DeserializeArray(DeserializationContext context, FieldValueProxy arrayValue) {
      Preconditions.CheckState(arrayValue.type() == FieldValueProxy.Type.Array, "Expected to receive an array FieldValue");

      using (var array = FirestoreCpp.ConvertFieldValueToVector(arrayValue)) {
        uint usize = array.Size();
        if (usize > System.Int32.MaxValue) {
          throw new ArgumentException(
              "Array contains too many values (more than System.Int32.MaxValue)");
        }

        Array result = Array.CreateInstance(_elementType, usize);
        for (int i = 0; i < usize; i++) {
          FieldValueProxy value = array.GetUnsafeView((uint)i);
          var converted = ValueDeserializer.Deserialize(context, value, _elementType);
          result.SetValue(converted, i);
        }

        return result;
      }
    }
  }
}
