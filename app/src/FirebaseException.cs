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

/// Exception thrown for any Task exception.
///
/// Each API has different error codes, so the error code should be looked up
/// relative to the API that produced the Task.
public sealed class FirebaseException : System.Exception
{
  /// Initializes a new FirebaseException.
  public FirebaseException()
  {
    ErrorCode = 0;
  }

  /// Initializes a new FirebaseException, with the given error code.
  public FirebaseException(int errorCode)
  {
    ErrorCode = errorCode;
  }

  /// Initializes a new FirebaseException, with the given error code and
  /// message.
  public FirebaseException(int errorCode, string message)
    : base(message)
  {
    ErrorCode = errorCode;
  }

  /// Initializes a new FirebaseException, with the given error code,
  /// message, and a reference to the inner exception.
  public FirebaseException(int errorCode, string message,
                           System.Exception inner)
    : base(message, inner)
  {
    ErrorCode = errorCode;
  }

  /// Returns the API-defined non-zero error code.
  /// If the error code is 0, the error is with the Task itself, and not the
  /// API. See the exception message for more detail.
  public int ErrorCode { get; private set; }
}

}
