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

/// <summary>Helper class for Transactions.</summary>
/// Each instance of this class will handle one transaction.
/// We keep the handler alive until it is explicitly disposed, as it should stay alive
/// until the task returned by RunTransaction completes.
internal sealed class InternalTransactionHandler : IDisposable {
  // Delegate definition for the C++ -> C# callback.
  public delegate InternalTransactionResult TransactionDelegate(
      int callbackId, System.IntPtr mutableData);

  // To be supported on all platforms, cross-language callbacks need to be static,
  // so we use a callback id to find the actual handler of the callback.
  // The map will also keep the handler alive until it is removed in its Dispose function.
  private static int uidGenerator = 0;
  private static Dictionary<int, InternalTransactionHandler> transactionCallbacks =
      new Dictionary<int, InternalTransactionHandler>();

  [MonoPInvokeCallback(typeof(TransactionDelegate))]
  private static InternalTransactionResult DoTransaction(
      int callbackId, System.IntPtr mutableData) {
    InternalTransactionHandler handler = null;
    if (transactionCallbacks.TryGetValue(callbackId, out handler)) {
      // Unlike in other callbacks, we do NOT OWN the mutable data.
      // First, that's why we pass "false" to the constructor.
      // Second, this means that the InternalMutableData is only valid for the
      // duration of this callback. Clients MUST NOT keep a reference to it.
      InternalMutableData md = new InternalMutableData(mutableData, false);
      TransactionResult result = TransactionResult.Abort();
      try {
        result = handler.transaction(new MutableData(md, handler.database));
      } catch (Exception e) {
        LogUtil.LogMessage(
            LogLevel.Warning, "Exception in transaction delegate, aborting transaction\n" + e);
      }
      // Third, we Dispose the InternalMutableData now, after the callback executed.
      // This sets its internal C pointer to null, which will give better errors in
      // case someone did keep a reference to it.
      md.Dispose();
      if (result.IsSuccess) {
        return InternalTransactionResult.TransactionResultSuccess;
        // TODO(phohmeyer): Do we need to do something with the MutableData in the result?
      } else {
        return InternalTransactionResult.TransactionResultAbort;
      }
    } else {
      return InternalTransactionResult.TransactionResultAbort;
    }
  }

  static InternalTransactionHandler() {
    InternalDatabaseReference.RegisterTransactionCallback(DoTransaction);
  }

  private Func<MutableData, TransactionResult> transaction;
  // We only keep a reference to the database to ensure that it's kept alive,
  // as the underlying C++ code needs that.
  private FirebaseDatabase database;

  public InternalTransactionHandler(
      Func<MutableData, TransactionResult> transaction, FirebaseDatabase database) {
    this.transaction = transaction;
    this.database = database;
    lock (transactionCallbacks) {
      CallbackId = uidGenerator++;
      transactionCallbacks[CallbackId] = this;
    }
  }

  // Dispose MUST be called explicitly, as instances of this class will NOT be garbage
  // collected otherwise. The map keeps them alive until Dispose is called.
  // Because it MUST be called explicitly, there's no finalizer.
  public void Dispose() {
    lock (transactionCallbacks) {
      transactionCallbacks.Remove(CallbackId);
    }
  }

  public int CallbackId { get; private set; }
}

}
