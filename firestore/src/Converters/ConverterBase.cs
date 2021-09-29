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
using System.Runtime.InteropServices;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  /// <summary>
  /// A base class for many converters, allowing them to simply override the method for the one
  /// deserialization case required. Serializing to a single value has to be implemented; serializing
  /// to a map will fail with an exception unless <see cref="SerializeMap(SerializationContext, object, IDictionary{string, FieldValueProxy})"/>
  /// is overridden.
  /// </summary>
  internal abstract class ConverterBase : IFirestoreInternalConverter {
    protected readonly BclType TargetType;

    protected ConverterBase(BclType targetType) {
      TargetType = targetType;
    }

    public // TODO(b/141568173): Significant newline.
    virtual object DeserializeMap(DeserializationContext context, FieldValueProxy mapValue) {
      throw new ArgumentException(String.Format("Unable to convert map value to {0}", TargetType));
    }

    public // TODO(b/141568173): Significant newline.
    virtual object DeserializeValue(DeserializationContext context, FieldValueProxy value) {
      switch (value.type()) {
        case FieldValueProxy.Type.Array:
          return DeserializeArray(context, value);
        case FieldValueProxy.Type.Boolean:
          return DeserializeBoolean(context, value.boolean_value());
        case FieldValueProxy.Type.Blob:
          return DeserializeBytes(context, ConvertFromProxyBlob(value));
        case FieldValueProxy.Type.Double:
          return DeserializeDouble(context, value.double_value());
        case FieldValueProxy.Type.GeoPoint:
          return DeserializeGeoPoint(context, GeoPoint.ConvertFromProxy(value.geo_point_value()));
        case FieldValueProxy.Type.Integer:
          return DeserializeInteger(context, value.integer_value());
        case FieldValueProxy.Type.Map:
          return DeserializeMap(context, value);
        case FieldValueProxy.Type.Reference:
          return DeserializeReference(context, new DocumentReference(value.reference_value(), context.Firestore));
        case FieldValueProxy.Type.String:
          return DeserializeString(context, value.string_value());
        case FieldValueProxy.Type.Timestamp:
          return DeserializeTimestamp(context, Timestamp.ConvertFromProxy(value.timestamp_value()));
        default:
          throw new ArgumentException(String.Format("Unable to convert value type {0}", value.type()));
      }
    }

    public // TODO(b/141568173): Significant newline.
    abstract FieldValueProxy Serialize(SerializationContext context, object value);

    public virtual void SerializeMap(SerializationContext context, object value, IDictionary<string, FieldValueProxy> map) {
      throw new ArgumentException(String.Format("Unable to convert {0} to a map", TargetType));
    }

    protected virtual object DeserializeArray(DeserializationContext context, FieldValueProxy arrayValue) {
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

    // Some helpers for marshalling C# types to/from FieldValueProxy
    // (since the SWIG-generated methods aren't very friendly).

    internal static FieldValueProxy ConvertToProxyMap(IDictionary<string, FieldValueProxy> map) {
      var internalMap = new FieldToValueMap();
      foreach (var key_value in map) {
        internalMap.Insert(key_value.Key, key_value.Value);
      }
      return FirestoreCpp.ConvertMapToFieldValue(internalMap);
    }

    internal static FieldValueProxy ConvertToProxyArray(IList<FieldValueProxy> list) {
      var internalArray = new FieldValueVector();
      for (int i = 0; i < list.Count; ++i) {
        internalArray.PushBack(list[i]);
      }
      return FirestoreCpp.ConvertVectorToFieldValue(internalArray);
    }

    internal static byte[] ConvertFromProxyBlob(FieldValueProxy internalValue) {
      IntPtr ptr = SWIGTYPE_p_unsigned_char.getCPtr(internalValue.blob_value()).Handle;
      uint size = internalValue.blob_size();
      byte[] bytes = new byte[size];
      Marshal.Copy(ptr, bytes, 0, (int)size);
      return bytes;
    }

    internal static FieldValueProxy ConvertToProxyBlob(byte[] value) {
      int size = Marshal.SizeOf((byte)0) * (int)value.Length;
      IntPtr ptr = (IntPtr)Marshal.AllocHGlobal(size);
      try {
        Marshal.Copy(value, 0, ptr, size);
        FieldValueProxy result = FieldValueProxy.Blob(
            new SWIGTYPE_p_unsigned_char(ptr, /*futureUse=*/true),
            (uint)size);
        return result;
      } finally {
        // We have no way to reset the one in SWIGTYPE_p_unsigned_char.  But as far as we do
        // not use it, it is safe to keep the ptr there.
        Marshal.FreeHGlobal(ptr);
      }
    }
  }
}
