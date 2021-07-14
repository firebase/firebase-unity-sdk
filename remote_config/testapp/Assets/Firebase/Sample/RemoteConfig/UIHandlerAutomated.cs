namespace Firebase.Sample.RemoteConfig {
  using Firebase.Extensions;
  using System;
  using System.Threading.Tasks;

  // An automated version of the UIHandler that runs tests on Firebase Remote Config.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    protected override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        TestDisplayData,
        TestDisplayAllKeys,
        TestFetchData,
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      base.Start();
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();

      if (isFirebaseInitialized) {
        testRunner.Update();
      }
    }

    Task TestDisplayData() {
      DisplayData();
      return Task.FromResult(true);
    }

    Task TestDisplayAllKeys() {
      DisplayAllKeys();
      return Task.FromResult(true);
    }

    Task TestFetchData() {
      return FetchDataAsync().ContinueWithOnMainThread((_) => {
        DebugLog("TestFetchData data=" + String.Join(",", new string[] {
          "config_test_string: " +
               Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_string").StringValue,
          "config_test_int: " +
               Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_int").LongValue,
          "config_test_float: " +
               Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_float").DoubleValue,
          "config_test_bool: " +
               Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_bool").BooleanValue
      }));
      });
    }
  }
}
