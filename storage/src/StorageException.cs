/*
 * Copyright 2017 Google LLC
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using Firebase.Storage.Internal;

namespace Firebase.Storage {
  /// <summary>
  ///   Represents an Exception resulting from an operation on a
  ///   <see cref="StorageReference" />
  /// </summary>
  [Serializable]
  public sealed class StorageException : Exception {
    /// <returns>An unknown error has occurred. See the inner exception or
    /// <see cref="StorageException.HttpResultCode" /> for more information.
    /// </returns>
    public const int ErrorUnknown = -13000;
    /// <returns>The specified object could not be found on the server.</returns>
    public const int ErrorObjectNotFound = -13010;
    /// <returns>The specified bucket could not be found on the server.</returns>
    public const int ErrorBucketNotFound = -13011;
    /// <returns>The specified project could not be found on the server.</returns>
    public const int ErrorProjectNotFound = -13012;
    /// <returns>Free Tier quota has been exceeded.  Change your pricing plan
    /// to avoid this error.</returns>
    public const int ErrorQuotaExceeded = -13013;
    /// <returns>The given signin credentials are not valid.</returns>
    public const int ErrorNotAuthenticated = -13020;
    /// <returns>The given signin credentials are not allowed to perform this operation.</returns>
    public const int ErrorNotAuthorized = -13021;
    /// <returns>The retry timeout was exceeded.  Check your network connection
    /// or increase the value of one of <see cref="FirebaseStorage.MaxDownloadRetryTime" />
    /// <see cref="FirebaseStorage.MaxUploadRetryTime" />
    /// or <see cref="FirebaseStorage.MaxOperationRetryTime" />
    /// </returns>
    public const int ErrorRetryLimitExceeded = -13030;
    /// <returns>There was an error validating the operation due to a checksum failure.</returns>
    public const int ErrorInvalidChecksum = -13031;
    /// <returns>The operation was canceled from the client.</returns>
    public const int ErrorCanceled = -13040;

    // Maps C++ error codes to StorageException errors and HTTP status code (where appropriate).
    // NOTE: The C++ implementation does not forward HTTP status codes to the user visible API.
    // Since the Mono implementation exposes HTTP status codes in StorageException, the following
    // table adds the HTTP status code for each error where appropriate.
    private static readonly Dictionary<ErrorInternal, Tuple<int, HttpStatusCode>>
      errorToStorageErrorAndHttpStatusCode =
        new Dictionary<ErrorInternal, Tuple<int, HttpStatusCode>>() {
      { ErrorInternal.ErrorObjectNotFound,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorObjectNotFound,
                                       HttpStatusCode.NotFound) },
      { ErrorInternal.ErrorBucketNotFound,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorBucketNotFound,
                                       HttpStatusCode.NotFound) },
      { ErrorInternal.ErrorProjectNotFound,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorProjectNotFound,
                                       HttpStatusCode.NotFound) },
      { ErrorInternal.ErrorQuotaExceeded,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorProjectNotFound,
                                       HttpStatusCode.ServiceUnavailable) },
      { ErrorInternal.ErrorUnauthenticated,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorNotAuthenticated,
                                       HttpStatusCode.Unauthorized) },
      { ErrorInternal.ErrorUnauthorized,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorNotAuthorized,
                                       HttpStatusCode.Unauthorized) },
      { ErrorInternal.ErrorRetryLimitExceeded,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorRetryLimitExceeded,
                                       HttpStatusCode.Conflict) },
      { ErrorInternal.ErrorNonMatchingChecksum,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorInvalidChecksum,
                                       HttpStatusCode.Conflict) },
      { ErrorInternal.ErrorDownloadSizeExceeded,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorUnknown, 0) },
      { ErrorInternal.ErrorCancelled,
        new Tuple<int, HttpStatusCode>(StorageException.ErrorCanceled, 0) },
    };

    // Used to construct an exception for an unknown network error.
    private static readonly Tuple<int, HttpStatusCode> unknownError =
      new Tuple<int, HttpStatusCode>(StorageException.ErrorUnknown, HttpStatusCode.Ambiguous);

    internal StorageException(int errorCode, int httpResultCode, string errorMessage)
      : base(String.IsNullOrEmpty(errorMessage) ?
             GetErrorMessageForCode(errorCode) : errorMessage) {
      ErrorCode = errorCode;
      HttpResultCode = httpResultCode;
    }

    /// <summary>
    /// Construct a StorageException from an AggregateException class, converting FirebaseException
    /// fields if it's present.
    /// </summary>
    /// <param name="exception">AggregateException to wrap.  This accepts an Exception for
    /// so that Task.Exception can be passed to the method without casting at the call site.</param>
    /// <returns>StorageException instance wrapper.</returns>
    internal static StorageException CreateFromException(Exception exception) {
      Tuple<int, HttpStatusCode> errorAndStatusCode;
      AggregateException aggregateException = (AggregateException)exception;
      FirebaseException firebaseException = null;
      string errorMessage = null;
      foreach (var innerException in aggregateException.InnerExceptions) {
          var storageException = innerException as StorageException;
          firebaseException = innerException as FirebaseException;
          if (storageException != null) {
            return storageException;
          } else if (firebaseException != null) {
            break;
          }
      }
      // Try to convert the exception to a StorageException.
      if (firebaseException == null) {
        errorMessage = exception.ToString();
        errorAndStatusCode = unknownError;
      } else {
        errorMessage = firebaseException.Message;
        if (!errorToStorageErrorAndHttpStatusCode.TryGetValue(
              (ErrorInternal)firebaseException.ErrorCode, out errorAndStatusCode)) {
          errorAndStatusCode = unknownError;
        }
      }
      int httpStatusCodeInt = (int)errorAndStatusCode.Item2;
      return new StorageException(errorAndStatusCode.Item1, httpStatusCodeInt, errorMessage);
    }

    /// <returns>
    /// A code that indicates the type of error that occurred. This value will
    /// be one of the set of constants defined on <see cref="StorageException" />.
    /// </returns>
    public int ErrorCode { get; private set; }

    /// <returns>the Http result code (if one exists) from a network operation.</returns>
    public int HttpResultCode { get; private set; }

    /// <returns>
    ///   True if this request failed due to a network condition that
    ///   may be resolved in a future attempt.
    /// </returns>
    public bool IsRecoverableException {
      get { return ErrorCode == ErrorRetryLimitExceeded; }
    }

    // TODO(smiles): Since we have error strings baked into the C++ library, determine whether this
    // can be removed.
    internal static string GetErrorMessageForCode(int errorCode) {
      switch (errorCode) {
        case ErrorUnknown: {
          return "An unknown error occurred, please check the HTTP result code and inner "
                 + "exception for server response.";
        }

        case ErrorObjectNotFound: {
          return "Object does not exist at location.";
        }

        case ErrorBucketNotFound: {
          return "Bucket does not exist.";
        }

        case ErrorProjectNotFound: {
          return "Project does not exist.";
        }

        case ErrorQuotaExceeded: {
          return "Quota for bucket exceeded, please view quota on www.firebase.google"
                 + ".com/storage.";
        }

        case ErrorNotAuthenticated: {
          return "User is not authenticated, please authenticate using Firebase "
                 + "Authentication and try again.";
        }

        case ErrorNotAuthorized: {
          return "User does not have permission to access this object.";
        }

        case ErrorRetryLimitExceeded: {
          return "The operation retry limit has been exceeded.";
        }

        case ErrorInvalidChecksum: {
          return "Object has a checksum which does not match. Please retry the operation.";
        }

        case ErrorCanceled: {
          return "The operation was cancelled.";
        }

        default: {
          return "An unknown error occurred, please check the HTTP result code and inner "
                 + "exception for server response.";
        }
      }
    }
  }
}
