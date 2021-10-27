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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using BclType = System.Type;

namespace Firebase.Firestore.Converters {

  /// <summary>
  /// Converter for <see cref="IList"/>-based types. (Note that this doesn't handle types that
  /// implement the generic <see cref="IList{T}"/> interface without the non-generic one.)
  /// This type handles deserialization; the base type handles serialization.
  /// </summary>
  internal sealed class EnumerableConverter : EnumerableConverterBase {
    private readonly BclType _elementType;

    internal EnumerableConverter(BclType targetType) : base(targetType) {
      _elementType = typeof(object);

      // (We could also make this type generic, like DictionaryConverter. There's a difference
      // in that we don't need a generic interface in the conversion code here.)
      var interfaces = targetType.GetInterfaces();
      Type genericEnumerable = null;
      if (targetType.IsInterface && targetType.IsGenericType &&
          targetType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
        genericEnumerable = targetType;
      } else {
        genericEnumerable = interfaces.FirstOrDefault(iface => iface.IsGenericType &&
                                                               iface.GetGenericTypeDefinition() ==
                                                                   typeof(IEnumerable<>));
      }
      if (genericEnumerable != null) {
        _elementType = genericEnumerable.GetGenericArguments()[0];
      }
    }

    protected override object DeserializeArray(DeserializationContext context,
                                               FieldValueProxy arrayValue) {
      Preconditions.CheckState(arrayValue.type() == FieldValueProxy.Type.Array,
                               "Expected to receive an array FieldValue");

      // Internal type to hold deserialized values.
      var listType = typeof(List<>).MakeGenericType(_elementType);
      if (!TargetType.IsAssignableFrom(listType)) {
        throw new NotSupportedException(
            String.Format("Deserializing to {0} is not supported. Please change to a List " +
                              "or use a custom converter to convert to it.",
                          TargetType.FullName));
      }

      using (var array = FirestoreCpp.ConvertFieldValueToVector(arrayValue)) {
        uint usize = array.Size();
        if (usize > System.Int32.MaxValue) {
          throw new ArgumentException(
              "List contains too many values. (More than System.Int32.MaxValue)");
        }

        // TODO: See if using a compiled expression tree is faster.
        var ret = (IList)Activator.CreateInstance(listType);
        for (int i = 0; i < usize; i++) {
          FieldValueProxy value = array.GetUnsafeView((uint)i);
          var deserialized = ValueDeserializer.Deserialize(context, value, _elementType);
          ret.Add(deserialized);
        }

        return ret;
      }
    }
  }
}
