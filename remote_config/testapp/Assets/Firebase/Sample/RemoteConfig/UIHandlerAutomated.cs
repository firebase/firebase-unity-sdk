namespace Firebase.Sample.RemoteConfig {
  using Firebase.Extensions;
  using System;
  using System.Linq;
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
// Skip the Realtime RC test on desktop as it is not yet supported.
#if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) && !UNITY_EDITOR
        TestAddOnConfigUpdateListener,
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
        TestAddAndRemoveConfigUpdateListener,
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

    // Throw when value1 != value2.
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!(object.Equals(value1, value2))) {
        throw new Exception(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                                          testRunner.CurrentTestDescription, value1, value2,
                                          message));
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

    private void ConfigUpdateListenerEventHandler(
        object sender, Firebase.RemoteConfig.ConfigUpdateEventArgs args) {
      if (args.Error != Firebase.RemoteConfig.RemoteConfigError.None) {
        DebugLog(String.Format("Error occurred while listening: {0}", args.Error));
        return;
      }
      DebugLog(String.Format("Auto-fetch has received a new config. Updated keys: {0}",
          string.Join(", ", args.UpdatedKeys)));
      var info = Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.Info;
      Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.ActivateAsync()
        .ContinueWithOnMainThread(task => {
          DebugLog(String.Format("Remote data loaded and ready (last fetch time {0}).",
                                info.FetchTime));
        });
    }

    Task TestAddOnConfigUpdateListener() {
      bool hasDefaultValue =
          Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_string").Source
          == Firebase.RemoteConfig.ValueSource.DefaultValue;
      if (!hasDefaultValue) {
        // Some previous run of the integration test already has cached local data. This can happen if the test is run twice in a row on the same device.
        DebugLog("WARNING: The device already has fetched data from a previous run of the test. To test config update listener, clear app data and re-run the test.");
        return Task.FromResult(true);
      }

      TaskCompletionSource<bool> test_success = new TaskCompletionSource<bool>();
      EventHandler<Firebase.RemoteConfig.ConfigUpdateEventArgs> myHandler =
          (object sender, Firebase.RemoteConfig.ConfigUpdateEventArgs args) => {
            DebugLog(String.Format("Auto-fetch has received a config"));
            // Verify that the config update contains all expected keys.
            String[] expectedKeys = new String[] {
              "config_test_string", "config_test_int", "config_test_int", "config_test_float"
            };
            foreach (String expectedKey in expectedKeys) {
              if (!args.UpdatedKeys.Contains(expectedKey)) {
                test_success.SetException(new Exception(String.Format(
                  "ConfigUpdate does not contain an update for key '{0}'",
                  expectedKey)));
              }
            }
            test_success.SetResult(true);
          };
      DebugLog("Enabling auto-fetch:");
      Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.OnConfigUpdateListener
          += myHandler;
      return test_success.Task;
    }

    Task TestAddAndRemoveConfigUpdateListener() {
      // This test just verifies that listeners can be added and removed.
      EventHandler<Firebase.RemoteConfig.ConfigUpdateEventArgs> myHandler =
          (object sender, Firebase.RemoteConfig.ConfigUpdateEventArgs args) => {};
      DebugLog("Adding a config update listener");
      Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.OnConfigUpdateListener
          += myHandler;
      DebugLog("Removing a config update listener");
      Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.OnConfigUpdateListener
          -= myHandler;
      return Task.FromResult(true);
    }

    Task TestFetchData() {
      // Note: FetchDataAsync calls both Fetch and Activate.
      return FetchDataAsync().ContinueWithOnMainThread((_) => {
        // Verify that last fetch time has increased.
        // Verify that RemoteConfig now has the expected values.
        AssertEq("Unexpected value for config_test_string", "Hello from the new cloud x3",
          Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_string").StringValue);
        AssertEq("Unexpected value for config_test_int", 42,
          Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_int").LongValue);
        AssertEq("Unexpected value for config_test_float", 3.14,
          Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_float").DoubleValue);
        AssertEq("Unexpected value for config_test_bool", true,
          Firebase.RemoteConfig.FirebaseRemoteConfig.DefaultInstance.GetValue("config_test_bool").BooleanValue);
      });
    }
  }
}
