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
  ///   Simple static methods to be called at the start of your own methods to verify
  ///   correct arguments and state.
  /// </summary>
  /// <remarks>
  ///   Simple static methods to be called at the start of your own methods to verify
  ///   correct arguments and state.
  ///   Copied (and extended) from the version present in Ice Cream Sandwich+.
  /// </remarks>
  /// <hide />
  internal static class Preconditions {
    /// <summary>
    ///   Ensures that an object reference passed as a parameter to the calling method is not null.
    /// </summary>
    /// <param name="reference">an object reference</param>
    /// <returns>the non-null reference that was validated</returns>
    /// <exception cref="System.ArgumentNullException">
    ///   if
    ///   <paramref name="reference" />
    ///   is null
    /// </exception>
    public static T CheckNotNull<T>(T reference) {
      if (reference == null) {
        throw new ArgumentException("the given parameter was null");
      }
      return reference;
    }

    /// <summary>Ensures that the given String is not empty and not null.</summary>
    /// <param name="string">the String to test</param>
    /// <returns>the non-null non-empty String that was validated</returns>
    /// <exception cref="System.ArgumentException">
    ///   if
    ///   <c>string</c>
    ///   is null or empty
    /// </exception>
    public static string CheckNotEmpty(string @string) {
      if (string.IsNullOrEmpty(@string)) {
        throw new ArgumentException("Given String is empty or null");
      }
      return @string;
    }

    /// <summary>Ensures that the given String is not empty and not null.</summary>
    public static string CheckNotEmpty(string @string, object errorMessage) {
      if (string.IsNullOrEmpty(@string)) {
        throw new ArgumentException(errorMessage.ToString());
      }
      return @string;
    }

    /// <summary>
    ///   Ensures that an object reference passed as a parameter to the calling method is not null.
    /// </summary>
    public static T CheckNotNull<T>(T reference, string errorMessage) {
      if (reference == null) {
        throw new ArgumentNullException(errorMessage);
      }
      return reference;
    }

    /// <summary>
    ///   Ensures that an integer passed as a parameter to the calling method is not zero.
    /// </summary>
    public static int CheckNotZero(int value, string errorMessage) {
      if (value == 0) {
        throw new ArgumentException(errorMessage);
      }
      return value;
    }

    /// <summary>
    ///   Ensures that an integer passed as a parameter to the calling method is not zero.
    /// </summary>
    public static int CheckNotZero(int value) {
      if (value == 0) {
        throw new ArgumentException("Given Integer is zero");
      }
      return value;
    }

    /// <summary>
    ///   Ensures that a long passed as a parameter to the calling method is not zero.
    /// </summary>
    public static long CheckNotZero(long value, string errorMessage) {
      if (value == 0) {
        throw new ArgumentException(errorMessage);
      }
      return value;
    }

    /// <summary>
    ///   Ensures that a long passed as a parameter to the calling method is not zero.
    /// </summary>
    public static long CheckNotZero(long value) {
      if (value == 0) {
        throw new ArgumentException("Given Long is zero");
      }
      return value;
    }

    /// <summary>
    ///   Ensures the truth of an expression involving the state of the calling
    ///   instance, but not involving any parameters to the calling method.
    /// </summary>
    public static void CheckState(bool expression) {
      if (!expression) {
        throw new InvalidOperationException();
      }
    }

    /// <summary>
    ///   Ensures the truth of an expression involving the state of the calling
    ///   instance, but not involving any parameters to the calling method.
    /// </summary>
    public static void CheckState(bool expression, string errorMessage) {
      if (!expression) {
        throw new InvalidOperationException(errorMessage);
      }
    }

    /// <summary>
    ///   Ensures the truth of an expression involving the state of the calling
    ///   instance, but not involving any parameters to the calling method.
    /// </summary>
    public static void CheckState(bool expression,
      string errorMessage,
      params object[] errorMessageArgs) {
      if (!expression) {
        throw new InvalidOperationException(string.Format(errorMessage, errorMessageArgs));
      }
    }

    /// <summary>
    ///   Ensures the truth of an expression involving parameters to the calling method.
    /// </summary>
    public static void CheckArgument(bool expression, string errorMessage) {
      if (!expression) {
        throw new ArgumentException(errorMessage);
      }
    }

    /// <summary>
    ///   Ensures the truth of an expression involving parameters to the calling method.
    /// </summary>
    public static void CheckArgument(bool expression,
      string errorMessage,
      params object[] errorMessageArgs) {
      if (!expression) {
        throw new ArgumentException(string.Format(errorMessage, errorMessageArgs));
      }
    }

    /// <summary>
    ///   Ensures the truth of an expression involving parameters to the calling method.
    /// </summary>
    public static void CheckArgument(bool expression) {
      if (!expression) {
        throw new ArgumentException();
      }
    }

    // The following methods are copied from google-common library Preconditions class
    /// <summary>
    ///   Ensures that
    ///   <paramref name="index" />
    ///   specifies a valid <i>element</i> in an array,
    ///   list or string of size
    ///   <paramref name="size" />
    ///   . An element index may range from zero,
    ///   inclusive, to
    ///   <paramref name="size" />
    ///   , exclusive.
    /// </summary>
    public static int CheckElementIndex(int index, int size) {
      return CheckElementIndex(index, size, "index");
    }

    /// <summary>
    ///   Ensures that
    ///   <paramref name="index" />
    ///   specifies a valid <i>element</i> in an array,
    ///   list or string of size
    ///   <paramref name="size" />
    ///   . An element index may range from zero,
    ///   inclusive, to
    ///   <paramref name="size" />
    ///   , exclusive.
    /// </summary>
    public static int CheckElementIndex(int index, int size, string desc) {
      if ((index < 0)
          || (index >= size)) {
        throw new IndexOutOfRangeException(BadElementIndex(index, size, desc));
      }
      return index;
    }

    private static string BadElementIndex(int index, int size, string desc) {
      if (index < 0) {
        return string.Format("%s (%s) must not be negative", desc, index);
      } else {
        if (size < 0) {
          throw new ArgumentException("negative size: " + size);
        } else {
          // index >= size
          return string.Format("%s (%s) must be less than size (%s)", desc, index, size);
        }
      }
    }

    /// <summary>
    ///   Ensures that
    ///   <paramref name="index" />
    ///   specifies a valid <i>position</i> in an array,
    ///   list or string of size
    ///   <paramref name="size" />
    ///   . A position index may range from zero
    ///   to
    ///   <paramref name="size" />
    ///   , inclusive.
    /// </summary>
    public static int CheckPositionIndex(int index, int size) {
      return CheckPositionIndex(index, size, "index");
    }

    /// <summary>
    ///   Ensures that
    ///   <paramref name="index" />
    ///   specifies a valid <i>position</i> in an array,
    ///   list or string of size
    ///   <paramref name="size" />
    ///   . A position index may range from zero
    ///   to
    ///   <paramref name="size" />
    ///   , inclusive.
    /// </summary>
    public static int CheckPositionIndex(int index, int size, string desc) {
      if ((index < 0)
          || (index > size)) {
        throw new IndexOutOfRangeException(BadPositionIndex(index, size, desc));
      }
      return index;
    }

    private static string BadPositionIndex(int index, int size, string desc) {
      if (index < 0) {
        return string.Format("%s (%s) must not be negative", desc, index);
      } else {
        if (size < 0) {
          throw new ArgumentException("negative size: " + size);
        } else {
          // index > size
          return string.Format("%s (%s) must not be greater than size (%s)", desc, index, size);
        }
      }
    }

    /// <summary>
    ///   Ensures that
    ///   <paramref name="start" />
    ///   and
    ///   <paramref name="end" />
    ///   specify a valid <i>positions</i>
    ///   in an array, list or string of size
    ///   <paramref name="size" />
    ///   , and are in order. A
    ///   position index may range from zero to
    ///   <paramref name="size" />
    ///   , inclusive.
    /// </summary>
    public static void CheckPositionIndexes(int start, int end, int size) {
      if ((start < 0)
          || (end < start)
          || (end > size)) {
        throw new IndexOutOfRangeException(BadPositionIndexes(start, end, size));
      }
    }

    private static string BadPositionIndexes(int start, int end, int size) {
      if ((start < 0)
          || (start > size)) {
        return BadPositionIndex(start, size, "start index");
      }
      if ((end < 0)
          || (end > size)) {
        return BadPositionIndex(end, size, "end index");
      }
      // end < start
      return string.Format("end index (%s) must not be less than start index (%s)", end, start);
    }
  }
}
