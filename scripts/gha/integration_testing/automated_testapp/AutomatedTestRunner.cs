namespace Firebase.Sample {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Threading.Tasks;
  using UnityEngine;
  using Firebase.TestLab;

  public class AutomatedTestRunner {
    // Function used to log the results of the tests. Defaults to UnityEngine.Debug.Log().
    public Action<string> LogFunc { get; set; }
    // The index of the currently running test.
    private int currentTestIndex = -1;
    // Has the Runner started executing the test cases.
    private bool startedExecution = false;
    // List of test cases to run.
    private List<AutomatedTestCase> tests = null;
    // Have all the tests been run and finished.
    public bool Finished { get; private set; }

    // The currently running test case
    private AutomatedTestCase CurrentRunningTest {
      get {
        if (tests == null || currentTestIndex < 0 || currentTestIndex >= tests.Count) {
          return null;
        }
        return tests[currentTestIndex];
      }
    }
    public string CurrentTestDescription {
      get {
        return CurrentRunningTest?.Description;
      }
    }

    private TestLabManager testLabManager;

    // Used to output summary of the test run.
    private int passedTestsCount = 0;
    private int failedTestsCount = 0;
    // Descriptions of failing tests.
    private List<string> failingTestDescriptions = new List<string>();
    // Warnings to print.
    private List<string> warningsToPrint = new List<string>();

    // If a test takes longer to run than the timeout, it will be skipped and considered failed.
    // The unit is seconds, used by Unity.
    public float TestTimeoutSeconds { get; set; }
    // The time in seconds since the testapp started when all tests were finished. Used to delay
    // notifying the test lab manager to shut down the app, to provide time for results to appear
    // in the testapp. Otherwise, video recording cuts off early.
    private float endTime;

    private AutomatedTestRunner(Func<Task>[] testsToRun, TestLabManager testLabManager,
                                Action<string> logFunc = null, string[] testNames = null,
                                int maxAttempts = 1) {
      LogFunc = logFunc != null ? logFunc : UnityEngine.Debug.Log;
      Finished = false;
      TestTimeoutSeconds = 60.0f;
      this.testLabManager = testLabManager;
      tests = new List<AutomatedTestCase>();

      for (int i = 0; i < testsToRun.Length; ++i) {
        string testName = null;
        if (testNames != null && i < testNames.Length) {
          testName = testNames[i];
        }
        AddTestCase(testsToRun[i], testName, maxAttempts);
      }
    }

    public void AddTestCase(Func<Task> test, string testName = null, int maxAttempts = 1) {
      tests.Add(new AutomatedTestCase(this, test, testName, maxAttempts));
    }

    // Static factory method for the test runner. Creates an instance and sets up
    // the environment for testing.
    public static AutomatedTestRunner CreateTestRunner(
      Func<Task>[] testsToRun, Action<string> logFunc = null, string[] testNames = null,
      int maxAttempts = 1) {
      TestLabManager testLabManager = InitializeTestLabManager();
      return new AutomatedTestRunner(testsToRun, testLabManager, logFunc, testNames, maxAttempts);
    }

    public void FailTest(string reason) {
      CurrentRunningTest?.FailTest(reason);
    }

    /// <summary>
    /// Configures a TestLabManager object to be used with Firebase Test Lab. Required even
    /// if not using Firebase Test Lab.
    /// </summary>
    private static TestLabManager InitializeTestLabManager() {
      TestLabManager testLabManager = TestLabManager.Instantiate();
      Application.logMessageReceived += (string condition, string stackTrace, LogType type) => {
          testLabManager.LogToResults(FormatLog(condition, stackTrace, type));
        };
      return testLabManager;
    }

    private static string FormatLog(string condition, string stackTrace, LogType type) {
      return string.Format("[{0}]({1}): {2}\n{3}\n", DateTime.Now, type, condition, stackTrace);
    }

    // Runs through the tests, checking if the current one is done.
    // If so, logs the relevant information, and proceeds to the next test.
    public void Update() {
      if (tests == null) {
        return;
      }

      if (!startedExecution) {
        LogFunc("Starting executing tests.");
        startedExecution = true;
      }

      if (currentTestIndex >= tests.Count) {
        if (Finished) {
          // No tests left to run.
          // Wait 5 seconds before notifying test lab manager so video can capture end of test.
          if (Time.time > endTime + 5f) {
            testLabManager.NotifyHarnessTestIsComplete();
          }
        } else {
          // All tests are finished!
          LogFunc("All tests finished");
          LogFunc(String.Format("PASS: {0}, FAIL: {1}\n\n" +
                                "{2}{3}", passedTestsCount, failedTestsCount,
                                failedTestsCount > 0 ? "Failing Tests:\n" : "",
                                String.Join("\n", failingTestDescriptions.ToArray())));
          failingTestDescriptions.Clear();
          // Print out any warnings
          if (warningsToPrint.Count > 0) {
            LogFunc(String.Format("WARNINGS: {0}\n{1}", warningsToPrint.Count,
                                  String.Join("\n", warningsToPrint.ToArray())));
          }
          Finished = true;
          endTime = Time.time;
        }
        return;
      }

      // Update the currently running test
      if (CurrentRunningTest != null) {
        CurrentRunningTest.Update();

        // If the test is finished, log the results
        if (CurrentRunningTest.IsFinished) {
          CurrentRunningTest.LogResult();
          CurrentRunningTest.CollectWarnings(warningsToPrint);
          if (CurrentRunningTest.Status == AutomatedTestStatus.Succeeded) {
            ++passedTestsCount;
          } else {
            ++failedTestsCount;
            failingTestDescriptions.Add(CurrentRunningTest.Description);
          }
        }
      }

      // If the current test is done, start up the next one.
      if (CurrentRunningTest == null || CurrentRunningTest.IsFinished) {
        StartNextTest();
      }
    }

    void StartNextTest() {
      ++currentTestIndex;
      if (currentTestIndex < tests.Count && tests[currentTestIndex] != null) {
        // Start running the next test.
        tests[currentTestIndex].StartTest(currentTestIndex, tests.Count);
      }
    }

    // Informs the test runner that the currently running test contains expected assertion failures.
    public void DisablePostTestIgnoredFailureCheck() {
      if (CurrentRunningTest != null) {
        CurrentRunningTest.postTestIgnoredFailureCheckEnabled = false;
      }
    }
  }

  // This is a class for forcing code to run on the main thread.
  // You can give it arbitrary code via the RunOnMainThread method,
  // and it will execute that code during the Update() function.  (Which
  // Unity always runs on the main thread.)
  // The returned Task object also contains a helpful function
  // to block execution until the job is completed.  (WaitUntilComplete)
  public class MainThreadDispatcher : MonoBehaviour {
    // Queue of all the pending jobs that will be executed.
    System.Collections.Queue jobQueue =
        System.Collections.Queue.Synchronized(new System.Collections.Queue());

    // Update is called once per frame.  Every frame we grab any available
    // jobs that are pending and execute them.  Since unity always calls
    // update on the main thread, this guarantees that our jobs will also
    // execute on the main thread as well.
    void Update() {
      while (jobQueue.Count > 0) {
        (jobQueue.Dequeue() as Action)();
      }
    }

    // Specify code to execute on the main thread.  Returns a Task object
    // that can be used to track the progress of the job.
    public Task RunOnMainThread(System.Action jobFunction) {
      TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
      jobQueue.Enqueue(new Action(() => {
        try {
          jobFunction();
        } catch (Exception e) {
          completionSource.SetException(e);
        }
        completionSource.SetResult(true);
      }));
      return completionSource.Task;
    }
  }

  public enum AutomatedTestStatus {
    NotStarted,
    Running,
    Succeeded,
    Failed,
    TimedOut,
  }

  public class AutomatedTestCase {
    // The test runner that owns this test case
    public AutomatedTestRunner Runner { get; private set; }
    public Action<string> LogFunc { get { return Runner?.LogFunc; } }
    // The test to be run.
    Func<Task> testToRun;
    // List of exceptions thrown in the currently running test.
    private List<Exception> testExceptions = new List<Exception>();
    // Whether the currently running test is expected to fail.
    public bool postTestIgnoredFailureCheckEnabled = true;

    private string customName = null;

    private int maxAttempts = 1;
    private int currentAttempt = 1;

    // Used to measure the time currently elapsed by the test, to see if the test timed out.
    private float startTime = 0.0f;
    // If it timed out, how much time had elapsed.
    private float timeOutTime;

    // Name and index of the current running test function.
    public string Description { get; private set; }
    // The Task that represents the currently running test.
    public Task Result { get; private set; }
    public AutomatedTestStatus Status { get; private set; }
    public bool TookMultipleAttempts { get; private set; }

    public bool IsFinished { get {
        return Status == AutomatedTestStatus.Succeeded ||
          Status == AutomatedTestStatus.Failed ||
          Status == AutomatedTestStatus.TimedOut;
      }
    }

    public AutomatedTestCase(AutomatedTestRunner runner, Func<Task> test, string customName = null, int maxAttempts = 1) {
      Runner = runner;
      testToRun = test;
      this.customName = customName;
      // Use reflection to check for the attempts attribute first
      System.Reflection.MethodInfo mInfo = test.Method;
      if (mInfo != null) {
        AutomatedTestAttemptsAttribute attr =
          (AutomatedTestAttemptsAttribute)Attribute.GetCustomAttribute(mInfo, typeof(AutomatedTestAttemptsAttribute));
        if (attr != null) {
          maxAttempts = attr.attemptAmount;
        }
      }
      this.maxAttempts = maxAttempts;
      Status = AutomatedTestStatus.NotStarted;
    }

    public void StartTest(int index, int testCount) {
      // Set the Description, based on the name and parameters
      string testName = testToRun.Method.Name;
      if (customName != null) {
        testName = customName;
      }
      Description = String.Format("{0} ({1}/{2})", testName,
                                  index + 1, testCount);

      // Mark that the test is running for outside observers.
      Status = AutomatedTestStatus.Running;
      StartTestInternal();
    }

    private void StartTestInternal() {
      // Log that the test is starting (also, which attempt this is, if multiple)
      string attemptDesc = "";
      if (currentAttempt > 1) {
        attemptDesc = " (attempt " + currentAttempt + " of " + maxAttempts + ")";
      }
      LogFunc("Test " + Description + " started..." + attemptDesc);
      startTime = Time.time;

      // Call the function to start the test
      if (testToRun != null) {
        try {
          Result = testToRun();
        } catch (Exception e) {
          Result = Task.FromException(e);
        }
      }
    }

    public void FailTest(string reason) {
      // Save the reason as an exception
      var e = new Exception(reason);
      testExceptions.Add(e);
      throw e;
    }

    private bool TryRetryTest() {
      if (currentAttempt < maxAttempts) {
        testExceptions.Clear();
        TookMultipleAttempts = true;
        currentAttempt++;
        StartTestInternal();
        return true;
      }
      return false;
    }

    public void Update() {
      // Check the Task to determine if it is finished
      if (Status == AutomatedTestStatus.Running) {
        if (Result == null || Result.IsCompleted || Result.IsCanceled || Result.IsFaulted) {
          // Determine if the test failed
          bool failed = (Result == null || Result.IsCanceled || Result.IsFaulted);
          // If it gathered exceptions, and they weren't expected, fail
          failed |= (postTestIgnoredFailureCheckEnabled && testExceptions.Count > 0);

          // If it failed, attempt to retry it
          if (failed) {
            if (TryRetryTest()) {
              return;
            }
          }
          // Otherwise, finish the test
          Status = failed ? AutomatedTestStatus.Failed : AutomatedTestStatus.Succeeded;
        } else if (Time.time - startTime > Runner.TestTimeoutSeconds) {
          // The test has timed out
          // Try to restart it
          if (TryRetryTest()) {
            return;
          }
          // Otherwise, finish the test
          timeOutTime = Time.time - startTime;
          Status = AutomatedTestStatus.TimedOut;
        }
      }
    }

    public void LogResult() {
      if (Status == AutomatedTestStatus.Succeeded) {
        LogFunc("Test " + Description + " passed.");
      } else if (Status == AutomatedTestStatus.Failed) {
        if (Result == null) {
          LogFunc(String.Format("Test {0} failed.\n\n" +
                                "No task was returned by the test.",
                                Description));
        } else if (Result.IsFaulted) {
          LogFunc(String.Format("Test {0} failed!\n\n" +
                                "Exception message: {1}",
                                Description,
                                Result.Exception.Flatten().ToString()));
        } else {
          // Assume exceptions were thrown, causing the failure
          LogFunc(String.Format("Test {0} failed with {1} exception(s).\n\n",
                                Description, testExceptions.Count.ToString()));
          for (int i = 0; i < testExceptions.Count; ++i) {
            LogFunc(String.Format("Exception message ({0}): {1}\n\n", (i + 1).ToString(),
                                  testExceptions[i].ToString()));
          }
        }
      } else if (Status == AutomatedTestStatus.TimedOut) {
        LogFunc("Test " + Description + " timed out, elapsed time: " + timeOutTime);
      }
    }

    public void CollectWarnings(List<string> warnings) {
      // Add a warning if multiple attempts were made and it passed.
      if (TookMultipleAttempts && Status == AutomatedTestStatus.Succeeded) {
        warnings.Add(String.Format("Test {0} took {1} of {2} attempts to pass (Possible Flake).",
                                   Description, currentAttempt, maxAttempts));
      }
    }
  }

  [AttributeUsage(AttributeTargets.Method)]
  public class AutomatedTestAttemptsAttribute : Attribute {
    public int attemptAmount;

    public AutomatedTestAttemptsAttribute(int attempts) {
      attemptAmount = attempts;
    }
  }
}
