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

namespace Firebase.Firestore.Internal {

  /// Static class containing utility methods to convert between public and
  /// SWIG-generated ("proxy") enums. Public enums are mainy used for
  /// documentation purposes -- they are otherwise equivalent to the proxy ones.
  static class Enums {

    public static AggregateSourceProxy Convert(AggregateSource aggregateSource) {
      switch (aggregateSource) {
        case AggregateSource.Server:
          return AggregateSourceProxy.Server;
        default:
          throw new System.ArgumentOutOfRangeException("Unexpected enum value: " +
                                                       aggregateSource.ToString());
      }
    }
    public static SourceProxy Convert(Source source) {
          switch (source) {
            case Source.Default:
              return SourceProxy.Default;
            case Source.Server:
              return SourceProxy.Server;
            case Source.Cache:
              return SourceProxy.Cache;
            default:
              throw new System.ArgumentOutOfRangeException("Unexpected enum value: " +
                  source.ToString());
          }
    }

    public static MetadataChangesProxy Convert(MetadataChanges metadataChanges) {
          switch (metadataChanges) {
            case MetadataChanges.Exclude:
              return MetadataChangesProxy.Exclude;
            case MetadataChanges.Include:
              return MetadataChangesProxy.Include;
            default:
              throw new System.ArgumentOutOfRangeException("Unexpected enum value: " +
                  metadataChanges.ToString());
          }
    }

    public static FirestoreError Convert(ErrorProxy error) {
          switch (error) {

            case ErrorProxy.Ok:
              return FirestoreError.Ok;

            case ErrorProxy.Cancelled:
              return FirestoreError.Cancelled;

            case ErrorProxy.Unknown:
              return FirestoreError.Unknown;

            case ErrorProxy.InvalidArgument:
              return FirestoreError.InvalidArgument;

            case ErrorProxy.DeadlineExceeded:
              return FirestoreError.DeadlineExceeded;

            case ErrorProxy.NotFound:
              return FirestoreError.NotFound;

            case ErrorProxy.AlreadyExists:
              return FirestoreError.AlreadyExists;

            case ErrorProxy.PermissionDenied:
              return FirestoreError.PermissionDenied;

            case ErrorProxy.ResourceExhausted:
              return FirestoreError.ResourceExhausted;

            case ErrorProxy.FailedPrecondition:
              return FirestoreError.FailedPrecondition;

            case ErrorProxy.Aborted:
              return FirestoreError.Aborted;

            case ErrorProxy.OutOfRange:
              return FirestoreError.OutOfRange;

            case ErrorProxy.Unimplemented:
              return FirestoreError.Unimplemented;

            case ErrorProxy.Internal:
              return FirestoreError.Internal;

            case ErrorProxy.Unavailable:
              return FirestoreError.Unavailable;

            case ErrorProxy.DataLoss:
              return FirestoreError.DataLoss;

            case ErrorProxy.Unauthenticated:
              return FirestoreError.Unauthenticated;

            default:
              throw new System.ArgumentOutOfRangeException("Unexpected enum value: " +
                  error.ToString());
          }
    }
}

}
