/*
 * Copyright 2017 Google LLC
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

namespace Firebase.Storage.Internal {
  /// <summary>
  ///   Argument validation helpers; each throws an <see cref="ArgumentException" /> (or
  ///   <see cref="ArgumentNullException" />) when its condition does not hold.
  /// </summary>
  internal static class Preconditions {
    /// <summary>
    ///   Ensures that an object reference passed as a parameter to the calling method is not null.
    /// </summary>
    /// <returns>the non-null reference that was validated</returns>
    public static T CheckNotNull<T>(T reference) {
      if (reference == null) {
        throw new ArgumentException("the given parameter was null");
      }
      return reference;
    }

    /// <summary>
    ///   Ensures that an object reference passed as a parameter to the calling method is not null.
    /// </summary>
    /// <returns>the non-null reference that was validated</returns>
    public static T CheckNotNull<T>(T reference, string errorMessage) {
      if (reference == null) {
        throw new ArgumentNullException(errorMessage);
      }
      return reference;
    }

    /// <summary>Ensures that the given string is not empty and not null.</summary>
    /// <returns>the non-null non-empty string that was validated</returns>
    public static string CheckNotEmpty(string @string, string errorMessage) {
      if (string.IsNullOrEmpty(@string)) {
        throw new ArgumentException(errorMessage);
      }
      return @string;
    }

    /// <summary>Ensures the truth of an expression involving a parameter of the calling method.</summary>
    public static void CheckArgument(bool expression, string errorMessage) {
      if (!expression) {
        throw new ArgumentException(errorMessage);
      }
    }
  }
}
