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

using Firebase.Firestore.Converters;
using Firebase.Firestore.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using BclType = System.Type;

namespace Firebase.Firestore {
  /// <summary>
  /// TODO(unity): Currently we don't expose this class and so it's basically dead code, but
  /// keeping it around for parity with google-cloud-dotnet.
  ///
  /// Registry of custom converters. This is used to avoid requiring Firestore-specific attributes on types
  /// which may be intended to be non-Firestore-specific.
  /// </summary>
  internal sealed class ConverterRegistry : IEnumerable {
    // Only present so we can iterate in a vaguely sensible way.
    private readonly List<object> _converterList = new List<object>();
    private readonly Dictionary<BclType, IFirestoreInternalConverter> _converters =
        new Dictionary<BclType, IFirestoreInternalConverter>();

    /// <summary>
    /// Adds the given converter to the registry.
    /// </summary>
    /// <typeparam name="T">The </typeparam>
    /// <param name="converter">The converter to add.</param>
    /// <exception cref="ArgumentException">There is already a converter in the registry for the given type.</exception>
    public void Add<T>(FirestoreConverter<T> converter) {
      Preconditions.CheckNotNull(converter, nameof(converter));
      _converters.Add(typeof(T), new CustomConverter<T>(converter));
      _converterList.Add(converter);
    }

    // We only really implement IEnumerable for the sake of collection initializers, but we're at least
    // vaguely pleasant about it.
    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => _converterList.GetEnumerator();

    internal IDictionary<BclType, IFirestoreInternalConverter> ToConverterDictionary() {
      // Clone the dictionary so that any further changes are ignored, and we don't have any thread
      // safety concerns. (We'll only be reading from the returned dictionary.)
      return new Dictionary<BclType, IFirestoreInternalConverter>(_converters);
    }
  }
}
