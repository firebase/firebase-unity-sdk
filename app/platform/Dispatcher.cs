/*
 * Copyright 2017 Google LLC
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

using System.Threading.Tasks;

namespace Firebase {

// Enables callbacks to be dispatched from any thread and be handled on
// the thread that owns the instance to this class (eg. the UIThread).
internal class Dispatcher {
  private int ownerThreadId;
  private System.Collections.Generic.Queue<System.Action> queue =
      new System.Collections.Generic.Queue<System.Action>();

  public Dispatcher() {
    ownerThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
  }

  private class CallbackStorage<TResult> {
    public TResult Result { get; set; }
    public System.Exception Exception { get; set; }
  }

  // Triggers the job to run on the main thread, and waits for it to finish.
  public TResult Run<TResult>(System.Func<TResult> callback) {
    if (ManagesThisThread()) {
      return callback();
    }

    var waitHandle = new System.Threading.EventWaitHandle(
        false, System.Threading.EventResetMode.ManualReset);

    var result = new CallbackStorage<TResult>();
    lock(queue) {
      queue.Enqueue(() => {
        try {
          result.Result = callback();
        }
        catch (System.Exception e) {
          result.Exception = e;
        }
        finally {
          waitHandle.Set();
        }
      });
    }
    waitHandle.WaitOne();
    if (result.Exception != null) throw result.Exception;
    return result.Result;
  }

  // Triggers the job to run on the main thread, and returns a task which
  // completes once the job is done.
  public Task<TResult> RunAsync<TResult>(System.Func<TResult> callback) {
    if (ManagesThisThread()) {
      return RunAsyncNow(callback);
    }

    TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();

    lock(queue) {
      queue.Enqueue(() => {
        try {
          tcs.SetResult(callback());
        }
        catch (System.Exception e) {
          tcs.SetException(e);
        }
      });
    }

    return tcs.Task;
  }

  // Triggers the job immediately, and returns a task with the result from the job.
  internal static Task<TResult> RunAsyncNow<TResult>(System.Func<TResult> callback) {
    Task<TResult> task;
    try {
      task = Task.FromResult(callback());
    }
    catch (System.Exception e) {
      // Cannot use Task.FromException() since Parse does not support it.
      TaskCompletionSource<TResult> tcs = new TaskCompletionSource<TResult>();
      tcs.TrySetException(e);
      task = tcs.Task;
    }
    return task;
  }

  // Determines whether this thread is managed by this instance.
  internal bool ManagesThisThread() {
    return System.Threading.Thread.CurrentThread.ManagedThreadId == ownerThreadId;
  }

  // This dispatches jobs queued up for the owning thread.
  // It must be called regularly or the threads waiting for job will be
  // blocked.
  public void PollJobs() {
    System.Diagnostics.Debug.Assert(ManagesThisThread());

    System.Action job;
    while (true) {
      lock(queue) {
        if (queue.Count > 0) {
          job = queue.Dequeue();
        } else {
          break;
        }
      }
      ExceptionAggregator.Wrap(job);
    }
  }
}

}  // namespace Firebase
