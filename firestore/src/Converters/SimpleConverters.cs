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

namespace Firebase.Firestore.Converters {
  // All the simple converters in a single place for simplicity.

  internal sealed class StringConverter : ConverterBase {
    internal StringConverter() : base(typeof(string)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.String((string)value);
    protected override object DeserializeString(DeserializationContext context, string value) => value;
  }

  internal abstract class IntegerConverterBase : ConverterBase {
    internal IntegerConverterBase(System.Type type) : base(type) {
    }

    // All integer types allow conversion from double as well.
    protected override object DeserializeDouble(DeserializationContext context, double value) =>
        DeserializeInteger(context, checked((long)value));
  }

  internal sealed class ByteConverter : IntegerConverterBase {
    internal ByteConverter() : base(typeof(byte)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((byte)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((byte)value);
  }

  internal sealed class SByteConverter : IntegerConverterBase {
    internal SByteConverter() : base(typeof(sbyte)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((sbyte)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((sbyte)value);
  }

  internal sealed class Int16Converter : IntegerConverterBase {
    internal Int16Converter() : base(typeof(short)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((short)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((short)value);
  }

  internal sealed class UInt16Converter : IntegerConverterBase {
    internal UInt16Converter() : base(typeof(ushort)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((ushort)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((ushort)value);
  }

  internal sealed class Int32Converter : IntegerConverterBase {
    internal Int32Converter() : base(typeof(int)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((int)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((int)value);
  }

  internal sealed class UInt32Converter : IntegerConverterBase {
    internal UInt32Converter() : base(typeof(uint)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((uint)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((uint)value);
  }

  internal sealed class Int64Converter : IntegerConverterBase {
    internal Int64Converter() : base(typeof(long)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((long)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => value;
  }

  internal sealed class UInt64Converter : IntegerConverterBase {
    internal UInt64Converter() : base(typeof(ulong)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Integer((long)(ulong)value);
    protected override object DeserializeInteger(DeserializationContext context, long value) => checked((ulong)value);
  }

  internal sealed class SingleConverter : ConverterBase {
    internal SingleConverter() : base(typeof(float)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Double((float)value);
    protected override object DeserializeDouble(DeserializationContext context, double value) => (float)value;
    // We allow serialization from integer values as some interactions with Firestore end up storing
    // an integer value even when a double value is expected, if the value happens to be an integer.
    // See https://github.com/googleapis/google-cloud-dotnet/issues/3013
    protected override object DeserializeInteger(DeserializationContext context, long value) => (float)value;
  }

  internal sealed class DoubleConverter : ConverterBase {
    internal DoubleConverter() : base(typeof(double)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Double((double)value);
    protected override object DeserializeDouble(DeserializationContext context, double value) => value;
    // We allow serialization from integer values as some interactions with Firestore end up storing
    // an integer value even when a double value is expected, if the value happens to be an integer.
    // See https://github.com/googleapis/google-cloud-dotnet/issues/3013
    protected override object DeserializeInteger(DeserializationContext context, long value) => (double)value;
  }

  internal sealed class BooleanConverter : ConverterBase {
    internal BooleanConverter() : base(typeof(bool)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => FieldValueProxy.Boolean((bool)value);
    protected override object DeserializeBoolean(DeserializationContext context, bool value) => value;
  }

  internal sealed class TimestampConverter : ConverterBase {
    internal TimestampConverter() : base(typeof(Timestamp)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        FieldValueProxy.Timestamp(((Timestamp)value).ConvertToProxy());
    protected override object DeserializeTimestamp(DeserializationContext context, Timestamp value) => value;
  }

  internal sealed class GeoPointConverter : ConverterBase {
    internal GeoPointConverter() : base(typeof(GeoPoint)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        FieldValueProxy.GeoPoint(((GeoPoint)value).ConvertToProxy());
    protected override object DeserializeGeoPoint(DeserializationContext context, GeoPoint value) => value;
  }

  internal sealed class ByteArrayConverter : ConverterBase {
    internal ByteArrayConverter() : base(typeof(byte[])) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        ConvertToProxyBlob((byte[])value);
    protected override object DeserializeBytes(DeserializationContext context, byte[] value) => value;
  }

  internal sealed class BlobConverter : ConverterBase {
    internal BlobConverter() : base(typeof(Blob)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        ConvertToProxyBlob(((Blob)value).ToBytes());
    protected override object DeserializeBytes(DeserializationContext context, byte[] value) => Blob.CopyFrom(value);
  }

  // Note that we need this FieldValueProxy "pass-thru" converter because we (currently) expose
  // FieldValue sentinels as the raw FieldValueProxy value. (e.g. FieldValue.ServerTimestamp
  // just returns FieldValueProxy.ServerTimestamp())
  internal sealed class FieldValueProxyConverter : ConverterBase {
    internal FieldValueProxyConverter() : base(typeof(FieldValueProxy)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) => ((FieldValueProxy)value);
  }

  internal sealed class DateTimeConverter : ConverterBase {
    internal DateTimeConverter() : base(typeof(DateTime)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        FieldValueProxy.Timestamp(Timestamp.FromDateTime((DateTime)value).ConvertToProxy());
    protected override object DeserializeTimestamp(DeserializationContext context, Timestamp value) => value.ToDateTime();
  }

  internal sealed class DateTimeOffsetConverter : ConverterBase {
    internal DateTimeOffsetConverter() : base(typeof(DateTimeOffset)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        FieldValueProxy.Timestamp(Timestamp.FromDateTimeOffset((DateTimeOffset)value).ConvertToProxy());
    protected override object DeserializeTimestamp(DeserializationContext context, Timestamp value) => value.ToDateTimeOffset();
  }

  internal sealed class DocumentReferenceConverter : ConverterBase {
    internal DocumentReferenceConverter() : base(typeof(DocumentReference)) { }
    public // TODO(b/141568173): Significant newline.
    override FieldValueProxy Serialize(SerializationContext context, object value) =>
        FieldValueProxy.Reference(((DocumentReference)value).Proxy);
    protected override object DeserializeReference(DeserializationContext context, DocumentReference value) =>
        value;
  }
}
