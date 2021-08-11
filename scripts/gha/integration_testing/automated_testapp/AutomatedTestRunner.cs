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
    // The list of tests to run through.
    private Func<Task>[] tests = null;
    // The index of the currently running test.
    private int currentTestIndex = -1;
    // Name and index of the current running test function.
    public string CurrentTestDescription { get; private set; }
    // The Task that represents the currently running test.
    public Task CurrentTestResult { get; private set; }
    // Have all the tests been run and finished.
    public bool Finished { get; private set; }
    private bool startedExecution = false;
    // Custom names to use for the tests, instead of the function names.
    private string[] customTestNames = null;

    private TestLabManager testLabManager;

    // Used to output summary of the test run.
    private int passedTestsCount = 0;
    private int failedTestsCount = 0;
    // Descriptions of failing tests.
    private List<string> failingTestDescriptions = new List<string>();

    // Used to measure the time currently elapsed by the test, to see if the test timed out.
    private float startTime = 0.0f;
    // If a test takes longer to run than the timeout, it will be skipped and considered failed.
    // The unit is seconds, used by Unity.
    public float TestTimeoutSeconds { get; set; }
    // The time in seconds since the testapp started when all tests were finished. Used to delay
    // notifying the test lab manager to shut down the app, to provide time for results to appear
    // in the testapp. Otherwise, video recording cuts off early.
    private float endTime;

    private AutomatedTestRunner(Func<Task>[] testsToRun, TestLabManager testLabManager,
                                Action<string> logFunc = null, string[] testNames = null) {
      LogFunc = logFunc != null ? logFunc : UnityEngine.Debug.Log;
      tests = testsToRun;
      customTestNames = testNames;
      Finished = false;
      TestTimeoutSeconds = 60.0f;
      this.testLabManager = testLabManager;
    }

    // Static factory method for the test runner. Creates an instance and sets up
    // the environment for testing.
    public static AutomatedTestRunner CreateTestRunner(
      Func<Task>[] testsToRun, Action<string> logFunc = null, string[] testNames = null) {
      TestLabManager testLabManager = InitializeTestLabManager();
      return new AutomatedTestRunner(testsToRun, testLabManager, logFunc, testNames);
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

    // Is the current test still running.
    public bool IsRunningTest {
      get {
        return !(CurrentTestResult == null ||
          CurrentTestResult.IsCompleted ||
          CurrentTestResult.IsCanceled ||
          CurrentTestResult.IsFaulted);
      }
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

      if (currentTestIndex >= tests.Length) {
        if (Finished) {
          // No tests left to run.
          // Wait 5 seconds before notifying test lab manager so video can capture end of test.
          if (Time.time > endTime + 5f) {
            testLabManager.NotifyHarnessTestIsComplete();
          }
          return;
        } else {
          // All tests are finished!
          LogFunc("All tests finished");
          LogFunc(String.Format("PASS: {0}, FAIL: {1}\n\n" +
                                "{2}{3}", passedTestsCount, failedTestsCount,
                                failedTestsCount > 0 ? "Failing Tests:\n" : "",
                                String.Join("\n", failingTestDescriptions.ToArray())));
          failingTestDescriptions = new List<string>();
          Finished = true;
          endTime = Time.time;
        }
      }

      // If the current test is done, start up the next one.
      if (!IsRunningTest) {
        // Log the results of the test that has finished.
        if (CurrentTestResult != null) {
          if (CurrentTestResult.IsFaulted) {
            LogFunc(String.Format("Test {0} failed!\n\n" +
                                  "Exception message: {1}",
                                  CurrentTestDescription,
                                  CurrentTestResult.Exception.Flatten().ToString()));
            ++failedTestsCount;
            failingTestDescriptions.Add(CurrentTestDescription);
          } else {
            // If the task was not faulted, assume it succeeded.
            LogFunc("Test " + CurrentTestDescription + " passed.");
            ++passedTestsCount;
          }
        } else if (currentTestIndex >= 0 && currentTestIndex < tests.Length &&
                   tests[currentTestIndex] != null) {
          LogFunc(String.Format("Test {0} failed.\n\n" +
                                "No task was returned by the test.",
                                CurrentTestDescription));
          ++failedTestsCount;
          failingTestDescriptions.Add(CurrentTestDescription);
        }

        StartNextTest();
      } else {
        // Watch out for timeout
        float elapsedTime = Time.time - startTime;
        if (elapsedTime > TestTimeoutSeconds) {
          LogFunc("Test " + CurrentTestDescription + " timed out, elapsed time: " + elapsedTime);
          ++failedTestsCount;
          failingTestDescriptions.Add(CurrentTestDescription);
          StartNextTest();
        }
      }
    }

    void StartNextTest() {
      CurrentTestResult = null;
      ++currentTestIndex;
      if (currentTestIndex < tests.Length && tests[currentTestIndex] != null) {
        // Start running the next test.
        var testFunc = tests[currentTestIndex];
        string testName = testFunc.Method.Name;
        if (customTestNames != null && currentTestIndex < customTestNames.Length) {
          testName = customTestNames[currentTestIndex];
        }
        CurrentTestDescription = String.Format("{0} ({1}/{2})", testName,
                                               currentTestIndex + 1, tests.Length);
        try {
          LogFunc("Test " + CurrentTestDescription + " started...");
          CurrentTestResult = testFunc();
        } catch (Exception e) {
          var tcs = new TaskCompletionSource<bool>();
          tcs.SetException(e);
          CurrentTestResult = tcs.Task;
        }
        startTime = Time.time;
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
}
