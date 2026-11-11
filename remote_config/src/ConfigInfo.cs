/*
 * Copyright 2024 Google LLC
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

namespace Firebase.RemoteConfig {
  /// <summary>Describes the most recent fetch request status.</summary>
  public enum LastFetchStatus {
    /// <summary>The most recent fetch succeeded and its data is ready to be activated.</summary>
    Success = 0,
    /// <summary>The most recent fetch request failed.</summary>
    Failure = 1,
    /// <summary>The most recent fetch is in progress or no fetch has been performed yet.</summary>
    Pending = 2,
  }

  /// <summary>Describes the most recent fetch failure reason.</summary>
  public enum FetchFailureReason {
    /// <summary>The fetch has not failed.</summary>
    Invalid = 0,
    /// <summary>The most recent fetch failed because it was throttled by the server.</summary>
    Throttled = 1,
    /// <summary>The most recent fetch failed due to a network or server error.</summary>
    Error = 2,
  }

  /// @brief Describes the state of the most recent Fetch() call.
  /// Normally returned as a result of the GetInfo() function.
  public sealed class ConfigInfo {
    /// @brief The time when the last fetch operation completed.
    public DateTime FetchTime { get; internal set; }

    /// @brief The time when Remote Config data refreshes will no longer
    /// be throttled.
    public DateTime ThrottledEndTime { get; internal set; }

    /// @brief The status of the last fetch request.
    public LastFetchStatus LastFetchStatus { get; internal set; }

    /// @brief The reason the most recent fetch failed.
    public FetchFailureReason LastFetchFailureReason { get; internal set; }

    internal ConfigInfo(long fetchTimeMillis, long throttledEndTimeMillis, LastFetchStatus status, FetchFailureReason failureReason) {
      var epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
      (FetchTime, ThrottledEndTime) = (epoch.AddMilliseconds(Math.Max(0L, fetchTimeMillis)), epoch.AddMilliseconds(Math.Max(0L, throttledEndTimeMillis)));
      (LastFetchStatus, LastFetchFailureReason) = (status, failureReason);
    }
  }
}
