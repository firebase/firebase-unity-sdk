namespace Firebase.Sample.Functions {
  using Firebase.Extensions;
  using Firebase.Functions;
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;

  // An automated version of the UIHandler that runs tests on Firebase Analytics.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;
    private Firebase.Auth.FirebaseAuth firebaseAuth;
    private string appCheckDebugTokenForAutomated = "REPLACE_WITH_APP_CHECK_TOKEN";

    // Returns the set of all integration tests.
    public static IEnumerable<TestCase> AllTests() {
      {
        var data = new Dictionary<string, object>();
        data["bool"] = true;
        data["int"] = 2;
        data["long"] = 3L;
        data["string"] = "four";
        var array = new List<object>();
        array.Add(5);
        array.Add(6);
        data["array"] = array;
        data["null"] = null;

        var expected = new Dictionary<string, object>();
        expected["message"] = "stub response";
        expected["code"] = 42L;
        expected["long"] = 420L;
        var expectedArray = new List<object>();
        expectedArray.Add(1L);
        expectedArray.Add(2L);
        expectedArray.Add(3L);
        expected["array"] = expectedArray;

        yield return new TestCase("dataTest", data, expected);
      }

      {
        var data = new Dictionary<string, object>();
        data["firstNumber"] = 5;
        data["secondNumber"] = 7;
        var expected = new Dictionary<string, object>();
        expected["operationResult"] = 12L;
        yield return new TestCase("addNumbers", data, expected, FunctionsErrorCode.None, new HttpsCallableOptions { LimitedUseAppCheckTokens = true });
      }

      var empty = new Dictionary<string, object>();
      yield return new TestCase("scalarTest", 17, 76L);
      yield return new TestCase("scalarTest", 17, 76L, FunctionsErrorCode.None, new HttpsCallableOptions { LimitedUseAppCheckTokens = true });
      yield return new TestCase("tokenTest", empty, empty);
      // Only run this on iOS and Android.
      // yield return new TestCase("instanceIdTest", empty, empty);
      yield return new TestCase("nullTest", null, null);

      // Test various error cases.
      yield return new TestCase("missingResultTest", null, null,
        FunctionsErrorCode.Internal);
      yield return new TestCase("unhandledErrorTest", null, null,
        FunctionsErrorCode.Internal);
      yield return new TestCase("unknownErrorTest", null, null,
        FunctionsErrorCode.Internal);
      yield return new TestCase("explicitErrorTest", null, null,
        FunctionsErrorCode.OutOfRange);

      // Test calling via Url
      string projectId = FirebaseApp.DefaultInstance.Options.ProjectId;
      yield return new TestCaseWithURL("scalarTest via Url",
        new System.Uri("https://us-central1-" + projectId + ".cloudfunctions.net/scalarTest"),
        17, 76L);
    }

    protected override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      var testCases = AllTests().ToArray();
      var tests = from t in testCases select MakeFunc(t);
      var names = from t in testCases select t.Name;

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests.ToArray(),
        logFunc: DebugLog,
        testNames: names.ToArray()
      );

      UIEnabled = false;
      base.Start();
    }

    protected override void InitializeFirebase() {
      try {
        var debugFactoryType = Type.GetType("Firebase.AppCheck.DebugAppCheckProviderFactory, Firebase.AppCheck");
        var appCheckType = Type.GetType("Firebase.AppCheck.FirebaseAppCheck, Firebase.AppCheck");
        
        if (debugFactoryType != null && appCheckType != null) {
          DebugLog("Initializing Firebase App Check with Debug Provider via reflection");
          var instanceProp = debugFactoryType.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
          var debugFactoryInstance = instanceProp.GetValue(null);
          
          var setDebugTokenMethod = debugFactoryType.GetMethod("SetDebugToken", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
          setDebugTokenMethod.Invoke(debugFactoryInstance, new object[] { appCheckDebugTokenForAutomated });
          
          var setProviderFactoryMethod = appCheckType.GetMethod("SetAppCheckProviderFactory", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
          setProviderFactoryMethod.Invoke(null, new object[] { debugFactoryInstance });
          
          // Initialize the AppCheck instance so it's available.
          var getInstanceMethod = appCheckType.GetMethod("GetInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new Type[] { typeof(FirebaseApp) }, null);
          getInstanceMethod.Invoke(null, new object[] { FirebaseApp.DefaultInstance });
          
          DebugLog("Successfully initialized App Check via reflection");
        } else {
          DebugLog("AppCheck assembly not found, skipping initialization");
        }
      } catch (Exception e) {
        DebugLog("Failed to initialize App Check via reflection: " + e.Message);
      }

      // One of the automated tests requires Auth, so we want to sign in before running it.
      firebaseAuth = Firebase.Auth.FirebaseAuth.DefaultInstance;
      firebaseAuth.SignInAnonymouslyAsync().ContinueWithOnMainThread(
        t => base.InitializeFirebase());
    }

    Func<Task> MakeFunc(TestCase t) {
      return () => t.RunAsync(functions, DebugLog);
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (functions != null) {
        testRunner.Update();
      }
    }
  }
}
