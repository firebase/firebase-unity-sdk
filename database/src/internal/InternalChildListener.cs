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

/// <summary>Helper class for Child events (Added, Changed, Moved, Removed).</summary>
/// Each instance of this class will listen to one C++ Query and then forward the callback
/// to the C# event listeners registered with the C# Query.
internal sealed class InternalChildListener : InternalListener {
  // Delegate definitions for C++ -> C# callbacks.
  public delegate void OnCancelledDelegate(int callbackId, Error error, string msg);
  public delegate void OnChildChangeDelegate(int callbackId,
      ChildChangeType changeType, System.IntPtr snapshot, string previousChildName);
  public delegate void OnChildRemovedDelegate(int callbackId, System.IntPtr snapshot);

  // Gets the ChildListener for the given callbackId.
  private static bool TryGetListener(
      int callbackId, out InternalChildListener childListener) {
    InternalListener listener = null;
    if (InternalListener.TryGetListener(callbackId, out listener)) {
      childListener = listener as InternalChildListener;
      return childListener != null;
    } else {
      childListener = null;
      return false;
    }
  }

  [MonoPInvokeCallback(typeof(OnChildChangeDelegate))]
  private static void OnChildChangeHandler(int callbackId,
      ChildChangeType changeType, System.IntPtr snapshot, string previousChildName) {
    ExceptionAggregator.Wrap(() => {
        InternalDataSnapshot s = new InternalDataSnapshot(snapshot, true);
        EventHandler<ChildChangedEventArgs> handler = null;
        InternalChildListener listener = null;
        if (TryGetListener(callbackId, out listener)) {
          switch (changeType) {
            case ChildChangeType.Added: handler = listener.childAddedImpl; break;
            case ChildChangeType.Changed: handler = listener.childChangedImpl; break;
            case ChildChangeType.Moved: handler = listener.childMovedImpl; break;
          }
        }
        if (handler != null) {
          handler(null, new ChildChangedEventArgs(
              DataSnapshot.CreateSnapshot(s, listener.database), previousChildName));
        } else {
          // If there's no listeners, we dispose the snapshot immediately.
          // This also deletes the C++ snapshot, which we own.
          s.Dispose();
        }
      });
  }

  [MonoPInvokeCallback(typeof(OnChildRemovedDelegate))]
  private static void OnChildRemovedHandler(int callbackId,
                                            System.IntPtr snapshot) {
    ExceptionAggregator.Wrap(() => {
        InternalDataSnapshot s = new InternalDataSnapshot(snapshot, true);
        EventHandler<ChildChangedEventArgs> handler = null;
        InternalChildListener listener = null;
        if (TryGetListener(callbackId, out listener)) {
          handler = listener.childRemovedImpl;
        }
        if (handler != null) {
          handler(null, new ChildChangedEventArgs(
              DataSnapshot.CreateSnapshot(s, listener.database), null));
        } else {
          // If there's no listeners, we dispose the snapshot immediately.
          // This also deletes the C++ snapshot, which we own.
          s.Dispose();
        }
      });
  }

  [MonoPInvokeCallback(typeof(OnCancelledDelegate))]
  private static void OnCancelledHandler(int callbackId,
                                         Error error, string msg) {
    ExceptionAggregator.Wrap(() => {
        EventHandler<ChildChangedEventArgs> handler = null;
        InternalChildListener listener;
        if (TryGetListener(callbackId, out listener)) {
          handler = listener.cancelledImpl;
        }
        if (handler != null) {
          handler(null, new ChildChangedEventArgs(DatabaseError.FromError(error, msg)));
        }
      });
  }

  static InternalChildListener() {
    InternalQuery.RegisterChildListenerCallbacks(
        OnCancelledHandler, OnChildChangeHandler, OnChildRemovedHandler);
  }

  private object eventLock = new object();
  private InternalQuery internalQuery;
  // We manage the ValueListenerImpl's lifetime, so we need to keep a pointer.
  // We lazily create it when a first listener registers with this instance.
  private System.IntPtr cppListener = System.IntPtr.Zero;
  // We only keep a reference to the database to ensure that it's kept alive,
  // as the underlying C++ code needs that.
  private FirebaseDatabase database;

  public InternalChildListener(InternalQuery internalQuery, FirebaseDatabase database) {
    this.internalQuery = internalQuery;
    this.database = database;
  }

  protected override void CreateCppListener(int callbackId) {
    if (cppListener == System.IntPtr.Zero) {  // SHOULD be true
      cppListener = internalQuery.CreateChildListener(callbackId);
    }
  }

  protected override void DestroyCppListener() {
    if (cppListener != System.IntPtr.Zero) {
      InternalQuery.DestroyChildListener(cppListener);
      cppListener = System.IntPtr.Zero;
    }
  }

  protected override bool HasNoListeners() {
    // All listeners are added to the cancelledImpl event handler, so if it is
    // empty, there should be no listeners.
    return cancelledImpl == null;
  }

  // Handler for "cancelled" callbacks - given that there is only one C++ listner,
  // all C# child listeners will receive the "cancelled" callback at the same time.
  private event EventHandler<ChildChangedEventArgs> cancelledImpl;

  private event EventHandler<ChildChangedEventArgs> childAddedImpl;
  public event EventHandler<ChildChangedEventArgs> ChildAdded {
    add {
      lock(eventLock) {
        BeforeAddingListener();
        childAddedImpl += value;
        cancelledImpl += value;
      }
    }
    remove {
      lock(eventLock) {
        childAddedImpl -= value;
        cancelledImpl -= value;
        AfterRemovingListener();
      }
    }
  }

  public event EventHandler<ChildChangedEventArgs> childChangedImpl;
  public event EventHandler<ChildChangedEventArgs> ChildChanged {
    add {
      lock(eventLock) {
        BeforeAddingListener();
        childChangedImpl += value;
        cancelledImpl += value;
      }
    }
    remove {
      lock(eventLock) {
        childChangedImpl -= value;
        cancelledImpl -= value;
        AfterRemovingListener();
      }
    }
  }

  public event EventHandler<ChildChangedEventArgs> childMovedImpl;
  public event EventHandler<ChildChangedEventArgs> ChildMoved {
    add {
      lock(eventLock) {
        BeforeAddingListener();
        childMovedImpl += value;
        cancelledImpl += value;
      }
    }
    remove {
      lock(eventLock) {
        childMovedImpl -= value;
        cancelledImpl -= value;
        AfterRemovingListener();
      }
    }
  }

  public event EventHandler<ChildChangedEventArgs> childRemovedImpl;
  public event EventHandler<ChildChangedEventArgs> ChildRemoved {
    add {
      lock(eventLock) {
        BeforeAddingListener();
        childRemovedImpl += value;
        cancelledImpl += value;
      }
    }
    remove {
      lock(eventLock) {
        childRemovedImpl -= value;
        cancelledImpl -= value;
        AfterRemovingListener();
      }
    }
  }
}

}
