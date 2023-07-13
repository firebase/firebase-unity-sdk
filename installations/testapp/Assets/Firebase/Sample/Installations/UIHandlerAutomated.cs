using Firebase.Extensions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Sample.Installations {
  // An automated version of the UIHandler that runs tests on Firebase Analytics.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    public override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestCreateDestroy,
        TestCreateDestroyRace,
        TestInstallationsGetIdAsync,
        TestInstallationsDeleteAsync,
        TestInstallationsGetTokenAsync,
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

    // If the specified task was canceled or failed, set an exception on the
    // task completion source and return true.  Returns false if no error
    // occurred.
    static bool PropagateErrorFromTaskToTaskCompletionSource(
        string operation, Task task,
        TaskCompletionSource<bool> taskCompletionSource) {
      if (task.IsCanceled) {
        taskCompletionSource.SetException(
          new Exception(String.Format("{0} was canceled.", operation)));
        return true;
      } else if (task.IsFaulted) {
        taskCompletionSource.SetException(task.Exception);
        return true;
      }
      return false;
    }

    static void SignalTaskCompletionSourceFromTaskStatus(
        string operation, Task task,
        TaskCompletionSource<bool> taskCompletionSource) {
      if (!PropagateErrorFromTaskToTaskCompletionSource(operation, task,
                                                        taskCompletionSource)) {
        taskCompletionSource.SetResult(true);
      }
    }

    static void SignalTaskCompletionSourceFromTaskStatus(
        string operation, Task<string> task,
        TaskCompletionSource<bool> taskCompletionSource) {
      if (PropagateErrorFromTaskToTaskCompletionSource(operation, task,
                                                        taskCompletionSource)) {
        return;
      } else if (String.IsNullOrEmpty(task.Result)) {
        taskCompletionSource.SetException(
           new Exception(String.Format("{0} returned no result.")));
        return;
      }
      taskCompletionSource.SetResult(true);
    }

    // Validate it's possible to create and destroy auth objects.
    Task TestCreateDestroy() {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      // Cleanup Installations object referenced by the sample application.
      installations = null;
      System.GC.Collect();
      System.GC.WaitForPendingFinalizers();
      // Kick off test on another thread.
      (new Thread(() => {
        Exception caughtException = null;
        try {
          var CREATE_DESTROY_ITERATIONS = 100;
          List<Task> taskList = new List<Task>();
          // Dispose of an App object that is in use by an installations object.
          for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
            UnityEngine.Debug.Log(String.Format("Dispose app {0}/{1}", i + 1,
                                                CREATE_DESTROY_ITERATIONS));
            var app = Firebase.FirebaseApp.DefaultInstance;
            installations = Firebase.Installations.FirebaseInstallations.GetInstance(app);
            app.Dispose();

            Task task = null;
            try {
              task = installations.GetIdAsync();
            } catch (System.NullReferenceException) {
              // Expecting NullReferenceException because installations should be disposed after
              // app is disposed.
            }

            if (task != null) {
              taskList.Add(task);
            }
            app = null;
            installations = null;
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
          }

          // Make sure that the pending tasks have all finished before continuing.
          // Parse does not support WaitAll, so use WhenAll to combine to one task, then wait.
          Task.WhenAll(taskList.ToArray()).Wait();

          // Throw an exception and stop the test if GetIdAsync() did not throw a
          // NullReferenceException
          if (taskList.Count > 0) {
            throw new System.Exception("Expecting GetIdAsync() to thrown an exception");
          }

          // Ensure finalization of Installations and app objects does not result in a crash due to out of
          // order destruction of native objects.
          for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
            UnityEngine.Debug.Log(String.Format("Finalize app and Installations {0}/{1}", i + 1,
                                                CREATE_DESTROY_ITERATIONS));
            var app = Firebase.FirebaseApp.DefaultInstance;
            installations = Firebase.Installations.FirebaseInstallations.GetInstance(app);
            UnityEngine.Debug.Log(String.Format("Created Installations {0} for {1}", installations.App.Name,
                                                app.Name));
            app = null;
            installations = null;
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
          }
        } catch (Exception exception) {
          caughtException = exception;
        } finally {
          // Try to restore UIHandler's initial state.
          installations = Firebase.Installations.FirebaseInstallations.DefaultInstance;
          UnityEngine.Debug.Log(
              String.Format("Recreated Installations for app {0}", installations.App.Name));

          // Postpone the task completion after installations is restore.  Otherwise, later tests
          // may be installations as null pointer.
          if (caughtException == null) {
            tcs.TrySetResult(true);
          } else {
            tcs.TrySetException(caughtException);
          }
        }
      })).Start();
      return tcs.Task;
    }

    // Validate it's possible to create and destroy Installations objects in race condition between
    // main thread and GC thread.
    Task TestCreateDestroyRace() {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      // Cleanup Installations object referenced by the sample application.
      installations = null;
      System.GC.Collect();
      System.GC.WaitForPendingFinalizers();
      // Kick off test on another thread.
      (new Thread(() => {
        Exception caughtException = null;
        try {
          var CREATE_DESTROY_ITERATIONS = 100;
          // Dispose of an App object that is in use by an auth object.
          for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
            UnityEngine.Debug.Log(String.Format("Dispose app {0}/{1}", i + 1,
                                                CREATE_DESTROY_ITERATIONS));
            var app = Firebase.FirebaseApp.DefaultInstance;
            installations = Firebase.Installations.FirebaseInstallations.GetInstance(app);

            try {
              installations.GetIdAsync();
            } catch (System.NullReferenceException) {
              // Possible NullReferenceException because installations might be still
              // being disposed from the previous loop, but not ideal.
              DebugLog("WARNING: GetIdAsync threw a NullReferenceException");
            }

            app = null;
            installations = null;
            System.GC.Collect();
          }

          // Wait for pending finalizers for the next test
          System.GC.WaitForPendingFinalizers();

          // Ensure finalization of Installations and app objects does not result in a crash due to
          // out of order destruction of native objects.
          for (int i = 0; i < CREATE_DESTROY_ITERATIONS; ++i) {
            UnityEngine.Debug.Log(String.Format("Finalize app and Installations {0}/{1}", i + 1,
                                                CREATE_DESTROY_ITERATIONS));
            var app = Firebase.FirebaseApp.DefaultInstance;
            installations = Firebase.Installations.FirebaseInstallations.GetInstance(app);
            UnityEngine.Debug.Log(String.Format("Created Installations {0} for {1}",
                                                installations.App.Name, app.Name));
            app = null;
            installations = null;
            System.GC.Collect();
          }
        } catch (Exception exception) {
          caughtException = exception;
        } finally {
          // Wait for pending finalizers before restoring
          System.GC.WaitForPendingFinalizers();

          // Try to restore UIHandler's initial state.
          installations = Firebase.Installations.FirebaseInstallations.DefaultInstance;
          UnityEngine.Debug.Log(
              String.Format("Recreated Installations for app {0}", installations.App.Name));

          // Postpone the task completion after installations is restore.  Otherwise, later tests
          // may be installations as null pointer.
          if (caughtException == null) {
            tcs.SetResult(true);
          } else {
            tcs.SetException(caughtException);
          }
        }
      })).Start();
      return tcs.Task;
    }

    Task TestInstallationsGetIdAsync() {
      TaskCompletionSource<bool> taskCompletionSource =
        new TaskCompletionSource<bool>();
      base.GetIdAsync().ContinueWithOnMainThread(task => {
        SignalTaskCompletionSourceFromTaskStatus("GetIdAsync", task,
                                                 taskCompletionSource);
      });
      return taskCompletionSource.Task;
    }

    Task TestInstallationsDeleteAsync() {
      TaskCompletionSource<bool> taskCompletionSource =
        new TaskCompletionSource<bool>();
      base.DeleteAsync().ContinueWithOnMainThread(task => {
        SignalTaskCompletionSourceFromTaskStatus("DeleteAsync", task, taskCompletionSource);
      });
      return taskCompletionSource.Task;
    }

    Task TestInstallationsGetTokenAsync() {
      TaskCompletionSource<bool> taskCompletionSource =
        new TaskCompletionSource<bool>();
      base.GetTokenAsync().ContinueWithOnMainThread(task => {
        SignalTaskCompletionSourceFromTaskStatus("GetTokenAsync", task,
                                                 taskCompletionSource);
      });
      return taskCompletionSource.Task;
    }
  }
}
