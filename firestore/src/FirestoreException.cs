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

using System;

namespace Firebase.Firestore {

  /// <summary>
  /// A class of exceptions thrown by Cloud Firestore.
  /// </summary>
  public sealed class FirestoreException : Exception {

    /// <summary>
    /// Initializes a new <c>FirestoreException</c>, with the given error code.
    /// </summary>
    public FirestoreException(FirestoreError errorCode) {
      ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new <c>FirestoreException</c>, with the given error code and message.
    /// </summary>
    public FirestoreException(FirestoreError errorCode, string message) : base(message) {
      ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new <c>FirestoreException</c>, with the given error code, original exception
    /// and message.
    /// </summary>
    internal FirestoreException(FirestoreError errorCode, Exception exception, string message)
        : base(message, exception) {
      ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new <c>FirestoreException</c>, with the given error code and message.
    /// </summary>
    /// <remarks>
    /// This constructor exists exclusively for use by SWIG-generated code; it should not be used
    /// for any other purpose due to the lack of type safety of the <c>errorCode</c> argument.
    /// </remarks>
    internal FirestoreException(int errorCode, string message) : this((FirestoreError)errorCode, message) {
    }

    /// <summary>
    /// The error code describing the error.
    /// </summary>
    public FirestoreError ErrorCode { get; private set; }
  }

}
