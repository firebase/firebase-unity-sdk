// Copyright 2023 Google Inc. All rights reserved.
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

namespace Firebase.Sample.AppCheck {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.AppCheck;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;
  using System.Threading.Tasks;
  using UnityEngine;

  // An automated version of the UIHandler that runs tests on Firebase AppCheck.
  public class UIHandlerAutomated : UIHandler {
    // Delegate which validates a completed task.
    delegate Task TaskValidationDelegate(Task task);

    private Firebase.Sample.AutomatedTestRunner testRunner;

    // Your Firebase project's Debug token goes here.
    // You can get this from Firebase Console, in the App Check settings.
    private string appCheckDebugTokenForAutomated = "REPLACE_WITH_APP_CHECK_TOKEN";

    protected override void Start() {
      // Set up the debug token, and install it as the factory to use.
      DebugAppCheckProviderFactory.Instance.SetDebugToken(appCheckDebugTokenForAutomated);
      FirebaseAppCheck.SetAppCheckProviderFactory(DebugAppCheckProviderFactory.Instance);

      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestGetDebugToken,
        TestGetAppCheckToken
      };

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      Debug.Log("NOTE: Some API calls report failures using UnityEngine.Debug.LogError which will " +
                "pause execution in the editor when 'Error Pause' in the console window is " +
                "enabled.  `Error Pause` should be disabled to execute this test.");

      UIEnabled = true;
      // Set this state to true, since App Check is a bit finicky around start up,
      // this will just disable the usual UIHandler UI, since it won't work as expected.
      runningAutomatedTests = true;
      // Do not call base.Start(), as we don't want to initialize Firebase (and instead do it in the tests).
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (testRunner != null) {
        testRunner.Update();
      }
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition)
        throw new Exception(String.Format("Assertion failed ({0}): {1}",
                                          testRunner.CurrentTestDescription, message));
    }

    // Throw when value1 != value2.
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!(object.Equals(value1, value2))) {
        throw new Exception(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                                          testRunner.CurrentTestDescription, value1, value2,
                                          message));
      }
    }

    Task TestGetDebugToken() {
      IAppCheckProvider provider = DebugAppCheckProviderFactory.Instance.CreateProvider(FirebaseApp.DefaultInstance);
      return provider.GetTokenAsync().ContinueWithOnMainThread(task => {
        if (task.IsFaulted) {
          throw task.Exception;
        } else {
          Assert("Debug token is empty", task.Result.Token != "");

          // The expire time should be within roughly an hour, so check for that.
          DateTime time = DateTime.UtcNow.AddMinutes(120);
          Assert("Debug token time is too long", task.Result.ExpireTime < time);
        }
      });
    }

    Task TestGetAppCheckToken() {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

      FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(true).ContinueWithOnMainThread(t1 => {
        if (t1.IsFaulted) {
          tcs.TrySetException(t1.Exception);
        } else {
          FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(false).ContinueWithOnMainThread(t2 => {
            if (t2.IsFaulted) {
              tcs.TrySetException(t2.Exception);
            } else if (t1.Result.Token != t2.Result.Token) {
              tcs.TrySetException(new Exception("GetAppCheckTokenAsync(false) returned a different token"));
            } else {
              FirebaseAppCheck.DefaultInstance.GetAppCheckTokenAsync(true).ContinueWithOnMainThread(t3 => {
                if (t3.IsFaulted) {
                  throw t3.Exception;
                } else if (t1.Result.Token == t3.Result.Token) {
                  tcs.TrySetException(new Exception("GetAppCheckTokenAsync(true) returned the same token"));
                } else {
                  // Done with the test
                  tcs.TrySetResult(true);
                }
              });
            }
          });
        }
      });

      return tcs.Task;
    }
  }
}
