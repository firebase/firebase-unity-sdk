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

namespace Firebase.Storage {
  /// <summary>
  ///   A class that receives progress updates for storage uploads and downloads.
  /// </summary>
  public class StorageProgress<T> : IProgress<T> {
    private readonly Action<T> reportCallback;

    /// <summary>
    /// Creates an instance of a StorageProgress class.
    /// </summary>
    /// <param name="callback">
    ///   A delegate that will be called periodically during a long running
    ///  operation.
    /// </param>
    public StorageProgress(Action<T> callback) {
      reportCallback = callback;
    }

    /// <summary>
    /// Called periodically during a long running operation, this method
    /// will pass value to the delegate passed in the constructor.
    /// </summary>
    /// <param name="value">
    ///   Current state of the long running operation.
    /// </param>
    public virtual void Report(T value) {
      ExceptionAggregator.Wrap(() => { reportCallback(value); });
    }
  }
}
