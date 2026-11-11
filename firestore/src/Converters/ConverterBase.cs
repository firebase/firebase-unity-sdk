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
  /// A base class for many converters, allowing them to simply override the method for the one
  /// deserialization case required. Serializing to a single value has to be implemented; serializing
  /// to a map will fail with an exception unless <see cref="SerializeMap(SerializationContext, object, IDictionary{string, FirestoreValue})"/>
  /// is overridden.
  /// </summary>
  internal abstract class ConverterBase : IFirestoreInternalConverter {
    protected readonly BclType TargetType;

    protected ConverterBase(BclType targetType) {
      TargetType = targetType;
    }

    public virtual object DeserializeMap(DeserializationContext context, FirestoreValue mapValue) {
      throw new ArgumentException(String.Format("Unable to convert map value to {0}", TargetType));
    }

    public virtual object DeserializeValue(DeserializationContext context, FirestoreValue value) {
      switch (value.Kind) {
        case FirestoreValueKind.Array:
          return DeserializeArray(context, value);
        case FirestoreValueKind.Boolean:
          return DeserializeBoolean(context, value.BooleanValue);
        case FirestoreValueKind.Blob:
          return DeserializeBytes(context, value.BlobValue.ToBytes());
        case FirestoreValueKind.Double:
          return DeserializeDouble(context, value.DoubleValue);
        case FirestoreValueKind.GeoPoint:
          return DeserializeGeoPoint(context, value.GeoPointValue);
        case FirestoreValueKind.Integer:
          return DeserializeInteger(context, value.IntegerValue);
        case FirestoreValueKind.Map:
          return DeserializeMap(context, value);
        case FirestoreValueKind.Reference: {
          DocumentReferenceValue refVal = value.ReferenceValue;
          FirebaseFirestore targetDb = context.Firestore.GetDatabaseInstanceForReference(
              refVal.ProjectId, refVal.DatabaseId);
          return DeserializeReference(context, new DocumentReference(refVal.Path, targetDb));
        }
        case FirestoreValueKind.String:
          return DeserializeString(context, value.StringValue);
        case FirestoreValueKind.Timestamp:
          return DeserializeTimestamp(context, value.TimestampValue);
        default:
          throw new ArgumentException(String.Format("Unable to convert value type {0}", value.Kind));
      }
    }

    public abstract FirestoreValue Serialize(SerializationContext context, object value);

    public virtual void SerializeMap(
        SerializationContext context, object value, IDictionary<string, FirestoreValue> map) {
      throw new ArgumentException(String.Format("Unable to convert {0} to a map", TargetType));
    }

    protected virtual object DeserializeArray(DeserializationContext context, FirestoreValue arrayValue) {
      throw new ArgumentException(String.Format("Unable to convert array value to {0}", TargetType));
    }

    protected virtual object DeserializeBoolean(DeserializationContext context, bool value) {
      throw new ArgumentException(String.Format("Unable to convert Boolean value to {0}", TargetType));
    }

    protected virtual object DeserializeBytes(DeserializationContext context, byte[] value) {
      throw new ArgumentException(String.Format("Unable to convert bytes value to {0}", TargetType));
    }

    protected virtual object DeserializeDouble(DeserializationContext context, double value) {
      throw new ArgumentException(String.Format("Unable to convert double value to {0}", TargetType));
    }

    protected virtual object DeserializeGeoPoint(DeserializationContext context, GeoPoint value) {
      throw new ArgumentException(String.Format("Unable to convert GeoPoint value to {0}", TargetType));
    }

    protected virtual object DeserializeInteger(DeserializationContext context, long value) {
      throw new ArgumentException(String.Format("Unable to convert integer value to {0}", TargetType));
    }

    protected virtual object DeserializeReference(DeserializationContext context, DocumentReference value) {
      throw new ArgumentException(String.Format("Unable to convert DocumentReference value to {0}", TargetType));
    }

    protected virtual object DeserializeString(DeserializationContext context, string value) {
      throw new ArgumentException(String.Format("Unable to convert string value to {0}", TargetType));
    }

    protected virtual object DeserializeTimestamp(DeserializationContext context, Timestamp value) {
      throw new ArgumentException(String.Format("Unable to convert Timestamp value to {0}", TargetType));
    }
  }
}
