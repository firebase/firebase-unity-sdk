/*
 * Copyright 2026 Google LLC
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

namespace Firebase.Functions
{
  /// <summary>
  /// Represents a response from a Server-Sent Event (SSE) streaming callable function.
  /// </summary>
  public abstract class StreamResponse
  {
    // Prevent external inheritance.
    internal StreamResponse() {}

    /// <summary>
    /// An intermediate event message yielded during the stream.
    /// </summary>
    public sealed class Message : StreamResponse
    {
      internal Message(object data) { Data = data; }
      public object Data { get; private set; }
    }

    /// <summary>
    /// The final result of the function, marking the end of the stream.
    /// </summary>
    public sealed class Result : StreamResponse
    {
      internal Result(object data) { Data = data; }
      public object Data { get; private set; }
    }
  }
}
