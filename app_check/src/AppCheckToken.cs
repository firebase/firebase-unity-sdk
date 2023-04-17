/*
 * Copyright 2023 Google LLC
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

namespace Firebase.AppCheck {

/// @brief Token used by the Firebase App Check service.
///
/// Struct to hold tokens emitted by the Firebase App Check service which are
/// minted upon a successful application verification. These tokens are the
/// federated output of a verification flow, the structure of which is
/// independent of the mechanism by which the application was verified.
public struct AppCheckToken {
  /// A Firebase App Check token.
  public string Token { get; set; }

  /// The time at which the token will expire.
  public DateTime ExpireTime { get; set; }

  internal static readonly DateTime s_unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

  // Construct an AppCheckToken given the swig generated class for one.
  internal static AppCheckToken FromAppCheckTokenInternal(AppCheckTokenInternal tokenInternal) {
    return new AppCheckToken {
      Token = tokenInternal.token,
      ExpireTime = s_unixEpoch.AddMilliseconds((double)tokenInternal.expire_time_millis)
    };
  }

  // Get the expire time in milliseconds since Epoch, which is used by C++.
  internal long ExpireTimeMs {
    get {
      TimeSpan ts = ExpireTime - s_unixEpoch;
      return (long)ts.TotalMilliseconds;
    }
  }
}

}
