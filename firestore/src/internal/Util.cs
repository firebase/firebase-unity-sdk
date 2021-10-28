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
using System.Threading.Tasks;

namespace Firebase.Firestore.Internal {
  internal static class Util {
    internal static void Unreachable() {
      Debug.Assert(false, "Firestore unreachable code");
    }

    // TODO(rgowman): This should use a message template + parameters, like the other platforms.
    internal static void HardAssert(bool condition, string message) {
      Debug.Assert(condition, message);
    }

    // TODO(rgowman): This should use a message template + parameters, like the other platforms.
    internal static void HardFail(string message) {
      Debug.Assert(false, message);
    }

    /**
     * Returns the given obj if it is non-null; otherwise, results in a failed assertion, similar to
     * HardAssert.
     *
     * Example usage:
     *   my_ref = NotNull(suspicious_ref);
     *
     * Used for internal checks only. For user-supplied null reference violations, we should be
     * throwing an ArgumentNullException via
     * Firebase.Firestore.Internal.Preconditions.CheckNotNull().
     */
    internal static T NotNull<T>(T obj, string message = null) {
      Debug.Assert(obj != null, message);
      return obj;
    }

    /// <summary>
    /// Returns a Task that has failed with a given exception, similar to Task.FromException()
    /// in .Net 4.6.
    /// </summary>
    internal static Task<T> TaskFromException<T>(Exception exception) {
      TaskCompletionSource<T> s = new TaskCompletionSource<T>();
      s.SetException(exception);
      return s.Task;
    }

    /// <summary>
    /// Returns a Task that converts the result of the given Task to a different type.
    /// </summary>
    /// <param name="task">The Task whose result to translate.</param>
    /// <param name="mapFunc">The function to convert the result of the given task to the result
    /// type with which the Task returned from this method will complete.</param>
    /// <returns>
    /// A new Task that is a continuation of the given task and completes with the result of the
    /// given Task or fails with the same exception as the given Task.
    /// </returns>
    internal static Task<U> MapResult<T, U>(Task<T> task, Func<T, U> mapFunc) {
      return task.ContinueWith<U>((Task<T> completedTask) => {
        FlattenAndThrowException(completedTask);
        return mapFunc(completedTask.Result);
      });
    }

    /// <summary>
    /// Returns a Task that has a result, created from one that does not.
    /// </summary>
    /// <param name="task">The Task to which to add a result.</param>
    /// <param name="result">The result to set in the returned Task.</param>
    /// <returns>
    /// A new Task that is a continuation of the given task and completes with the given result or
    /// faults with the same exception as the given Task.
    /// </returns>
    internal static Task<U> MapResult<U>(Task task, U result) {
      return task.ContinueWith<U>((Task completedTask) => {
        FlattenAndThrowException(completedTask);
        return result;
      });
    }

    /// <summary>
    /// Unwraps and throws the exception with which the given Task faulted, if any.
    /// <para>
    /// This method either re-throws the faulting exception of the given Task, or returns normally,
    /// which indicates that the given Task completed successfully.
    /// </para>
    /// </summary>
    /// <param name="completedTask">The Task whose faulting exception to re-throw.</param>
    internal static void FlattenAndThrowException(Task completedTask) {
      if (completedTask.IsFaulted && completedTask.Exception != null) {
        // If the flattened AggregateException has exactly one inner exception then pull it out
        // and re-throw it. Do not simply re-throw the AggregateException as that will cause the
        // downstream tasks to fault undesirably with a multi-level hierarchy of
        // AggregateExceptions (b/158309069).
        throw FlattenException(completedTask.Exception);
      } else if (completedTask.IsFaulted || completedTask.Exception != null) {
        Unreachable();
        throw new AssertFailedException("should never get here");
      }
    }

    /// <summary>
    /// Unwraps and returns the "root" exception from the given AggregateException.
    /// </summary>
    /// <param name="aggregateException">The exception to flatten.</param>
    /// <returns>
    /// Returns the innermost encapsulted exception extracted from the given AggregateException.
    /// </returns>
    internal static Exception FlattenException(AggregateException aggregateException) {
      AggregateException flattenedException = aggregateException.Flatten();
      if (flattenedException.InnerExceptions.Count == 1) {
        return flattenedException.InnerExceptions[0];
      } else {
        return flattenedException;
      }
    }

    /// <summary>
    /// Logs the given exception with some additional information. The purpose of this function is
    /// to make the handling of managed exceptions inside P/Invoke calls uniform.
    /// </summary>
    /// <param name="exception">The exception to log.</param>
    /// <param name="methodName">The name of the method that triggered the exception.</param>
    /// <remarks>
    /// C# exceptions should not be allowed to escape into the C++ layer when invoked by P/Invoke
    /// (i.e., from C++). P/Invoke assumes that the unmanaged code is C and would ignore the C++
    /// stack unwinding, resulting in incorrect behavior. See
    /// https://www.mono-project.com/docs/advanced/pinvoke/#runtime-exception-propagation
    /// </remarks>
    internal static void OnPInvokeManagedException(Exception exception, string methodName) {
      Firebase.LogUtil.LogMessage(
          LogLevel.Error,
          "Unexpected exception thrown by " + methodName + "() when invoked by P/Invoke: " +
          exception.ToString());
    }
  }
}
