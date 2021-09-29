// Copyright 2021 Google LLC
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

namespace Firebase.Firestore.Internal {

  /// <summary>
  /// Thrown when a Firestore internal assertion is violated.
  /// </summary>
  /// <remarks>
  /// This exception is perfect for situations that "should never happen" or when state is found
  /// that can only be a symptom of a bug in the code. It should not be used when users use our APIs
  /// incorrectly, such as specifying a null value when null is forbidden or calling a method on a
  /// "closed" instance.
  /// </remarks>
  internal class AssertFailedException : Exception {

    public AssertFailedException() {
    }

    public AssertFailedException(string message) : base(message) {
    }

    public AssertFailedException(string message, Exception inner) : base(message, inner) {
    }

  }

}
