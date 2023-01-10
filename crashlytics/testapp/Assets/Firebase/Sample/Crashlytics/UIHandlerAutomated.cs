namespace Firebase.Sample.Crashlytics {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Reflection;
  using System.Threading.Tasks;
  
  using Firebase.Crashlytics;
  using UnityEngine;

  // An automated version of the UIHandler that runs tests on Firebase Crashlytics.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    public override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: testsToRun,
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

    #region Tests

    private Func<Task>[] testsToRun {
      get {
        Func<Task>[] tests = {
          TestCrashlyticsInitialized,
        };

        return tests;
      }
    }

    private Task TestCrashlyticsInitialized() {
      return WrapWithTask(() => {
        // Action
        var isInitialized = IsCrashlyticsInitialized();

        // Validation
        if (!isInitialized) {
          throw new Exception("Crashlytics was NOT initialized");
        }

        return true;
      });
    }

    #endregion

    #region  Helper Functions

    private Task<T> WrapWithTask<T>(Func<T> function) {
      TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();
      try {
        T result = function();
        tcs.TrySetResult(result);
      } catch (Exception e) {
        tcs.TrySetException(e);
      }
      return tcs.Task;
    }

    private bool IsCrashlyticsInitialized() {
      // This is tightly bound to the implementation of Crashlytics.
      Debug.Log("Testing Crashlytics Initialization...");
      Type crashlyticsType = typeof(Crashlytics);
      Assembly assembly = crashlyticsType.Assembly;
      Type platformType = assembly.GetType("Firebase.Crashlytics.Crashlytics+PlatformAccessor");

      FieldInfo implFieldInfo = platformType.GetField("_impl", BindingFlags.NonPublic | BindingFlags.Static);
      object impl = implFieldInfo.GetValue(null);

      if (impl == null) {
        throw new NullReferenceException("Cannot find Firebase.Crashlytics.Crashlytics+PlatformAccessor._impl via reflection");
      }

      MethodInfo isInitializedMethodInfo =  impl.GetType().GetMethod("IsSDKInitialized");
      bool result = (bool)isInitializedMethodInfo.Invoke(impl, new object[] {});
      DebugLog("Crashlytics.impl.isSDKInitialized(): " + result);

#if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
      return result;
#else
      // The desktop stub implementation returns false, so expect that.
      return !result;
#endif
    }

    #endregion
  }
}
