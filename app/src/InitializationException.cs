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

namespace Firebase {

/// The exception that is thrown when a problem occurs with initialization
/// of a Firebase module or class.
public sealed class InitializationException : System.Exception
{
  /// The error code describing the cause of the failure.
  public InitResult InitResult { get; private set; }

  /// Initializes a new InitializationException, with the given result.
  public InitializationException(InitResult result) : base() {
    this.InitResult = result;
  }

  /// Initializes a new InitializationException, with the given result and
  /// message.
  public InitializationException(InitResult result, string message)
      : base(message) {
    this.InitResult = result;
  }

  /// Initializes a new InitializationException, with the given result,
  /// message, and a reference to the inner exception.
  public InitializationException(InitResult result, string message,
      System.Exception inner) : base(message, inner) {
    this.InitResult = result;
  }
}

}  // namespace Firebase
