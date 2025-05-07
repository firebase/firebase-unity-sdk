/*
 * Copyright 2025 Google LLC
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

namespace Firebase.AI {

/// <summary>
/// Configuration parameters for sending requests to the backend.
/// </summary>
public readonly struct RequestOptions {
  // Since the user could create `RequestOptions` with the default constructor,
  // which isn't hidable in our C# version, we default to null, and use that
  // to determine if it should be 180.
  private readonly TimeSpan? _timeout;

  /// <summary>
  /// Intended for internal use only.
  /// This provides access to the default timeout value.
  /// </summary>
  internal static TimeSpan DefaultTimeout => TimeSpan.FromSeconds(180);

  /// <summary>
  /// Intended for internal use only.
  /// This provides access to the timeout value used for API requests.
  /// </summary>
  internal TimeSpan Timeout => _timeout ?? DefaultTimeout;

  /// <summary>
  /// Initialize a `RequestOptions` object.
  /// </summary>
  /// <param name="timeout">The request's timeout interval. Defaults to 180 seconds if given null.</param>
  public RequestOptions(TimeSpan? timeout = null) {
    _timeout = timeout;
  }
}

}
