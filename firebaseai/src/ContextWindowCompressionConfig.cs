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

using System.Collections.Generic;
using Firebase.AI.Internal;

namespace Firebase.AI
{
  /// <summary>
  /// Configures the sliding window context compression mechanism.
  /// </summary>
  public class SlidingWindow
  {
    /// <summary>
    /// The session reduction target, i.e., how many tokens we should keep.
    /// </summary>
    public int? TargetTokens { get; }

    public SlidingWindow(int? targetTokens = null)
    {
      TargetTokens = targetTokens;
    }

    internal Dictionary<string, object> ToJson()
    {
      var dict = new Dictionary<string, object>();
      if (TargetTokens.HasValue)
      {
        dict["targetTokens"] = TargetTokens.Value;
      }
      return dict;
    }
  }

  /// <summary>
  /// Enables context window compression to manage the model's context window.
  /// </summary>
  public class ContextWindowCompressionConfig
  {
    /// <summary>
    /// The number of tokens (before running a turn) that triggers the context
    /// window compression.
    /// </summary>
    public int? TriggerTokens { get; }

    /// <summary>
    /// The sliding window compression mechanism.
    /// </summary>
    public SlidingWindow? SlidingWindow { get; }

    public ContextWindowCompressionConfig(int? triggerTokens = null, SlidingWindow? slidingWindow = null)
    {
      TriggerTokens = triggerTokens;
      SlidingWindow = slidingWindow;
    }

    internal Dictionary<string, object> ToJson()
    {
      var dict = new Dictionary<string, object>();
      if (TriggerTokens.HasValue)
      {
        dict["triggerTokens"] = TriggerTokens.Value;
      }
      if (SlidingWindow != null)
      {
        dict["slidingWindow"] = SlidingWindow.ToJson();
      }
      return dict;
    }
  }
}
