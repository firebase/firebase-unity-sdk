/*
* Copyright 2026 Google LLC
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
using System.Net.Http;
using Google.MiniJSON;

namespace Firebase.Functions.Internal
{
  // Helper class to map HTTP responses to FunctionsErrorCode and extract error details.
  internal static class FunctionsErrorParser
  {
    /// <summary>
    /// Maps an HTTP status code to the corresponding FunctionsErrorCode.
    /// Default is Internal if no specific mapping exists.
    /// </summary>
    internal static FunctionsErrorCode MapHttpStatusToEnum(int httpStatusCode)
    {
      switch (httpStatusCode)
      {
        case 200: return FunctionsErrorCode.None;
        case 400: return FunctionsErrorCode.InvalidArgument;
        case 401: return FunctionsErrorCode.Unauthenticated;
        case 403: return FunctionsErrorCode.PermissionDenied;
        case 404: return FunctionsErrorCode.NotFound;
        case 409: return FunctionsErrorCode.AlreadyExists;
        case 429: return FunctionsErrorCode.ResourceExhausted;
        case 499: return FunctionsErrorCode.Cancelled;
        case 500: return FunctionsErrorCode.Internal;
        case 501: return FunctionsErrorCode.Unimplemented;
        case 503: return FunctionsErrorCode.Unavailable;
        case 504: return FunctionsErrorCode.DeadlineExceeded;
        // This shouldn't happen, but the iOS and Android SDKs default to INTERNAL.
        default: return FunctionsErrorCode.Internal;
      }
    }

    /// <summary>
    /// Maps a canonical status string (e.g., from a JSON error response) to the corresponding FunctionsErrorCode.
    /// Default is Internal if no specific mapping exists.
    /// </summary>
    internal static FunctionsErrorCode MapStatusStringToEnum(string status)
    {
      switch (status)
      {
        case "OK": return FunctionsErrorCode.None;
        case "CANCELLED": return FunctionsErrorCode.Cancelled;
        case "UNKNOWN": return FunctionsErrorCode.Unknown;
        case "INVALID_ARGUMENT": return FunctionsErrorCode.InvalidArgument;
        case "DEADLINE_EXCEEDED": return FunctionsErrorCode.DeadlineExceeded;
        case "NOT_FOUND": return FunctionsErrorCode.NotFound;
        case "ALREADY_EXISTS": return FunctionsErrorCode.AlreadyExists;
        case "PERMISSION_DENIED": return FunctionsErrorCode.PermissionDenied;
        case "RESOURCE_EXHAUSTED": return FunctionsErrorCode.ResourceExhausted;
        case "FAILED_PRECONDITION": return FunctionsErrorCode.FailedPrecondition;
        case "ABORTED": return FunctionsErrorCode.Aborted;
        case "OUT_OF_RANGE": return FunctionsErrorCode.OutOfRange;
        case "UNIMPLEMENTED": return FunctionsErrorCode.Unimplemented;
        case "INTERNAL": return FunctionsErrorCode.Internal;
        case "UNAVAILABLE": return FunctionsErrorCode.Unavailable;
        case "DATA_LOSS": return FunctionsErrorCode.DataLoss;
        case "UNAUTHENTICATED": return FunctionsErrorCode.Unauthenticated;
        default: return FunctionsErrorCode.Internal;
      }
    }

    /// <summary>
    /// Returns a human-readable description for a given FunctionsErrorCode.
    /// </summary>
    internal static string ErrorDescription(FunctionsErrorCode code)
    {
      switch (code)
      {
        case FunctionsErrorCode.None: return "OK";
        case FunctionsErrorCode.Cancelled: return "CANCELLED";
        case FunctionsErrorCode.Unknown: return "UNKNOWN";
        case FunctionsErrorCode.InvalidArgument: return "INVALID ARGUMENT";
        case FunctionsErrorCode.DeadlineExceeded: return "DEADLINE EXCEEDED";
        case FunctionsErrorCode.NotFound: return "NOT FOUND";
        case FunctionsErrorCode.AlreadyExists: return "ALREADY EXISTS";
        case FunctionsErrorCode.PermissionDenied: return "PERMISSION DENIED";
        case FunctionsErrorCode.ResourceExhausted: return "RESOURCE EXHAUSTED";
        case FunctionsErrorCode.FailedPrecondition: return "FAILED PRECONDITION";
        case FunctionsErrorCode.Aborted: return "ABORTED";
        case FunctionsErrorCode.OutOfRange: return "OUT OF RANGE";
        case FunctionsErrorCode.Unimplemented: return "UNIMPLEMENTED";
        case FunctionsErrorCode.Internal: return "INTERNAL";
        case FunctionsErrorCode.Unavailable: return "UNAVAILABLE";
        case FunctionsErrorCode.DataLoss: return "DATA LOSS";
        case FunctionsErrorCode.Unauthenticated: return "UNAUTHENTICATED";
        default: return "INTERNAL";
      }
    }

    /// <summary>
    /// Parses an HTTP response to extract the FunctionsErrorCode and error message,
    /// returning a FunctionsException containing these details.
    /// Fallbacks to HTTP status code if the JSON body cannot be parsed.
    /// </summary>
    internal static FunctionsException ParseError(HttpResponseMessage response, string responseBody)
    {
      int statusCode = (int)response.StatusCode;
      FunctionsErrorCode code = MapHttpStatusToEnum(statusCode);
      string message = ErrorDescription(code);

      if (!string.IsNullOrEmpty(responseBody))
      {
        try
        {
          var jsonMap = Json.Deserialize(responseBody) as Dictionary<string, object>;
          if (jsonMap != null && jsonMap.TryGetValue("error", out var errorObj) && errorObj is Dictionary<string, object> errorDict)
          {
            if (errorDict.TryGetValue("status", out var statusStr) && statusStr is string s)
            {
              code = MapStatusStringToEnum(s);
              // If the code in the body is invalid, treat the whole response as malformed.
              if (code == FunctionsErrorCode.Internal && s != "INTERNAL")
              {
                return new FunctionsException(FunctionsErrorCode.Internal, "INTERNAL");
              }
            }

            if (errorDict.TryGetValue("message", out var msgStr) && msgStr is string msg)
            {
              message = msg;
            }
            else
            {
              message = ErrorDescription(code);
            }
          }
        }
        catch (Exception)
        {
          // Ignore parsing errors, we just use the HTTP status as fallback
        }
      }

      return new FunctionsException(code, message);
    }
  }
}
