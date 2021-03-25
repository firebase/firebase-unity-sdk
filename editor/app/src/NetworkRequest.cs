/*
 * Copyright 2019 Google LLC
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

// The Unity API and Mono network APIs are not reliable.
// As such, editor network calls will go through the network_request.py.
// This file contains the C# abstraction to call the python script.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;
using UnityEditor;

using GooglePlayServices;    // For CommandLine

namespace Firebase.Editor
{
  /// <summary>
  /// The class is responsible for taking the contents of a network request and
  /// making the request via the network_request.py/.exe
  /// </summary>
  internal class NetworkRequest
  {
    private static PythonExecutor executor =
      new PythonExecutor(Path.Combine(Path.Combine("Assets", "Firebase"), "Editor"),
                         "network_request.py", "e6e32fecbfd44fab946fa160e4861924",
                         "network_request.exe", "d3cd5d0a941c4cdc8ab4b1b684b05191");

    /// Returns the status of the network request
    public enum Status
    {
      // These values are defined in the network_request.py

      /// The request succeeded (200-299)
      SUCCESS = 0,

      /// The request failed with a system error
      SYS_ERROR = 1,

      /// The input for the network request is invalid
      INVALID_REQUEST_VALUES_ERROR = 2,

      /// There's a generic error from the Python HttpLib library
      GENERIC_HTTPLIB_ERROR = 3,

      /// The request timed out
      HTTP_TIMEOUT_ERROR = 4,

      /// The request returned a 302
      HTTP_REDIRECT_ERROR = 5,

      /// The request returned a 404
      HTTP_NOT_FOUND_ERROR = 6,

      /// The request returned a 500+
      HTTP_SERVER_ERROR = 7,

      /// The request returned an unknown http status code
      HTTP_UNKNOWN_ERROR = 8,

      /// An error occured from trying to execute the command line
      /// which calls the network_request.py/exe
      UNKNOWN_EXECUTION_ERROR = 9
    };

    /// <summary>
    /// Result of the network request's execution.
    /// </summary>
    public class Result {

      /// <summary>
      /// Status code from the script.
      /// </summary>
      public Status Status { get { return (Status)CommandLineResult.exitCode; } }

      /// <summary>
      /// Full command line results.
      /// </summary>
      public CommandLine.Result CommandLineResult { get; set; }
    }

    /// HTTP Methods
    /// (Only POST is supported by the network_request python script right now)
    public enum Method
    {
      /// HTTP Post method
      POST
    }

    private Method method;
    private string url;
    private Dictionary<String, String> headers;
    private string body;
    private float timeout;

    private string projectPath; // Make read only

    #region Constructor

    /// <summary>
    /// Constructor for the network request
    /// </summary>
    /// <param name="method"> HTTP Method for the request </param>
    /// <param name="url"> Url for the request </param>
    /// <param name="headers"> Headers for the request </param>
    /// <param name="body"> Body for the request </param>
    /// <param name="timeOut"> Timeout for the request </param>
    public NetworkRequest(Method method,
                          string url,
                          Dictionary<String, String> headers,
                          string body,
                          float timeOut)
    {
      this.method = method;
      this.url = url;
      this.headers = headers;
      this.body = body;
      this.timeout = timeOut;

      this.projectPath = Application.dataPath;
    }

    #endregion   // Constructor

    #region Create Command line

    /// Returns the arguments for network_request.py/exe
    private IEnumerable<string> NetworkRequestExecutableArgs
    {
      get
      {
        var args = new List<string>();
        args.Add(method.ToString().ToLower());
        args.Add("--url");
        args.Add(String.Format("\"{0}\"", url));

        if (headers != null)
        {
          foreach (var key in this.headers.Keys)
          {
            args.Add("--header");
            args.Add(String.Format("\"{0}:{1}\"", key, this.headers[key]));
          }
        }

        if (body != null) {
          args.Add("--body");
          args.Add(String.Format("\"{0}\"", body));
        }

        if (timeout > 0)
        {
          args.Add("--timeout");
          args.Add(timeout.ToString());
        }
        return args;
     }
    }

    #endregion   // Create Command line

    /// <summary>
    /// Makes network request synchronously
    /// </summary>
    /// <param name="completionHandler">
    /// Handler to be called with the Result when completed
    /// </param>
    /// <param name="showLogs"> Show logs for the request execution </param>
    public void MakeAsynchronousRequest(Action<Result> completionHandler,
                                        bool showLogs = false)
    {
      var args = NetworkRequestExecutableArgs;
      var command = executor.GetCommand(args);
      try
      {
        if (showLogs)
        {
          Debug.Log(String.Format("Making async network request: \n{0}", command));
        }

        executor.RunAsync(executor.GetArguments(args),
                          (CommandLine.Result result) => {
                            if (showLogs) {
                              Debug.Log(String.Format("Network request completed.\n{0}",
                                                      result.message));
                            }
                            completionHandler(new Result { CommandLineResult = result });
                          });
      }
      catch(Exception e)
      {
        if (showLogs)
        {
          Debug.LogWarning(String.Format("Network execution error: {0}\n{1}", e, command));
        }

        completionHandler(new Result {
            CommandLineResult = new CommandLine.Result {
              message = e.ToString(),
              exitCode = (int)Status.UNKNOWN_EXECUTION_ERROR
            }
          });
      }
    }

  } // class NetworkRequest
}  // namespace Firebase.Editor
