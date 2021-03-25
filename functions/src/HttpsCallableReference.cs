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
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Functions {
  /// <summary>Represents a reference to a Google Cloud Functions HTTPS callable function.</summary>
  /// <remarks>
  ///   Represents a reference to a Google Cloud Functions HTTPS callable function.
  ///   (see <a href="https://cloud.google.com/functions/">Google Cloud Functions</a>)
  /// </remarks>
  public sealed class HttpsCallableReference {
    // Functions object this reference was created from.
    private readonly FirebaseFunctions firebaseFunctions;

    /// <summary>
    /// Construct a wrapper around the HttpsCallableReferenceInternal object.
    /// </summary>
    internal HttpsCallableReference(FirebaseFunctions functions,
      HttpsCallableReferenceInternal callableReferenceInternal) {
      firebaseFunctions = functions;
      Internal = callableReferenceInternal;
    }

    private HttpsCallableReferenceInternal Internal { get; set; }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   service which created this reference.
    /// </summary>
    /// <returns>
    ///   The
    ///   <see cref="FirebaseFunctions" />
    ///   service.
    /// </returns>
    public FirebaseFunctions Functions { get { return firebaseFunctions; } }

    /// <summary>
    ///   ...
    /// </summary>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   with the result of the function call.
    /// </returns>
    public Task<HttpsCallableResult> CallAsync() {
      return CallAsync(null);
    }

    /// <summary>
    ///   ...
    /// </summary>
    /// <param name="data">The data to pass to the function.</param>
    /// <returns>
    ///   A <see cref="Task"/>
    ///   with the result of the function call.
    /// </returns>
    public Task<HttpsCallableResult> CallAsync(object data) {
      var dataVariant = Variant.FromObject(data);
      return Internal.CallAsync(dataVariant).ContinueWith(task => {
        // We need to preserve the Internal object during the async call.
        if (Internal == null) {
          LogUtil.LogMessage(LogLevel.Error,
                                 "The underlying object of the HttpsCallableReference was lost.");
        }
        if (task.IsFaulted) {
          // Try to convert the exception into a FunctionsException.
          var ex = task.Exception;
          foreach (var inner in ex.InnerExceptions) {
            if (inner is FirebaseException) {
              throw new FunctionsException((FirebaseException) inner);
            }
          }
          throw ex;
        }
        return new HttpsCallableResult(task.Result.data().ToObject());
      });
    }
  }
}
