/*
 * Copyright 2018 Google LLC
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
using System.Threading.Tasks;

namespace Firebase.Internal {
  // Compatibility methods to handle differences between Parse's Task and
  // .NET 4.x Task implementations.
  internal static class TaskCompletionSourceCompat<T> {
    // This is a separate method as referencing SetException(AggregateException)
    // will cause the method to throw an exception *before* it's executed when
    // using .NET 4.x.
    private static void SetExceptionInternal(TaskCompletionSource<T> tcs,
                                             AggregateException exception) {
      tcs.SetException(exception);
    }

    // Safely set an aggregate exception on a Parse or .NET 4.x Task.
    public static void SetException(TaskCompletionSource<T> tcs,
                                    AggregateException exception) {
      try {
        // Parse's implementation of SetException accepts AggregateException the
        // .NET 4.x only supports exception.
        SetExceptionInternal(tcs, exception);
      } catch (Exception) {
        // We must be using the real Task library.
        tcs.SetException((Exception)exception);
      }
    }
  }
}  // namespace Firebase.Internal
