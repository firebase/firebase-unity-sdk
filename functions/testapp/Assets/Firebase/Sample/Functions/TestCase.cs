// Copyright 2018 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.Functions {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.Functions;
  using System;
  using System.Collections.Generic;
  using System.Threading.Tasks;

  public class TestCase {
    // The display name of the test.
    public string Name { get; private set; }

    // The name of the HTTPS callable function to call.
    public string FunctionName { get; private set; }

    // The parameters to pass to the function.
    public object Input { get; private set; }

    // The data expected to be returned from the function.
    public object ExpectedData { get; private set; }

    // The error code expected to be returned from the function.
    public FunctionsErrorCode ExpectedError { get; private set; }

    // The options to pass to the function.
    public HttpsCallableOptions Options { get; private set; }

    public TestCase(string name, string functionName, object input, object expectedResult,
        FunctionsErrorCode expectedError = FunctionsErrorCode.None,
        HttpsCallableOptions options = null) {
      Name = name;
      FunctionName = functionName;
      Input = input;
      ExpectedData = expectedResult;
      ExpectedError = expectedError;
      Options = options;
    }

    // Returns the CallableReference to be used by the test. Overridable to allow
    // different ways to generate the CallableReference.
    public virtual HttpsCallableReference GetReference(FirebaseFunctions functions) {
      return functions.GetHttpsCallable(FunctionName, Options);
    }

    // Runs the given test and returns whether it passed.
    public virtual Task RunAsync(FirebaseFunctions functions,
        Utils.Reporter reporter) {
      var func = GetReference(functions);
      return func.CallAsync(Input).ContinueWithOnMainThread((task) => {
        if (ExpectedError == FunctionsErrorCode.None) {
          // We expected no error.
          if (task.IsFaulted) {
            // The function unexpectedly failed.
            throw task.Exception;
          }
          // The function succeeded.
          if (!Utils.DeepEquals(ExpectedData, task.Result.Data, reporter)) {
            throw new Exception(String.Format("Incorrect result. Got {0}. Want {1}.",
              Utils.DebugString(task.Result.Data),
              Utils.DebugString(ExpectedData)));
          }
          return;
        }

        // The function was expected to fail.
        FunctionsException e = null;
        foreach (var inner in task.Exception.InnerExceptions) {
          if (inner is FunctionsException) {
            e = (FunctionsException)inner;
            break;
          }
        }
        if (e == null) {
          // We didn't get a proper Functions Exception.
          throw task.Exception;
        }

        if (e.ErrorCode != ExpectedError) {
          // The code wasn't right.
          throw new Exception(String.Format("Error {0}: {1}", e.ErrorCode, e.Message));
        }
        reporter(String.Format("  Got expected error {0}: {1}", e.ErrorCode,
          e.Message));
      });
    }
  }

  // TestCase that uses a URL to call the function directly.
  public class TestCaseWithURL : TestCase {
    // The URL of the function to call
    System.Uri URL { get; set; }

    public TestCaseWithURL(string name, System.Uri url, object input, object expectedResult,
        FunctionsErrorCode expectedError = FunctionsErrorCode.None)
          : base(name, url.ToString(), input, expectedResult, expectedError) {
            URL = url;
        }

    // Generate the CallableReference using the URL
    public override HttpsCallableReference GetReference(FirebaseFunctions functions) {
      return functions.GetHttpsCallableFromURL(URL, Options);
    }
  }

  public struct ExpectedStreamResponse {
    public bool IsResult;
    public object Data;
  }

  // TestCase that tests streaming.
  public class StreamingTestCase : TestCase {
    // List of expected messages/results in order.
    public List<ExpectedStreamResponse> ExpectedStreamResponses { get; private set; }

    public StreamingTestCase(string name, string functionName, object input, List<ExpectedStreamResponse> expectedResponses, HttpsCallableOptions options = null)
        : base(name, functionName, input, null, FunctionsErrorCode.None, options) {
      ExpectedStreamResponses = expectedResponses;
    }

    public override async Task RunAsync(FirebaseFunctions functions, Utils.Reporter reporter) {
      var func = GetReference(functions);

      int index = 0;
      await foreach (var response in func.StreamAsync(Input)) {
        if (index >= ExpectedStreamResponses.Count) {
          throw new Exception(String.Format("Got more stream responses than expected ({0}).", index + 1));
        }
        var expected = ExpectedStreamResponses[index];
        bool gotResult = response is StreamResponse.Result;
        if (gotResult != expected.IsResult) {
          throw new Exception(String.Format("Response type mismatch at index {0}. Got {1}, want {2}.",
            index, gotResult ? "Result" : "Message", expected.IsResult ? "Result" : "Message"));
        }
        
        object gotData = gotResult ? ((StreamResponse.Result)response).Data : ((StreamResponse.Message)response).Data;
        if (!Utils.DeepEquals(expected.Data, gotData, reporter)) {
          throw new Exception(String.Format("Payload mismatch at index {0}. Got {1}, want {2}.",
            index, Utils.DebugString(gotData), Utils.DebugString(expected.Data)));
        }
        index++;
      }

      if (index < ExpectedStreamResponses.Count) {
        throw new Exception(String.Format("Got fewer stream responses than expected. Got {0}, want {1}.",
          index, ExpectedStreamResponses.Count));
      }
    }
  }

  // TestCase that tests streaming with expected error.
  public class StreamingTestCaseWithError : TestCase {
    public StreamingTestCaseWithError(string name, string functionName, object input, FunctionsErrorCode expectedError, HttpsCallableOptions options = null)
        : base(name, functionName, input, null, expectedError, options) {}

    public override async Task RunAsync(FirebaseFunctions functions, Utils.Reporter reporter) {
      var func = GetReference(functions);

      try {
        await foreach (var response in func.StreamAsync(Input)) {
          // We expect an error, so we shouldn't get standard responses.
        }
        throw new Exception("Stream completed successfully but expected error: " + ExpectedError);
      } catch (FunctionsException ex) {
        if (ex.ErrorCode != ExpectedError) {
          throw new Exception(String.Format("Got error {0} but expected {1}. Message: {2}", ex.ErrorCode, ExpectedError, ex.Message));
        } else {
          reporter(String.Format("  Got expected stream error {0}: {1}", ex.ErrorCode, ex.Message));
        }
      }
    }
  }
}
