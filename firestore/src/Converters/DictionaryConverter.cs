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

using System;
using System.Collections.Generic;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  /// <summary>
  /// Base class for dictionary-based map values.
  /// </summary>
  /// <typeparam name="TValue">Type of values in the dictionary.</typeparam>
  internal sealed class DictionaryConverter<TValue> : MapConverterBase {
    private readonly BclType _concreteType;
    private readonly bool _isStandardDictionary;

    internal DictionaryConverter(BclType targetType) : base(targetType) {
      _concreteType = targetType;
      // If we've been asked to deserialize to an interface, let's just see if Dictionary<,>
      // would work, but otherwise give up.
      if (targetType.IsInterface) {
        var candidateType = typeof(Dictionary<string, TValue>);
        if (targetType.IsAssignableFrom(candidateType)) {
          _concreteType = candidateType;
        } else {
          throw new ArgumentException(String.Format("Unable to deserialize map to {0}", targetType));
        }
      }
      _isStandardDictionary = _concreteType == typeof(Dictionary<string, TValue>);
    }

    public // TODO(b/141568173): Significant newline.
    override object DeserializeMap(DeserializationContext context, FirestoreValue mapValue) {
      Preconditions.CheckState(
          mapValue.Kind == FirestoreValueKind.Map, "Expected to receive a map FirestoreValue");
      IDictionary<string, FirestoreValue> src = mapValue.MapValue;
      // TODO: Compile an expression tree on construction, or at least accept an optional delegate for construction
      // (allowing for special-casing of Dictionary<string, object>).
      IDictionary<string, TValue> ret = _isStandardDictionary
          ? new Dictionary<string, TValue>(src.Count, StringComparer.Ordinal)
          : (IDictionary<string, TValue>)Activator.CreateInstance(_concreteType);
      foreach (KeyValuePair<string, FirestoreValue> kv in src) {
        ret.Add(kv.Key, (TValue)ValueDeserializer.Deserialize(context, kv.Value, typeof(TValue)));
      }
      return ret;
    }

    public override void SerializeMap(
        SerializationContext context, object value, IDictionary<string, FirestoreValue> map) {
      var dictionary = (IDictionary<string, TValue>)value;
      foreach (KeyValuePair<string, TValue> pair in dictionary) {
        if (pair.Key == null) {
          throw new ArgumentException("Dictionary keys cannot be null.");
        }
        map[pair.Key] = ValueSerializer.Serialize(context, pair.Value);
      }
    }
  }
}
