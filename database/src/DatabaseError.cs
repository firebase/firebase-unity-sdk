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
using System.IO;
using Firebase.Database.Internal;

namespace Firebase.Database {
  /// <summary>
  ///   Instances of DatabaseError are passed within event arguments when an
  /// operation failed.
  /// </summary>
  /// <remarks>
  ///   Instances of DatabaseError are passed to callbacks when an operation failed.
  ///   They contain a description of the specific error that occurred.
  /// </remarks>
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

    private static readonly IDictionary<int, string> ErrorReasons = new Dictionary<int, string>();

    private static readonly IDictionary<string, int> ErrorCodes = new Dictionary<string, int>();

    static DatabaseError() {
      // Preempted was removed, this is for here for completeness and history
      // public static final int PREEMPTED = -5;
      // client codes
      // Firebase Database error codes
      ErrorReasons[DataStale] = "The transaction needs to be Run again with current data";
      ErrorReasons[OperationFailed] = "The server indicated that this operation failed";
      ErrorReasons[PermissionDenied] =
        "This client does not have permission to perform this operation";
      ErrorReasons[Disconnected] = "The operation had to be aborted due to a network disconnect";
      ErrorReasons[ExpiredToken] = "The supplied auth token has expired";
      ErrorReasons[InvalidToken] = "The supplied auth token was invalid";
      ErrorReasons[MaxRetries] = "The transaction had too many retries";
      ErrorReasons[OverriddenBySet] = "The transaction was overridden by a subsequent set";
      ErrorReasons[Unavailable] = "The service is unavailable";
      ErrorReasons[UserCodeException] =
        "User code called from the Firebase Database runloop threw an exception:\n";
      // client codes
      ErrorReasons[NetworkError] = "The operation could not be performed due to a network error";
      ErrorReasons[WriteCanceled] = "The write was canceled by the user.";
      ErrorReasons[UnknownError] = "An unknown error occurred";
      // Firebase Database error codes
      ErrorCodes["datastale"] = DataStale;
      ErrorCodes["failure"] = OperationFailed;
      ErrorCodes["permission_denied"] = PermissionDenied;
      ErrorCodes["disconnected"] = Disconnected;
      ErrorCodes["expired_token"] = ExpiredToken;
      ErrorCodes["invalid_token"] = InvalidToken;
      ErrorCodes["maxretries"] = MaxRetries;
      ErrorCodes["overriddenbyset"] = OverriddenBySet;
      ErrorCodes["unavailable"] = Unavailable;
      // client codes
      ErrorCodes["network_error"] = NetworkError;
      ErrorCodes["write_canceled"] = WriteCanceled;
    }

    private DatabaseError(int code, string message) : this(code, message, null) {
    }

    private DatabaseError(int code, string message, string details) {
      Code = code;
      Message = message;
      Details = details == null ? string.Empty : details;
    }

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
    /// <param name="status">The status string</param>
    /// <returns>An error corresponding to the status</returns>
    internal static DatabaseError FromStatus(string status) {
      return FromStatus(status, null);
    }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="status">The status string</param>
    /// <param name="reason">The reason for the error</param>
    /// <returns>An error corresponding to the status</returns>
    internal static DatabaseError FromStatus(string status, string reason) {
      return FromStatus(status, reason, null);
    }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="code">The error code</param>
    /// <returns>An error corresponding to the code</returns>
    internal static DatabaseError FromCode(int code) {
      if (!ErrorReasons.ContainsKey(code)) {
        throw new ArgumentException("Invalid Firebase Database error code: " + code);
      }
      var message = ErrorReasons[code];
      return new DatabaseError(code, message, null);
    }

    /// <summary>
    ///   <strong>For internal use</strong>
    /// </summary>
    /// <hide />
    /// <param name="status">The status string</param>
    /// <param name="reason">The reason for the error</param>
    /// <param name="details">Additional details or null</param>
    /// <returns>An error corresponding the to the status</returns>
    internal static DatabaseError FromStatus(string status, string reason, string details) {
      int code;
      if (!ErrorCodes.TryGetValue(status.ToLower(), out code)) {
        code = UnknownError;
      }
      var message = reason == null ? ErrorReasons[code] : reason;
      return new DatabaseError(code, message, details);
    }

    /// <param name="error">The C++ error enum</param>
    /// <returns>The (integer) error code corresponding to the error enum</returns>
    internal static int ErrorToCode(Error error) {
      switch (error) {
        case Error.Disconnected: return Disconnected;
        case Error.ExpiredToken: return ExpiredToken;
        case Error.InvalidToken: return InvalidToken;
        case Error.MaxRetries: return MaxRetries;
        case Error.NetworkError: return NetworkError;
        case Error.OperationFailed: return OperationFailed;
        case Error.OverriddenBySet: return OverriddenBySet;
        case Error.PermissionDenied: return PermissionDenied;
        case Error.Unavailable: return Unavailable;
        case Error.UnknownError: return UnknownError;
        case Error.WriteCanceled: return WriteCanceled;
        // You specified an invalid Variant type for a field. For example,
        // a DatabaseReference's Priority and the keys of a Map must be of
        // scalar type (MutableString, StaticString, Int64, Double).
        case Error.InvalidVariantType: // TODO
        // An operation that conflicts with this one is already in progress. For
        // example, calling SetValue and SetValueAndPriority on a DatabaseReference
        // is not allowed.
        case Error.ConflictingOperationInProgress: // TODO
        // The transaction was aborted, because the user's DoTransaction function
        // returned kTransactionResultAbort instead of kTransactionResultSuccess.
        case Error.TransactionAbortedByUser: // TODO
        default: return UnknownError;
      }
    }

    /// <param name="error">The C++ error enum</param>
    /// <param name="msg">The error message from C++</param>
    /// <returns>An error corresponding to the C++ error and message</returns>
    internal static DatabaseError FromError(Error error, string msg) {
      int code = ErrorToCode(error);
      string message = (msg == null || msg == "") ? ErrorReasons[code] : msg;
      return new DatabaseError(code, message, null);
    }

    internal static DatabaseError FromException(Exception e) {
      var reason = ErrorReasons[UserCodeException] + e.Message;
      return new DatabaseError(UserCodeException, reason);
    }

    public override string ToString() {
      return "DatabaseError: " + Message;
    }

    /// <summary>
    ///   Can be used if a third party needs an Exception from Firebase Database for integration
    ///   purposes.
    /// </summary>
    /// <returns>
    ///   An exception wrapping this error, with an appropriate message and no stack trace.
    /// </returns>
    public DatabaseException ToException() {
      return new DatabaseException("Firebase Database error: " + Message);
    }
  }
}
