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
    private int _maxAttempts = 5;

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
    public int MaxAttempts {
      get => Volatile.Read(ref _maxAttempts);
      set {
        if (value < 1) {
          throw new ArgumentException("MaxAttempts must be at least 1.", nameof(value));
        }
        Volatile.Write(ref _maxAttempts, value);
      }
    }

    /// <inheritdoc />
    public override string ToString() =>
        nameof(TransactionOptions) + "{" + nameof(MaxAttempts) + "=" + MaxAttempts + "}";
  }
}
