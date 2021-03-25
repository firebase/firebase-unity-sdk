/*
 * Copyright 2019 Google LLC
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

namespace Firebase.Platform {

using System;
using System.Threading;

// Lazily fetches and caches a value from the main thread that could change on each notification
// of FirebaseHandler.Update.
internal class MainThreadProperty<T> {
  // Delegate which fetches the property being monitored.
  private Func<T> getPropertyDelegate;
  // Counter at the last update.
  private int lastGetPropertyTickCount = -1;
  // Value cached at lastGetPropertyTickCount.
  private T cachedValue;

  // Construct a property which fetches it's value using the specified delegate on the main thread.
  public MainThreadProperty(Func<T> getPropertyDelegate) {
    this.getPropertyDelegate = getPropertyDelegate;
  }

  // Get the current property value if it has been cached this frame or block until the latest
  // value can be read from the main thread.
  // This is still inherently racy as the returned value of the property could be modified during
  // the current update.
  public T Value {
    get {
      lock (getPropertyDelegate) {
        if (FirebaseHandler.TickCount == lastGetPropertyTickCount) return cachedValue;
      }
      var task = FirebaseHandler.RunOnMainThreadAsync(() => {
          T mainThreadValue = getPropertyDelegate();
          lock (getPropertyDelegate) {
            cachedValue = mainThreadValue;
            lastGetPropertyTickCount = FirebaseHandler.TickCount;
          }
          return mainThreadValue;
        });
      // The .NET 3.x Task library doesn't support Task.Wait(ms) so poll the task status.
      const int TIMEOUT_MILLISECONDS = 100;
      for (int i = 0;
           !(task.IsCompleted || task.IsFaulted || task.IsCanceled) &&
             i < TIMEOUT_MILLISECONDS; ++i) {
        Thread.Sleep(1);
      }
      if (task.IsFaulted) throw task.Exception;
      if (!task.IsCompleted || task.IsCanceled) {
        // If we timed out on this frame it's likely we won't see another frame,
        // so reuse the cached value next time.
        lock (getPropertyDelegate) {
          lastGetPropertyTickCount = FirebaseHandler.TickCount;
          return cachedValue;
        }
      }
      return task.Result;
    }
  }
}

}
