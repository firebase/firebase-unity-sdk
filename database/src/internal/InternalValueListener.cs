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

/// <summary>Helper class for ValueChanged events.</summary>
/// Each instance of this class will listen to one C++ Query and then forward the callback
/// to the C# event listeners registered with the C# Query.
internal sealed class InternalValueListener : InternalListener {
  // Delegate definitions for C++ -> C# callbacks.
  public delegate void OnValueChangedDelegate(int callbackId, System.IntPtr snapshot);
  public delegate void OnCancelledDelegate(int callbackId, Error error, string msg);

  // Gets the ValueListener for the given callbackId.
  private static bool TryGetListener(
      int callbackId, out InternalValueListener valueListener) {
    InternalListener listener = null;
    if (InternalListener.TryGetListener(callbackId, out listener)) {
      valueListener = listener as InternalValueListener;
      return valueListener != null;
    } else {
      valueListener = null;
      return false;
    }
  }

  [MonoPInvokeCallback(typeof(OnValueChangedDelegate))]
  private static void OnValueChangedHandler(int callbackId,
                                            System.IntPtr snapshot) {
    ExceptionAggregator.Wrap(() => {
        InternalDataSnapshot s = new InternalDataSnapshot(snapshot, true);
        EventHandler<ValueChangedEventArgs> handler = null;
        InternalValueListener listener = null;
        if (TryGetListener(callbackId, out listener)) {
          handler = listener.valueChangedImpl;
        }
        if (handler != null) {
          handler(null, new ValueChangedEventArgs(
              DataSnapshot.CreateSnapshot(s, listener.database)));
        } else {
          // If there's no listeners, we dispose the snapshot immediately.
          s.Dispose();
        }
      });
  }

  [MonoPInvokeCallback(typeof(OnCancelledDelegate))]
  private static void OnCancelledHandler(int callbackId,
                                         Error error, string msg) {
    ExceptionAggregator.Wrap(() => {
        EventHandler<ValueChangedEventArgs> handler = null;
        InternalValueListener listener;
        if (TryGetListener(callbackId, out listener)) {
          handler = listener.valueChangedImpl;
        }
        if (handler != null) {
          handler(null, new ValueChangedEventArgs(DatabaseError.FromError(error, msg)));
        }
      });
  }

  static InternalValueListener() {
    InternalQuery.RegisterValueListenerCallbacks(
        OnCancelledHandler, OnValueChangedHandler);
  }

  private object eventLock = new object();
  private InternalQuery internalQuery;
  // We manage the ValueListenerImpl's lifetime, so we need to keep a pointer.
  // We lazily create it when a first listener registers with this instance.
  private System.IntPtr cppListener = System.IntPtr.Zero;
  // We only keep a reference to the database to ensure that it's kept alive,
  // as the underlying C++ code needs that.
  private FirebaseDatabase database;

  public InternalValueListener(InternalQuery internalQuery, FirebaseDatabase database) {
    this.internalQuery = internalQuery;
    this.database = database;
  }

  protected override void CreateCppListener(int callbackId) {
    if (cppListener == System.IntPtr.Zero) {  // SHOULD be true
      cppListener = internalQuery.CreateValueListener(callbackId);
    }
  }

  protected override void DestroyCppListener() {
    if (cppListener != System.IntPtr.Zero) {
      InternalQuery.DestroyValueListener(cppListener);
      cppListener = System.IntPtr.Zero;
    }
  }

  protected override bool HasNoListeners() {
    return valueChangedImpl == null;
  }

  private event EventHandler<ValueChangedEventArgs> valueChangedImpl;
  public event EventHandler<ValueChangedEventArgs> ValueChanged {
    add {
      lock(eventLock) {
        BeforeAddingListener();
        valueChangedImpl += value;
      }
    }
    remove {
      lock(eventLock) {
        valueChangedImpl -= value;
        AfterRemovingListener();
      }
    }
  }
}

}
