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
          return ErrorNotAuthenticated;
        case 403:
          return ErrorNotAuthorized;
        case 404:
          return ErrorObjectNotFound;
        case 402:
        case 429:
          return ErrorQuotaExceeded;
        case 408:
          return ErrorRetryLimitExceeded;
        default:
          return ErrorUnknown;
      }
    }

    // HTTP 404 is returned for a missing object, bucket or project alike; disambiguate using the
    // server's error message (e.g. "Not Found.  Could not access bucket \"x\"") so that
    // ErrorBucketNotFound / ErrorProjectNotFound are reachable, falling back to ErrorObjectNotFound.
    internal static int RefineNotFoundErrorCode(string serverErrorMessage) {
      if (!string.IsNullOrEmpty(serverErrorMessage)) {
        if (serverErrorMessage.IndexOf("bucket", StringComparison.OrdinalIgnoreCase) >= 0) {
          return ErrorBucketNotFound;
        }
        if (serverErrorMessage.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0) {
          return ErrorProjectNotFound;
        }
      }
      return ErrorObjectNotFound;
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

      if (httpStatusCode == 404) {
        errorCode = RefineNotFoundErrorCode(serverErrorMessage);
      }

      if (string.IsNullOrEmpty(serverErrorMessage)) {
        serverErrorMessage = GetErrorMessageForCode(errorCode);
      }

      return new StorageException(errorCode, httpStatusCode, serverErrorMessage, innerException);
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
