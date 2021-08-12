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
using System.Threading.Tasks;
using System;

namespace Firebase.Firestore {
  /// <summary>
  /// Represents a listener for either document or query snapshots that is returned from
  /// <c>Listen</c> methods.
  /// </summary>
  /// <remarks>
  /// The listener can be removed by calling <see cref="ListenerRegistration.Stop"/>.
  /// </remarks>
  public sealed class ListenerRegistration : IDisposable {
    private readonly ListenerRegistrationProxy _proxy;

    /// <summary>
    /// A task that will complete when the listen operation finishes.
    /// </summary>
    /// <remarks>
    /// The task will finish in a state of <c>TaskStatus.Faulted</c> if any kind of exception
    /// was thrown, including any non-retriable RPC exceptions. The task will finish in a state of
    /// <c>TaskStatus.RanToCompletion"</c> if the listener stopped gracefully.
    /// </remarks>
    public Task ListenerTask {
      get { return taskSource.Task; }
    }

    internal readonly int callbackId;
    internal readonly TaskCompletionSource<object> taskSource;
    private IListenerRegistrationMap owner;

    internal ListenerRegistration(IListenerRegistrationMap owner, int callbackId, TaskCompletionSource<object> tcs, ListenerRegistrationProxy proxy) {
      this.owner = Util.NotNull(owner);
      this.callbackId = callbackId;

      _proxy = Util.NotNull(proxy);
      taskSource = Util.NotNull(tcs);
    }

    /// <summary>
    /// Removes the listener being tracked by this <c>ListenerRegistration</c>.
    /// </summary>
    /// <remarks>
    /// If <see cref="ListenerTask" /> is not completed, then it will transition to the
    /// <c>TaskStatus.RanToCompletion"</c> state. After the initial call of this method,
    /// subsequent calls have no effect.
    /// </remarks>
    public void Stop() {
      _proxy.Remove();
      taskSource.TrySetResult(null);

      owner.Unregister(callbackId);
    }

    /// <summary>
    /// Calls the <see cref="Stop()" /> method.
    /// </summary>
    /// <remarks>
    /// Note that this method is *not* invoked by the destructor. This is intentional as this class
    /// does not handle unmanaged resources. The usage of the <see cref="IDisposable" /> interface
    /// does however enable using this class with external reactive libraries that expect it.
    /// </remarks>
    public void Dispose() {
      Stop();
    }
  }
}
