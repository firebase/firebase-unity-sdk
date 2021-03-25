/*
 * Copyright 2019 Google LLC
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

using System;
using System.Collections.Generic;

using Firebase.Platform;

/// <summary>
/// Class that catches and caches exceptions so they can be thrown when ThrowPendingExceptions()
/// is called.  The common use case for this class is catching exceptions from C# methods that
/// have been called via P/Invoke.  In some cases, given the call graph
/// C#(1)-->(native/C/C++)-->C#(2), if an exception occurs in C#(2) when the application re-enters
/// the native layer / VM a pending exception in Unity will lead to an unhandled native exception
/// that can crash the application.
/// </summary>
internal class ExceptionAggregator {
    [ThreadStatic]
    private static List<Exception> threadLocalExceptions = null;

    /// <summary>
    /// Initialize exceptions list and get the instance.
    /// </summary>
    /// <returns>Reference to the exceptions list.</returns>
    private static List<Exception> Exceptions {
        get {
            if (threadLocalExceptions == null) {
                threadLocalExceptions = new List<Exception>();
            }
            return threadLocalExceptions;
        }
    }

    /// <summary>
    /// Get the cached exceptions and clear the pending exceptions list.
    /// </summary>
    /// <returns>Exceptions combined into an AggregateException if there are more than one, the
    /// cached Exception if only one is present or null if no exceptions occurred.</returns>
    public static Exception GetAndClearPendingExceptions() {
        var exceptions = Exceptions;
        var numberOfExceptions = exceptions.Count;
        Exception exceptionToThrow = null;
        if (numberOfExceptions == 1) {
            // If only one exception is pending throw it.
            exceptionToThrow = exceptions[0];
        } else if (numberOfExceptions > 1) {
            // If more than one exception is pending, combine into an aggregate exception.
            exceptionToThrow = new AggregateException(exceptions.ToArray());
        }
        // Remove all cached exceptions.
        exceptions.Clear();
        return exceptionToThrow;
    }

    /// <summary>
    /// Throw the cached exceptions and clear the pending exceptions list.
    /// </summary>
    public static void ThrowAndClearPendingExceptions() {
        var exceptionToThrow = GetAndClearPendingExceptions();
        // If an exception is pending, throw it.
        if (exceptionToThrow != null) {
	          LogException(exceptionToThrow);
            throw exceptionToThrow;
        }
    }

    /// <summary>
    /// Log the cached exceptions as an error.
    /// </summary>
    /// <param name="exception">Exception to log as an error message.</param>
    /// <returns>Exception reference passed to this method.</returns>
    public static Exception LogException(Exception exception) {
        if (exception != null) {
            var aggregateException = exception as AggregateException;
            if (aggregateException != null) {
                var exceptionStrings = new List<string>();
                foreach (var innerException in aggregateException.Flatten().InnerExceptions) {
                    exceptionStrings.Add(innerException.ToString());
                }
                FirebaseLogger.LogMessage(PlatformLogLevel.Error,
                                          String.Join("\n\n", exceptionStrings.ToArray()));
            } else {
                FirebaseLogger.LogMessage(PlatformLogLevel.Error, exception.ToString());
            }
        }
	      return exception;
    }

    /// <summary>
    /// Call a closure, catching and caching exceptions so they're thrown when
    /// ThrowPendingExceptions() is called.
    /// </summary>
    /// <param name="action">Closure to execute.</param>
    public static void Wrap(Action action) {
        // Try executing the closure.
        try {
            action();
        } catch (Exception ex) {
            // If an exception occurs, cache it in thread local storage.
            Exceptions.Add(ex);
        }
    }

    /// <summary>
    /// Call a closure, catching and caching exceptions so they're thrown when
    /// ThrowPendingExceptions() is called.
    /// </summary>
    /// <param name="func">Function to execute.</param>
    /// <param name="errorValue">Value to return from the function if an exception occurs.</param>
    /// <returns>Value from the function
    public static T Wrap<T>(Func<T> func, T errorValue) {
        // Try executing the closure.
        try {
            return func();
        } catch (Exception ex) {
            // If an exception occurs, cache it in thread local storage.
            Exceptions.Add(ex);
        }
        return errorValue;
    }
}

}
