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
