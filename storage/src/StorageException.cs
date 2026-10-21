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
using System.Globalization;
using System.Net;
using Google.MiniJSON;

namespace Firebase.Storage {
  /// <summary>
  ///   Represents an Exception resulting from an operation on a
  ///   <see cref="StorageReference" />
  /// </summary>
  [Serializable]
  public sealed class StorageException : Exception {
    internal const int ErrorNone = 0;
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
    internal const int ErrorUnauthenticated = ErrorNotAuthenticated;
    /// <returns>The given signin credentials are not allowed to perform this operation.</returns>
    public const int ErrorNotAuthorized = -13021;
    internal const int ErrorUnauthorized = ErrorNotAuthorized;
    /// <returns>The retry timeout was exceeded.  Check your network connection
    /// or increase the value of one of <see cref="FirebaseStorage.MaxDownloadRetryTime" />
    /// <see cref="FirebaseStorage.MaxUploadRetryTime" />
    /// or <see cref="FirebaseStorage.MaxOperationRetryTime" />
    /// </returns>
    public const int ErrorRetryLimitExceeded = -13030;
    /// <returns>There was an error validating the operation due to a checksum failure.</returns>
    public const int ErrorInvalidChecksum = -13031;
    internal const int ErrorNonMatchingChecksum = ErrorInvalidChecksum;
    /// <returns>The operation was canceled from the client.</returns>
    public const int ErrorCanceled = -13040;
    internal const int ErrorCancelled = ErrorCanceled;

    // Maps legacy C++ error codes (firebase::storage::Error) to StorageException errors and HTTP status code.
    private static readonly Dictionary<int, Tuple<int, HttpStatusCode>>
      errorToStorageErrorAndHttpStatusCode =
        new Dictionary<int, Tuple<int, HttpStatusCode>>() {
      { 2, // ErrorObjectNotFound
        new Tuple<int, HttpStatusCode>(StorageException.ErrorObjectNotFound,
                                       HttpStatusCode.NotFound) },
      { 3, // ErrorBucketNotFound
        new Tuple<int, HttpStatusCode>(StorageException.ErrorBucketNotFound,
                                       HttpStatusCode.NotFound) },
      { 4, // ErrorProjectNotFound
        new Tuple<int, HttpStatusCode>(StorageException.ErrorProjectNotFound,
                                       HttpStatusCode.NotFound) },
      { 5, // ErrorQuotaExceeded
        new Tuple<int, HttpStatusCode>(StorageException.ErrorQuotaExceeded,
                                       HttpStatusCode.ServiceUnavailable) },
      { 6, // ErrorUnauthenticated
        new Tuple<int, HttpStatusCode>(StorageException.ErrorNotAuthenticated,
                                       HttpStatusCode.Unauthorized) },
      { 7, // ErrorUnauthorized
        new Tuple<int, HttpStatusCode>(StorageException.ErrorNotAuthorized,
                                       HttpStatusCode.Unauthorized) },
      { 8, // ErrorRetryLimitExceeded
        new Tuple<int, HttpStatusCode>(StorageException.ErrorRetryLimitExceeded,
                                       HttpStatusCode.Conflict) },
      { 9, // ErrorNonMatchingChecksum
        new Tuple<int, HttpStatusCode>(StorageException.ErrorInvalidChecksum,
                                       HttpStatusCode.Conflict) },
      { 10, // ErrorDownloadSizeExceeded
        new Tuple<int, HttpStatusCode>(StorageException.ErrorUnknown, 0) },
      { 11, // ErrorCancelled
        new Tuple<int, HttpStatusCode>(StorageException.ErrorCanceled, 0) },
    };

    // Used to construct an exception for an unknown network error.
    private static readonly Tuple<int, HttpStatusCode> unknownError =
      new Tuple<int, HttpStatusCode>(StorageException.ErrorUnknown, HttpStatusCode.Ambiguous);

    internal StorageException(int errorCode, int httpResultCode, string errorMessage,
                              Exception innerException = null)
      : base(String.IsNullOrEmpty(errorMessage) ?
             GetErrorMessageForCode(errorCode) : errorMessage,
             innerException) {
      ErrorCode = errorCode;
      HttpResultCode = httpResultCode;
    }

