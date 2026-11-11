// Copyright 2019 Google LLC
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
using System.Linq.Expressions;
using System.Reflection;
using Firebase.Firestore.Internal;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class AttributedIdAssigner {
    private static readonly Dictionary<BclType, AttributedIdAssigner> s_assigners =
        new Dictionary<BclType, AttributedIdAssigner>();

    private readonly List<Action<object, string>> _idSetters;
    private readonly List<Action<object, DocumentReference>> _refSetters;

    private AttributedIdAssigner(
        List<Action<object, string>> idSetters,
        List<Action<object, DocumentReference>> refSetters) {
      _idSetters = idSetters;
      _refSetters = refSetters;
    }

    private void AssignId(object value, DocumentReference reference) {
      if (_idSetters.Count > 0) {
        string id = reference.Id;
        for (int i = 0; i < _idSetters.Count; i++) {
          _idSetters[i](value, id);
        }
      }
      for (int i = 0; i < _refSetters.Count; i++) {
        _refSetters[i](value, reference);
      }
    }

    internal static void MaybeAssignId(object value, DocumentReference reference) {
      if (value == null) return;
      AttributedIdAssigner assigner;
      BclType type = value.GetType();
      lock (s_assigners) {
        if (!s_assigners.TryGetValue(type, out assigner)) {
          assigner = MaybeCreateAssigner(type);
          s_assigners[type] = assigner;
        }
      }
      assigner?.AssignId(value, reference);
    }

    /// <summary>
    /// Returns an "ID assigner" suitable for the given BCL type, or null if the type isn't decorated with FirestoreDataAttribute,
    /// or doesn't have any FirestoreDocumentId attributes.
    /// </summary>
    /// <param name="type">The type to inspect for ID properties.</param>
    /// <returns>An assigner, or null if the type doesn't need any ID assignments performed on it.</returns>
    private static AttributedIdAssigner MaybeCreateAssigner(BclType type) {
      if (!type.IsDefined(typeof(FirestoreDataAttribute), inherit: true)) {
        return null;
      }

      // While it's unusual, it's not an error to have multiple properties decorated with [FirestoreDocumentId]
      var idSetters = new List<Action<object, string>>();
      var refSetters = new List<Action<object, DocumentReference>>();
      // We look for static properties specifically to find problems. We'll never use static properties.
      foreach (PropertyInfo property in type.GetProperties(
          BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        string typeName = property.DeclaringType.FullName;
        if (!property.IsDefined(typeof(FirestoreDocumentIdAttribute), inherit: true)) {
          continue;
        }
        MethodInfo setMethod = property.GetSetMethod(nonPublic: true);
        Preconditions.CheckState(setMethod != null,
            "{0}.{1} has no setter, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestoreDocumentIdAttribute));
        Preconditions.CheckState(!setMethod.IsStatic,
            "{0}.{1} is static, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestoreDocumentIdAttribute));
        Preconditions.CheckState(setMethod.GetParameters().Length == 1,
            "{0}.{1} is an indexer, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestoreDocumentIdAttribute));
        Preconditions.CheckState(!property.IsDefined(typeof(FirestorePropertyAttribute), inherit: true),
            "{0}.{1} is decorated with FirestorePropertyAttribute, and should not be decorated with {2}.",
            typeName, property.Name, nameof(FirestoreDocumentIdAttribute));
        if (property.PropertyType == typeof(string)) {
          idSetters.Add(BuildSetter<string>(type, property, setMethod));
        } else if (property.PropertyType == typeof(DocumentReference)) {
          refSetters.Add(BuildSetter<DocumentReference>(type, property, setMethod));
        } else {
          throw new InvalidOperationException(String.Format(
              "{0}.{1} is not a string or {2} property, and should not be decorated with {3}.",
              typeName, property.Name, nameof(DocumentReference), nameof(FirestoreDocumentIdAttribute)));
        }
      }
      return idSetters.Count == 0 && refSetters.Count == 0
          ? null
          : new AttributedIdAssigner(idSetters, refSetters);
    }

    private static Action<object, TProp> BuildSetter<TProp>(
        BclType declaringType, PropertyInfo prop, MethodInfo setMethod) {
      if (!declaringType.IsValueType) {
        try {
          ParameterExpression targetParam = Expression.Parameter(typeof(object), "target");
          ParameterExpression valParam = Expression.Parameter(typeof(TProp), "val");
          MethodCallExpression call = Expression.Call(
              Expression.Convert(targetParam, declaringType), setMethod, valParam);
          return Expression.Lambda<Action<object, TProp>>(call, targetParam, valParam).Compile();
        } catch (Exception) {
          // Fall back to PropertyInfo on AOT runtimes without dynamic codegen.
        }
      }
      return (target, val) => prop.SetValue(target, val, index: null);
    }
  }
}
