/*
 * Copyright 2016 Google LLC
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

namespace Firebase.Database {
  /// <summary>
  ///   Instances of DatabaseError are passed within event arguments when an
  /// operation failed.
  /// </summary>
  /// <remarks>
  ///   Instances of DatabaseError are passed to callbacks when an operation failed.
  ///   They contain a description of the specific error that occurred.
  /// </remarks>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class DatabaseError {
    /// <summary>
    ///   <strong>Internal use.</strong>
    /// </summary>
    internal const int DataStale = -1;

    /// <summary>The server indicated that this operation failed.</summary>
    public const int OperationFailed = -2;

    /// <summary>This client does not have permission to perform this operation.</summary>
    public const int PermissionDenied = -3;

    /// <summary>The operation had to be aborted due to a network disconnect.</summary>
    public const int Disconnected = -4;

    /// <summary>The supplied auth token has expired.</summary>
    public const int ExpiredToken = -6;

    /// <summary>The specified authentication token is invalid.</summary>
    /// <remarks>
    ///   The specified authentication token is invalid. This can occur when the token is malformed,
    ///   expired, or the secret that was used to generate it has been revoked.
    /// </remarks>
    public const int InvalidToken = -7;

    /// <summary>The transaction had too many retries.</summary>
    public const int MaxRetries = -8;

    /// <summary>The transaction was overridden by a subsequent set.</summary>
    public const int OverriddenBySet = -9;

    /// <summary>The service is unavailable.</summary>
    public const int Unavailable = -10;

    /// <summary>An exception occurred in user code.</summary>
    public const int UserCodeException = -11;

    /// <summary>The operation could not be performed due to a network error.</summary>
    public const int NetworkError = -24;

    /// <summary>The write was canceled locally.</summary>
    public const int WriteCanceled = -25;

    /// <summary>An unknown error occurred.</summary>
    /// <remarks>
    ///   An unknown error occurred. Please refer to the error message and error details for more information.
    /// </remarks>
    public const int UnknownError = -999;

    private static readonly Dictionary<int, string> ErrorReasons = new Dictionary<int, string> {
      // Preempted was removed, this is for here for completeness and history
      // public static final int PREEMPTED = -5;
      // client codes
      // Firebase Database error codes
      [DataStale] = "The transaction needs to be Run again with current data",
      [OperationFailed] = "The server indicated that this operation failed",
      [PermissionDenied] = "This client does not have permission to perform this operation",
      [Disconnected] = "The operation had to be aborted due to a network disconnect",
      [ExpiredToken] = "The supplied auth token has expired",
      [InvalidToken] = "The supplied auth token was invalid",
      [MaxRetries] = "The transaction had too many retries",
      [OverriddenBySet] = "The transaction was overridden by a subsequent set",
      [Unavailable] = "The service is unavailable",
      [UserCodeException] = "User code called from the Firebase Database runloop threw an exception:\n",
      // client codes
      [NetworkError] = "The operation could not be performed due to a network error",
      [WriteCanceled] = "The write was canceled by the user.",
      [UnknownError] = "An unknown error occurred",
    };

    private static readonly Dictionary<string, int> ErrorCodes =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) {
          // Firebase Database error codes
          ["datastale"] = DataStale,
          ["failure"] = OperationFailed,
          ["permission_denied"] = PermissionDenied,
          ["disconnected"] = Disconnected,
          ["expired_token"] = ExpiredToken,
          ["invalid_token"] = InvalidToken,
          ["maxretries"] = MaxRetries,
          ["overriddenbyset"] = OverriddenBySet,
          ["unavailable"] = Unavailable,
          // client codes
          ["network_error"] = NetworkError,
          ["write_canceled"] = WriteCanceled,
        };

    private DatabaseError(int code, string message, string details = null, Exception cause = null) {
      Code = code;
      Message = message ?? string.Empty;
      Details = details ?? string.Empty;
      Cause = cause;
    }

    internal Exception Cause { get; }

    /// <returns>One of the defined status codes declared under <see cref="DatabaseError"/>, depending on the error</returns>
    public int Code { get; private set; }

    /// <returns>A human-readable description of the error</returns>
    public string Message { get; private set; }

    /// <returns>
    ///   Human-readable details on the error and additional information.
    /// </returns>
    public string Details { get; private set; }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="code">The error code</param>
    /// <returns>An error corresponding to the code</returns>
    internal static DatabaseError FromCode(int code) {
      if (!ErrorReasons.TryGetValue(code, out string message)) {
        message = ErrorReasons[UnknownError];
      }
      return new DatabaseError(code, message);
    }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="status">The status string</param>
    /// <param name="reason">The reason for the error</param>
    /// <param name="details">Additional details or null</param>
    /// <returns>An error corresponding the to the status</returns>
    internal static DatabaseError FromStatus(string status, string reason = null, string details = null) {
      if (string.IsNullOrEmpty(status) || !ErrorCodes.TryGetValue(status, out int code)) {
        code = UnknownError;
      }
      string message = string.IsNullOrEmpty(reason) ? ErrorReasons[code] : reason;
      return new DatabaseError(code, message, details);
    }

    internal static DatabaseError FromException(Exception e) {
      string reason = ErrorReasons[UserCodeException] + (e?.Message ?? string.Empty);
      return new DatabaseError(UserCodeException, reason, details: null, cause: e);
    }

    /// <summary>Returns a string representation of this error.</summary>
    public override string ToString() {
      return "DatabaseError: " + Message;
    }

    /// <summary>
    ///   Can be used if a third party needs an Exception from Firebase Database for integration
    ///   purposes.
    /// </summary>
    /// <returns>
    ///   An exception wrapping this error, with an appropriate message.
    /// </returns>
    public DatabaseException ToException() {
      return Cause != null
          ? new DatabaseException("Firebase Database error: " + Message, Cause)
          : new DatabaseException("Firebase Database error: " + Message);
    }
  }
}
