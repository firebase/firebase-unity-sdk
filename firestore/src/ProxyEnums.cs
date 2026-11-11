// Copyright 2026 Google LLC
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
  /// Legacy compatibility enum mirroring <see cref="AggregateSource"/>.
  /// </summary>
  public enum AggregateSourceProxy {
    /// <summary>Perform the aggregation on the server.</summary>
    Server = 0,
  }

  /// <summary>
  /// Legacy compatibility enum mirroring <see cref="FirestoreError"/>.
  /// </summary>
  public enum ErrorProxy {
    /// <summary>The operation completed successfully.</summary>
    Ok = 0,
    /// <summary>The operation completed successfully.</summary>
    None = 0,
    /// <summary>The operation was cancelled.</summary>
    Cancelled = 1,
    /// <summary>Unknown error or an error from a different error domain.</summary>
    Unknown = 2,
    /// <summary>Client specified an invalid argument.</summary>
    InvalidArgument = 3,
    /// <summary>Deadline expired before operation could complete.</summary>
    DeadlineExceeded = 4,
    /// <summary>Some requested document was not found.</summary>
    NotFound = 5,
    /// <summary>Some document that we attempted to create already exists.</summary>
    AlreadyExists = 6,
    /// <summary>The caller does not have permission to execute the specified operation.</summary>
    PermissionDenied = 7,
    /// <summary>Some resource has been exhausted.</summary>
    ResourceExhausted = 8,
    /// <summary>Operation was rejected because the system is not in a required state.</summary>
    FailedPrecondition = 9,
    /// <summary>The operation was aborted, typically due to a concurrency issue.</summary>
    Aborted = 10,
    /// <summary>Operation was attempted past the valid range.</summary>
    OutOfRange = 11,
    /// <summary>Operation is not implemented or not supported/enabled.</summary>
    Unimplemented = 12,
    /// <summary>Internal errors.</summary>
    Internal = 13,
    /// <summary>The service is currently unavailable.</summary>
    Unavailable = 14,
    /// <summary>Unrecoverable data loss or corruption.</summary>
    DataLoss = 15,
    /// <summary>The request does not have valid authentication credentials.</summary>
    Unauthenticated = 16,
  }

  /// <summary>
  /// Legacy compatibility enum mirroring <see cref="MetadataChanges"/>.
  /// </summary>
  public enum MetadataChangesProxy {
    /// <summary>Snapshot events are not raised for metadata-only changes.</summary>
    Exclude = 0,
    /// <summary>Snapshot events are raised for metadata-only changes.</summary>
    Include = 1,
  }

  /// <summary>
  /// Legacy compatibility enum mirroring <see cref="Source"/>.
  /// </summary>
  public enum SourceProxy {
    /// <summary>Fetch from server with fallback to cache.</summary>
    Default = 0,
    /// <summary>Fetch only from server.</summary>
    Server = 1,
    /// <summary>Fetch only from local cache.</summary>
    Cache = 2,
  }
}
