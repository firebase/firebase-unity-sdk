// Copyright 2022 Google LLC
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

using System;
using System.Threading;

namespace Firebase.Firestore {

/// <summary>
/// Options to customize transaction behavior for
/// <see cref="FirebaseFirestore.RunTransactionAsync"/>.
/// </summary>
public sealed class TransactionOptions {

  // The lock that must be held during all accesses to _proxy.
  private readonly ReaderWriterLock _proxyLock = new ReaderWriterLock();

  // The underlying C++ TransactionOptions object.
  private TransactionOptionsProxy _proxy = new TransactionOptionsProxy();

  internal TransactionOptionsProxy Proxy {
    get {
      _proxyLock.AcquireReaderLock(Int32.MaxValue);
      try {
        return new TransactionOptionsProxy(_proxy);
      } finally {
        _proxyLock.ReleaseReaderLock();
      }
    }
  }

  /// <summary>
  /// Creates the default <c>TransactionOptions</c>.
  /// </summary>
  public TransactionOptions() {
  }

  /// <summary>
  /// The maximum number of attempts to commit, after which the transaction fails.
  /// </summary>
  ///
  /// <remarks>
  /// The default value is 5, and must be greater than zero.
  /// </remarks>
  public Int32 MaxAttempts {
    get {
      _proxyLock.AcquireReaderLock(Int32.MaxValue);
      try {
        return _proxy.max_attempts();
      } finally {
        _proxyLock.ReleaseReaderLock();
      }
    }
    set {
      _proxyLock.AcquireWriterLock(Int32.MaxValue);
      try {
        _proxy.set_max_attempts(value);
      } finally {
        _proxyLock.ReleaseWriterLock();
      }
    }
  }

  /// <inheritdoc />
  public override string ToString() {
    return nameof(TransactionOptions) + "{" + nameof(MaxAttempts) + "=" + MaxAttempts + "}";
  }

}

}
