// Copyright 2021 Google LLC
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
/// Indicates whether metadata-only changes (that is, <see cref="DocumentSnapshot.Metadata"/> or
/// <see cref="QuerySnapshot.Metadata"/> changed) should trigger snapshot events.
/// </summary>
public enum MetadataChanges {
  /// Snapshot events will not be triggered by metadata-only changes.
  Exclude,

  /// Snapshot events will be triggered by any changes, including metadata-only changes.
  Include
}

}
