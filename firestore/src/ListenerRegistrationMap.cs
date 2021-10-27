// Copyright 2020 Google LLC
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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Firebase.Firestore {
  /// <summary>
  /// An helper interface to pass around an instance of ListenerRegistrationMap without
  /// needing to know the generic argument for a given instance.
  /// </summary>
  internal interface IListenerRegistrationMap {
    void Unregister(int uid);
  }

  /// <summary>
  /// A thread safe helper class that tracks callbacks and provides a unique id to reference
  /// the registation of each one. This is meant to be used by classes that interop with C++
  /// that expose listeners such as DocumentReference, CollectionReference, etc.
  /// </summary>
  internal class ListenerRegistrationMap<T> : IListenerRegistrationMap where T : class {
    private int uidGenerator = 0;
    private Dictionary<int, Tuple<object, T>> callbacks = new Dictionary<int, Tuple<object, T>>();

    public ListenerRegistrationMap() {
      AssertGenericArgumentIsDelegate();
    }

    private void AssertGenericArgumentIsDelegate() {
      // C# doesn't let you specify Delegate as a generic constraint.
      // It can only be verified at runtime.
      if (!typeof(T).IsSubclassOf(typeof(Delegate))) {
        throw new ArgumentOutOfRangeException("The generic argument for the " +
            GetType().Name + " must be a delegate. This is an error " +
            "in your code.");
      }
    }

    /// <summary>
    /// Registers a callback and returns a unique id that can be used to look it back up later.
    /// </summary>
    public int Register(object owner, T callback) {
      lock (callbacks) {
        int uid = uidGenerator++;
        callbacks[uid] = new Tuple<object, T>(owner, callback);
        return uid;
      }
    }

    /// <summary>
    /// Looks up a callback based on a unique id.
    /// </summary>
    public bool TryGetCallback(int uid, out T callback) {
      lock (callbacks) {
        Tuple<object, T> item;

        if (!callbacks.TryGetValue(uid, out item)) {
          callback = null;
          return false;
        } else {
          callback = item.Item2;
          return true;
        }
      }
    }

    /// <summary>
    /// Unregisters a callback based on its id. Passing an id that does not exist has no effect.
    /// </summary>
    public void Unregister(int uid) {
      lock (callbacks) {
        callbacks.Remove(uid);
      }
    }

    /// <summary>
    /// Clears all callbacks associated with a provided owner.
    /// </summary>
    public void ClearCallbacksForOwner(object owner) {
      lock (callbacks) {
        var uidsForOwner = new List<int>();

        foreach (var item in callbacks) {
          if (Equals(owner, item.Value.Item1)) {
            uidsForOwner.Add(item.Key);
          }
        }

        foreach (int uid in uidsForOwner) {
          Unregister(uid);
        }
      }
    }
  }
}
