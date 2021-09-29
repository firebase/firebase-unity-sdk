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

using Firebase.Firestore.Internal;
using System;
using System.Collections.Generic;
using System.Reflection;
using BclType = System.Type;

namespace Firebase.Firestore.Converters {
  internal sealed class AttributedIdAssigner {
    private static readonly Dictionary<BclType, AttributedIdAssigner> s_assigners =
        new Dictionary<BclType, AttributedIdAssigner>();

    private readonly List<PropertyInfo> _idProperties;
    private readonly List<PropertyInfo> _referenceProperties;

    private AttributedIdAssigner(List<PropertyInfo> idProperties, List<PropertyInfo> referenceProperties) {
      _idProperties = idProperties;
      _referenceProperties = referenceProperties;
    }

    private void AssignId(object value, DocumentReference reference) {
      // TODO: Create delegates instead of using reflection on every invocation?

      foreach (var property in _idProperties) {
        property.SetValue(value, reference.Id, index: null);
      }

      foreach (var property in _referenceProperties) {
        property.SetValue(value, reference, index: null);
      }
    }

    internal static void MaybeAssignId(object value, DocumentReference reference) {
      if (value == null) {
        return;
      }
      lock (s_assigners) {
        AttributedIdAssigner assigner;
        if (!s_assigners.TryGetValue(value.GetType(), out assigner)) {
          assigner = MaybeCreateAssigner(value.GetType());
          s_assigners[value.GetType()] = assigner;
        }
        assigner?.AssignId(value, reference);
      }
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
      List<PropertyInfo> setIdProperties = new List<PropertyInfo>();
      List<PropertyInfo> setReferenceProperties = new List<PropertyInfo>();
      // We look for static properties specifically to find problems. We'll never use static properties.
      foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
        string typeName = property.DeclaringType.FullName;
        if (!property.IsDefined(typeof(FirestoreDocumentIdAttribute), inherit: true)) {
          continue;
        }
        var setMethod = property.GetSetMethod(nonPublic: true);
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
          setIdProperties.Add(property);
        } else if (property.PropertyType == typeof(DocumentReference)) {
          setReferenceProperties.Add(property);
        } else {
          throw new InvalidOperationException(String.Format(
              "{0}.{1} is not a string or {2} property, and should not be decorated with {3}.",
              typeName, property.Name, nameof(DocumentReference), nameof(FirestoreDocumentIdAttribute)));
        }
      }
      return setIdProperties.Count == 0 && setReferenceProperties.Count == 0
          ? null : new AttributedIdAssigner(setIdProperties, setReferenceProperties);
    }
  }
}
