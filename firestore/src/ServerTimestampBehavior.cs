// Copyright 2019 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;

namespace Firebase.Firestore {
  internal static class ServerTimestampBehaviorConverter {
    public static DocumentSnapshotProxy.ServerTimestampBehavior ConvertToProxy(this ServerTimestampBehavior stb) {
      switch (stb) {
        case ServerTimestampBehavior.None:
          return DocumentSnapshotProxy.ServerTimestampBehavior.None;
        case ServerTimestampBehavior.Estimate:
          return DocumentSnapshotProxy.ServerTimestampBehavior.Estimate;
        case ServerTimestampBehavior.Previous:
          return DocumentSnapshotProxy.ServerTimestampBehavior.Previous;
      }
      throw new ArgumentException("Unsupported ServerTimestampBehavior:" + stb);
    }
  }

  /// <summary>
  /// Controls the return value for server timestamps that have not yet been set to their final
  /// value.
  /// </summary>
  public enum ServerTimestampBehavior {
    /// <summary>
    /// Return <c>null</c> for server timestamps that have not yet been set to their final value.
    /// </summary>
    None = 0,

    /// <summary>
    /// Return local estimates for server timestamps that have not yet been set to their final
    /// value.  This estimate will likely differ from the final value and may cause these pending
    /// values to change once the server result becomes available.
    /// </summary>
    Estimate = 1,

    /// <summary>
    /// Return the previous value for server timestamps that have not yet been set to their final
    /// value.
    /// </summary>
    Previous = 2
  }
}
