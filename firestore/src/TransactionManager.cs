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
using Firebase.Platform;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Firestore {
  using TransactionCallbackMap = ListenerRegistrationMap<Func<TransactionCallbackProxy, Task>>;

  /// <summary>
  /// Manages in-flight transactions on behalf of `FirebaseFirestore`.
  /// </summary>
  /// <remarks>
  /// This class is thread safe and all methods may be invoked concurrently by multiple threads.
  /// </remarks>
  internal sealed class TransactionManager : IDisposable {
    private static readonly TransactionCallbackMap _callbacks = new TransactionCallbackMap();

    private readonly FirebaseFirestore _firestore;
    private readonly TransactionManagerProxy _transactionManagerProxy;

    /// <summary>
    /// Creates a new instance of this class.
    /// </summary>
    /// <param name="firestore">The `Firestore` object to use.</param>.
    /// <param name="firestoreProxy">The `FirestoreProxy` object to use.</param>.
    internal TransactionManager(FirebaseFirestore firestore, FirestoreProxy firestoreProxy) {
      _firestore = Util.NotNull(firestore);
      _transactionManagerProxy = new TransactionManagerProxy(Util.NotNull(firestoreProxy));
    }

    ~TransactionManager() {
      Dispose();
    }

    /// <summary>
    /// Releases all resources held by this object.
    /// </summary>
    /// <remarks>
    /// Any scheduled transactions will be unscheduled, any running transactions will be cancelled,
    /// and all future calls to `RunTransactionAsync()` will not actually run a transaction but will
    /// instead return a faulted `Task`.
    ///
    /// This method is idempotent; however, only the first invocation has side effects.
    /// </remarks>
    public void Dispose() {
      _callbacks.ClearCallbacksForOwner(this);
      _transactionManagerProxy.CppDispose();
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs a transaction.
    /// </summary>
    /// <param name="options">The transaction options to use.</param>.
    /// <param name="callback">The callback to run.</param>.
    /// <returns>A task that completes when the transaction has completed.</returns>
    internal Task<T> RunTransactionAsync<T>(TransactionOptions options,
        Func<Transaction, Task<T>> callback) {
      // Store the result of the most recent invocation of the user-supplied callback.
      bool callbackWrapperInvoked = false;
      Task<T> lastCallbackTask = null;
      Exception lastCallbackException = null;

      // Create a callback that calls the caller-supplied callback.
      Func<TransactionCallbackProxy, Task> callbackWrapper = callbackProxy => {
        Util.HardAssert(FirebaseHandler.DefaultInstance.IsMainThread(),
                   "Callback must be invoked on the main thread");
        callbackWrapperInvoked = true;
        lastCallbackTask = null;
        lastCallbackException = null;

        try {
          var transaction = new Transaction(callbackProxy, _firestore);
          Task<T> callbackTask =
              FirebaseHandler.RunOnMainThread<Task<T>>(() => { return callback(transaction); });
          lastCallbackTask = callbackTask;
          return callbackTask;
        } catch (Exception e) {
          lastCallbackException = e;
          return null;
        }
      };

      Int32 callbackId = _callbacks.Register(this, callbackWrapper);

      // Create a continuation to apply to the `Task` returned from `RunTransaction()`.
      Func<Task, T> overallCallback = overallTask => {
        _callbacks.Unregister(callbackId);

        if (!callbackWrapperInvoked) {
          throw new InvalidOperationException("Firestore instance has been disposed");
        } else if (lastCallbackException != null) {
          throw lastCallbackException;
        } else if (lastCallbackTask == null) {
          throw new NullReferenceException(
              "The callback specified to " + nameof(RunTransactionAsync) +
              "() returned a null Task, but it is required to return a non-null Task");
        } else if (lastCallbackTask.IsFaulted) {
          throw Util.FlattenException(lastCallbackTask.Exception);
        } else if (overallTask.IsFaulted) {
          throw Util.FlattenException(overallTask.Exception);
        } else {
          return lastCallbackTask.Result;
        }
      };

      return _transactionManagerProxy.RunTransactionAsync(callbackId, options.Proxy, ExecuteCallback)
          .ContinueWith<T>(overallCallback);
    }

    internal delegate bool TransactionCallbackDelegate(System.IntPtr transactionCallbackProxyPtr);

    [MonoPInvokeCallback(typeof(TransactionCallbackDelegate))]
    static bool ExecuteCallback(System.IntPtr transactionCallbackProxyPtr) {
      try {
        // Create the proxy object _before_ doing anything else to ensure that the C++ object's
        // memory does not get leaked (https://github.com/firebase/firebase-unity-sdk/issues/49).
        var callbackProxy =
            new TransactionCallbackProxy(transactionCallbackProxyPtr, /*cMemoryOwn=*/true);

        Func<TransactionCallbackProxy, Task> callbackWrapper;
        if (!_callbacks.TryGetCallback(callbackProxy.callback_id(), out callbackWrapper)) {
          return false;
        }

        Task callbackTask = callbackWrapper(callbackProxy);
        if (callbackTask == null) {
          return false;
        }

        callbackTask.ContinueWith(task => {
          bool callbackSucceeded = !(task.IsCanceled || task.IsFaulted);
          callbackProxy.OnCompletion(callbackSucceeded);
        });
        return true;

      } catch (Exception e) {
        Util.OnPInvokeManagedException(e, nameof(ExecuteCallback));
        return false;
      }
    }
  }
}