    internal static int GetErrorCodeFromHttpStatusCode(int httpStatusCode) {
      if (httpStatusCode == 0 || (httpStatusCode >= 200 && httpStatusCode < 300) ||
          httpStatusCode == 308) {
        return ErrorNone;
      }
      switch (httpStatusCode) {
        case 401:
          return ErrorUnauthenticated;
        case 403:
          return ErrorUnauthorized;
        case 404:
          return ErrorObjectNotFound;
        case 402:
        case 429:
          return ErrorQuotaExceeded;
        case -2:
        case 408:
          return ErrorRetryLimitExceeded;
        default:
          return ErrorUnknown;
      }
    }

    internal static StorageException CreateFromHttpResponse(
        int httpStatusCode, string responseBody, Exception innerException = null) {
      int errorCode = GetErrorCodeFromHttpStatusCode(httpStatusCode);
      if (errorCode == ErrorNone) {
        errorCode = ErrorUnknown;
      }

      string serverErrorMessage = null;
      if (!string.IsNullOrEmpty(responseBody)) {
        try {
          var dict = Json.Deserialize(responseBody) as Dictionary<string, object>;
          if (dict != null && dict.TryGetValue("error", out var errorObj) && errorObj != null) {
            if (errorObj is IDictionary<string, object> errorDict) {
              if (errorDict.TryGetValue("message", out var msgObj) && msgObj != null) {
                string msgStr = Convert.ToString(msgObj, CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(msgStr)) {
                  serverErrorMessage = msgStr;
                }
              }
            } else if (errorObj is string errorStr && !string.IsNullOrEmpty(errorStr)) {
              serverErrorMessage = errorStr;
            }
          }
        } catch (Exception) {
          // Fall back to responseBody or default error message below.
        }
        if (string.IsNullOrEmpty(serverErrorMessage)) {
          serverErrorMessage = responseBody;
        }
      }

      if (string.IsNullOrEmpty(serverErrorMessage)) {
        serverErrorMessage = GetErrorMessageForCode(errorCode);
      }

      return new StorageException(errorCode, httpStatusCode, serverErrorMessage, innerException);
    }

    /// <summary>
    /// Construct a StorageException from an AggregateException class, converting FirebaseException
    /// fields if it's present.
    /// </summary>
    /// <param name="exception">AggregateException to wrap.  This accepts an Exception for
    /// so that Task.Exception can be passed to the method without casting at the call site.</param>
    /// <returns>StorageException instance wrapper.</returns>
    internal static StorageException CreateFromException(Exception exception) {
      if (exception == null) {
        return new StorageException(ErrorUnknown, (int)HttpStatusCode.Ambiguous, null);
      }
      if (exception is StorageException directStorageException) {
        return directStorageException;
      }
      if (exception is OperationCanceledException) {
        return new StorageException(ErrorCanceled, 0, GetErrorMessageForCode(ErrorCanceled),
                                    exception);
      }

      Tuple<int, HttpStatusCode> errorAndStatusCode;
      FirebaseException firebaseException = exception as FirebaseException;
      string errorMessage = null;
      if (exception is AggregateException aggregateException) {
        foreach (var innerException in aggregateException.InnerExceptions) {
          var storageException = innerException as StorageException;
          if (storageException != null) {
            return storageException;
          }
          if (innerException is OperationCanceledException) {
            return new StorageException(ErrorCanceled, 0, GetErrorMessageForCode(ErrorCanceled),
                                        innerException);
          }
          firebaseException = innerException as FirebaseException;
          if (firebaseException != null) {
            break;
          }
        }
      }
      // Try to convert the exception to a StorageException.
      if (firebaseException == null) {
        errorMessage = exception.ToString();
        errorAndStatusCode = unknownError;
      } else {
        errorMessage = firebaseException.Message;
        if (!errorToStorageErrorAndHttpStatusCode.TryGetValue(
              firebaseException.ErrorCode, out errorAndStatusCode)) {
          errorAndStatusCode = unknownError;
        }
      }
      int httpStatusCodeInt = (int)errorAndStatusCode.Item2;
      return new StorageException(errorAndStatusCode.Item1, httpStatusCodeInt, errorMessage,
                                  exception);
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
