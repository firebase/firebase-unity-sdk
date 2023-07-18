namespace Firebase.Sample.Analytics {
  using Firebase.Extensions;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Threading.Tasks;
  using UnityEngine;

  // An automated version of the UIHandler that runs tests on Firebase Analytics.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    Task<T> WrapWithTask<T>(Func<T> function) {
      TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
      try {
        T result = function();
        tcs.TrySetResult(result);
      } catch (Exception e) {
        tcs.TrySetException(e);
      }
      return tcs.Task;
    }

    public override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestAnalyticsLoginDoesNotThrow,
        TestAnalyticsProgressDoesNotThrow,
        TestAnalyticsScoreDoesNotThrow,
        TestAnalyticsGroupJoinDoesNotThrow,
        TestAnalyticsLevelUpDoesNotThrow,
        // This test regularly fails on iOS simulator, and there isn't a great way
        // to determine if this is on a device or simulator, so just disable on
        // GHA iOS and tvOS for now.
#if !(FIREBASE_RUNNING_FROM_CI && (UNITY_IOS || UNITY_TVOS))
        TestGetSessionId,
#endif  // !(FIREBASE_RUNNING_FROM_CI && (UNITY_IOS || UNITY_TVOS))
        TestAnalyticsSetConsentDoesNotThrow,
        TestInstanceIdChangeAfterReset,
        TestResetAnalyticsData,
        // Temporarily disabled until this test is deflaked. b/143603151
        //TestCheckAndFixDependenciesInvalidOperation,
        TestCheckAndFixDependenciesDoubleCall,
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      base.Start();
    }

    // Passes along the update call to automated test runner.
    public override void Update() {
      base.Update();
      if (firebaseInitialized) {
        testRunner.Update();
      }
    }

    Task TestAnalyticsLoginDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsLogin();
        return true;
      });
    }

    Task TestAnalyticsProgressDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsProgress();
        return true;
      });
    }

    Task TestAnalyticsScoreDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsScore();
        return true;
      });
    }

    Task TestAnalyticsGroupJoinDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsGroupJoin();
        return true;
      });
    }

    Task TestAnalyticsLevelUpDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsLevelUp();
        return true;
      });
    }

    Task TestAnalyticsSetConsentDoesNotThrow() {
      return WrapWithTask(() => {
        base.AnalyticsSetConsent();
        return true;
      });
    }
    Task TestCheckAndFixDependenciesInvalidOperation() {
      // Only run the test on Android, as CheckAndFixDependenciesAsync is short
      // lived on other platforms, and thus could finish before the extra call.
      if (Application.platform == RuntimePlatform.Android) {
        Task checkDeps = Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
        bool gotException = false;
        try {
          Firebase.FirebaseApp app = Firebase.FirebaseApp.DefaultInstance;
          if (app == null) {
            throw new System.Exception("Failed to create default app instance.");
          }
        } catch (System.InvalidOperationException) {
          gotException = true;
        }
        return checkDeps.ContinueWithOnMainThread(t => {
          if (!gotException) {
            throw new System.Exception("Did not get InvalidOperationException " +
                                        "when calling a Firebase function during " +
                                        "CheckAndFixDependenciesAsync.");
          } else if (t.IsFaulted) {
            throw t.Exception;
          }
        });
      } else {
        return Task.FromResult(true);
      }
    }

    Task TestCheckAndFixDependenciesDoubleCall() {
      // There isn't a guarantee that the exception will be thrown, since it
      // starts a thread before possibly throwing the exception, so instead we
      // say the test passed if it finished with or without the expected exception.
      Action<Task<DependencyStatus>> taskHandler = t => {
        // We are allowed to have InvalidOperationException being thrown.
        if (t.IsFaulted && !(t.Exception.InnerException is System.InvalidOperationException)) {
          throw t.Exception;
        }
      };
      List<Task> tasks = new List<Task>();
      tasks.Add(Firebase.FirebaseApp.CheckAndFixDependenciesAsync()
        .ContinueWithOnMainThread(taskHandler));
      try {
        tasks.Add(Firebase.FirebaseApp.CheckAndFixDependenciesAsync()
          .ContinueWithOnMainThread(taskHandler));
      } catch (System.InvalidOperationException) {
        // Ignore this type of exception
      }
      return Task.WhenAll(tasks);
    }

    // Get the analytics instance ID executing nextCoroutine on the main thread when this is
    // complete.
    protected IEnumerator GetAnalyticsInstanceId(TaskCompletionSource<string> tcs,
                                                  long delayBeforeNextCoroutineInSeconds,
                                                  Func<IEnumerator> nextCoroutine) {
      var task = base.DisplayAnalyticsInstanceId();
      while (!task.IsCompleted) yield return null;
      tcs.TrySetResult(task.Result);
      // Wait for some time to workaround b/110166640, b/110166255 and a yet to be root caused problem
      // that causes the ID to not change if a reset event happens too quickly after the application
      // starts.
      yield return new UnityEngine.WaitForSeconds(delayBeforeNextCoroutineInSeconds);
      yield return nextCoroutine();
    }

    // Poll for a change in the analytics instance ID.
    protected IEnumerator WaitForAnalyticsInstanceIdToChange(string oldAnalyticsInstanceId,
                                                              TaskCompletionSource<bool> tcs) {
      string newAnalyticsInstanceId = null;
      long startTime = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
      const long timeoutInMilliseconds = 1000;
      // The reset operation is asynchronous so we need to poll until the ID changes.
      while ((String.IsNullOrEmpty(newAnalyticsInstanceId) ||
              newAnalyticsInstanceId == oldAnalyticsInstanceId) &&
              ((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond) - startTime) <
        timeoutInMilliseconds) {
        var newIdTask = base.DisplayAnalyticsInstanceId();
        while (!newIdTask.IsCompleted) yield return null;
        newAnalyticsInstanceId = newIdTask.Result;
      }
      if (!String.IsNullOrEmpty (newAnalyticsInstanceId) &&
          newAnalyticsInstanceId != oldAnalyticsInstanceId) {
        tcs.TrySetResult(true);
      } else {
        tcs.TrySetException(new Exception(String.Format(
          "App instance ID did not change old={0} new={1}",
          oldAnalyticsInstanceId, newAnalyticsInstanceId)));
      }
    }

    Task TestInstanceIdChangeAfterReset() {
      var tcs = new TaskCompletionSource<bool>();
      var oldIdTcs = new TaskCompletionSource<string>();
      StartCoroutine(GetAnalyticsInstanceId(oldIdTcs, 5, () => {
          base.ResetAnalyticsData();
          return WaitForAnalyticsInstanceIdToChange(oldIdTcs.Task.Result, tcs);
        }));
      return tcs.Task;
    }

    Task TestGetSessionId() {
      // Depending on platform, GetSessionId needs a few seconds for Analytics
      // to initialize. Pause for 5 seconds before running this test.
      var tcs = new TaskCompletionSource<bool>();
      Task.Delay(TimeSpan.FromSeconds(5)).ContinueWithOnMainThread(task_ => {
        base.DisplaySessionId().ContinueWithOnMainThread(task => {
          if (task.IsCanceled) {
            tcs.TrySetException(new Exception("Unexpectedly canceled"));
          } else if (task.IsFaulted) {
            // Session ID has problems when running on the Android test infrastructure,
            // as it depends on play services, which is not guaranteed to be updated.
            // There is better logic to check this in the C++ tests, which we will just
            // rely on to test the general logic.
            if (Application.platform == RuntimePlatform.Android) {
              DebugLog("GetSessionId got an exception, but that might be expected on Android");
              DebugLog("Exception: " + task.Exception);
              tcs.TrySetResult(true);
              return;
            }
            tcs.TrySetException(task.Exception);
          } else if (task.Result == 0) {
            tcs.TrySetException(new Exception("Zero ID returned"));
          } else {
            tcs.TrySetResult(true);
          }
        });
      });
      return tcs.Task;
    }

    Task TestResetAnalyticsData() {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      base.DisplayAnalyticsInstanceId().ContinueWithOnMainThread(task => {
          if (task.IsCanceled) {
            tcs.TrySetException(new Exception("Unexpectedly canceled"));
          } else if (task.IsFaulted) {
            tcs.TrySetException(task.Exception);
          } else if (String.IsNullOrEmpty(task.Result)) {
            tcs.TrySetException(new Exception("Empty ID returned"));
          } else {
            tcs.TrySetResult(true);
          }
        });
      return tcs.Task;
    }
  }
}
