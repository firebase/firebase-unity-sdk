/*
 * Copyright 2016 Google LLC
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

using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;

namespace Firebase.Unity {
  /**
   * This class is installed as the .Net SynchronizationContext if none
   * is already installed.  A SynchronizationContext is used to marshal calls
   * to the thread from other contexts.  This unity specific version uses
   * a co-routine to process a queue of delegate callbacks.  A gameobject
   * must be passed in for a behavior to be added to for processing.
   * TODO(benwu): create a service container in App and move this there.
   */
  [UnityEngine.Scripting.Preserve]
  internal class UnitySynchronizationContext : SynchronizationContext {
    // The single static instance of the Unity SynchronizationContext
    static UnitySynchronizationContext _instance = null;
    // The queue of callbacks to execute.
    private Queue<Tuple<SendOrPostCallback, object>> queue;
    // The behavior that manages the coroutine to pulls off the queue.
    private SynchronizationContextBehavoir behavior;
    // A id of the main thread so we can detect Send calls already on
    // the main thread.  These get executed immediately.
    private int mainThreadId;
    // Default timeout for wainting on the manualresetevent for Send calls.
    private const int Timeout = 15000;
    // A cache of ManualResetEvents that we use to implement Send calls.
    private static Dictionary<int, ManualResetEvent> signalDictionary
                        = new Dictionary<int, ManualResetEvent>();


    private UnitySynchronizationContext(GameObject gameObject) {
      mainThreadId = Thread.CurrentThread.ManagedThreadId;
      behavior = gameObject.AddComponent<SynchronizationContextBehavoir>();
      queue = behavior.CallbackQueue;
    }

    public static UnitySynchronizationContext Instance {
      get {
        if (_instance == null) {
          throw new InvalidOperationException("SyncContext not initialized.");
        }
        return _instance;
      }
    }

    /// <summary>
    /// This method creates a synchronization context for Unity's main thread.
    /// As such, it should only be called from the main thread and it is not
    /// thread safe.
    /// </summary>
    public static void Create(GameObject gameObject) {
      if (_instance == null) {
        _instance = new UnitySynchronizationContext(gameObject);
      }
    }

    /// <summary>
    /// This method destroys a synchronization context for Unity's main thread.
    /// As such, it should only be called from the main thread and it is not
    /// thread safe.
    /// If something is still holding a reference to the _instance that thing
    /// will no longer work. Therefore "Destroy" should only be called if all
    /// other Firebase objects are dead.
    /// </summary>
    public static void Destroy() {
      _instance = null;
    }

    private ManualResetEvent GetThreadEvent() {
      Debug.Assert(signalDictionary != null);
      ManualResetEvent newSignal;

      lock (signalDictionary) {
        // We use a single manualresetevent per thread to avoid a lot of
        // unnecessary allocs of signals.
        if (!signalDictionary.TryGetValue(Thread.CurrentThread.ManagedThreadId,
            out newSignal)) {
          newSignal = new ManualResetEvent(false);
          signalDictionary[Thread.CurrentThread.ManagedThreadId] = newSignal;
        }
      }
      newSignal.Reset();
      return newSignal;
    }

    /// <summary>
    /// Starts a coroutine on the unity main thread.
    /// This can be useful for performing complex operations interleaved
    /// on the main unity thread.
    /// </summary>
    public void PostCoroutine(Func<IEnumerator> coroutine) {
      Post(new SendOrPostCallback(x => {
          Func<IEnumerator> func = (Func<IEnumerator>)x;
          behavior.StartCoroutine(func());
        }), coroutine);
    }

    private IEnumerator SignaledCoroutine(Func<IEnumerator> coroutine,
                                          ManualResetEvent newSignal) {
      yield return coroutine();
      newSignal.Set();
    }

    /// <summary>
    /// Starts a coroutine on the unity main thread and blocks until done.
    /// This can be useful for performing complex operations interleaved
    /// on the main unity thread.
    /// </summary>
    public void SendCoroutine(Func<IEnumerator> coroutine,
          int timeout = Timeout) {
      if (mainThreadId == Thread.CurrentThread.ManagedThreadId) {
        behavior.StartCoroutine(coroutine());
      } else {
        ManualResetEvent newSignal = GetThreadEvent();
        PostCoroutine(() => SignaledCoroutine(coroutine, newSignal));
        newSignal.WaitOne(timeout);
      }
    }

    public override void Post(SendOrPostCallback d, object state) {
      lock (queue) {
        queue.Enqueue(new Tuple<SendOrPostCallback, object>(d, state));
      }
    }

    public override void Send(SendOrPostCallback d, object state) {
      if (mainThreadId == Thread.CurrentThread.ManagedThreadId) {
        d(state);
      } else {
        ManualResetEvent newSignal = GetThreadEvent();
        Post(new SendOrPostCallback(x => {
            try {
              d(x);
            } catch(Exception e) {
              Debug.Log(e.ToString());
            }
            newSignal.Set();
          })
          , state);
        newSignal.WaitOne(Timeout);
      }
    }

    /**
    * This class actually does the work of:
    * a) pulling items off the queue and invoking them
    * b) starting coroutines for those passed into the unitysynchcontext.
    */
    class SynchronizationContextBehavoir : MonoBehaviour {
      Queue<Tuple<SendOrPostCallback, object>> callbackQueue;

      public Queue<Tuple<SendOrPostCallback, object>> CallbackQueue {
        get {
          if (callbackQueue == null) {
            callbackQueue = new Queue<Tuple<SendOrPostCallback, object>>();
          }
          return callbackQueue;
        }
      }

      [UnityEngine.Scripting.Preserve]
      IEnumerator Start() {
        while (true) {
          Tuple<SendOrPostCallback, object> entry = null;
          lock (CallbackQueue) {
            if (CallbackQueue.Count > 0) {
              entry = CallbackQueue.Dequeue();
            }
          }
          if (entry != null && entry.Item1 != null) {
            try {
              entry.Item1(entry.Item2);
            } catch (Exception e) {
              UnityEngine.Debug.Log(e.ToString());
            }
          }
          yield return null;
        }
      }
    }
  }
}
