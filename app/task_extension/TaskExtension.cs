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

using Firebase.Platform;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Extensions {
  /// <summary>
  /// Extension methods for System.Threading.Tasks.Task and System.Threading.Tasks.Task \< T \>
  /// that allow the continuation function to be executed on the main thread in Unity. This is
  /// compatible with both .NET 3.x and .NET 4.x.
  ///
  /// To disable these extension methods entirely, uncheck all platform of
  /// /Assets/Firebase/Plugins/Firebase.TaskExtension.dll in Unity Editor.
  /// </summary>
  public static class TaskExtension {

    /// <summary>
    /// Extend Task. Returns a Task which completes once the given task is complete and the given
    /// continuation function is called from the main thread in Unity.
    /// </summary>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <returns>A new Task that is complete after the continuation is executed on the main
    /// thread.</returns>
    public static Task ContinueWithOnMainThread(this Task task, Action<Task> continuation) {
      return task.ContinueWith(t => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          continuation(t);
          return true;
        });
      }).Unwrap();
    }

    /// <summary>
    /// Extend Task. Returns a Task which completes once the given task is complete and the given
    /// continuation function is called from the main thread in Unity, with a cancellation
    /// token.
    /// </summary>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A new Task that is complete after the continuation is executed on the main
    /// thread.</returns>
    public static Task ContinueWithOnMainThread(this Task task, Action<Task> continuation,
        CancellationToken cancellationToken) {
      return task.ContinueWith(t => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          continuation(t);
          return true;
        });
      }, cancellationToken).Unwrap();
    }

    /// <summary>
    /// Extend Task. Returns a Task \< TResult \> which completes once the given task is
    /// complete and the given continuation function with return value TResult is called from the
    /// main thread in Unity.
    /// </summary>
    /// <typeparam name="TResult">The type returned by the continuation.</typeparam>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <returns>A new Task of type TResult, that is complete after the continuation is executed
    /// on the main thread.</returns>
    public static Task<TResult> ContinueWithOnMainThread<TResult>(this Task task,
        Func<Task, TResult> continuation) {
      return task.ContinueWith(t => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          return continuation(t);
        });
      }).Unwrap();
    }

    /// <summary>
    /// Extend Task. Returns a Task \< TResult \> which completes once the given task is
    /// complete and the given continuation function with return value TResult is called from the
    /// main thread in Unity, with a cancellation token.
    /// </summary>
    /// <typeparam name="TResult">The type returned by the continuation.</typeparam>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A new Task of type TResult, that is complete after the continuation is executed
    /// on the main thread.</returns>
    public static Task<TResult> ContinueWithOnMainThread<TResult>(this Task task,
        Func<Task, TResult> continuation, CancellationToken cancellationToken) {
      return task.ContinueWith(t => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          return continuation(t);
        });
      }, cancellationToken).Unwrap();
    }

    /// <summary>
    /// Extend Task \< T \>. Returns a Task which completes once the given task is complete and
    /// the given continuation function is called from the main thread in Unity.
    /// </summary>
    /// <typeparam name="T">The Task template type used as an input parameter for the
    /// continuation.</typeparam>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <returns>A new Task that is complete after the continuation is executed on the main
    /// thread.</returns>
    public static Task ContinueWithOnMainThread<T>(this Task<T> task,
      Action<Task<T>> continuation) {
      return task.ContinueWith((t) => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          continuation(t);
          return true;
        });
      }).Unwrap();
    }

    /// <summary>
    /// Extend Task \< T \>. Returns a Task \< TResult \> which completes once the given task
    /// is complete and the given continuation function with return value TResult is called from
    /// the main thread in Unity.
    /// </summary>
    /// <typeparam name="TResult">The return type from continuation and the Task template
    /// return type of this extension method.
    /// continuation.</typeparam>
    /// <typeparam name="T">The Task template type used as an input parameter for the
    /// continuation.</typeparam>
    /// <param name="task">The task to continue with.</param>
    /// <param name="continuation">The continuation function to be executed on the main thread
    /// once the given task completes.</param>
    /// <returns>A new Task of type TResult, that is complete after the continuation is executed
    /// on the main thread.</returns>
    public static Task<TResult> ContinueWithOnMainThread<TResult, T>(this Task<T> task,
        Func<Task<T>, TResult> continuation) {
      return task.ContinueWith((t) => {
        return FirebaseHandler.RunOnMainThreadAsync(() => {
          return continuation(t);
        });
      }).Unwrap();
    }
  }
}
