namespace Firebase.Sample.Database {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;
  using System.Linq;

  using UnityEngine;

  using Firebase;
  using Firebase.Database;

  public class UIHandlerAutomated : UIHandler {

    // b/74812208 : StartAt(x,key) and EndAt(x,key) do not behave as documented.
    // If true, expect the actual behavior, which is consistent across all platforms.
    bool expectB74812208 = true;

    const int NumMillisPerSecond = 1000;

    private Firebase.Sample.AutomatedTestRunner testRunner;
    private FirebaseDatabase database;
    // Database URL
    private string databaseUrl = "REPLACE_WITH_YOUR_DATABASE_URL";

#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    // Manually create the default app on desktop so that it's possible to specify the database URL.
    private FirebaseApp defaultApp;
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR

    static Thread mainThread = null;

    protected override void Start() {
      mainThread = Thread.CurrentThread;
      Func<Task>[] tests = {
        TestDataSnapshot,

        TestDbRefParent,
        TestDbRefRoot,
        TestDbRefChild,
        TestDbRefPush,
        TestDbRefSetValue,
        TestDbRefSetJson,
        TestDbRefSetInvalid,
        TestDbRefSetWPriority,
        TestDbRefUpdateChildren,
        TestDbRefRemoveValue,
        TestDbRefKey,
        TestDbRefToString,
        TestDbRefGoOffline,

        TestQueryGetValue,
        TestQueryGetValueEmpty,
        TestQueryValueChangedEmpty,
        TestQueryKeepSynced,
        TestQueryStartAtString,
        TestQueryStartAtDouble,
        TestQueryStartAtBool,
        TestQueryStartAtStringAndKey,
        TestQueryStartAtDoubleAndKey,
        TestQueryStartAtBoolAndKey,
        TestQueryEndAtString,
        TestQueryEndAtDouble,
        TestQueryEndAtBool,
        TestQueryEndAtStringAndKey,
        TestQueryEndAtDoubleAndKey,
        TestQueryEndAtBoolAndKey,
        TestQueryEqualToString,
        TestQueryEqualToDouble,
        TestQueryEqualToBool,
#if !(UNITY_IOS || UNITY_TVOS)
        // TODO(b/129906113) These tests don't work on iOS, and are removed
        // until they are fixed.
        TestQueryEqualToStringAndKey,
        TestQueryEqualToDoubleAndKey,
        TestQueryEqualToBoolAndKey,
#endif  // !(UNITY_IOS || UNITY_TVOS)
        TestQueryLimitToFirst,
        TestQueryLimitToLast,
        TestQueryOrderByChild,
        TestQueryReference,

        TestDbGoOffline,
        TestOnDisconnect,

        TestDbTransactionAbort,
        TestDbTransactionAddValue,
        TestDbTransactionMutableData,

        TestGetInstanceByURL,

        TestCreateDestroy,
        TestCreateDestroyRaceCondition,

        TestLogLevel,

        TestSnapshotWithCoroutinesChild,
        TestSnapshotWithCoroutinesChildren,
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      DebugLog("NOTE: Some API calls report failures using UnityEngine.Debug.LogError which will " +
               "pause execution in the editor when 'Error Pause' in the console window is " +
               "enabled.  `Error Pause` should be disabled to execute this test.");

      UIEnabled = false;
      base.Start();
    }

    // Reduce log spam by just logging the first App creation.
    private bool loggedOnceCreateDefaultApp = false;

    // Create the default FirebaseApp on non-mobile platforms.
    private void CreateDefaultApp() {
#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
      defaultApp = FirebaseApp.Create(new AppOptions { DatabaseUrl = new Uri(databaseUrl) });
      if (!loggedOnceCreateDefaultApp) {
        DebugLog(String.Format("Default app created with database url {0}",
                                defaultApp.Options.DatabaseUrl));
        loggedOnceCreateDefaultApp = true;
      }
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    }

    // Remove all reference to the default FirebaseApp.
    private void DestroyDefaultApp() {
#if !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
      defaultApp = null;
#endif  // !(UNITY_IOS || UNITY_TVOS || UNITY_ANDROID) || UNITY_EDITOR
    }

    protected override void InitializeFirebase() {
      StartTests();
      isFirebaseInitialized = true;
    }

    // This method was used for tests on Forge, but needs work:
    // 1. The automated test on Forge is currently broken due to some turbo.adb issue.
    // 2. This test has become much bigger and the Forge test doesn't cover it.
    IEnumerator GetDatabaseUrlAndStartTests() {
      // When run on Forge, the automated test uses a fake database.
      // This allows the test to be hermetic (= no network access), decouple it
      // from changes to the database server code and simulate server events.
      //
      // We don't know exactly which URL will be used for the fake, so we ask
      // "getdatabaseurl.google.com" for it. That server doesn't exist, but the
      // TestRunner can easily spoof it.
      WWW www = new WWW("https://getdatabaseurl.google.com");
      yield return www;
      DebugLog(String.Format("Received databaseurl '{0}'", www.text));
      // If we received something that looks like an URL, use it.
      if (www.text.StartsWith("http")) {
        databaseUrl = "REPLACE_WITH_YOUR_DATABASE_URL";
        // Currently, the instrumented test on Forge only expects the listeners.
        StartListener();
      } else {
        // When not running on Forge, start the full test battery.
        StartTests();
      }
    }

    void StartTests() {
      CreateDefaultApp();
      database = FirebaseDatabase.DefaultInstance;
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (testRunner != null && isFirebaseInitialized) {
        testRunner.Update();
      }
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition) throw new Exception(String.Format("Assertion failed ({0}): {1}",
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

    // If doing many tests within a task, this helps test all of the options
    // without throwing an exception. At the end, you can assert that the Count
    // of the failedTestMessages is 0.
    private class AccumulateErrors {
      public List<string> failedTestMessages = new List<string>();

      public void ExpectEq<T>(string testName, T value1, T value2) {
        if (!(object.Equals(value1, value2))) {
          failedTestMessages.Add(String.Format("Test {0} failed.\nExpected: {1}\nActual: {2}",
            testName, value2.ToString(), value1.ToString()));
        }
      }
      public void ExpectTrue(string testName, bool value1) { ExpectEq(testName, value1, true); }
      public void ExpectFalse(string testName, bool value1) { ExpectEq(testName, value1, false); }
    };


    // Wait for the Task to complete, then assert that it's Completed.
    private void WaitAndAssertCompleted(string name, Task t) {
      t.Wait();
      Assert(name + " completed successfully", t.IsCompleted);
    }

    private void WaitAndExpectException(string taskName, Task t, params Type[] expectedExceptions) {
      try {
        t.Wait();
        Assert("Waiting on " + taskName + " should have thrown an exception", false);
      } catch (AggregateException e) {
        List<Type> expected = new List<Type>(expectedExceptions);
        foreach (Exception inner in e.InnerExceptions) {
          if (expected.Contains(inner.GetType())) {
            expected.Remove(inner.GetType());
          } else {
            throw e;
          }
        }
        if (expected.Count > 0) {
          string missingExceptions = "";
          foreach (var missing in expected) {
            missingExceptions += " " + missing.ToString();
          }
          Assert(taskName + " missing the following exceptions: " + missingExceptions, false);
        }
      }
    }

    // Returns a completed task.
    private Task CompletedTask(Exception exception = null) {
      TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
      if (exception == null) {
        taskCompletionSource.SetResult(true);
      } else {
        taskCompletionSource.SetException(exception);
      }
      return taskCompletionSource.Task;
    }

    // "Deep" compare two objects.
    // This extends object.Equals by comparing the content of lists and dictionaries.
    private static bool EqualContent(object a, object b) {
      if (a == null || b == null) {
        return a == null && b == null;
      } else if (a.GetType() != b.GetType()) {
        return false;
      } else if (a is IList) {
        IList aa = (IList)a;
        IList bb = (IList)b;
        if (aa.Count != bb.Count) {
          return false;
        }
        var aaa = aa.GetEnumerator();
        var bbb = bb.GetEnumerator();
        while (aaa.MoveNext() && bbb.MoveNext()) {
          if (!EqualContent(aaa.Current, bbb.Current)) {
            return false;
          }
        }
        return true;
      } else if (a is IDictionary) {
        IDictionary aa = (IDictionary)a;
        IDictionary bb = (IDictionary)b;
        if (aa.Count != bb.Count) {
          return false;
        }
        foreach (DictionaryEntry entry in aa) {
          if (bb.Contains(entry.Key)) {
            object x = bb[entry.Key];
            if (!EqualContent(entry.Value, x)) {
              return false;
            }
          } else {
            return false;
          }
        }
        return true;
      }
      return object.Equals(a, b);
    }

    // This class is used for asynchronous checks.
    // As many of the following methods change the database, we use listeners to validate
    // that the database has indeed been changed as expected. And those listeners run on
    // a separate thread.
    //
    // The expected use is:
    //
    // AsyncChecks checks = new AsyncChecks(parent);
    // checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, valueAdded);
    //   [add more checks as needed]
    //   [do things that trigger the events]
    // checks.WaitForEvents();
    // checks.AssertAllEventsDone();
    // Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
    private class AsyncChecks {
      public enum ChildEventType {
        Added, Changed, Moved, Removed
      }

      private struct ExpectedEvent {
        public ChildEventType EventType;
        public object Key;
        public object Value;
        public object Priority;

        public override string ToString() {
          return Priority != null ?
            string.Format("Child {0} with key : {1}, value : {2}, priority : {3}",
                EventType, Key, Value, Priority) :
            string.Format("Child {0} with key : {1}, value : {2}", EventType, Key, Value);
        }

        // Check whether the two objects "match".
        //
        // They match if they are:
        // 1) Identical
        // 2) One of them is a type and the other is of that type.
        //
        // This allows to specify "typeof(Int64)" to check that a certain value
        // is an integer, without having to know what integer exactly. Which is
        // useful to e.g. test that the special "ServerValue.TimeStamp" is
        // replaced by an actual timestamp (an integer).
        private static bool Matches(object lhs, object rhs) {
          if (lhs == rhs)
            return true;
          if (lhs is Type) {
            return rhs.GetType() == lhs;
          } else if (rhs is Type) {
            return lhs.GetType() == rhs;
          }
          return EqualContent(lhs, rhs);
        }

        public static bool Equals(ExpectedEvent lhs, ExpectedEvent rhs) {
          return lhs.EventType == rhs.EventType && object.Equals(lhs.Key, rhs.Key) &&
              Matches(lhs.Value, rhs.Value) && Matches(lhs.Priority, rhs.Priority);
        }
      }

      private DatabaseReference reference;
      private Queue<ExpectedEvent> expectedEvents = new Queue<ExpectedEvent>();
      private object eventLock = new object();
      // As listeners are called asynchronously we might have to wait for them
      // and we use an EventWaitHandle to do so.
      private EventWaitHandle allEventsReceived = new ManualResetEvent(false);
      // We keep references of the listeners, so that we can remove them afterwards.
      private EventHandler<ChildChangedEventArgs> childAddedListener;
      private EventHandler<ChildChangedEventArgs> childAddedListenerThrowException;
      private EventHandler<ChildChangedEventArgs> childChangedListener;
      private EventHandler<ChildChangedEventArgs> childChangedListenerThrowException;
      private EventHandler<ChildChangedEventArgs> childMovedListener;
      private EventHandler<ChildChangedEventArgs> childMovedListenerThrowException;
      private EventHandler<ChildChangedEventArgs> childRemovedListener;
      private EventHandler<ChildChangedEventArgs> childRemovedListenerThrowException;

      public AsyncChecks(DatabaseReference reference) {
        this.reference = reference;
        FailMessage = "";
        AllGood = true;

        childAddedListener = (object sender, ChildChangedEventArgs e) => {
          this.ChildEvent(ChildEventType.Added, e.Snapshot);
        };
        childAddedListenerThrowException = (object sender, ChildChangedEventArgs e) => {
          throw new Exception("Child added");
        };
        childChangedListener = (object sender, ChildChangedEventArgs e) => {
          this.ChildEvent(ChildEventType.Changed, e.Snapshot);
        };
        childChangedListenerThrowException = (object sender, ChildChangedEventArgs e) => {
          throw new Exception("Child changed");
        };
        childMovedListener = (object sender, ChildChangedEventArgs e) => {
          this.ChildEvent(ChildEventType.Moved, e.Snapshot);
        };
        childMovedListenerThrowException = (object sender, ChildChangedEventArgs e) => {
          throw new Exception("Child moved");
        };
        childRemovedListener = (object sender, ChildChangedEventArgs e) => {
          this.ChildEvent(ChildEventType.Removed, e.Snapshot);
        };
        childRemovedListenerThrowException = (object sender, ChildChangedEventArgs e) => {
          throw new Exception("Child removed");
        };
        reference.ChildAdded += childAddedListener;
        reference.ChildChanged += childChangedListener;
        reference.ChildMoved += childMovedListener;
        reference.ChildRemoved += childRemovedListener;
#if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
        // The desktop implementation will currently fail if exceptions are thrown in child
        reference.ChildAdded += childAddedListenerThrowException;
        reference.ChildChanged += childChangedListenerThrowException;
        reference.ChildMoved += childMovedListenerThrowException;
        reference.ChildRemoved += childRemovedListenerThrowException;
#endif  // #if (UNITY_IOS || UNITY_TVOS || UNITY_ANDROID)
      }

      public bool AllGood { get; private set; }
      public string FailMessage { get; private set; }

      // Expect a specific event to happen.
      // Events will be expected sequentially, i.e. it's an error if they arrive out of order.
      // TODO: Add support for out-of-order events.
      // TODO: (Maybe) Add support for wild-cards (current key and value must be matched exactly).
      public void ExpectEvent(ChildEventType type, object key, object value) {
        ExpectEvent(type, key, value, null);
      }

      // Expect an event to happen.
      // This allows to also specify the priority.
      public void ExpectEvent(ChildEventType type, object key, object value, object priority) {
        lock (eventLock) {
          expectedEvents.Enqueue(
              new ExpectedEvent { EventType = type, Key = key, Value = value, Priority = priority });
        }
      }

      // Wait until all events are completed.
      public void WaitForEvents() {
        lock (eventLock) {
          // This "if" prevents a deadlock in the case where we never expected any events.
          if (expectedEvents.Count == 0)
            return;
        }
        // Wait for outstanding events.
        // If we hit the timeout, AssertAllEventsDone will fail and give a better error
        // messages then relying on the overall test timeout in the AutomatedTestRunner.
        allEventsReceived.WaitOne(20 * NumMillisPerSecond);
      }

      // Assert that all expected events happened.
      // This MUST be called, as it also cleans up (i.e. unregisters) the listeners.
      public void AssertAllEventsDone() {
        lock (eventLock) {
          if (expectedEvents.Count > 0) {
            AllGood = false;
            FailMessage += String.Format("Missing {0} child events\n", expectedEvents.Count);
          }
        }
        // No more events are expected, clean up the listeners.
        reference.ChildAdded -= childAddedListener;
        reference.ChildAdded -= childAddedListenerThrowException;
        reference.ChildChanged -= childChangedListener;
        reference.ChildChanged -= childChangedListenerThrowException;
        reference.ChildMoved -= childMovedListener;
        reference.ChildMoved -= childMovedListenerThrowException;
        reference.ChildRemoved -= childRemovedListener;
        reference.ChildRemoved -= childRemovedListenerThrowException;
      }

      // Common handler for all child events.
      // Asserts that the actual child event is equal to the next expected event.
      private void ChildEvent(ChildEventType type, DataSnapshot snapshot) {
        lock (eventLock) {
          ExpectedEvent actual = new ExpectedEvent {
            EventType = type, Key = snapshot.Key, Value = snapshot.Value,
            Priority = snapshot.Priority
          };
          if (expectedEvents.Count == 0) {
            AllGood = false;
            FailMessage += String.Format("Did not expect any event, got: [{0}]\n", actual);
          } else {
            ExpectedEvent expected = expectedEvents.Dequeue();
            if (!ExpectedEvent.Equals(expected, actual)) {
              AllGood = false;
              FailMessage += String.Format("Unexpected event. Got [{0}] instead of [{1}].\n",
                  actual, expected);
            }
            if (expectedEvents.Count == 0) {
              // This was the last expected event, signal the WaitHandle.
              allEventsReceived.Set();
            }
          }
        }
      }
    }

    Task TestDataSnapshot() {
      // Wrapper needed because we wait for an async call.
      return Task.Run(() => {
        AccumulateErrors testSuite = new AccumulateErrors();
        Semaphore sem = new Semaphore(0, 1);
        // This test will set a JSON value twice and use listeners to verify that
        // the values did indeed get set.
        string childKey = "DataStoreTestChildKey";
        string valueAddedJson = "{\"objects\":[\"Rock\", \"Paper\", \"Scissors\"]," +
          "\"b\":\"Hi\"," +
          "\"c\":{\"0\": \"First\", \"1\": \"Second\", \".priority\": \"3.14\"}," +
          "\"d\":{\"one\": \"First\", \"two\": \"Second\"} }";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();

        EventHandler<ChildChangedEventArgs> childAddedListener =
            (object sender, ChildChangedEventArgs e) => {
              try {
                testSuite.ExpectEq("Snapshot.ChildrenCount", e.Snapshot.ChildrenCount, 4);
                List<string> childrenKeys = new List<string>();
                foreach (var child in e.Snapshot.Children) {
                  childrenKeys.Add(child.Key);
                }
              // c has a priority and therefore comes last:
              // Children with no priority (the default) come first.
              // Whenever two children have the same priority (including no priority), they are sorted
              // by key. Numeric keys come first (sorted numerically), followed by the remaining keys
              // (sorted lexicographically).
              string[] expectedKeys = new string[] { "b", "d", "objects", "c" };
                testSuite.ExpectTrue("Snapshot.Children & Child.Key",
                  e.Snapshot.Children.Select(x => x.Key).SequenceEqual(expectedKeys));

                testSuite.ExpectEq("fixme", e.Snapshot.Child("b").Value, "Hi");
                testSuite.ExpectTrue("ChildList is List Type",
                  e.Snapshot.Child("objects").Value is List<object>);
                testSuite.ExpectEq("GetRawJsonValue",
                  e.Snapshot.Child("objects").GetRawJsonValue(), @"[""Rock"",""Paper"",""Scissors""]");
                testSuite.ExpectTrue("ChildList w/ priority is List Type",
                  e.Snapshot.Child("c").Value is List<object>);
                testSuite.ExpectTrue("ChildDict is Dictionary Type",
                  e.Snapshot.Child("d").Value is Dictionary<string, object>);

                testSuite.ExpectFalse("Snapshot.Child(\"WrongKey\").Exists is false",
                  e.Snapshot.Child("WrongKey").Exists);
                testSuite.ExpectTrue("Snapshot.Child(\"b\").Exists", e.Snapshot.Child("b").Exists);

                testSuite.ExpectEq("PathKey", e.Snapshot.Child("objects/1").Value, "Paper");

                testSuite.ExpectFalse("Child with string value has no children",
                  e.Snapshot.Child("b").HasChildren);
                testSuite.ExpectTrue("Child with List value has children",
                  e.Snapshot.Child("objects").HasChildren);

                testSuite.ExpectEq("Child(null).Value should have returned null",
                    e.Snapshot.Child(null).Value, null);
                testSuite.ExpectFalse("HasChild(null) should have returned false.",
                    e.Snapshot.HasChild(null));
              } catch (Exception ex) {
                testSuite.failedTestMessages.Add(ex.Message);
              // If we don't catch this now, it may break the test suite.
            } finally {
              // Releases access to a resource (aka. Post()).
              sem.Release();
              }
            };

        // Use a custom listener because we really only care about testing the DataStore we get back.
        parent.ChildAdded += childAddedListener;

        // Establish a child with childKey, and assign it a value.
        var r = parent.Child(childKey);
        WaitAndAssertCompleted("DataStoreTest SetRawJsonValue",
            r.SetRawJsonValueAsync(valueAddedJson));

        parent.ChildAdded -= childAddedListener;

        // Remove the value (to clean up).
        WaitAndAssertCompleted("DataStoreTest RemoveValue", r.RemoveValueAsync());

        // There's no guarantee that the Add listener will be done, even if the set task is complete.
        // So use this semaphore to wait, and be sure the tests are done.
        sem.WaitOne();

        Assert("All checks passed.\n" +
          String.Join("\n\n", testSuite.failedTestMessages.ToArray()),
          testSuite.failedTestMessages.Count == 0);
      });
    }


    // Make sure it's possible to create and tear down database instances.
    Task TestCreateDestroy() {
      // This requires the task runner, because WaitForPendingFinalizers will block the main thread.
      return Task.Run(() => {
        // TODO(phohmeyer): While this test passes, it seems that the app & database objects
        // are not ACTUALLY destroyed, so it's a no-op. Something keeps them alive.
        try {
          // Run through a few iterations of destroying and creating app & database objects.
          for (int i = 0; i < 100; ++i) {
            // Dereference the default database object.
            database = null;
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            // Re-create the database object.
            database = FirebaseDatabase.DefaultInstance;
          }
        } finally {
          // Try to recover.
          database = FirebaseDatabase.DefaultInstance;
        }
      });
    }

    // Make sure it's possible to create and tear down database instances without race condition.
    Task TestCreateDestroyRaceCondition() {
      // This requires the task runner, because WaitForPendingFinalizers will block the main thread.
      return Task.Run(() => {
        try {
          // Dereference the default database object.
          database = null;

          // Run through a few iterations of destroying and creating app & database objects.
          for (int i = 0; i < 100; ++i) {
            {
              // By creating DatabaseReference, this may creates or references existing app and/or
              // database object.
              DatabaseReference dbRef =
                  FirebaseDatabase.DefaultInstance.RootReference.Child("Test");
            }

            // By dereferencing DatabaseReference, app & database are dereferenced as well.
            // By calling GC.Collect(), app & database will be marked as unreachable and be
            // scheduled to finalize in GC.
            // Do not call WaitForPendingFinalizers() to create race condition
            System.GC.Collect();
          }

          // Wait for GC for the next test
          System.GC.WaitForPendingFinalizers();

          // Run through a few iterations of destroying and creating app & database objects.  This
          // gradually increases wait time after garbage collection starts.
          for (int i = 0; i < 100; ++i) {
            {
              // By creating DatabaseReference, this may creates or references existing app and/or
              // database object.
              DatabaseReference dbRef =
                  FirebaseDatabase.DefaultInstance.RootReference.Child("Test");
            }

            // By dereferencing DatabaseReference, app & database are dereferenced as well.
            // By calling GC.Collect(), app & database will be marked as unreachable and be
            // scheduled to finalize in GC.
            // Do not call WaitForPendingFinalizers() to create race condition
            System.GC.Collect();

            // Since there is no way to predict when the finalizer of FirebaseApp and
            // FirebaseDatabase is called, gradually increase wait time after kicking off garbage
            // collection to attempt to overlap the next FirebaseApp/FirebaseDatabase creation
            // and finalization from GC thread.
            Thread.Sleep(i);
          }

          // Wait for GC for the next test
          System.GC.WaitForPendingFinalizers();
        } finally {
          // Try to recover.
          System.GC.WaitForPendingFinalizers();
          database = FirebaseDatabase.DefaultInstance;
        }
      });
    }

    // Make sure it's possible to set / get LogLevel.
    Task TestLogLevel() {
      // On Android it's not possible to configure the database log level after any operations have
      // been performed on the instance. This uses an empty database reference so that it's possible
      // to configure the log level.
      var emptyDb = FirebaseDatabase.GetInstance("https://unity-test-app-empty.firebaseio.com/");
      AssertEq("Log level is the default value",
               emptyDb.LogLevel, Firebase.LogLevel.Info);
      foreach (var level in
               new [] {
                 Firebase.LogLevel.Verbose,
                 Firebase.LogLevel.Debug,
                 Firebase.LogLevel.Info,
                 Firebase.LogLevel.Warning,
                 Firebase.LogLevel.Error,
                 Firebase.LogLevel.Assert
               }) {
        emptyDb.LogLevel = level;
        AssertEq(String.Format("Database log level is {0} after being set",
                               level),
                 emptyDb.LogLevel, level);
      }
      emptyDb.LogLevel = Firebase.LogLevel.Info;
      return CompletedTask();
    }

    // ==========================================================================
    // Tests for the DatabaseReference class
    // ==========================================================================

    Task TestDbRefParent() {
      AssertEq("Leaders' parent is the Root",
          database.GetReference("Leaders").Parent, database.RootReference);
      AssertEq("Leaders/0's parent is Leaders",
          database.GetReference("Leaders/0").Parent, database.GetReference("Leaders"));
      AssertEq("Root has no parent", database.RootReference.Parent, null);
      return CompletedTask();
    }

    Task TestDbRefRoot() {
      AssertEq("Leaders' root is the Root",
          database.GetReference("Leaders").Root, database.RootReference);
      AssertEq("Leaders/0's root is still the Root",
          database.GetReference("Leaders/0").Root, database.RootReference);
      AssertEq("Root's root referes itself", database.RootReference.Root, database.RootReference);
      return CompletedTask();
    }

    Task TestDbRefChild() {
      AssertEq("GetReference(Leaders) == root.Child(Leaders)",
          database.GetReference("Leaders"), database.RootReference.Child("Leaders"));
      Assert("Non-existing child can be created",
          database.RootReference.Child("does-never-exists") != null);
      AssertEq("Non-existing child's key is correct",
          database.RootReference.Child("does-never-exists").Key, "does-never-exists");
      try {
        database.GetReference(null);
        Assert("GetReference(null) should have thrown an exception.", false);
      } catch (ArgumentException) {
        // This is the expected case
      }
      try {
        database.GetReference("Leaders").Child(null);
        Assert("Child(null) should have thrown an exception.", false);
      } catch (ArgumentException) {
        // This is the expected case
      }
      return CompletedTask();
    }

    Task TestDbRefPush() {
      var firstPush = database.RootReference.Push();
      var secondPush = database.RootReference.Push();
      Assert("Push returns something", firstPush != null);
      Assert("Push returns something different each time", firstPush != secondPush);
      return CompletedTask();
    }

    Task TestDbRefSetValue() {
      return Task.Run(() => {
        // This test will set a value twice and use listeners to verify that
        // the values did indeed get set.
        string childKey = "ChildKey";
        object valueAdded = "First Value";
        Dictionary<string, object> valueChanged = new Dictionary<string, object>();
        valueChanged["a"] = 123L;
        valueChanged["b"] = "Hi";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // We expect three listener events: two for the two calls to SetValue and one for the call
        // to RemoveValue when we clean up.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, valueAdded);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, valueChanged);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, valueChanged);

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference;
        WaitAndAssertCompleted("First SetValue", r.SetValueAsync(valueAdded));
        // 2. Set the reference's value to something else;
        WaitAndAssertCompleted("Second SetValue", r.SetValueAsync(valueChanged));
        // Remove the value (to clean up)
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    Task TestDbRefSetJson() {
      return Task.Run(() => {
        // This test will set a JSON value twice and use listeners to verify that
        // the values did indeed get set.
        string childKey = "ChildKey";
        string valueAddedJson = "\"First Value\"";
        object valueAdded = "First Value";
        string valueChangedJson = "{ \"a\":123, \"b\":\"Hi\" }";
        Dictionary<string, object> valueChanged = new Dictionary<string, object>();
        valueChanged["a"] = 123L;
        valueChanged["b"] = "Hi";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // We expect three listener events: two for the two calls to SetValue and one for the call
        // to RemoveValue when we clean up.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, valueAdded);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, valueChanged);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, valueChanged);

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference;
        WaitAndAssertCompleted("First SetRawJsonValue", r.SetRawJsonValueAsync(valueAddedJson));
        // 2. Set the reference's value to something else;
        WaitAndAssertCompleted("Second SetRawJsonValue", r.SetRawJsonValueAsync(valueChangedJson));
        // Remove the value (to clean up)
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    Task TestDbRefSetInvalid() {
      return Task.Run(() => {
        // This test will set invalid values (JSON & object).
        // We expect an invalid SetValue to raise an exception, while an invalid SetRawJsonValue
        // will remove the data at this location. (That is the current behavior.)
        string childKey = "ChildKey";
        string childValue = "Value";
        string badJson = "First Value";
        object badObject = new Uri("http://www.google.com");

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // We expect two events: one to create the child and then the remove when setting
        // an invalid JSON value.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, childValue);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, childValue);

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference;
        WaitAndAssertCompleted("SetValue", r.SetValueAsync(childValue));
        // 2. Try to set an invalid value (invalid type inside it)
        try {
          r.SetValueAsync(badObject);
          Assert("SetValueAsync should have thrown an exception.", false);
        } catch (DatabaseException) {
          // This is the expected case
        }
        // 3. Try to set an invalid JSON value
        // This will remove the data here, so we expect a ChildRemoved event.
        WaitAndAssertCompleted("SetRawJsonValue", r.SetRawJsonValueAsync(badJson));
        // Wait for the events
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        // We should not have to remove the value, but in case something went wrong,
        // we still want to clean up as to not pollute the database.
        r.RemoveValueAsync().Wait();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    Task TestDbRefSetWPriority() {
      return Task.Run(() => {
        // This test will set the priority - either together with a value in SetValueAsync
        // or on its own with SetPriorityAsync. It also tests invalid priorities.
        string childKey = "ChildKey";
        object valueAdded = "First Value";
        object priority = 3.14;
        Dictionary<string, object> valueChanged = new Dictionary<string, object>();
        valueChanged["a"] = 123L;
        valueChanged["b"] = "Hi";
        object priorityChanged = "prio";
        object invalidPriority = valueChanged;

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // Changing the priority generates two events: one Move, one Changed.
        // The move seems to always come first, although I don't know if it's guaranteed.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, valueAdded, null);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Moved, childKey, valueAdded, priority);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, valueAdded, priority);
        checks.ExpectEvent(
            AsyncChecks.ChildEventType.Moved, childKey, valueChanged, priorityChanged);
        checks.ExpectEvent(
            AsyncChecks.ChildEventType.Changed, childKey, valueChanged, priorityChanged);
        checks.ExpectEvent(
            AsyncChecks.ChildEventType.Removed, childKey, valueChanged, priorityChanged);

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference
        WaitAndAssertCompleted("SetValue", r.SetValueAsync(valueAdded));
        // 2. Set the reference's priority
        WaitAndAssertCompleted("SetPriority", r.SetPriorityAsync(priority));
        // 3. Set value and priority
        WaitAndAssertCompleted("SetValue with priority",
            r.SetValueAsync(valueChanged, priorityChanged));
        // 4. Set an invalid priority
        try {
          r.SetPriorityAsync(invalidPriority);
          Assert("SetValueAsync should have thrown an exception.", false);
        } catch (DatabaseException) {
          // This is the expected case
        }
        // 5. Set value and invalid priority
        try {
          r.SetValueAsync(valueAdded, invalidPriority);
          Assert("SetValueAsync should have thrown an exception.", false);
        } catch (DatabaseException) {
          // This is the expected case
        }
        // Remove the value (to clean up)
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    Task TestDbRefUpdateChildren() {
      return Task.Run(() => {
        // This test will update multiple children at once with UpdateChildren.
        string childKey = "ChildKey";
        Dictionary<string, object> initialValue = new Dictionary<string, object>();
        initialValue["stay"] = "This should be unchanged";
        initialValue["change"] = "This should change";
        initialValue["remove"] = "This should be removed";
        Dictionary<string, object> change = new Dictionary<string, object>();
        change["new"] = "This is new";
        change["change"] = "This is changed";
        change["remove"] = null;  // setting it to null removes the child
        // "stay" is not mentioned and should therefore stay the same
        Dictionary<string, object> expectedValue = new Dictionary<string, object>();
        expectedValue["stay"] = "This should be unchanged";
        expectedValue["new"] = "This is new";
        expectedValue["change"] = "This is changed";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        try {
          AsyncChecks checks = new AsyncChecks(parent);
          checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, initialValue);
          checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, expectedValue);
          checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, expectedValue);

          var r = parent.Child(childKey);
          // 1. Set a value in a newly created reference
          WaitAndAssertCompleted("SetValue", r.SetValueAsync(initialValue));
          // 2. Update the children
          WaitAndAssertCompleted("UpdateChildren", r.UpdateChildrenAsync(change));
          // Remove the value (to clean up)
          WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
          // Wait for the ChildRemoved listener to be called
          checks.WaitForEvents();
          checks.AssertAllEventsDone();
          Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
        } finally {
          // Finally, cleanup, as to not pollute the test database
          parent.RemoveValueAsync();
        }
      });
    }

    Task TestDbRefRemoveValue() {
      return Task.Run(() => {
        string childKey = "ChildKey";
        object valueAdded = "First Value";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, valueAdded);
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, valueAdded);

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference
        WaitAndAssertCompleted("SetValue", r.SetValueAsync(valueAdded));
        // 2. Remove the value
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // 3. Remove the value a second time - this should be a no-op
        WaitAndAssertCompleted("Second RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    Task TestDbRefKey() {
      AssertEq("Leaders' key", database.GetReference("Leaders").Key, "Leaders");
      AssertEq("Leaders/0's key", database.GetReference("Leaders/0").Key, "0");
      AssertEq("Root has no key", database.RootReference.Key, null);
      return CompletedTask();
    }

    Task TestDbRefToString() {
      // Depending on the platform, FirebaseDatabase might add a port to the base URL
      string[] acceptedBaseUrls = {
      "https://unity-test-app-b1baa.firebaseio.com:443",
      "https://unity-test-app-b1baa.firebaseio.com",
      "https://unity-test-app-b1baa.firebaseio.com/"};
      string baseUrl = database.RootReference.ToString();
      Assert(baseUrl + " is acceptable base URL", acceptedBaseUrls.Contains(baseUrl));

      AssertEq("Leaders.ToString",
          database.GetReference("Leaders").ToString(), baseUrl + "/Leaders");
      AssertEq("Leaders/0.ToString",
          database.GetReference("Leaders/0").ToString(), baseUrl + "/Leaders/0");
      AssertEq("Root.ToString", database.RootReference.ToString(), baseUrl);
      return CompletedTask();
    }

    Task TestDbRefGoOffline() {
      // Be _very_ careful with this test. There is local caching, so this test
      // only works as expected if the local sync tree is empty.
      // We also MUST go online again, as this is global state.
      // Note: this test does fail with the old Firebase Database SDK - turns out
      // that DatabaseReference.GoOffline() is broken in it.
      return Task.Run(() => {
        DatabaseReference.GoOffline();
        bool expectChange = false;
        bool receivedExpectedChange = false;
        bool receivedUnexpectedChange = false;
        var changeEvent = new ManualResetEvent(false);
        var reference = database.GetReference("OfflineTest/Child");
        EventHandler<ValueChangedEventArgs> valueChangedListener =
            (object sender, ValueChangedEventArgs e) => {
              if (expectChange) {
                receivedExpectedChange = true;
              } else {
                receivedUnexpectedChange = true;
              }
              changeEvent.Set();
            };
        reference.ValueChanged += valueChangedListener;
        // The expected outcome is that we do NOT receive an event, because we are offline.
        // As we expect NOTHING to happen, to good outcome is to wait for the entire
        // timeout of 5 seconds. Unfortunately, I don't see any way around waiting.
        changeEvent.WaitOne(5 * NumMillisPerSecond);
        // We do expect to receive an event as soon as we go online again.
        expectChange = true;
        DatabaseReference.GoOnline();
        changeEvent.WaitOne(20 * NumMillisPerSecond);
        reference.ValueChanged -= valueChangedListener;
        Assert("Received no changes while offline", !receivedUnexpectedChange);
        Assert("Received change after getting online", receivedExpectedChange);
      });
    }

    // ==========================================================================
    // Tests for the Query class
    // ==========================================================================

    // Helper method to simplefy the other query tests.
    // It sets up a sub-tree with a specific JSON value, so that queries can be run on it.
    Task CommonQueryTest(string jsonValue, Action<DatabaseReference> test) {
      return Task.Run(() => {
        // Setup the test sub-tree.
        string childKey = "ChildKey";
        var parent = database.RootReference.Child("TestTree").Push();
        var r = parent.Child(childKey);
        try {
          WaitAndAssertCompleted("SetValue", r.SetRawJsonValueAsync(jsonValue));
          // Run the actual test.
          test(r);
        } finally {
          // Delete the test sub-tree.
          r.RemoveValueAsync();
        }
      });
    }

    Task TestQueryGetValue() {
      string json = "\"GetValueAsync\"";
      return CommonQueryTest(json, r => {
        // 2. Get the same value and assert that it's the same.
        var task = r.GetValueAsync();
        task.Wait();
        Assert("GetValueAsync completed successfully", task.IsCompleted);
        AssertEq("GetValueAsync returned the expected value",
            json, task.Result.GetRawJsonValue());
      });
    }

    // Verify if the empty DataSnapshot is valid.
    void VerifyEmptyDataSnapshot(
        DataSnapshot snapshot, DatabaseReference originalRef, int recursionLevel) {
      Assert("DataSnapshot should never be null", snapshot != null);

      Assert("DataSnapshot.Children should never be null", snapshot.Children != null);
      AssertEq("Empty DataSnapshot.ChildrenCount should be 0", snapshot.ChildrenCount, 0);
      AssertEq("Empty DataSnapshot.Exists should be false", snapshot.Exists, false);
      AssertEq("Empty DataSnapshot.HasChildren should be false", snapshot.HasChildren, false);
      AssertEq("Empty DataSnapshot.Key should be the same to its original DatabaseReference.Key",
        snapshot.Key, originalRef.Key);
      AssertEq("Empty DataSnapshot.Priority should be null", snapshot.Priority, null);
      AssertEq("Empty DataSnapshot.Value should be null", snapshot.Value, null);
      AssertEq("Empty DataSnapshot.GetValue(false) should be null", snapshot.GetValue(false), null);
      AssertEq("Empty DataSnapshot.GetValue(true) should be null", snapshot.GetValue(true), null);

      AssertEq("GetValueAsync returned expected raw Json value", snapshot.GetRawJsonValue(), null);

      DatabaseReference reference = snapshot.Reference;
      AssertEq("Empty DataSnapshot.Reference should be the same to its original DatabaseReference",
        originalRef, reference);

      string childPath = "NonExisting";
      AssertEq(string.Format("Empty DataSnapshot.HasChild(\"{0}\") should be false", childPath),
        snapshot.HasChild(childPath), false);

      if (recursionLevel > 1) {
        DataSnapshot childSnapshot = snapshot.Child(childPath);
        VerifyEmptyDataSnapshot(childSnapshot, reference.Child(childPath), recursionLevel - 1);
      }
    }

    Task TestQueryGetValueEmpty() {
      return Task.Run(() => {
        // Find a path that does not have any value yet.
        var r = database.RootReference.Child("TestTree").Push();

        var task = r.GetValueAsync();
        task.Wait();

        Assert("GetValueAsync completed successfully", task.IsCompleted);
        // Verify the returned DataSnapshot and its child
        VerifyEmptyDataSnapshot(task.Result, r, 2);
      });
    }

    Task TestQueryValueChangedEmpty() {
      return Task.Run(() => {
        // Find a path that does not have any value yet.
        var r = database.RootReference.Child("TestTree").Push();

        TaskCompletionSource<DataSnapshot> tcs = new TaskCompletionSource<DataSnapshot>();
        EventHandler<ValueChangedEventArgs> valueChangedListener =
            (object sender, ValueChangedEventArgs args) => {
          try {
            if (args.DatabaseError != null) {
              throw new System.Exception(args.DatabaseError.Message);
            }
            tcs.SetResult(args.Snapshot);
          } catch (System.Exception e) {
            tcs.SetException(e);
          }
        };

        r.ValueChanged += valueChangedListener;

        tcs.Task.Wait();

        r.ValueChanged -= valueChangedListener;

        Assert("ValueChanged was triggered successfully", tcs.Task.IsCompleted);
        // Verify the returned DataSnapshot and its child
        VerifyEmptyDataSnapshot(tcs.Task.Result, r, 2);
      });
    }

    Task TestQueryKeepSynced() {
      // TODO: How to test Query.KeepSynced(true)?
      // Ideas: Call "KeepSynced(true)"
      // Trigger a Firebase Functions event that modifies the server
      // GoOffline
      // GetValue to see that we got the value before going offline
      // Undo keep synced
      // Trigger a Firebase Functions event that modifies the server
      // GoOffline
      // GetValue to see that we didn't get the new value
      return CompletedTask();
    }

    Task TestQueryStartAtString() {
      // By default, StartAt uses priorities, so we add some to the tree:
      // Note: In general, I'll use "Replace(',")" to make json strings easier to read.
      string json =
          ("{ 'A': {'.value':'2', '.priority':'G'}," +
           "  'B': {'.value':'1', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}}").Replace('\'', '"');
      string expectedByPriority = "{'A':'2','B':'1'}".Replace('\'', '"');
      string expectedByKey = "{'B':'1','C':'3'}".Replace('\'', '"');
      string expectedByValue = "{'A':'2','C':'3'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.StartAt("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().StartAt("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        task = r.OrderByKey().StartAt("B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByKey", expectedByKey, task.Result.GetRawJsonValue());
        // 4. Order by value
        task = r.OrderByValue().StartAt("2").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryStartAtDouble() {
      // By default, StartAt uses priorities, so we add some to the tree:
      string json =
          ("{ '7': {'.value':2, '.priority':6}," +
           "  '8': {'.value':1, '.priority':5}," +
           "  '9': {'.value':3, '.priority':4}}").Replace('\'', '"');
      string expectedByPriority = "{'7':2,'8':1}".Replace('\'', '"');
      string expectedByValue = "{'7':2,'9':3}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.StartAt(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().StartAt(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().StartAt(8).GetValueAsync();
          task.Wait();
          Assert("StartAtDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().StartAt(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryStartAtBool() {
      // By default, StartAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':true,  '.priority':'H'}," +
           "  'B': {'.value':false, '.priority':'F'}," +
           "  'C': {'.value':true,  '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValueFalse = "{'A':true,'B':false,'C':true,'D':false}".Replace('\'', '"');
      string expectedByValueTrue = "{'A':true,'C':true}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.StartAt(true).GetValueAsync();
          task.Wait();
          Assert("StartAtBool with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().StartAt(true).GetValueAsync();
          task.Wait();
          Assert("StartAtBool with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().StartAt(true).GetValueAsync();
          task.Wait();
          Assert("StartAtBool with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().StartAt(false).GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue(false)", expectedByValueFalse, task.Result.GetRawJsonValue());
        task = r.OrderByValue().StartAt(true).GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue(true)", expectedByValueTrue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryStartAtStringAndKey() {
      // By default, StartAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'4', '.priority':'G'}," +
           "  'B': {'.value':'2', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}," +
           "  'D': {'.value':'1', '.priority':'H'}}").Replace('\'', '"');
      string expectedByPriority = expectB74812208 ?
          "{'A':'4','B':'2','D':'1'}".Replace('\'', '"') :
          "{'B':'2','D':'1'}".Replace('\'', '"');
      string expectedByValue = expectB74812208 ?
          "{'A':'4','B':'2','C':'3'}".Replace('\'', '"') :
          "{'B':'2','C':'3'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.StartAt("F", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().StartAt("F", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        try {
          task = r.OrderByKey().StartAt("B", "C").GetValueAsync();
          task.Wait();
          Assert("StartAtStringAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().StartAt("2", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());

        // 5. Order by null child
        try {
          r.StartAt("A", null);
          Assert("StartAt(x, null) should have thrown an exception.", false);
        } catch (ArgumentException) {
          // This is the expected case
        }
      });
    }

    Task TestQueryStartAtDoubleAndKey() {
      // By default, StartAt uses priorities, so we add some to the tree:
      string json =
          ("{ '6': {'.value':4, '.priority':5}," +
           "  '7': {'.value':2, '.priority':4}," +
           "  '8': {'.value':3, '.priority':3}," +
           "  '9': {'.value':1, '.priority':6}}").Replace('\'', '"');
      string expectedByPriority = expectB74812208 ?
          "{'6':4,'7':2,'9':1}".Replace('\'', '"') :
          "{'7':2,'9':1}".Replace('\'', '"');
      string expectedByValue = expectB74812208 ?
          "{'6':4,'7':2,'8':3}".Replace('\'', '"') :
          "{'7':2,'8':3}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.StartAt(4, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().StartAt(4, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().StartAt(8, "8").GetValueAsync();
          task.Wait();
          Assert("StartAtDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().StartAt(2, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryStartAtBoolAndKey() {
      // By default, StartAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':false, '.priority':'H'}," +
           "  'B': {'.value':true,  '.priority':'F'}," +
           "  'C': {'.value':true,  '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValueFalse = expectB74812208 ?
          "{'B':true,'C':true,'D':false}".Replace('\'', '"') :
          "{'C':true,'D':false}".Replace('\'', '"');
      string expectedByValueTrue = "{'C':true}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.StartAt(true, "A").GetValueAsync();
          task.Wait();
          Assert("StartAtBoolAndKey with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().StartAt(true, "B").GetValueAsync();
          task.Wait();
          Assert("StartAtBoolAndKey with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().StartAt(true, "C").GetValueAsync();
          task.Wait();
          Assert("StartAtBoolAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, StartAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().StartAt(false, "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue(false,'C')", expectedByValueFalse, task.Result.GetRawJsonValue());
        task = r.OrderByValue().StartAt(true, "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with StartAt", task);
        AssertEq("OrderByValue(true,'C')", expectedByValueTrue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEndAtString() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'2', '.priority':'G'}," +
           "  'B': {'.value':'1', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}}").Replace('\'', '"');
      string expectedByPriority = "{'B':'1','C':'3'}".Replace('\'', '"');
      string expectedByKey = "{'A':'2','B':'1'}".Replace('\'', '"');
      string expectedByValue = "{'A':'2','B':'1'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EndAt("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EndAt("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        task = r.OrderByKey().EndAt("B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByKey", expectedByKey, task.Result.GetRawJsonValue());
        // 4. Order by value
        task = r.OrderByValue().EndAt("2").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEndAtDouble() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ '7': {'.value':2, '.priority':6}," +
           "  '8': {'.value':1, '.priority':5}," +
           "  '9': {'.value':3, '.priority':4}}").Replace('\'', '"');
      string expectedByPriority = "{'8':1,'9':3}".Replace('\'', '"');
      string expectedByValue = "{'7':2,'8':1}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EndAt(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EndAt(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EndAt(8).GetValueAsync();
          task.Wait();
          Assert("EndAtDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().EndAt(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEndAtBool() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':true,  '.priority':'H'}," +
           "  'B': {'.value':false, '.priority':'F'}," +
           "  'C': {'.value':true,  '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValueFalse = "{'B':false,'D':false}".Replace('\'', '"');
      string expectedByValueTrue = "{'A':true,'B':false,'C':true,'D':false}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.EndAt(true).GetValueAsync();
          task.Wait();
          Assert("EndAtBool with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().EndAt(true).GetValueAsync();
          task.Wait();
          Assert("EndAtBool with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EndAt(true).GetValueAsync();
          task.Wait();
          Assert("EndAtBool with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().EndAt(false).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue(false)", expectedByValueFalse, task.Result.GetRawJsonValue());
        task = r.OrderByValue().EndAt(true).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue(true)", expectedByValueTrue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEndAtStringAndKey() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'4', '.priority':'G'}," +
           "  'B': {'.value':'2', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}," +
           "  'D': {'.value':'1', '.priority':'H'}}").Replace('\'', '"');
      string expectedByPriority = expectB74812208 ?
        "{'A':'4','B':'2','C':'3'}".Replace('\'', '"') :
        "{'A':'4','B':'2'}".Replace('\'', '"');
      string expectedByValue = expectB74812208 ?
        "{'B':'2','C':'3','D':'1'}".Replace('\'', '"') :
        "{'B':'2','C':'3'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EndAt("G", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EndAt("G", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        try {
          task = r.OrderByKey().EndAt("B", "C").GetValueAsync();
          task.Wait();
          Assert("EndAtStringAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().EndAt("3", "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());

        // 5. Order by null child
        try {
          r.EndAt("A", null);
          Assert("EndAt(x, null) should have thrown an exception.", false);
        } catch (ArgumentException) {
          // This is the expected case
        }
      });
    }

    Task TestQueryEndAtDoubleAndKey() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ '6': {'.value':4, '.priority':5}," +
           "  '7': {'.value':2, '.priority':4}," +
           "  '8': {'.value':3, '.priority':3}," +
           "  '9': {'.value':1, '.priority':6}}").Replace('\'', '"');
      string expectedByPriority = expectB74812208 ?
          "{'6':4,'7':2,'8':3}".Replace('\'', '"') :
          "{'6':4,'7':2}".Replace('\'', '"');
      string expectedByValue = expectB74812208 ?
          "{'7':2,'8':3,'9':1}".Replace('\'', '"') :
          "{'7':2,'8':3}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EndAt(5, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EndAt(5, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EndAt(8, "8").GetValueAsync();
          task.Wait();
          Assert("EndAtDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().EndAt(3, "8").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEndAtBoolAndKey() {
      // By default, EndAt uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':true,  '.priority':'H'}," +
           "  'B': {'.value':false, '.priority':'F'}," +
           "  'C': {'.value':true,  '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValueFalse = "{'B':false}".Replace('\'', '"');
      string expectedByValueTrue = expectB74812208 ?
          "{'A':true,'B':false,'D':false}".Replace('\'', '"') :
          "{'A':true,'B':false}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.EndAt(true, "A").GetValueAsync();
          task.Wait();
          Assert("EndAtBoolAndKey with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().EndAt(true, "B").GetValueAsync();
          task.Wait();
          Assert("EndAtBoolAndKey with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EndAt(true, "C").GetValueAsync();
          task.Wait();
          Assert("EndAtBoolAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EndAt threw
        }
        // 4. Order by value
        task = r.OrderByValue().EndAt(false, "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue(false,'C')", expectedByValueFalse, task.Result.GetRawJsonValue());
        task = r.OrderByValue().EndAt(true, "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EndAt", task);
        AssertEq("OrderByValue(true,'B')", expectedByValueTrue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEqualToString() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'2', '.priority':'F'}," +
           "  'B': {'.value':'1', '.priority':'F'}," +
           "  'C': {'.value':'2', '.priority':'E'}}").Replace('\'', '"');
      string expectedByPriority = "{'A':'2','B':'1'}".Replace('\'', '"');
      string expectedByKey = "{'B':'1'}".Replace('\'', '"');
      string expectedByValue = "{'A':'2','C':'2'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EqualTo("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EqualTo("F").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        task = r.OrderByKey().EqualTo("B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByKey", expectedByKey, task.Result.GetRawJsonValue());
        // 4. Order by value
        task = r.OrderByValue().EqualTo("2").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEqualToDouble() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ '7': {'.value':2, '.priority':5}," +
           "  '8': {'.value':1, '.priority':5}," +
           "  '9': {'.value':2, '.priority':4}}").Replace('\'', '"');
      string expectedByPriority = "{'7':2,'8':1}".Replace('\'', '"');
      string expectedByValue = "{'7':2,'9':2}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EqualTo(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EqualTo(5).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EqualTo(8).GetValueAsync();
          task.Wait();
          Assert("EqualToDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 4. Order by value
        task = r.OrderByValue().EqualTo(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEqualToBool() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':true,  '.priority':'H'}," +
           "  'B': {'.value':false, '.priority':'F'}," +
           "  'C': {'.value':true,  '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValueFalse = "{'B':false,'D':false}".Replace('\'', '"');
      string expectedByValueTrue = "{'A':true,'C':true}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.EqualTo(true).GetValueAsync();
          task.Wait();
          Assert("EqualToBool with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().EqualTo(true).GetValueAsync();
          task.Wait();
          Assert("EqualToBool with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EqualTo(true).GetValueAsync();
          task.Wait();
          Assert("EqualToBool with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 4. Order by value
        task = r.OrderByValue().EqualTo(false).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue(false)", expectedByValueFalse, task.Result.GetRawJsonValue());
        task = r.OrderByValue().EqualTo(true).GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue(true)", expectedByValueTrue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEqualToStringAndKey() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'4', '.priority':'F'}," +
           "  'B': {'.value':'3', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'F'}," +
           "  'D': {'.value':'3', '.priority':'H'}}").Replace('\'', '"');
      string expectedByPriority = "{'B':'3'}".Replace('\'', '"');
      string expectedByValue = "{'C':'3'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EqualTo("F", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EqualTo("F", "B").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        try {
          task = r.OrderByKey().EqualTo("B", "C").GetValueAsync();
          task.Wait();
          Assert("EqualToStringAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 4. Order by value
        task = r.OrderByValue().EqualTo("3", "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());

        // 5. Order by null child
        try {
          r.EqualTo("A", null);
          Assert("EqualTo(x, null) should have thrown an exception.", false);
        } catch (ArgumentException) {
          // This is the expected case
        }
      });
    }

    Task TestQueryEqualToDoubleAndKey() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ '6': {'.value':4, '.priority':4}," +
           "  '7': {'.value':3, '.priority':4}," +
           "  '8': {'.value':3, '.priority':4}," +
           "  '9': {'.value':3, '.priority':6}}").Replace('\'', '"');
      string expectedByPriority = "{'7':3}".Replace('\'', '"');
      string expectedByValue = "{'8':3}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.EqualTo(4, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().EqualTo(4, "7").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EqualTo(8, "8").GetValueAsync();
          task.Wait();
          Assert("EqualToDouble with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 4. Order by value
        task = r.OrderByValue().EqualTo(3, "8").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryEqualToBoolAndKey() {
      // By default, EqualTo uses priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':true,  '.priority':'H'}," +
           "  'B': {'.value':false, '.priority':'F'}," +
           "  'C': {'.value':false, '.priority':'G'}," +
           "  'D': {'.value':false, '.priority':'E'}}").Replace('\'', '"');
      string expectedByValue = "{'C':false}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        Task<DataSnapshot> task;
        // 1. Default sort order : priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.EqualTo(true, "A").GetValueAsync();
          task.Wait();
          Assert("EqualToBoolAndKey with default order should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 2. Order by priority
        // Expected to throw, as bool is not a valid priority type
        try {
          task = r.OrderByPriority().EqualTo(true, "B").GetValueAsync();
          task.Wait();
          Assert("EqualToBoolAndKey with Priorities should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 3. Order by key - expected to throw, as keys must be strings
        try {
          task = r.OrderByKey().EqualTo(true, "C").GetValueAsync();
          task.Wait();
          Assert("EqualToBoolAndKey with Keys should fail.", false);
        } catch (ArgumentException) {
          // This is the good case, EqualTo threw
        }
        // 4. Order by value
        task = r.OrderByValue().EqualTo(false, "C").GetValueAsync();
        WaitAndAssertCompleted("GetValue with EqualTo", task);
        AssertEq("OrderByValue(false,'C')", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryLimitToFirst() {
      // By default, queries order by priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'4', '.priority':'G'}," +
           "  'B': {'.value':'2', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}," +
           "  'D': {'.value':'1', '.priority':'H'}}").Replace('\'', '"');
      string expectedByPriority = "{'B':'2','C':'3'}".Replace('\'', '"');
      string expectedByKey = "{'A':'4','B':'2'}".Replace('\'', '"');
      string expectedByValue = "{'B':'2','D':'1'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.LimitToFirst(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToFirst", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().LimitToFirst(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToFirst", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        task = r.OrderByKey().LimitToFirst(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToFirst", task);
        AssertEq("OrderByKey", expectedByKey, task.Result.GetRawJsonValue());
        // 4. Order by value
        task = r.OrderByValue().LimitToFirst(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToFirst", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryLimitToLast() {
      // By default, queries order by priorities, so we add some to the tree:
      string json =
          ("{ 'A': {'.value':'4', '.priority':'G'}," +
           "  'B': {'.value':'2', '.priority':'F'}," +
           "  'C': {'.value':'3', '.priority':'E'}," +
           "  'D': {'.value':'1', '.priority':'H'}}").Replace('\'', '"');
      string expectedByPriority = "{'A':'4','D':'1'}".Replace('\'', '"');
      string expectedByKey = "{'C':'3','D':'1'}".Replace('\'', '"');
      string expectedByValue = "{'A':'4','C':'3'}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. Default sort order : priority
        var task = r.LimitToLast(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToLast", task);
        AssertEq("Default Order", expectedByPriority, task.Result.GetRawJsonValue());
        // 2. Order by priority
        task = r.OrderByPriority().LimitToLast(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToLast", task);
        AssertEq("OrderByPriority", expectedByPriority, task.Result.GetRawJsonValue());
        // 3. Order by key
        task = r.OrderByKey().LimitToLast(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToLast", task);
        AssertEq("OrderByKey", expectedByKey, task.Result.GetRawJsonValue());
        // 4. Order by value
        task = r.OrderByValue().LimitToLast(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with LimitToLast", task);
        AssertEq("OrderByValue", expectedByValue, task.Result.GetRawJsonValue());
      });
    }

    Task TestQueryOrderByChild() {
      string json =
          ("{ 'A': {'sort':'4', 'other':'G'}," +
           "  'B': {'sort':'2', 'other':'F'}," +
           "  'C': {'sort':'3', 'other':'E'}," +
           "  'D': {'other':'H'}}").Replace('\'', '"');
      string expectedFirstTwo =
          "{'B':{'other':'F','sort':'2'},'D':{'other':'H'}}".Replace('\'', '"');
      string expectedLastTwo =
          "{'A':{'other':'G','sort':'4'},'C':{'other':'E','sort':'3'}}".Replace('\'', '"');
      return CommonQueryTest(json, r => {
        // 1. First two when ordered by child
        var task = r.OrderByChild("sort").LimitToFirst(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with OrderByChild", task);
        AssertEq("OrderByChild First", expectedFirstTwo, task.Result.GetRawJsonValue());
        // 2. And last two
        task = r.OrderByChild("sort").LimitToLast(2).GetValueAsync();
        WaitAndAssertCompleted("GetValue with OrderByChild", task);
        AssertEq("OrderByChild Last", expectedLastTwo, task.Result.GetRawJsonValue());

        try {
          r.OrderByChild(null);
          Assert("OrderByChild(null) should have thrown an exception.", false);
        } catch (ArgumentException) {
          // This is the expected case
        }
      });
    }

    Task TestQueryReference() {
      AssertEq("Reference.Reference equals itself",
          database.GetReference("TestTree"), database.GetReference("TestTree").Reference);
      AssertEq("Reference.<query ops>.Reference equals itself",
          database.GetReference("TestTree"),
          database.GetReference("TestTree").OrderByValue().StartAt("C").LimitToLast(5).Reference);
      return CompletedTask();
    }

    Task TestDbGoOffline() {
      // Be _very_ careful with this test. There is local caching, so this test
      // only works as expected if the local sync tree is empty.
      // We also MUST go online again, as this is global state.
      return Task.Run(() => {
        database.GoOffline();
        bool expectChange = false;
        bool receivedExpectedChange = false;
        bool receivedUnexpectedChange = false;
        var changeEvent = new ManualResetEvent(false);
        var reference = database.GetReference("OfflineTest2/Child");
        EventHandler<ValueChangedEventArgs> valueChangedListener =
            (object sender, ValueChangedEventArgs e) => {
              if (expectChange) {
                receivedExpectedChange = true;
              } else {
                receivedUnexpectedChange = true;
              }
              changeEvent.Set();
            };
        reference.ValueChanged += valueChangedListener;
        // The expected outcome is that we do NOT receive an event, because we are offline.
        // As we expect NOTHING to happen, to good outcome is to wait for the entire
        // timeout of 5 seconds. Unfortunately, I don't see any way around waiting.
        changeEvent.WaitOne(5 * NumMillisPerSecond);
        // We do expect to receive an event as soon as we go online again.
        expectChange = true;
        database.GoOnline();
        changeEvent.WaitOne(20 * NumMillisPerSecond);
        reference.ValueChanged -= valueChangedListener;
        Assert("Received no changes while offline", !receivedUnexpectedChange);
        Assert("Received change after getting online", receivedExpectedChange);
      });
    }

    Task TestOnDisconnect() {
      return Task.Run(() => {
        // This test queues many OnDisconnect actions, then goes offline and validates that they ran.
        // It will also validate that they did not run before going offline and that they don't
        // run a second time if we go offline again.

        // Long list of constants:
        string childKey = "ChildKey";

        string cancelDirectChild = "Cancel";
        string cancelParentChild = "CancelParentChild";
        string cancelParentParent = "CancelParent";
        string cancelValue = "CancelValue";

        string removeValueChild = "RemoveValue";
        string removeValueValue = "RemoveValueValue";

        string setValueChild = "SetValue";
        string setValueValue = "SetValueValue";

        string setValueAndStringPriorityChild = "SetValueAndStringPriority";
        string setValueAndStringValue = "SetValueAndStringPriorityValue";
        string setValueAndStringPriority = "SetValueAndStringPriority";

        string setValueAndDoublePriorityChild = "SetValueAndDoublePriority";
        string setValueAndDoubleValue = "SetValueAndDoublePriorityValue";
        double setValueAndDoublePriority = 3.14d;

        string updateChildrenChild = "UpdateChildren";
        Dictionary<string, object> initialValue = new Dictionary<string, object>();
        initialValue["stay"] = "This should be unchanged";
        initialValue["change"] = "This should change";
        initialValue["remove"] = "This should be removed";
        Dictionary<string, object> change = new Dictionary<string, object>();
        change["new"] = "This is new";
        change["change"] = "This is changed";
        change["remove"] = null;  // setting it to null removes the child
        // "stay" is not mentioned and should therefore stay the same
        Dictionary<string, object> expectedValue = new Dictionary<string, object>();
        expectedValue["stay"] = "This should be unchanged";
        expectedValue["new"] = "This is new";
        expectedValue["change"] = "This is changed";

        // End of the long list of constants

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();

        try {
          var r = parent.Child(childKey);
          // 1. Queue lots of OnDisconnect stuff
          WaitAndAssertCompleted("OnDisconnect.RemoveValue",
              r.Child(removeValueChild).OnDisconnect().RemoveValue());

          // Queue something, then cancel it.
          WaitAndAssertCompleted("OnDisconnect.RemoveValue",
              r.Child(cancelDirectChild).OnDisconnect().RemoveValue());
          WaitAndAssertCompleted("OnDisconnect.Cancel",
              r.Child(cancelDirectChild).OnDisconnect().Cancel());

          WaitAndAssertCompleted("OnDisconnect.RemoveValue",
              r.Child(cancelParentParent).Child(cancelParentChild).OnDisconnect().RemoveValue());
          // Cancelling at the parent level also cancels actions for child nodes:
          WaitAndAssertCompleted("OnDisconnect.Cancel",
              r.Child(cancelParentParent).OnDisconnect().Cancel());

          WaitAndAssertCompleted("OnDisconnect.SetValue",
              r.Child(setValueChild).OnDisconnect().SetValue(setValueValue));

          WaitAndAssertCompleted("OnDisconnect.SetValueAndPriority",
              r.Child(setValueAndStringPriorityChild).OnDisconnect().
              SetValue(setValueAndStringValue, setValueAndStringPriority));

          WaitAndAssertCompleted("OnDisconnect.SetValueAndPriority",
              r.Child(setValueAndDoublePriorityChild).OnDisconnect().
              SetValue(setValueAndDoubleValue, setValueAndDoublePriority));

          WaitAndAssertCompleted("OnDisconnect.UpdateChildren",
              r.Child(updateChildrenChild).OnDisconnect().UpdateChildren(change));

          // 2. Set pre-existing values
          // (We intentionally do that AFTER queueing the OnDisconnect actions.)
          WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
          WaitAndAssertCompleted("SetValue", r.Child(cancelDirectChild).SetValueAsync(cancelValue));
          WaitAndAssertCompleted("SetValue",
              r.Child(cancelParentParent).Child(cancelParentChild).SetValueAsync(cancelValue));
          WaitAndAssertCompleted("SetValue", r.Child(removeValueChild).SetValueAsync(removeValueValue));
          WaitAndAssertCompleted("SetValue", r.Child(updateChildrenChild).SetValueAsync(initialValue));

          // 3. Some asserts that things have not run yet:
          Task<DataSnapshot> t;
          t = r.GetValueAsync();
          WaitAndAssertCompleted("GetValue", t);
          AssertEq("Number of children before offline", 4, t.Result.ChildrenCount);
          Assert("To be removed child still there", t.Result.HasChild(removeValueChild));

          // GoOffline to trigger the OnDisconnect actions
          database.GoOffline();
          database.GoOnline();

          // Validate that everything is as expexted
          t = r.GetValueAsync();
          WaitAndAssertCompleted("GetValue", t);
          AssertEq("Number of children", 6, t.Result.ChildrenCount);

          Assert("Cancel child exists", t.Result.HasChild(cancelDirectChild));
          AssertEq("Cancel child unchanged", t.Result.Child(cancelDirectChild).Value, cancelValue);

          Assert("Cancel parent child exists",
              t.Result.HasChild(cancelParentParent) &&
              t.Result.Child(cancelParentParent).HasChild(cancelParentChild));
          AssertEq("Cancel parent child unchanged",
              t.Result.Child(cancelParentParent).Child(cancelParentChild).Value, cancelValue);

          Assert("Removed child does not exist", !t.Result.HasChild(removeValueChild));

          AssertEq("SetValue value", t.Result.Child(setValueChild).Value, setValueValue);
          AssertEq("SetValue priority", t.Result.Child(setValueChild).Priority, null);

          AssertEq("SetValueAndStringPriority value",
              t.Result.Child(setValueAndStringPriorityChild).Value, setValueAndStringValue);
          AssertEq("SetValueAndStringPriority priority",
              t.Result.Child(setValueAndStringPriorityChild).Priority, setValueAndStringPriority);

          AssertEq("SetValueAndDoublePriority value",
              t.Result.Child(setValueAndDoublePriorityChild).Value, setValueAndDoubleValue);
          AssertEq("SetValueAndDoublePriority priority",
              t.Result.Child(setValueAndDoublePriorityChild).Priority, setValueAndDoublePriority);

          Assert("UpdateChildren value as expected",
              EqualContent(t.Result.Child(updateChildrenChild).Value, expectedValue));

          // Redo everything, to check that OnDisconnect actions are only run once.
          WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
          WaitAndAssertCompleted("SetValue", r.Child(cancelDirectChild).SetValueAsync(cancelValue));
          WaitAndAssertCompleted("SetValue",
              r.Child(cancelParentParent).Child(cancelParentChild).SetValueAsync(cancelValue));
          WaitAndAssertCompleted("SetValue", r.Child(removeValueChild).SetValueAsync(removeValueValue));
          WaitAndAssertCompleted("SetValue", r.Child(updateChildrenChild).SetValueAsync(initialValue));
          t = r.GetValueAsync();
          WaitAndAssertCompleted("GetValue", t);
          AssertEq("Number of children before offline", 4, t.Result.ChildrenCount);
          Assert("To be removed child still there", t.Result.HasChild(removeValueChild));

          // GoOffline again (this time, there should be no OnDisconnect actions)
          database.GoOffline();
          database.GoOnline();

          // This time, the state should not have changed
          t = r.GetValueAsync();
          WaitAndAssertCompleted("GetValue", t);
          AssertEq("Number of children before offline", 4, t.Result.ChildrenCount);
          Assert("To be removed child still there", t.Result.HasChild(removeValueChild));
        } finally {
          // Finally, cleanup, as to not pollute the test database
          parent.RemoveValueAsync();
        }
      });
    }

    // Confirm that returning an Abort from RunTransaction is handled correctly.
    Task TestDbTransactionAbort() {
      return Task.Run(() => {
        var parent = database.RootReference.Child("TestTree").Push();
        try {
          string initialValue = "Initial Value";
          WaitAndAssertCompleted("Setting initial value", parent.SetValueAsync(initialValue));
          var task = parent.RunTransaction(mutableData => {
            mutableData.Value = "Different Value";
            return TransactionResult.Abort();
          });
          WaitAndExpectException("RunTransaction", task, typeof(DatabaseException));
          var valueTask = parent.GetValueAsync();
          WaitAndAssertCompleted("GetValueAsync", valueTask);
          AssertEq("Did not get the expected value after the transaction ran",
            (string)valueTask.Result.Value, initialValue);
        } finally {
          parent.RemoveValueAsync();
        }
      });
    }

    // Confirm that adding a value from a RunTransaction is handled correctly;
    Task TestDbTransactionAddValue() {
      return Task.Run(() => {
        long testValue = 42;
        var parent = database.RootReference.Child("TestTree").Push();
        try {
          var reference = parent.Child("TestDbTransactionAddValue");
          var task = reference.RunTransaction(mutableData => {
            AssertEq("Transaction function should be run from the main thread.", mainThread,
                     Thread.CurrentThread);
            mutableData.Value = testValue;
            return TransactionResult.Success(mutableData);
          });
          WaitAndAssertCompleted("RunTransaction", task);
          var valueTask = reference.GetValueAsync();
          WaitAndAssertCompleted("GetValueAsync", valueTask);
          AssertEq("Did not get the expected value after the transaction ran",
            (long)valueTask.Result.Value, testValue);
        } finally {
          parent.RemoveValueAsync();
        }
      });
    }

    // Check the various fields on mutable data.
    Task TestDbTransactionMutableData() {
      return Task.Run(() => {
        var parent = database.RootReference.Child("TestTree").Push();
        try {
          // We don't want to assert on the errors in a transaction, as the expected behavior
          // is that the transaction can run multiple times, potentially passing in incorrect
          // mutableData, until the final run, which would have the correct mutableData.
          AccumulateErrors errors = new AccumulateErrors();
          // First, check the mutable data on an empty tree.
          var firstTransaction = parent.RunTransaction(mutableData => {
            errors.failedTestMessages.Clear();

            errors.ExpectEq("MutableData should return invalid value on null child",
                mutableData.Child(null).Value, null);
            errors.ExpectFalse("MutableData should always return false for HasChild on null",
                mutableData.HasChild(null));

            errors.ExpectEq("Parent value wasn't empty at start", mutableData.Value, null);
            errors.ExpectTrue("Parent Key was not empty as expected",
              string.IsNullOrEmpty(mutableData.Key));
            errors.ExpectTrue("Parent initial claims to have children at start",
              !mutableData.HasChildren);
            // We need to change the mutable data, or it will retry until it times out.
            mutableData.Value = "First Transaction Finished";
            return TransactionResult.Success(mutableData);
          });
          WaitAndAssertCompleted("First RunTransaction", firstTransaction);
          Assert("Errors in the first Transaction.\n" +
            String.Join("\n\n", errors.failedTestMessages.ToArray()),
            errors.failedTestMessages.Count == 0);

          // Add some children to the parent
          WaitAndAssertCompleted("Create FirstChild", parent.Child("FirstChild").SetValueAsync(10));
          WaitAndAssertCompleted("Create FirstChildsChild",
            parent.Child("FirstChild").Child("FirstChildsChild)").SetValueAsync(100));
          WaitAndAssertCompleted("Create SecondChild",
            parent.Child("SecondChild").SetValueAsync("Twenty"));

          // Run another transaction, checking these new values
          var secondTransaction = parent.RunTransaction(mutableData => {
            errors.failedTestMessages.Clear();
            errors.ExpectTrue("Parent Key was not empty as expected",
              string.IsNullOrEmpty(mutableData.Key));
            errors.ExpectTrue("Parent should have children", mutableData.HasChildren);
            errors.ExpectEq("Parent should have two children", mutableData.ChildrenCount, 2);
            errors.ExpectTrue("Parent should have FirstChild", mutableData.HasChild("FirstChild"));
            errors.ExpectTrue("Parent should have SecondChild", mutableData.HasChild("SecondChild"));
            int count = 0;
            foreach (MutableData child in mutableData.Children) {
              if (child.Key != "FirstChild" && child.Key != "SecondChild") {
                errors.ExpectTrue("Parent had an unexpected child: " + child.Key, false);
              }
              count++;
            }
            errors.ExpectEq("Parent's Children enumerator had the wrong amount", count, 2);

            // Change the mutable data, or it will retry until it times out.
            mutableData.Child("SecondChild").Value = "Second Transaction Finished";
            return TransactionResult.Success(mutableData);
          });
          WaitAndAssertCompleted("Second RunTransaction", secondTransaction);
          Assert("Errors in the second Transaction.\n" +
            String.Join("\n\n", errors.failedTestMessages.ToArray()),
            errors.failedTestMessages.Count == 0);
        } finally {
          parent.RemoveValueAsync();
        }
      });
    }

    // Check that a "ServerValue" is replaced by a number after insertion.
    Task TestServerValue() {
      return Task.Run(() => {
        string childKey = "ChildKey";
        object valueAdded = ServerValue.Timestamp;

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // The child added event should be for a number, as the ServerValue was replaced.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, typeof(Int64));
        // We do exect a "Changed" event, as the initial "Added" event was done locally,
        // using a local timestamp - that value changes once we get the server timestamp.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, typeof(Int64));
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, typeof(Int64));

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference
        WaitAndAssertCompleted("SetValue", r.SetValueAsync(valueAdded));
        // 2. Remove the value
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    // Check that a "ServerValue" works for priorities.
    Task TestServerValuePriority() {
      return Task.Run(() => {
        string childKey = "ChildKey";
        object valueAdded = ServerValue.Timestamp;

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();
        AsyncChecks checks = new AsyncChecks(parent);
        // The child added event should be for a number, as the ServerValue was replaced.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Added, childKey, typeof(Int64), typeof(Int64));
        // We do exect a "Changed" event, as the initial "Added" event was done locally,
        // using a local timestamp - that value changes once we get the server timestamp.
        // And yes, the server saves the timestamp as a Double (if stored to a priority),
        // which is consistent with the restrictions on priorities. It's likely a bug
        // that the client uses an Int64 for the temporary value that it stores.
        checks.ExpectEvent(AsyncChecks.ChildEventType.Moved, childKey, typeof(Int64), typeof(Double));
        checks.ExpectEvent(AsyncChecks.ChildEventType.Changed, childKey, typeof(Int64), typeof(Double));
        checks.ExpectEvent(AsyncChecks.ChildEventType.Removed, childKey, typeof(Int64), typeof(Double));

        var r = parent.Child(childKey);
        // 1. Set a value in a newly created reference
        WaitAndAssertCompleted("SetValue", r.SetValueAsync(valueAdded, valueAdded));
        // 2. Remove the value
        WaitAndAssertCompleted("RemoveValue", r.RemoveValueAsync());
        // Wait for the ChildRemoved listener to be called
        checks.WaitForEvents();
        checks.AssertAllEventsDone();
        Assert("All checks in listeners passed.\n" + checks.FailMessage, checks.AllGood);
      });
    }

    // Use GetInstance(url) to connect to a different database.
    Task TestGetInstanceByURL() {
      return Task.Run(() => {
        // unity-test-app-ii is a read-only database with one value in it.
        FirebaseDatabase db =
          FirebaseDatabase.GetInstance("https://unity-test-app-ii.firebaseio.com/");
        db.LogLevel = LogLevel.Verbose;
        FirebaseDatabase db2 =
          FirebaseDatabase.GetInstance("https://unity-test-app-ii.firebaseio.com/");
        AssertEq("GetInstance returns same DB for same url", db, db2);
        Assert("GetInstance returns different DB for different url", db != database);

        var task = db.RootReference.Child("TestValue").GetValueAsync();
        WaitAndAssertCompleted("GetValueAsync from second DB", task);
        AssertEq("GetValueAsync returned the expected value",
            task.Result.GetRawJsonValue(), "\"Second Database\"");
      });
    }

    IEnumerator WaitSnapshot_Coroutine(DataSnapshot snapshot,
                                       AccumulateErrors testSuite, Semaphore sem) {
      yield return new WaitForSeconds(1f);
      try {
        testSuite.ExpectEq("SnapshotValue", snapshot.Value, "CoroutineValue");
      } catch (Exception ex) {
        testSuite.failedTestMessages.Add(ex.Message);
      } finally {
        sem.Release();
      }
    }

    Task SnapshotWithCoroutinesTestHelper(
        Action<DataSnapshot, AccumulateErrors, Semaphore> action) {
      // Wrapper needed because we wait for an async call.
      return Task.Run(() => {
        AccumulateErrors testSuite = new AccumulateErrors();
        Semaphore sem = new Semaphore(0, 1);

        string childKey = "SnapshotChildrenCoroutine";
        string valueAddedJson = "{\"CoroutineKey\":\"CoroutineValue\"}";

        // We use Push to create a random child to not interfere with someone else running this test.
        var parent = database.RootReference.Child("TestTree").Push();

        EventHandler<ChildChangedEventArgs> childAddedListener =
            (object sender, ChildChangedEventArgs e) => {
              try {
                action(e.Snapshot, testSuite, sem);
              } catch (Exception ex) {
                testSuite.failedTestMessages.Add(ex.Message);
                sem.Release();
            }
          };

        parent.ChildAdded += childAddedListener;

        // Establish a child with childKey, and assign it a value.
        var r = parent.Child(childKey);
        WaitAndAssertCompleted("DataStoreTest SetRawJsonValue",
            r.SetRawJsonValueAsync(valueAddedJson));

        parent.ChildAdded -= childAddedListener;

        // Remove the value (to clean up).
        WaitAndAssertCompleted("DataStoreTest RemoveValue", r.RemoveValueAsync());

        // Use this semaphore to wait, and be sure the tests are done.
        sem.WaitOne();

        Assert("All checks passed.\n" +
          String.Join("\n\n", testSuite.failedTestMessages.ToArray()),
          testSuite.failedTestMessages.Count == 0);
      });
    }

    // Test if a Snapshot obtained via Child will work with a Coroutine.
    Task TestSnapshotWithCoroutinesChild() {
      return SnapshotWithCoroutinesTestHelper((snapshot, testSuite, sem) => {
        testSuite.ExpectEq("Snapshot.ChildrenCount", snapshot.ChildrenCount, 1);
        testSuite.ExpectTrue("Snapshot.HasChild", snapshot.HasChild("CoroutineKey"));
        StartCoroutine(WaitSnapshot_Coroutine(snapshot.Child("CoroutineKey"), testSuite, sem));
      });
    }

    // Test if a Snapshot obtained via Children will work with a Coroutine.
    Task TestSnapshotWithCoroutinesChildren() {
      return SnapshotWithCoroutinesTestHelper((snapshot, testSuite, sem) => {
        testSuite.ExpectEq("Snapshot.ChildrenCount", snapshot.ChildrenCount, 1);
        foreach (var child in snapshot.Children) {
          StartCoroutine(WaitSnapshot_Coroutine(child, testSuite, sem));
        }
      });
    }
  }
}
