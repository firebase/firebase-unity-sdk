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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  /// <summary>
  /// A cache for serializers based on the target type. Some are prepopulated (e.g. for
  /// primitives); one for anonymous and attributed types are added on demand.
  /// </summary>
  internal static class ConverterCache {
    private static readonly Dictionary<BclType, IFirestoreInternalConverter> s_converters =
        new Dictionary<BclType, IFirestoreInternalConverter> {
          [typeof(byte)] = new ByteConverter(),
          [typeof(sbyte)] = new SByteConverter(),
          [typeof(short)] = new Int16Converter(),
          [typeof(ushort)] = new UInt16Converter(),
          [typeof(int)] = new Int32Converter(),
          [typeof(uint)] = new UInt32Converter(),
          [typeof(long)] = new Int64Converter(),
          [typeof(ulong)] = new UInt64Converter(),
          [typeof(string)] = new StringConverter(),
          [typeof(float)] = new SingleConverter(),
          [typeof(double)] = new DoubleConverter(),
          [typeof(bool)] = new BooleanConverter(),
          [typeof(Timestamp)] = new TimestampConverter(),
          [typeof(GeoPoint)] = new GeoPointConverter(),
          [typeof(Blob)] = new BlobConverter(),
          [typeof(byte[])] = new ByteArrayConverter(),
          [typeof(FieldValueProxy)] = new FieldValueProxyConverter(),
          [typeof(DateTime)] = new DateTimeConverter(),
          [typeof(DateTimeOffset)] = new DateTimeOffsetConverter(),
          // DictionaryConverter parameterized with primitive value types are created by default, because they do not
          // work otherwise on iOS, as they are compiled AOT, and compiler does not generate code for these types.
          [typeof(Dictionary<string, byte>)] = new DictionaryConverter<byte>(typeof(Dictionary<string, byte>)),
          [typeof(Dictionary<string, sbyte>)] = new DictionaryConverter<sbyte>(typeof(Dictionary<string, sbyte>)),
          [typeof(Dictionary<string, short>)] = new DictionaryConverter<short>(typeof(Dictionary<string, short>)),
          [typeof(Dictionary<string, ushort>)] = new DictionaryConverter<ushort>(typeof(Dictionary<string, ushort>)),
          [typeof(Dictionary<string, int>)] = new DictionaryConverter<int>(typeof(Dictionary<string, int>)),
          [typeof(Dictionary<string, uint>)] = new DictionaryConverter<uint>(typeof(Dictionary<string, uint>)),
          [typeof(Dictionary<string, long>)] = new DictionaryConverter<long>(typeof(Dictionary<string, long>)),
          [typeof(Dictionary<string, ulong>)] = new DictionaryConverter<ulong>(typeof(Dictionary<string, ulong>)),
          [typeof(Dictionary<string, float>)] = new DictionaryConverter<float>(typeof(Dictionary<string, float>)),
          [typeof(Dictionary<string, double>)] = new DictionaryConverter<double>(typeof(Dictionary<string, double>)),
          [typeof(Dictionary<string, bool>)] = new DictionaryConverter<bool>(typeof(Dictionary<string, bool>)),
          [typeof(Dictionary<string, object>)] = new DictionaryConverter<object>(typeof(Dictionary<string, object>)),
          [typeof(IDictionary<string, byte>)] = new DictionaryConverter<byte>(typeof(Dictionary<string, byte>)),
          [typeof(IDictionary<string, sbyte>)] = new DictionaryConverter<sbyte>(typeof(Dictionary<string, sbyte>)),
          [typeof(IDictionary<string, short>)] = new DictionaryConverter<short>(typeof(Dictionary<string, short>)),
          [typeof(IDictionary<string, ushort>)] = new DictionaryConverter<ushort>(typeof(Dictionary<string, ushort>)),
          [typeof(IDictionary<string, int>)] = new DictionaryConverter<int>(typeof(Dictionary<string, int>)),
          [typeof(IDictionary<string, uint>)] = new DictionaryConverter<uint>(typeof(Dictionary<string, uint>)),
          [typeof(IDictionary<string, long>)] = new DictionaryConverter<long>(typeof(Dictionary<string, long>)),
          [typeof(IDictionary<string, ulong>)] = new DictionaryConverter<ulong>(typeof(Dictionary<string, ulong>)),
          [typeof(IDictionary<string, float>)] = new DictionaryConverter<float>(typeof(Dictionary<string, float>)),
          [typeof(IDictionary<string, double>)] = new DictionaryConverter<double>(typeof(Dictionary<string, double>)),
          [typeof(IDictionary<string, bool>)] = new DictionaryConverter<bool>(typeof(Dictionary<string, bool>)),
          [typeof(IDictionary<string, object>)] = new DictionaryConverter<object>(typeof(Dictionary<string, object>)),
          [typeof(DocumentReference)] = new DocumentReferenceConverter()
        };

    internal static IFirestoreInternalConverter GetConverter(BclType targetType) {
      lock (s_converters) {
        IFirestoreInternalConverter converter;
        if (!s_converters.TryGetValue(targetType, out converter)) {
          converter = CreateConverter(targetType);
          s_converters[targetType] = converter;
        }
        return converter;
      }
    }

    /// <summary>
    /// This method must be called at least once to ensure that CreateDictionaryConverter is
    /// statically referenced to ensure that AOT targets generate code for it; otherwise,
    /// s_createDictionaryConverter will be null on those platforms (b/159051783).  Since this
    /// method performs a small amount of work, it should be invoked as infrequently as possible,
    /// such as each time that a Firestore instance is created.
    /// See https://docs.unity3d.com/Manual/ScriptingRestrictions.html for details.
    /// </summary>
    internal static void InitializeConverterCache() {
      // Using "bool" here is arbitrary; it does not matter which type is used as long as the
      // method is invoked at least once to preserve it for AOT compilation.
      CreateDictionaryConverter<bool>(typeof(bool));
    }

    private static readonly MethodInfo s_createDictionaryConverter = typeof(ConverterCache).GetMethod(nameof(CreateDictionaryConverter), BindingFlags.Static | BindingFlags.NonPublic);

    private static IFirestoreInternalConverter CreateDictionaryConverter<T>(BclType targetType) => new DictionaryConverter<T>(targetType);

    private static IFirestoreInternalConverter CreateConverter(BclType targetType) {
      if (targetType.IsArray) {
        return new ArrayConverter(targetType.GetElementType());
      }
      if (targetType.IsDefined(typeof(FirestoreDataAttribute), inherit: true)) {
        return AttributedTypeConverter.ForType(targetType);
      }
      // Simple way of checking for an anonymous type. Far from foolproof, but a reasonable start.
      if (targetType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: true)) {
        return new AnonymousTypeConverter(targetType);
      }

      if (targetType.IsEnum) {
        return new EnumConverter(targetType);
      }
      if (TryGetStringDictionaryValueType(targetType, out var dictionaryElementType)) {
        var method = s_createDictionaryConverter.MakeGenericMethod(dictionaryElementType);
        try {
          return (IFirestoreInternalConverter)method.Invoke(null, new object[] { targetType });
        } catch (TargetInvocationException e) {
          if (e.InnerException != null) {
            throw e.InnerException;
          } else {
            throw;
          }
        }
      }

      if (typeof(IEnumerable).IsAssignableFrom(targetType)) {
        return new EnumerableConverter(targetType);
      }

      throw new ArgumentException(String.Format("Unable to create converter for type {0}", targetType.FullName));
    }

    // Internal for testing

    /// <summary>
    /// If <paramref name="type"/> implements (or is) <see cref="IDictionary{TKey, TValue}"/> with TKey equal to string, returns true and sets
    /// <paramref name="elementType"/> to TValue. Otherwise, returns false and sets <paramref name="elementType"/> to null.
    /// </summary>
    internal static bool TryGetStringDictionaryValueType(BclType type, out BclType elementType) {
      elementType = type
          .GetInterfaces()
          .Concat(new[] { type }) // Make this method handle IDictionary<,> as an input; GetInterfaces doesn't return the type you call it on
          .Select(MapInterfaceToDictionaryValueTypeArgument).FirstOrDefault(t => t != null);
      return elementType != null;
    }

    private static BclType MapInterfaceToDictionaryValueTypeArgument(BclType iface) {
      if (!iface.IsGenericType || iface.IsGenericTypeDefinition) {
        return null;
      }
      var generic = iface.GetGenericTypeDefinition();
      if (generic != typeof(IDictionary<,>)) {
        return null;
      }
      var typeArguments = iface.GetGenericArguments();
      return typeArguments[0] == typeof(string) ? typeArguments[1] : null;
    }
  }
}
