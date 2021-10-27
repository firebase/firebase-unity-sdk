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
using System.Diagnostics;

namespace Firebase.Firestore.Internal {
  /// <summary>
  /// Preconditions for checking method arguments, state etc.  Inspired by classes like
  /// https://github.com/google/guava/blob/master/guava/src/com/google/common/base/Preconditions.java
  /// and https://github.com/googleapis/gax-dotnet/blob/master/Google.Api.Gax/GaxPreconditions.cs
  /// </summary>
  internal static class Preconditions {

    internal static T CheckNotNull<T>(T argument, string paramName) where T : class {
      if (argument == null) throw new ArgumentNullException(paramName);
      return argument;
    }

    /// <summary>
    /// Checks that a string argument is neither null, nor an empty string.
    /// </summary>
    /// <param name="argument">The argument provided for the parameter.</param>
    /// <param name="paramName">The name of the parameter in the calling method.</param>
    /// <exception cref="ArgumentNullException"><paramref name="argument"/> is null</exception>
    /// <exception cref="ArgumentException"><paramref name="argument"/> is empty</exception>
    /// <returns><paramref name="argument"/> if it is not null or empty</returns>
    internal static string CheckNotNullOrEmpty(string argument, string paramName) {
      if (argument == null) {
        throw new ArgumentNullException(paramName);
      } else if (argument == "") {
        throw new ArgumentException("An empty string was provided, but is not valid", paramName);
      } else {
        return argument;
      }
    }

    /// <summary>
    /// Checks that given condition is met, throwing an <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="condition">The (already evaluated) condition to check.</param>
    /// <param name="message">The message to include in the exception, if generated. This should not
    /// use interpolation, as the interpolation would be performed regardless of whether or
    /// not an exception is thrown.</param>
    public static void CheckState(bool condition, string message) {
      if (!condition) {
        throw new InvalidOperationException(message);
      }
    }

    /// <summary>
    /// Checks that given condition is met, throwing an <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="condition">The (already evaluated) condition to check.</param>
    /// <param name="format">The format string to use to create the exception message if the
    /// condition is not met.</param>
    /// <param name="arg0">The argument to the format string.</param>
    public static void CheckState<T>(bool condition, string format, T arg0) {
      if (!condition) {
        throw new InvalidOperationException(string.Format(format, arg0));
      }
    }

    /// <summary>
    /// Checks that given condition is met, throwing an <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="condition">The (already evaluated) condition to check.</param>
    /// <param name="format">The format string to use to create the exception message if the
    /// condition is not met.</param>
    /// <param name="arg0">The first argument to the format string.</param>
    /// <param name="arg1">The second argument to the format string.</param>
    public static void CheckState<T1, T2>(bool condition, string format, T1 arg0, T2 arg1) {
      if (!condition) {
        throw new InvalidOperationException(string.Format(format, arg0, arg1));
      }
    }

    /// <summary>
    /// Checks that given condition is met, throwing an <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    /// <param name="condition">The (already evaluated) condition to check.</param>
    /// <param name="format">The format string to use to create the exception message if the
    /// condition is not met.</param>
    /// <param name="arg0">The first argument to the format string.</param>
    /// <param name="arg1">The second argument to the format string.</param>
    /// <param name="arg2">The third argument to the format string.</param>
    public static void CheckState<T1, T2, T3>(bool condition, string format, T1 arg0, T2 arg1, T3 arg2) {
      if (!condition) {
        throw new InvalidOperationException(string.Format(format, arg0, arg1, arg2));
      }
    }

    /// <summary>
    /// Checks that given argument-based condition is met, throwing an <see cref="ArgumentException"/> otherwise.
    /// </summary>
    /// <param name="condition">The (already evaluated) condition to check.</param>
    /// <param name="paramName">The name of the parameter whose value is being tested.</param>
    /// <param name="message">The message to include in the exception, if generated. This should not
    /// use interpolation, as the interpolation would be performed regardless of whether or not an exception
    /// is thrown.</param>
    internal static void CheckArgument(bool condition, string paramName, string message) {
      if (!condition) {
        throw new ArgumentException(message, paramName);
      }
    }
  }
}
