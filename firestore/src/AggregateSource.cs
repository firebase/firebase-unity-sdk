// Copyright 2023 Google LLC
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

namespace Firebase.Firestore {

  /// <summary>
  /// The sources from which an <see cref="AggregateQuery.GetSnapshotAsync"/> can retrieve its
  /// results.
  /// </summary>
  public enum AggregateSource {
    /// Perform the aggregation on the server and download the result.
    /// The result received from the server is presented, unaltered, without
    /// considering any local state. That is, documents in the local cache are not
    /// taken into consideration, neither are local modifications not yet
    /// synchronized with the server. Previously-downloaded results, if any, are
    /// not used. Every request using this source necessarily involves a round trip
    /// to the server.
    ///
    /// The AggregateQuery will fail if the server cannot be reached, such as if
    /// the client is offline.
    Server,
}

}
