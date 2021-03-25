/*
 * Copyright 2018 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;

namespace Firebase.Database.Internal {

/// <summary>Helper class that handles the gory details of C++ -> C# callbacks.</summary>
/// This base class mostly handles the lifetime of listener instances.
/// We want instances of this class to stay alive for as long as:
///   1) The query that created it is alive
///   2) At least one listener is attached to it
///      This is because the typical use case is to attach a long-term listener
///      to a temporary query.
internal abstract class InternalListener : IDisposable {
  // To be supported on all platforms, cross-language callbacks need to be static,
  // so we use a callback_id to find the actual handler of the callback
  // As the map also keeps the listener alive, instances are responsible for making
  // sure they are in the map if and only if they have listeners attached to them.
  private static int uidGenerator = 0;
  private static Dictionary<int, InternalListener> databaseCallbacks =
      new Dictionary<int, InternalListener>();

  // Allow subclasses to query the databaseCallbacks map.
  protected static bool TryGetListener(int uid, out InternalListener listener) {
    lock (databaseCallbacks) {
      return databaseCallbacks.TryGetValue(uid, out listener);
    }
  }

  private int uid;

  public InternalListener() {
    lock (databaseCallbacks) {
      uid = uidGenerator++;
    }
  }

  ~InternalListener() {
    Dispose();
  }

  public void Dispose() {
    DestroyCppListener();
    lock (databaseCallbacks) {
      databaseCallbacks.Remove(uid);
    }
    GC.SuppressFinalize(this);
  }

  // The child classes know how to create the C++ listener they need.
  protected abstract void CreateCppListener(int callbackId);
  protected abstract void DestroyCppListener();

  // Returns true when there are no listeners attached to this instance.
  protected abstract bool HasNoListeners();

  // This should be called by child classes before adding a listener.
  // This method together with afterRemovingListener handles the registration
  // in the callback map and therefore the lifetime of this instance.
  protected void BeforeAddingListener() {
    if (HasNoListeners()) {  // Adds a first listener
      lock (databaseCallbacks) {
        // First, register this in the callback registry.
        // This will allow callbacks to find this instance and also keep this
        // alive for as long as there are listeners.
        databaseCallbacks[uid] = this;
      }
      CreateCppListener(uid);
    }
  }

  // This should be called by child classes after removing a listener.
  protected void AfterRemovingListener() {
    if (HasNoListeners()) {  // Removed last listeners
      DestroyCppListener();
      lock (databaseCallbacks) {
        // Also remove this from the callback registry.
        // We do this now to potentially make us eligible for garbage collection.
        databaseCallbacks.Remove(uid);
      }
    }
  }
}

}
