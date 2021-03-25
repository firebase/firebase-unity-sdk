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

namespace Firebase.Database {
  /// <summary>
  ///   Instances of this class represent the desired outcome of a single Run of a transaction.
  ///   Pass a handler to <see cref="DatabaseReference.RunTransaction"/>, and in your handler, you can either:
  ///   <ul>
  ///     <li>Set the data to the new value (success) via <see cref="TransactionResult.Success(MutableData)" /></li>
  ///     <li>abort the transaction via <see cref="TransactionResult.Abort()" /></li>
  ///   </ul>
  /// </summary>
  public sealed class TransactionResult {

    /// Aborts the transaction run with <see cref="DatabaseReference.RunTransaction"/> and
    /// returns an aborted <see cref="TransactionResult"/> which can be returned from RunTransaction.
    public static TransactionResult Abort() {
      return new TransactionResult(false);
    }

    /// Builds a successful result to be returned from the handler passed to
    /// <see cref="DatabaseReference.RunTransaction"/>.
    /// <param name="resultData">The desired data to be stored at the location.</param>
    /// <returns>
    ///   A
    ///   <see cref="TransactionResult" />
    ///   indicating the new data to be stored at the location.
    /// </returns>
    public static TransactionResult Success(MutableData resultData) {
      // TODO(phohmeyer): Do we need this data? I must test what happens if someone
      // writes a transaction that returns a different MutableData, e.g.
      // return TransactionResult.Success(mutableData.Child("key"));
      return new TransactionResult(true);
    }

    internal TransactionResult(bool success) {
      IsSuccess = success;
    }

    /// Whether or not this result is a success.
    public bool IsSuccess { get; private set; }
  }
}
