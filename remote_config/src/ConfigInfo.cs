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

namespace Firebase.RemoteConfig {

/// @brief Describes the state of the most recent Fetch() call.
/// Normally returned as a result of the GetInfo() function.
public sealed class ConfigInfo {

  private static readonly System.DateTime UnixEpochUtc =
      new System.DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);

  private static readonly System.DateTime MaxDateTimeUtc =
      new System.DateTime(System.DateTime.MaxValue.Ticks, System.DateTimeKind.Utc);

  private static readonly ulong MaxUnixEpochMilliseconds =
      (ulong)((System.DateTime.MaxValue.Ticks - UnixEpochUtc.Ticks) /
              System.TimeSpan.TicksPerMillisecond);

  private static System.DateTime FromUnixEpochMilliseconds(ulong milliseconds) {
    if (milliseconds > MaxUnixEpochMilliseconds) {
      return MaxDateTimeUtc;
    }
    return UnixEpochUtc.AddTicks((long)milliseconds * System.TimeSpan.TicksPerMillisecond);
  }

  /// @brief The time when the last fetch operation completed.
  public System.DateTime FetchTime { internal set; get; }

  /// @brief The time when Remote Config data refreshes will no longer
  /// be throttled.
  public System.DateTime ThrottledEndTime { internal set; get; }

  /// @brief The status of the last fetch request.
  public LastFetchStatus LastFetchStatus { internal set; get; }

  /// @brief The reason the most recent fetch failed.
  public FetchFailureReason LastFetchFailureReason { internal set; get; }

  internal ConfigInfo(ConfigInfoInternal configInfoInternal) {
    FetchTime = FromUnixEpochMilliseconds(configInfoInternal.fetch_time);
    ThrottledEndTime = FromUnixEpochMilliseconds(configInfoInternal.throttled_end_time);
    LastFetchStatus = configInfoInternal.last_fetch_status;
    LastFetchFailureReason = configInfoInternal.last_fetch_failure_reason;
  }
}

}
