// Copyright 2021 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.Firestore {
  using Firebase;
  using Firebase.Extensions;
  using Firebase.Firestore;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using System.Reflection;
  using System.Text;
  using System.Text.RegularExpressions;
  using System.Threading;
  using System.Threading.Tasks;
  using UnityEngine;

  // An automated version of the UIHandler that runs tests on Firebase Firestore.
  public class UIHandlerAutomated : UIHandler {
    private Firebase.Sample.AutomatedTestRunner testRunner;

    // Class for forcing code to run on the main thread.
    private MainThreadDispatcher mainThreadDispatcher;
    public int MainThreadId { get; private set; }

    // Number of characters that UnityEngine.Debug.Log will output to Android's logcat before
    // truncating the rest. (See LogInBatches docstring for further discussion.)
    private const int UnityAndroidLogLimit = 1000;

    private IEnumerable<string> SplitIntoBatches(string s) {
      // Process the string in chunks of "UnityAndroidLogLimit" characters (except the last chunk,
      // which is likely to be shorter): find the last newline within the current chunk, split at
      // that point, start a new chunk right after the split point, repeat until the string is
      // exhausted.
      int begin = 0;

      while (begin < s.Length) {
        int count = Math.Min(s.Length - begin, UnityAndroidLogLimit);
        int lastNewLineIndex = s.LastIndexOf("\n", begin + count, count);
        if (lastNewLineIndex != -1) {
          count = lastNewLineIndex - begin + 1;
        }

        yield return s.Substring(begin, count).TrimEnd();

        begin += count;
      }
    }

    /// <summary>
    /// Log the given message, split into "batches" of up to 1000 characters.
    /// </summary>
    /// <remarks>
    /// Unity's Debug.Log truncates logs to 1000 characters on Android. (Undocumented by Unity,
    /// but otherwise well known on the Unity forums.) This is particularly egregious when
    /// outputting stacktraces. This method will try to break on newlines.
    ///
    /// Note that Unity will add stacktraces to the logs in certain circumstances. These are added
    /// inside the Debug.Log call, and therefore, we can't prevent Unity from truncating them, so
    /// these seem to never be useful on Android. (See call to Application.SetStackTraceLogType.)
    /// </remarks>
    private void LogInBatches(string s) {
      foreach (string batch in SplitIntoBatches(s)) {
        DebugLog(batch);
      }
    }

    protected override void Start() {
      // Set the list of tests to run, note this is done at Start since they are
      // non-static.
      Func<Task>[] tests = {
        // clang-format off
        TestDeleteDocument,
        TestWriteDocument,
        TestWriteDocumentViaCollection,
        TestWriteDocumentWithIntegers,
        TestUpdateDocument,
        TestListenForSnapshotsInSync,
        TestMultiInstanceDocumentReferenceListeners,
        TestMultiInstanceQueryListeners,
        TestMultiInstanceSnapshotsInSyncListeners,
        TestUpdateFieldInDocument,
        TestArrayUnionAndRemove,
        TestUpdateFieldPath,
        TestWriteBatches,
        TestTransaction,
        TestTransactionWithNonGenericTask,
        TestTransactionAbort,
        TestTransactionTaskFailures,
        TestTransactionRollsBackIfException,
        TestTransactionPermanentError,
        TestTransactionDispose,
        TestTransactionsInParallel,
        // Waiting for all retries is slow, so we usually leave this test disabled.
        // TestTransactionMaxRetryFailure,
        TestTransactionOptions,
        TestTransactionWithExplicitMaxAttempts,
        TestSetOptions,
        TestCanTraverseCollectionsAndDocuments,
        TestCanTraverseCollectionAndDocumentParents,
        TestDocumentSnapshot,
        TestDocumentSnapshotIntegerIncrementBehavior,
        TestDocumentSnapshotDoubleIncrementBehavior,
        TestDocumentSnapshotServerTimestampBehavior,
        TestSnapshotMetadataEqualsAndGetHashCode,
        TestAuthIntegration,
        TestDocumentListen,
        TestDocumentListenWithMetadataChanges,
        TestQueryListen,
        TestQueryListenWithMetadataChanges,
        TestQuerySnapshotChanges,
        TestQueries,
        TestLimitToLast,
        TestArrayContains,
        TestArrayContainsAny,
        TestInQueries,
        TestDefaultInstanceStable,
        TestGetAsyncSource,
        TestWaitForPendingWrites,
        TestTerminate,
        TestClearPersistence,
        TestObjectMappingSmokeTest,
        TestObjectMapping,
        TestNotSupportedObjectMapping,
        TestFirestoreSingleton,
        TestFirestoreSettings,
        TestDocumentReferenceTaskFailures,
        TestCollectionReferenceTaskFailures,
        TestAssertTaskFaults,
        TestInternalExceptions,
        TestListenerRegistrationDispose,
        TestLoadBundles,
        TestQueryEqualsAndGetHashCode,
        TestQuerySnapshotEqualsAndGetHashCode,
        TestDocumentSnapshotEqualsAndGetHashCode,
        TestDocumentChangeEqualsAndGetHashCode,
        TestLoadBundlesForASecondTime_ShouldSkip,
        TestLoadBundlesFromOtherProjects_ShouldFail,
        LoadedBundleDocumentsAlreadyPulledFromBackend_ShouldNotOverwrite,
        TestInvalidArgumentAssertions,
        TestFirestoreDispose,
        TestFirestoreGetInstance,
        // clang-format on
      };

      // For local development convenience, populate `testFilter` with the tests that you would like
      // to run (instead of running the entire suite).
      Func<Task>[] testFilter = {
        // THIS LIST MUST BE EMPTY WHEN CHECKED INTO SOURCE CONTROL!
      };

      // Unity "helpfully" adds stack traces whenever you call Debug.Log. Unfortunately, these stack
      // traces are basically useless, since the good parts are always truncated.  (See comments on
      // LogInBatches.) So just disable them.
      Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

      mainThreadDispatcher = gameObject.AddComponent<MainThreadDispatcher>();
      if (mainThreadDispatcher == null) {
        Debug.LogError("Could not create MainThreadDispatcher component!");
        return;
      }
      mainThreadDispatcher.RunOnMainThread(() => {
        MainThreadId = Thread.CurrentThread.ManagedThreadId;
      });

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: testFilter.Length > 0 ? testFilter : tests,
        logFunc: LogInBatches
      );

      Debug.Log("NOTE: Some API calls report failures using UnityEngine.Debug.LogError which will " +
                "pause execution in the editor when 'Error Pause' in the console window is " +
                "enabled.  `Error Pause` should be disabled to execute this test.");

      UIEnabled = false;
      base.Start();
    }

    // Passes along the update call to automated test runner.
    protected override void Update() {
      base.Update();
      if (testRunner != null && isFirebaseInitialized) {
        testRunner.Update();
      }
    }

    // Fails the test by throwing an exception with the given `reason` string.
    public void FailTest(String reason) {
      testRunner.FailTest(reason);
    }

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition) {
        FailTest(String.Format("Assertion failed ({0}): {1}", testRunner.CurrentTestDescription,
                               message));
      }
    }

    // Throw when `obj` is null.
    private void AssertNotNull(string message, object obj) {
      if (obj == null) {
        FailTest(String.Format("Assertion failed ({0}): object is null: {1}",
                               testRunner.CurrentTestDescription, message));
      }
    }

    // Throw when value1 != value2 (using direct .Equals() check).
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!(object.Equals(value1, value2))) {
        FailTest(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                               testRunner.CurrentTestDescription, value1, value2, message));
      }
    }

    // Throw when value1 != value2 (using direct .Equals() check).
    private void AssertEq<T>(T value1, T value2) {
      AssertEq("Values not equal", value1, value2);
    }

    // Throw when value1 == value2 (using direct .Equals() check).
    private void AssertNe<T>(string message, T value1, T value2) {
      if ((object.Equals(value1, value2))) {
        FailTest(String.Format("Assertion failed ({0}): {1} == {2} ({3})",
                               testRunner.CurrentTestDescription, value1, value2, message));
      }
    }

    // Throw when value1 == value2 (using direct .Equals() check).
    private void AssertNe<T>(T value1, T value2) {
      AssertNe("Values are equal", value1, value2);
    }

    // Throw when `expectedSubstring` is not a substring of `s`, case-insensitively.
    private void AssertStringContainsNoCase(string s, string expectedSubstring) {
      if (!s.ToLower().Contains(expectedSubstring.ToLower())) {
        throw new Exception(String.Format("Assertion failed: The string \"{0}\" is not " +
                                              "contained, case-insensitively, in: {1}",
                                          expectedSubstring, s));
      }
    }

    // Throw when value1 != value2 (traversing the values recursively, including arrays and maps).
    private void AssertDeepEq<T>(string message, T value1, T value2) {
      if (!ObjectDeepEquals(value1, value2)) {
        FailTest(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                               testRunner.CurrentTestDescription, ObjectToString(value1),
                               ObjectToString(value2), message));
      }
    }

    // Throw when value1 != value2 (traversing the values recursively, including arrays and maps).
    private void AssertDeepEq<T>(T value1, T value2) {
      AssertDeepEq("Values not equal", value1, value2);
    }

    private void AssertIsType(object obj, Type expectedType) {
      if (!expectedType.IsInstanceOfType(obj)) {
        FailTest("object has type " + obj.GetType() + " but expected " + expectedType + " (" + obj +
                 ")");
      }
    }

    internal void AssertException(Type exceptionType, Action a) {
      AssertException("", exceptionType, a);
    }

    private void AssertException(String context, Type exceptionType, Action a) {
      try {
        a();
      } catch (Exception e) {
        if (e is TargetInvocationException) {
          e = e.InnerException;
        }
        if (!exceptionType.IsAssignableFrom(e.GetType())) {
          FailTest(
              String.Format("{0}\nException of wrong type was thrown. Expected {1} but got: {2}",
                            context, exceptionType, e));
        }
        return;
      }

      FailTest(String.Format("{0}\nExpected exception was not thrown ({1})", context,
                             testRunner.CurrentTestDescription));
    }

    private void AssertTaskProperties(Task t, bool isCompleted, bool isFaulted, bool isCanceled) {
      // Save the properties of the Task to local variables (instead of always accessing them on
      // the Task object) to ensure that the values reported in the message exactly match those used
      // in the assertion check.
      var actualIsCompleted = t.IsCompleted;
      var actualIsFaulted = t.IsFaulted;
      var actualIsCanceled = t.IsCanceled;

      var message = String.Format("IsCompleted={0} (expected {1}), IsFaulted={2} (expected {3}), "
                                  + "IsCanceled={4} (expected {5})",
                                  actualIsCompleted, isCompleted,
                                  actualIsFaulted, isFaulted,
                                  actualIsCanceled, isCanceled);

      Assert(message,
             isCompleted == actualIsCompleted
             && isFaulted == actualIsFaulted
             && isCanceled == actualIsCanceled);
    }

    internal void AssertTaskSucceeds(Task t) {
      t.Wait();
      AssertTaskProperties(t, isCompleted: true, isFaulted: false, isCanceled: false);
    }

    private T AssertTaskSucceeds<T>(Task<T> t) {
      t.Wait();
      AssertTaskProperties(t, isCompleted: true, isFaulted: false, isCanceled: false);
      return t.Result;
    }

    internal Exception AssertTaskFaults(Task t) {
      var exception = AwaitException(t);
      AssertTaskProperties(t, isCompleted: true, isFaulted: true, isCanceled: false);
      return exception;
    }

    internal Exception AssertTaskFaults(Type exceptionType, Task t) {
      var exception = AssertTaskFaults(t);
      Assert("The Task faulted (as expected); however, it was supposed to fault with" +
                 " an exception of type " + exceptionType +
                 " but it actually faulted with an exception of type " + exception.GetType() +
                 ": " + exception,
             exceptionType.IsAssignableFrom(exception.GetType()));
      return exception;
    }

    internal FirestoreException AssertTaskFaults(Task t, FirestoreError expectedError,
                                                 string expectedMessageRegex = null) {
      var exception = AssertTaskFaults(t);
      AssertEq(
          String.Format("The task faulted (as expected); however, its exception was expected to "
                        + "be FirestoreException with ErrorCode={0} but the actual exception was "
                        + "{1}: {2}", expectedError, exception.GetType(), exception),
          exception.GetType(), typeof(FirestoreException));

      var firestoreException = (FirestoreException)exception;
      AssertEq<FirestoreError>(
          String.Format("The task faulted with FirestoreException (as expected); however, its "
                        + "ErrorCode was expected to be {0} but the actual ErrorCode was {1}: {2}",
                        expectedError, firestoreException.ErrorCode, firestoreException),
          firestoreException.ErrorCode, expectedError);

      if (expectedMessageRegex != null) {
        Assert(String.Format("The task faulted with FirestoreException with ErrorCode={0} " +
                                 "(as expected); however, its message did not match the regular " +
                                 "expression {1}: {2}",
                             expectedError, expectedMessageRegex, firestoreException.Message),
               Regex.IsMatch(firestoreException.Message, expectedMessageRegex,
                             RegexOptions.Singleline));
      }

      return firestoreException;
    }

    private void AssertTaskIsPending(Task t) {
      AssertTaskProperties(t, isCompleted: false, isFaulted: false, isCanceled: false);
    }

    // Wait for the Task to complete, then assert that it's Completed.
    private void Await(Task t) {
      t.Wait();
      Assert("Task completed successfully", t.IsCompleted);
    }

    internal T Await<T>(Task<T> t) {
      Await((Task)t);
      return t.Result;
    }

    private Exception AwaitException(Task t) {
      try {
        t.Wait();
      } catch (AggregateException e) {
        AssertEq("AwaitException() task returned multiple exceptions", e.InnerExceptions.Count, 1);
        return e.InnerExceptions[0];
      }

      FailTest("AwaitException() task succeeded rather than throwing an exception.");
      return null;
    }

    // Waits for the `Count` property of the given list to be greater than or
    // equal to the given `minCount`, failing if that count is not reached.
    private void AssertListCountReaches<T>(ThreadSafeList<T> list, int minCount) {
      var stopWatch = new System.Diagnostics.Stopwatch();
      stopWatch.Start();

      while (true) {
        lock (list) {
          if (list.Count >= minCount) {
            return;
          }

          var remainingMilliseconds = 5000 - stopWatch.ElapsedMilliseconds;
          Assert(
              String.Format("Timeout waiting for the list to reach a count of {0} (count is {1})",
                            minCount, list.Count),
              remainingMilliseconds >= 0);

          Monitor.Wait(list, (int)remainingMilliseconds);
        }
      }
    }

    /**
     * Compares two objects for deep equality.
     */
    private bool ObjectDeepEquals(object left, object right) {
      if (left == right) {
        return true;
      } else if (left == null) {
        return right == null;
      } else if (left is IEnumerable && right is IEnumerable) {
        if (left is IDictionary && right is IDictionary) {
          return DictionaryDeepEquals(left as IDictionary, right as IDictionary);
        }
        return EnumerableDeepEquals(left as IEnumerable, right as IEnumerable);
      } else if (!left.GetType().Equals(right.GetType())) {
        return false;
      } else {
        return left.Equals(right);
      }
    }

    /**
     * Compares two IEnumerable for deep equality.
     */
    private bool EnumerableDeepEquals(IEnumerable left, IEnumerable right) {
      var leftEnumerator = left.GetEnumerator();
      var rightEnumerator = right.GetEnumerator();
      var leftNext = leftEnumerator.MoveNext();
      var rightNext = rightEnumerator.MoveNext();
      while (leftNext && rightNext) {
        if (!ObjectDeepEquals(leftEnumerator.Current, rightEnumerator.Current)) {
          return false;
        }
        leftNext = leftEnumerator.MoveNext();
        rightNext = rightEnumerator.MoveNext();
      }

      if (leftNext == rightNext) {
        return true;
      }

      return false;
    }

    /**
     * Compares two dictionaries for deep equality.
     */
    private bool DictionaryDeepEquals(IDictionary left, IDictionary right) {
      if (left.Count != right.Count) return false;

      foreach (object key in left.Keys) {
        if (!right.Contains(key)) return false;

        if (!ObjectDeepEquals(left[key], right[key])) {
          return false;
        }
      }

      return true;
    }

    private string ObjectToString(object item) {
      if (item == null) {
        return "null";
      } else if (item is IDictionary) {
        return DictionaryToString(item as IDictionary);
      } else if (item is IList) {
        return ListToString(item as IList);
      } else {
        return item.ToString();
      }
    }

    private string ListToString(IList list) {
      StringBuilder sb = new StringBuilder();
      bool first = true;
      sb.Append("[ ");
      foreach (object item in list) {
        if (!first) {
          sb.Append(", ");
        }
        first = false;
        sb.Append(ObjectToString(item));
      }
      sb.Append(" ]");
      return sb.ToString();
    }

    private string DictionaryToString(IDictionary dict) {
      StringBuilder sb = new StringBuilder();
      bool first = true;
      sb.Append("{ ");
      foreach (DictionaryEntry keyValue in dict) {
        if (!first) {
          sb.Append(", ");
        }
        first = false;
        sb.Append(ObjectToString(keyValue.Key) + ": " + ObjectToString(keyValue.Value));
      }
      sb.Append(" }");
      return sb.ToString();
    }

    private List<T> AsList<T>(params T[] elements) {
      return elements.ToList();
    }

    private const int AUTO_ID_LENGTH = 20;
    private const string AUTO_ID_ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static System.Random rand = new System.Random();
    private static string AutoId() {

      string result = "";
      for (int i = 0; i < AUTO_ID_LENGTH; i++) {
        result += AUTO_ID_ALPHABET[rand.Next(0, AUTO_ID_ALPHABET.Length)];
      }

      return result;
    }

    internal CollectionReference TestCollection() {
      return db.Collection("test-collection_" + AutoId());
    }

    internal DocumentReference TestDocument() {
      return TestCollection().Document();
    }

    /// Runs the given action in a task to avoid blocking the main thread.
    internal Task Async(Action action) {
      return Task.Run(action);
    }

    private List<Dictionary<string, object>> QuerySnapshotToValues(QuerySnapshot snap) {
      List<Dictionary<string, object>> res = new List<Dictionary<string, object>>();
      foreach (DocumentSnapshot doc in snap) {
        res.Add(doc.ToDictionary());
      }
      return res;
    }

    internal Dictionary<string, object> TestData(long n = 1) {
      return new Dictionary<string, object> {
        {"name", "room " + n},
        {"active", true},
        {"list", new List<object> { "one", 2L, "three", 4L } },
        {"metadata", new Dictionary<string, object> {
          {"createdAt", n},
          {"deep", new Dictionary<string, object> {
            {"field", "deep-field-" + n},
            {"nested-list", new List<object> { "a", "b", "c" }},
          }}
        }}
        // TODO(rgowman): Add other types here too.
      };
    }

    Task TestDeleteDocument() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var data = new Dictionary<string, object>{
          {"f1", "v1"},
        };

        Await(doc.SetAsync(data));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());
        Assert("Written document should exist", snap.Exists);

        Await(doc.DeleteAsync());
        snap = Await(doc.GetSnapshotAsync());
        Assert("Deleted document should not exist", !snap.Exists);

        AssertEq(snap.ToDictionary(), null);
      });
    }

    Task TestWriteDocument() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var data = TestData();

        Await(doc.SetAsync(data));
        var actual = Await(doc.GetSnapshotAsync()).ToDictionary();

        AssertDeepEq("Resulting data does not match", data, actual);
      });
    }

    Task TestWriteDocumentViaCollection() {
      return Async(() => {
        var data = TestData();
        DocumentReference doc = Await(TestCollection().AddAsync(data));

        var actual = Await(doc.GetSnapshotAsync()).ToDictionary();

        AssertDeepEq("Resulting data does not match", data, actual);

        // Verify that the task returned from AddAsync() faults correctly on error.
        var collectionWithInvalidName = TestCollection().Document("__badpath__").Collection("sub");
        var expectedFaultAddAsyncTask = collectionWithInvalidName.AddAsync(data);
        AssertTaskFaults(expectedFaultAddAsyncTask, FirestoreError.InvalidArgument);
      });
    }

    Task TestWriteDocumentWithIntegers() {
      return Async(() => {
        DocumentReference doc = db.Collection("col2").Document();
        var data = new Dictionary<string, object>{
          {"f1", 2},
          {"map", new Dictionary<string, object>{ {"nested f3", 4}, } },
        };
        var expected = new Dictionary<string, object>{
          {"f1", 2L},
          {"map", new Dictionary<string, object>{ {"nested f3", 4L}, } },
        };

        Await(doc.SetAsync(data));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        var actual = snap.ToDictionary();
        AssertDeepEq("Resulting data does not match", expected, actual);
      });
    }

    Task TestUpdateDocument() {
      return Async(() => {
        {
          DocumentReference doc = TestDocument();
          var data = TestData();
          var updateData = new Dictionary<string, object>{
            {"name", "foo"},
            {"metadata.createdAt", 42L}
          };
          var expected = TestData();
          expected["name"] = "foo";
          ((Dictionary<string, object>)expected["metadata"])["createdAt"] = 42L;

          Await(doc.SetAsync(data));
          Await(doc.UpdateAsync(updateData));
          DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

          var actual = snap.ToDictionary();
          AssertDeepEq("Resulting data does not match", expected, actual);
        }

        // Verify that multiple deletes in a single update call work.
        // https://github.com/firebase/quickstart-unity/issues/882
        {
          DocumentReference doc = TestDocument();
          AssertTaskSucceeds(doc.SetAsync(new Dictionary<string, object> {
            {"key1", "value1"},
            {"key2", "value2"},
            {"key3", "value3"},
            {"key4", "value4"},
            {"key5", "value5"},
          }));
          AssertTaskSucceeds(doc.UpdateAsync(new Dictionary<string, object> {
            {"key1", FieldValue.Delete},
            {"key3", FieldValue.Delete},
            {"key5", FieldValue.Delete},
          }));
          DocumentSnapshot snapshot = Await(doc.GetSnapshotAsync(Source.Cache));
          var expected = new Dictionary<string, object> {
            {"key2", "value2"},
            {"key4", "value4"},
          };
          AssertDeepEq("Resulting data does not match", expected, snapshot.ToDictionary());
        }
      });
    }

    Task TestListenForSnapshotsInSync() {
      return Async(() => {
        var events = new List<string>();

        var doc = TestDocument();
        var docAccumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        var docListener = doc.Listen(snapshot => {
          events.Add("doc");
          docAccumulator.Listener(snapshot);
        });

        Await(doc.SetAsync(TestData(1)));

        docAccumulator.Await();
        events.Clear();

        var syncAccumulator = new EventAccumulator<string>(MainThreadId, FailTest);
        var syncListener = doc.Firestore.ListenForSnapshotsInSync(() => {
          events.Add("sync");
          syncAccumulator.Listener("sync");
        });

        // Ensure that the Task from the ListenerRegistration is in the correct state.
        AssertTaskIsPending(syncListener.ListenerTask);

        Await(doc.SetAsync(TestData(2)));

        docAccumulator.Await();
        syncAccumulator.Await(2);

        var expectedEvents = new List<string> {
          "sync",  // Initial in-sync event
          "doc",   // From the Set()
          "sync"   // Another in-sync event
        };

        docListener.Stop();
        syncListener.Stop();
        AssertTaskSucceeds(syncListener.ListenerTask);

        AssertDeepEq("events", events, expectedEvents);
      });
    }

    Task TestMultiInstanceSnapshotsInSyncListeners() {
      return Async(() => {
        var db1Doc = TestDocument();
        var db1 = db1Doc.Firestore;
        var app1 = db1.App;

        var app2 = FirebaseApp.Create(app1.Options, "MultiInstanceSnapshotsInSyncTest");
        var db2 = FirebaseFirestore.GetInstance(app2);
        var db2Doc = db2.Collection(db1Doc.Parent.Id).Document(db1Doc.Id);

        var db1SyncAccumulator = new EventAccumulator<string>(MainThreadId, FailTest);
        var db1SyncListener = db1.ListenForSnapshotsInSync(() => {
          db1SyncAccumulator.Listener("db1 in sync");
        });
        db1SyncAccumulator.Await();

        var db2SyncAccumulator = new EventAccumulator<string>(MainThreadId, FailTest);
        db2.ListenForSnapshotsInSync(() => { db2SyncAccumulator.Listener("db2 in sync"); });
        db2SyncAccumulator.Await();

        db1Doc.Listen((snap) => { });
        db1SyncAccumulator.Await();

        db2Doc.Listen((snap) => { });
        db2SyncAccumulator.Await();

        // At this point we have two firestore instances and separate listeners
        // attached to each one and all are in an idle state. Once the second
        // instance is disposed the listeners on the first instance should
        // continue to operate normally and the listeners on the second
        // instance should not receive any more events.

        db2SyncAccumulator.ThrowOnAnyEvent();

        app2.Dispose();

        Await(db1Doc.SetAsync(TestData(2)));

        db1SyncAccumulator.Await();

        // TODO(b/158580488): Remove this line once the null ref exception
        // during snapshots-in-sync listener cleanup is fixed in C++.
        db1SyncListener.Stop();
      });
    }

    Task TestMultiInstanceDocumentReferenceListeners() {
      return Async(() => {
        var db1Doc = TestDocument();
        var db1 = db1Doc.Firestore;
        var app1 = db1.App;

        var app2 = FirebaseApp.Create(app1.Options, "MultiInstanceDocumentReferenceListenersTest");
        var db2 = FirebaseFirestore.GetInstance(app2);
        var db2Doc = db2.Collection(db1Doc.Parent.Id).Document(db1Doc.Id);

        var db1DocAccumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        db1Doc.Listen(db1DocAccumulator.Listener);
        db1DocAccumulator.Await();

        var db2DocAccumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        db2Doc.Listen(db2DocAccumulator.Listener);
        db2DocAccumulator.Await();

        // At this point we have two firestore instances and separate listeners
        // attached to each one and all are in an idle state. Once the second
        // instance is disposed the listeners on the first instance should
        // continue to operate normally and the listeners on the second
        // instance should not receive any more events.

        db2DocAccumulator.ThrowOnAnyEvent();

        app2.Dispose();

        Await(db1Doc.SetAsync(TestData(2)));

        db1DocAccumulator.Await();
      });
    }

    Task TestMultiInstanceQueryListeners() {
      return Async(() => {
        var db1Coll = TestCollection();
        var db1 = db1Coll.Firestore;
        var app1 = db1.App;

        var app2 = FirebaseApp.Create(app1.Options, "MultiInstanceQueryListenersTest");
        var db2 = FirebaseFirestore.GetInstance(app2);
        var db2Coll = db2.Collection(db1Coll.Id);

        var db1CollAccumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        db1Coll.Listen(db1CollAccumulator.Listener);
        db1CollAccumulator.Await();

        var db2CollAccumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        db2Coll.Listen(db2CollAccumulator.Listener);
        db2CollAccumulator.Await();

        // At this point we have two firestore instances and separate listeners
        // attached to each one and all are in an idle state. Once the second
        // instance is disposed the listeners on the first instance should
        // continue to operate normally and the listeners on the second
        // instance should not receive any more events.

        db2CollAccumulator.ThrowOnAnyEvent();

        app2.Dispose();

        Await(db1Coll.Document().SetAsync(TestData(1)));

        db1CollAccumulator.Await();
      });
    }

    Task TestUpdateFieldInDocument() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var data = new Dictionary<string, object>{
          {"f1", "v1"},
          {"f2", "v2"},
        };

        Await(doc.SetAsync(data));
        Await(doc.UpdateAsync("f2", "v2b"));

        var actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        var expected = new Dictionary<string, object>{
          {"f1", "v1"},
          {"f2", "v2b"},
        };
        AssertDeepEq("Resulting data does not match", expected, actual);
      });
    }

    Task TestArrayUnionAndRemove() {
      return Async(() => {
        DocumentReference doc = TestDocument();

        // Create via ArrayUnion
        var data = new Dictionary<string, object> { { "array", FieldValue.ArrayUnion(1L, 2L) } };
        Await(doc.SetAsync(data));
        var actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        var expected = new Dictionary<string, object> { { "array", new List<object> { 1L, 2L } } };
        AssertDeepEq("Create via ArrayUnion", expected, actual);

        // Append via Update with ArrayUnion
        data = new Dictionary<string, object> { { "array", FieldValue.ArrayUnion(1L, 4L) } };
        Await(doc.UpdateAsync(data));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {{ "array",
                                                      new List<object> { 1L, 2L, 4L }}};
        AssertDeepEq("Append via Update with ArrayUnion", expected, actual);

        // Append via Set/Merge with ArrayUnion
        data = new Dictionary<string, object> { { "array", FieldValue.ArrayUnion(2L, 3L) } };
        Await(doc.SetAsync(data, SetOptions.MergeAll));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {{ "array",
                                                     new List<object> { 1L, 2L, 4L, 3L }}};
        AssertDeepEq("Append via Set/Merge with ArrayUnion", expected, actual);

        // Append object via Update with ArrayUnion
        data = new Dictionary<string, object> {
          { "array",
            FieldValue.ArrayUnion(1L, new Dictionary<string, object>{{"a", "value"}})
          }};
        Await(doc.UpdateAsync(data));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {
          { "array",
            new List<object> { 1L, 2L, 4L, 3L, new Dictionary<string, object>{{"a", "value"}}}}};
        AssertDeepEq("Append object via Update with ArrayUnion", expected, actual);

        // Remove via Update with ArrayRemove
        data = new Dictionary<string, object> {
          { "array",
            FieldValue.ArrayRemove(1L, 4L)
          }};
        Await(doc.UpdateAsync(data));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {
          { "array",
            new List<object> { 2L,  3L, new Dictionary<string, object>{{"a", "value"}}}}};
        AssertDeepEq("Remove via Update with ArrayRemove", expected, actual);

        // Remove via Set/Merge with ArrayRemove
        data = new Dictionary<string, object> {
          { "array",
            FieldValue.ArrayRemove(1L, 3L)
          }};
        Await(doc.SetAsync(data, SetOptions.MergeAll));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {
          { "array",
            new List<object> { 2L, new Dictionary<string, object>{{"a", "value"}}}}};
        AssertDeepEq("Remove via Set/Merge with ArrayRemove", expected, actual);

        // Remove object via Update with ArrayRemove
        data = new Dictionary<string, object> {
          { "array",
            FieldValue.ArrayRemove(new Dictionary<string, object>{{"a", "value"}})
          }};
        Await(doc.UpdateAsync(data));
        actual = Await(doc.GetSnapshotAsync()).ToDictionary();
        expected = new Dictionary<string, object> {
          { "array",
            new List<object> { 2L }}};
        AssertDeepEq("Remove object via Update with ArrayRemove", expected, actual);
      });
    }

    Task TestUpdateFieldPath() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var data = new Dictionary<string, object>{
          {
            "f1", "v1"
          }, {
            "f2", new Dictionary<string, object>{
              {
                "a", new Dictionary<string, object>{
                  {
                    "b", new Dictionary<string, object>{
                      {"c", "v2"},
                    }
                  },
                }
              },
            }
          },
        };

        var updateData = new Dictionary<FieldPath, object>{
          { new FieldPath("f2", "a", "b", "c"), "v2b"},
          { new FieldPath("f2", "x", "y", "z"), "v3"},
        };

        var expected = new Dictionary<string, object>{
          {
            "f1", "v1"
          }, {
            "f2", new Dictionary<string, object>{
              {
                "a", new Dictionary<string, object>{
                  {
                    "b", new Dictionary<string, object>{
                      {"c", "v2b"},
                    }
                  },
                }
              }, {
                "x", new Dictionary<string, object>{
                  {
                    "y", new Dictionary<string, object>{
                      {"z", "v3"},
                    }
                  },
                }
              },
            }
          },
        };

        Await(doc.SetAsync(data));
        Await(doc.UpdateAsync(updateData));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        var actual = snap.ToDictionary();
        AssertDeepEq("Resulting data does not match", expected, actual);
      });
    }

    Task TestWriteBatches() {
      return Async(() => {
        DocumentReference doc1 = TestDocument();
        DocumentReference doc2 = TestDocument();
        DocumentReference doc3 = TestDocument();

        // Initialize doc1 and doc2 with some data.
        var initialData = new Dictionary<string, object>{
          {"field", "value"},
        };
        Await(doc1.SetAsync(initialData));
        Await(doc2.SetAsync(initialData));

        // Perform batch that deletes doc1, updates doc2, and overwrites doc3.
        Await(doc1.Firestore.StartBatch()
            .Delete(doc1)
            .Update(doc2, new Dictionary<string, object> { { "field2", "value2" } })
            .Update(doc2, new Dictionary<FieldPath, object> { { new FieldPath("field3"), "value3" } })
            .Update(doc2, "field4", "value4")
            .Set(doc3, initialData)
            .CommitAsync());

        DocumentSnapshot snap1 = Await(doc1.GetSnapshotAsync());
        AssertEq("doc1 should have been deleted", snap1.Exists, false);

        DocumentSnapshot snap2 = Await(doc2.GetSnapshotAsync());
        AssertDeepEq("doc2 should have been updated", snap2.ToDictionary(),
            new Dictionary<string, object> {
              { "field", "value" },
              { "field2", "value2" },
              { "field3", "value3" },
              { "field4", "value4" },
            });

        DocumentSnapshot snap3 = Await(doc3.GetSnapshotAsync());
        AssertDeepEq("doc3 should have been set", snap3.ToDictionary(), initialData);

        // Verify that the Task returned from CommitAsync() correctly reports errors.
        var docWithInvalidName = TestCollection().Document("__badpath__");
        Task commitWithInvalidDocTask = docWithInvalidName.Firestore.StartBatch()
            .Set(docWithInvalidName, TestData(0))
            .CommitAsync();
        AssertTaskFaults(commitWithInvalidDocTask, FirestoreError.InvalidArgument, "__badpath__");
      });
    }

    Task TestTransaction() {
      return Async(() => {
        DocumentReference doc1 = TestDocument();
        DocumentReference doc2 = TestDocument();
        DocumentReference doc3 = TestDocument();

        // Initialize doc1 and doc2 with some data.
        var initialData = new Dictionary<string, object>{
          {"field", "value"},
        };
        Await(doc1.SetAsync(initialData));
        Await(doc2.SetAsync(initialData));

        // Perform transaction that reads doc1, deletes doc1, updates doc2, and overwrites doc3.
        string result = Await(doc1.Firestore.RunTransactionAsync<string>((transaction) => {
          AssertEq(MainThreadId, Thread.CurrentThread.ManagedThreadId);
          return transaction.GetSnapshotAsync(doc1).ContinueWith((getTask) => {
            AssertDeepEq("doc1 value matches expected", getTask.Result.ToDictionary(), initialData);
            transaction.Delete(doc1);
            transaction.Update(doc2, new Dictionary<string, object> { { "field2", "value2" } });
            transaction.Update(doc2, new Dictionary<FieldPath, object> { { new FieldPath("field3"), "value3" } });
            transaction.Update(doc2, "field4", "value4");
            transaction.Set(doc3, initialData);
            return "txn result";
          });
        }));
        AssertEq("Transaction result passed through", result, "txn result");

        DocumentSnapshot snap1 = Await(doc1.GetSnapshotAsync());
        AssertEq("doc1 should have been deleted", snap1.Exists, false);

        DocumentSnapshot snap2 = Await(doc2.GetSnapshotAsync());
        AssertDeepEq("doc2 should have been updated", snap2.ToDictionary(),
            new Dictionary<string, object> {
              { "field", "value" },
              { "field2", "value2" },
              { "field3", "value3" },
              { "field4", "value4" },
            });

        DocumentSnapshot snap3 = Await(doc3.GetSnapshotAsync());
        AssertDeepEq("doc3 should have been set", snap3.ToDictionary(), initialData);
      });
    }

    Task TestTransactionOptions() {
      return Async(() => {
        // Verify the initial values of a newly-created TransactionOptions object.
        {
          var options = new TransactionOptions();
          AssertEq(options.MaxAttempts, 5);
        }

        // Verify that setting TransactionOptions.MaxAttempts works.
        {
          var options = new TransactionOptions();
          options.MaxAttempts = 1;
          AssertEq(options.MaxAttempts, 1);
          options.MaxAttempts = 42;
          AssertEq(options.MaxAttempts, 42);
          options.MaxAttempts = Int32.MaxValue;
          AssertEq(options.MaxAttempts, Int32.MaxValue);
        }

        // Verify that setting TransactionOptions.MaxAttempts throws on invalid value.
        {
          var options = new TransactionOptions();
          AssertException(typeof(ArgumentException), () => { options.MaxAttempts = 0; });
          AssertException(typeof(ArgumentException), () => { options.MaxAttempts = -1; });
          AssertException(typeof(ArgumentException), () => { options.MaxAttempts = -42; });
          AssertException(typeof(ArgumentException), () => { options.MaxAttempts = Int32.MinValue; });
        }

        // Verify that TransactionOptions.ToString() returns the right value.
        {
          var options = new TransactionOptions();
          options.MaxAttempts = 42;
          AssertEq(options.ToString(), "TransactionOptions{MaxAttempts=42}");
        }
      });
    }

    // Tests the overload of RunTransactionAsync() where the update function returns a non-generic
    // Task object.
    Task TestTransactionWithNonGenericTask() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        Await(db.RunTransactionAsync((transaction) => {
          transaction.Set(doc, TestData(1));
          // Create a plain (non-generic) `Task` result.
          return Task.Run(() => { });
        }));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());
        AssertDeepEq(snap.ToDictionary(), TestData(1));
      });
    }

    Task TestTransactionAbort() {
      return Async(() => {
        // Returning a failed task should abort the transaction.
        int retries = 0;
        Task txnTask = db.RunTransactionAsync((transaction) => {
          retries++;
          TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();
          tcs.SetException(new InvalidOperationException("Failed Task"));
          return tcs.Task;
        });
        Exception e = AwaitException(txnTask);
        AssertEq(retries, 1);
        AssertEq(e.GetType(), typeof(InvalidOperationException));
        AssertEq(e.Message, "Failed Task");

        // Throwing an exception should also abort the transaction.
        retries = 0;
        txnTask = db.RunTransactionAsync((transaction) => {
          retries++;
          throw new InvalidOperationException("Failed Exception");
        });
        e = AwaitException(txnTask);
        AssertEq(retries, 1);
        AssertEq(e.GetType(), typeof(InvalidOperationException));
        AssertEq(e.Message, "Failed Exception");
      });
    }

    Task TestTransactionTaskFailures() {
      return Async(() => {
        Task txnTask = db.RunTransactionAsync((transaction) => {
          var docWithInvalidName = TestCollection().Document("__badpath__");
          return transaction.GetSnapshotAsync(docWithInvalidName);
        });
        AssertTaskFaults(txnTask, FirestoreError.InvalidArgument, "__badpath__");
      });
    }

    Task TestTransactionRollsBackIfException() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        Task txnTask = db.RunTransactionAsync((transaction) => {
          return transaction.GetSnapshotAsync(doc).ContinueWith(snapshotTask => {
            transaction.Set(doc, new Dictionary<string, object> { { "key", 42 } }, null);
            throw new TestException();
          });
        });
        Exception exception = AssertTaskFaults(txnTask);
            AssertEq(exception.GetType(), typeof(TestException));

            // Verify that the transaction was rolled back.
            DocumentSnapshot snap = Await(doc.GetSnapshotAsync());
            Assert("Document is still written when transaction throws exceptions", !snap.Exists);
      });
    }

    private class TestException : Exception {
      public TestException() {}
    }

    Task TestTransactionPermanentError() {
      return Async(() => {
        int retries = 0;
        DocumentReference doc = TestDocument();
        // Try to update a document that doesn't exist. Should fail permanently (no retries)
        // with a "Not Found" error.
        Task txnTask = db.RunTransactionAsync((transaction) => {
          retries++;
          transaction.Update(doc, TestData(0));
          return Task.FromResult<object>(null);
        });
        AssertTaskFaults(txnTask, FirestoreError.NotFound, doc.Id);
        AssertEq(retries, 1);
      });
    }

    // Asserts that all methods of a `Transaction` object throw `InvalidOperationException`.
    private void AssertTransactionMethodsThrow(Transaction txn, DocumentReference doc) {
      var sampleDataString = new Dictionary<string, object> { { "key", "value" } };
      var sampleDataPath = new Dictionary<FieldPath, object> { { new FieldPath("key"), "value" } };

      AssertException(typeof(InvalidOperationException), () => txn.Set(doc, sampleDataString));
      AssertException(typeof(InvalidOperationException), () => txn.Update(doc, sampleDataString));
      AssertException(typeof(InvalidOperationException), () => txn.Update(doc, sampleDataPath));
      AssertException(typeof(InvalidOperationException), () => txn.Update(doc, "key", "value"));
      AssertException(typeof(InvalidOperationException), () => txn.Delete(doc));

      Exception exception = AssertTaskFaults(txn.GetSnapshotAsync(doc));
      AssertIsType(exception, typeof(InvalidOperationException));
    }

    Task TestTransactionDispose() {
      return Async(() => {
        // Verify that the Transaction is disposed if the callback returns a null Task.
        // TODO(b/197348363) Re-enable this test on Android once the bug where returning null from
        // the callback specified to `RunTransactionAsync()` causes the test to hang is fixed.
        #if !UNITY_ANDROID
        {
          DocumentReference doc = TestDocument();
          Transaction capturedTransaction = null;
          Task txnTask = db.RunTransactionAsync(transaction => {
            capturedTransaction = transaction;
            return null;
          });
          AssertTaskFaults(txnTask);
          AssertTransactionMethodsThrow(capturedTransaction, doc);
        }
        #endif

        // Verify that the Transaction is disposed if the callback throws an exception.
        {
          DocumentReference doc = TestDocument();
          Transaction capturedTransaction = null;
          Task txnTask = db.RunTransactionAsync(transaction => {
            capturedTransaction = transaction;
            throw new InvalidOperationException("forced exception");
          });
          AssertTaskFaults(txnTask);
          AssertTransactionMethodsThrow(capturedTransaction, doc);
        }

        // Verify that the Transaction is disposed if the callback returns a Task that succeeds.
        {
          DocumentReference doc = TestDocument();
          Transaction capturedTransaction = null;
          Task txnTask = db.RunTransactionAsync(transaction => {
            return transaction.GetSnapshotAsync(doc).ContinueWith(task => {
              // Call a method on Transaction to ensure that it does not throw an exception.
              transaction.Set(doc, new Dictionary<string, object> { { "answer", 42 } });
              capturedTransaction = transaction;
            });
          });
          AssertTaskSucceeds(txnTask);
          AssertTransactionMethodsThrow(capturedTransaction, doc);
        }

        // Verify that the Transaction is disposed if the callback returns a Task that faults.
        {
          DocumentReference doc = TestDocument();
          Transaction capturedTransaction = null;
          Task txnTask = db.RunTransactionAsync(transaction => {
            capturedTransaction = transaction;
            var taskCompletionSource = new TaskCompletionSource<object>();
            taskCompletionSource.SetException(new InvalidOperationException("forced exception"));
            return taskCompletionSource.Task;
          });
          AssertTaskFaults(txnTask);
          AssertTransactionMethodsThrow(capturedTransaction, doc);
        }

        // Verify that the Transaction is disposed when the Firestore instance terminated.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "transaction-terminate");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          DocumentReference doc = customDb.Document(TestDocument().Path);
          var barrier = new BarrierCompat(2);
          Transaction capturedTransaction = null;
          customDb.RunTransactionAsync(transaction => {
            return Task.Factory.StartNew<object>(() => {
              capturedTransaction = transaction;
              barrier.SignalAndWait();
              barrier.SignalAndWait();
              return null;
            });
          });
          try {
            barrier.SignalAndWait();
            AssertTaskSucceeds(customDb.TerminateAsync());
            // TODO(b/201415845) Remove the following two assertions once the commented call to
            // `AssertTaskFaults` below is uncommented. With that code commented the C# compiler
            // complains about these two variables being "unused".
            AssertNotNull("dummy call to prevent unused variable warning", doc);
            AssertNotNull("dummy call to prevent unused variable warning", capturedTransaction);
            // TODO(b/201415845) Uncomment this check once the crash on iOS and hang on Android
            // that it causes is fixed.
            // AssertTaskFaults(typeof(InvalidOperationException),
            //                  capturedTransaction.GetSnapshotAsync(doc));
          } finally {
            barrier.SignalAndWait();
          }
          customApp.Dispose();
          // TODO(b/171568274): Add an assertion that the Task returned from RunTransactionAsync()
          // either completes or faults once the inconsistent behavior is fixed.
        }

        // Verify that the Transaction is disposed when the Firestore instance is disposed.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "transaction-dispose1");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          DocumentReference doc = customDb.Document(TestDocument().Path);
          var barrier = new BarrierCompat(2);
          Transaction capturedTransaction = null;
          Task capturedTask = null;
          customDb.RunTransactionAsync(transaction => {
            capturedTask = Task.Factory.StartNew<object>(() => {
              capturedTransaction = transaction;
              barrier.SignalAndWait();
              barrier.SignalAndWait();
              return null;
            });
            return capturedTask;
          });
          try {
            barrier.SignalAndWait();
            customApp.Dispose();
            AssertTaskIsPending(capturedTask);
            AssertTransactionMethodsThrow(capturedTransaction, doc);
            AssertTaskIsPending(capturedTask);
          } finally {
            barrier.SignalAndWait();
          }
          AssertTaskSucceeds(capturedTask);
          // TODO(b/171568274): Add an assertion that the Task returned from RunTransactionAsync()
          // either completes or faults once the inconsistent behavior is fixed.
        }

        // Verify that the Transaction is disposed when the Firestore instance is disposed
        // directly from the transaction callback.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "transaction-dispose2");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          DocumentReference doc = customDb.Document(TestDocument().Path);
          var barrier = new BarrierCompat(2);
          Transaction capturedTransaction = null;
          customDb.RunTransactionAsync(transaction => {
            capturedTransaction = transaction;
            customApp.Dispose();
            barrier.SignalAndWait();
            barrier.SignalAndWait();
            var taskCompletionSource = new TaskCompletionSource<object>();
            taskCompletionSource.SetResult(null);
            return taskCompletionSource.Task;
          });
          try {
            barrier.SignalAndWait();
            AssertTransactionMethodsThrow(capturedTransaction, doc);
          } finally {
            barrier.SignalAndWait();
          }
          // TODO(b/171568274): Add an assertion that the Task returned from RunTransactionAsync()
          // either completes or faults once the inconsistent behavior is fixed.
        }

        // Verify that the Transaction is disposed when the Firestore instance is disposed
        // from the task returned from the transaction callback.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "transaction-dispose3");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          DocumentReference doc = customDb.Document(TestDocument().Path);
          var barrier = new BarrierCompat(2);
          Transaction capturedTransaction = null;
          Task capturedTask = null;
          customDb.RunTransactionAsync(transaction => {
            capturedTask = Task.Factory.StartNew<object>(() => {
              capturedTransaction = transaction;
              customApp.Dispose();
              barrier.SignalAndWait();
              barrier.SignalAndWait();
              return null;
            });
            return capturedTask;
          });
          try {
            barrier.SignalAndWait();
            AssertTaskIsPending(capturedTask);
            AssertTransactionMethodsThrow(capturedTransaction, doc);
          } finally {
            barrier.SignalAndWait();
          }
          // TODO(b/171568274): Add an assertion that the Task returned from RunTransactionAsync()
          // either completes or faults once the inconsistent behavior is fixed.
        }
      });
    }

    Task TestTransactionsInParallel() {
      return Async(() => {
        string documentPath = TestDocument().Path;

        FirebaseApp[] apps = new FirebaseApp[3];
        for (int i = 0; i < apps.Length; i++) {
          apps[i] = FirebaseApp.Create(db.App.Options, "transactions-in-parallel" + (i + 1));
        }
        FirebaseFirestore[] firestores = new FirebaseFirestore[apps.Length];
        for (int i = 0; i < firestores.Length; i++) {
          firestores[i] = FirebaseFirestore.GetInstance(apps[i]);
        }

        int numTransactionsPerFirestore = 3;
        List<Task> tasks = new List<Task>();
        for (int i = 0; i < numTransactionsPerFirestore; i++) {
          foreach (FirebaseFirestore firestore in firestores) {
            Task txnTask = firestore.RunTransactionAsync(transaction => {
              DocumentReference currentDoc = firestore.Document(documentPath);
              return transaction.GetSnapshotAsync(currentDoc).ContinueWith(task => {
                DocumentSnapshot currentSnapshot = task.Result;
                int currentValue;
                if (currentSnapshot.TryGetValue("count", out currentValue)) {
                  transaction.Update(currentDoc, "count", currentValue + 1);
                } else {
                  var data = new Dictionary<string, object> { { "count", 1 } };
                  transaction.Set(currentDoc, data);
                }
              });
            });
            tasks.Add(txnTask);
          }
        }

        foreach (Task task in tasks) {
          AssertTaskSucceeds(task);
        }

        DocumentReference doc = db.Document(documentPath);
        DocumentSnapshot snapshot = AssertTaskSucceeds(doc.GetSnapshotAsync(Source.Server));
        int actualValue = snapshot.GetValue<int>("count", ServerTimestampBehavior.None);
        int expectedValue = numTransactionsPerFirestore * firestores.Length;
        AssertEq<int>(actualValue, expectedValue);

        foreach (FirebaseApp app in apps) {
          app.Dispose();
        }
      });
    }

    Task TestTransactionWithExplicitMaxAttempts() {
      return Async(() => {
        var options = new TransactionOptions();
        options.MaxAttempts = 3;
        int numAttempts = 0;
        DocumentReference doc = TestDocument();

        Task txnTask = db.RunTransactionAsync(options, transaction => {
          numAttempts++;
          return transaction.GetSnapshotAsync(doc).ContinueWith(snapshot => {
            // Queue a write via the transaction.
            transaction.Set(doc, TestData(0));
            // But also write the document (out-of-band) so the transaction is retried.
            return doc.SetAsync(TestData(numAttempts));
          }).Unwrap();
        });

        AssertTaskFaults(txnTask, FirestoreError.FailedPrecondition);
        AssertEq(numAttempts, 3);
      });
    }

    Task TestTransactionMaxRetryFailure() {
      return Async(() => {
        int retries = 0;
        DocumentReference doc = TestDocument();
        FirebaseFirestore.LogLevel = LogLevel.Debug;
        Task txnTask = db.RunTransactionAsync((transaction) => {
          retries++;
          return transaction.GetSnapshotAsync(doc).ContinueWith((snapshot) => {
            // Queue a write via the transaction.
            transaction.Set(doc, TestData(0));
            // But also write the document (out-of-band) so the transaction is retried.
            return doc.SetAsync(TestData(retries));
          }).Unwrap();
        });
        AssertTaskFaults(txnTask, FirestoreError.FailedPrecondition);
        // The transaction API will retry 6 times before giving up.
        AssertEq(retries, 6);
      });
    }

    Task TestSetOptions() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var initialData = new Dictionary<string, object> {
          { "field1", "value1" },
        };

        var set1 = new Dictionary<string, object> {
          { "field2", "value2" }
        };
        var setOptions1 = SetOptions.MergeAll;

        var set2 = new Dictionary<string, object> {
          { "field3", "value3" },
          { "not-field4", "not-value4" }
        };
        var setOptions2 = SetOptions.MergeFields("field3");

        var set3 = new Dictionary<string, object> {
          { "field4", "value4" },
          { "not-field5", "not-value5" }
        };
        var setOptions3 = SetOptions.MergeFields(new FieldPath("field4"));

        var expected = new Dictionary<string, object> {
          { "field1", "value1" },
          { "field2", "value2" },
          { "field3", "value3" },
          { "field4", "value4" },
        };

        Await(doc.SetAsync(initialData));
        Await(doc.SetAsync(set1, setOptions1));
        Await(doc.Firestore.StartBatch().Set(doc, set2, setOptions2).CommitAsync());
        // TODO(b/139355404): Switch the following SetAsync() to use a transaction instead once
        // transactions are implemented.
        Await(doc.SetAsync(set3, setOptions3));

        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());
        AssertDeepEq(snap.ToDictionary(), expected);
      });
    }

    Task TestCanTraverseCollectionsAndDocuments() {
      return Async(() => {
        // doc path from root Firestore.
        AssertEq("a/b/c/d", db.Document("a/b/c/d").Path);

        // collection path from root Firestore.
        AssertEq("a/b/c/d", db.Collection("a/b/c").Document("d").Path);

        // doc path from CollectionReference.
        AssertEq("a/b/c/d", db.Collection("a").Document("b/c/d").Path);

        // collection path from DocumentReference.
        AssertEq("a/b/c/d/e", db.Document("a/b").Collection("c/d/e").Path);
      });
    }

    Task TestCanTraverseCollectionAndDocumentParents() {
      return Async(() => {
        CollectionReference collection = db.Collection("a/b/c");
        AssertEq("a/b/c", collection.Path);

        DocumentReference doc = collection.Parent;
        AssertEq("a/b", doc.Path);

        collection = doc.Parent;
        AssertEq("a", collection.Path);

        DocumentReference invalidDoc = collection.Parent;
        AssertEq(null, invalidDoc);
      });
    }

    Task TestDocumentSnapshot() {
      return Async(() => {
        DocumentReference doc = db.Collection("col2").Document();
        var data = TestData();

        Await(doc.SetAsync(data));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        TestDocumentSnapshotGetValue(snap, data);
        TestDocumentSnapshotTryGetValue(snap, data);
        TestDocumentSnapshotContainsField(snap, data);
      });
    }

    Task TestDocumentSnapshotServerTimestampBehavior() {
      return Async(() => {
        DocumentReference doc = db.Collection("col2").Document();

        bool cleanup = true;
        try {
          // Disable network so we can test unresolved server timestamp behavior.
          Await(doc.Firestore.DisableNetworkAsync());

          doc.SetAsync(new Dictionary<string, object> { { "timestamp", "prev" } });
          doc.SetAsync(new Dictionary<string, object> { { "timestamp", FieldValue.ServerTimestamp } });

          DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

          // Default / None should return null.
          AssertEq(snap.ToDictionary()["timestamp"], null);
          AssertEq(snap.GetValue<object>("timestamp"), null);
          AssertEq(snap.ToDictionary(ServerTimestampBehavior.None)["timestamp"], null);
          AssertEq(snap.GetValue<object>("timestamp", ServerTimestampBehavior.None), null);

          // Previous should be "prev"
          AssertEq(snap.ToDictionary(ServerTimestampBehavior.Previous)["timestamp"], "prev");
          AssertEq(snap.GetValue<object>("timestamp", ServerTimestampBehavior.Previous), "prev");

          // Estimate should be a timestamp.
          Assert("Estimate should be a Timestamp",
              snap.ToDictionary(ServerTimestampBehavior.Estimate)["timestamp"] is Timestamp);
          Assert("Estimate should be a Timestamp",
              snap.GetValue<object>("timestamp", ServerTimestampBehavior.Estimate) is Timestamp);
        } catch (ThreadAbortException) {
          // Don't try to do anything else on this thread if it was aborted, or else it will hang.
          cleanup = false;
        } finally {
          if (cleanup) {
            Await(doc.Firestore.EnableNetworkAsync());
          }
        }
      });
    }

    Task TestDocumentSnapshotIntegerIncrementBehavior() {
      return Async(() => {
        DocumentReference doc = TestDocument();

        var data = TestData();
        Await(doc.SetAsync(data));

        var incrementValue = FieldValue.Increment(1L);

        var updateData = new Dictionary<string, object>{
          {"metadata.createdAt", incrementValue }
        };

        Await(doc.UpdateAsync(updateData));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        var expected = TestData();
        ((Dictionary<string, object>)expected["metadata"])["createdAt"] = 2L;

        var actual = snap.ToDictionary();
        AssertDeepEq("Resulting data does not match", expected, actual);
      });
    }

    Task TestDocumentSnapshotDoubleIncrementBehavior() {
      return Async(() => {
        DocumentReference doc = TestDocument();

        var data = TestData();
        Await(doc.SetAsync(data));

        var incrementValue = FieldValue.Increment(1.5);

        var updateData = new Dictionary<string, object>{
          {"metadata.createdAt", incrementValue }
        };

        Await(doc.UpdateAsync(updateData));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        var expected = TestData();
        ((Dictionary<string, object>)expected["metadata"])["createdAt"] = 2.5;

        var actual = snap.ToDictionary();
        AssertDeepEq("Resulting data does not match", expected, actual);
      });
    }

    private void TestDocumentSnapshotGetValue(DocumentSnapshot snap, Dictionary<string, object> data) {
      AssertEq(data["name"], snap.GetValue<string>("name"));
      AssertDeepEq("Resulting data.metadata does not match.",
          data["metadata"],
          snap.GetValue<Dictionary<string, object>>("metadata"));
      AssertEq("deep-field-1", snap.GetValue<string>("metadata.deep.field"));
      AssertEq("deep-field-1", snap.GetValue<string>(new FieldPath("metadata", "deep", "field")));
      // Nonexistent field.
      AssertException(typeof(InvalidOperationException),
          () => snap.GetValue<object>("nonexistent"));
      // Existent field deserialized to wrong type.
      AssertException(typeof(ArgumentException),
          () => snap.GetValue<long>("name"));
    }

    private void TestDocumentSnapshotTryGetValue(DocumentSnapshot snap, Dictionary<string, object> data) {
      // Existent field.
      String name;
      Assert("Got value", snap.TryGetValue<string>("name", out name));
      AssertEq(data["name"], name);

      // Nonexistent field.
      Assert("Should not have got value", !snap.TryGetValue<string>("namex", out name));
      AssertEq(null, name);

      // Existent field deserialized to wrong type.
      AssertException(typeof(ArgumentException),
          () => {
            long l;
            snap.TryGetValue<long>("name", out l);
          });
    }

    private void TestDocumentSnapshotContainsField(DocumentSnapshot snap, Dictionary<string, object> data) {
      // Existent fields.
      Assert("Field should exist", snap.ContainsField("name"));
      Assert("Field should exist", snap.ContainsField("metadata.deep.field"));
      Assert("Field should exist", snap.ContainsField(new FieldPath("metadata", "deep", "field")));

      // Nonexistent field.
      Assert("Field should not exist", !snap.ContainsField("namex"));
    }

    Task TestSnapshotMetadataEqualsAndGetHashCode() {
      return Async(() => {
        var meta1 = new SnapshotMetadata(false, false);
        var meta2 = new SnapshotMetadata(false, true);
        var meta3 = new SnapshotMetadata(true, false);
        var meta4 = new SnapshotMetadata(true, true);
        var meta5 = new SnapshotMetadata(false, true);

        AssertEq(meta1.Equals(meta1), true);
        AssertEq(meta2.Equals(meta5), true);
        AssertEq(meta1.Equals(meta2), false);
        AssertEq(meta1.Equals(meta3), false);
        AssertEq(meta1.Equals(meta4), false);
        AssertEq(meta2.Equals(meta3), false);
        AssertEq(meta2.Equals(meta4), false);
        AssertEq(meta3.Equals(meta4), false);
        AssertEq(meta1.Equals(null), false);
        AssertEq(meta1.Equals("string"), false);

        AssertEq(meta1.GetHashCode() == meta1.GetHashCode(), true);
        AssertEq(meta2.GetHashCode() == meta5.GetHashCode(), true);
        AssertEq(meta1.GetHashCode() == meta2.GetHashCode(), false);
        AssertEq(meta1.GetHashCode() == meta3.GetHashCode(), false);
        AssertEq(meta1.GetHashCode() == meta4.GetHashCode(), false);
        AssertEq(meta2.GetHashCode() == meta3.GetHashCode(), false);
        AssertEq(meta2.GetHashCode() == meta4.GetHashCode(), false);
        AssertEq(meta3.GetHashCode() == meta4.GetHashCode(), false);
      });
    }

    /*
    Anonymous authentication must be enabled for TestAuthIntegration to pass.

    Also, the following security rules are required for TestAuthIntegrationt to pass:

    rules_version='2'
    service cloud.firestore {
      match /databases/{database}/documents {
        match /{somePath=**}/{collection}/{document} {
          allow read, write: if collection != 'private';
        }
        match /private/{document=**} {
          allow read, write: if request.auth != null;
        }
      }
    }
    */
    Task TestAuthIntegration() {
      return Async(() => {
        var firebaseAuth = Firebase.Auth.FirebaseAuth.DefaultInstance;
        var data = TestData();

        firebaseAuth.SignOut();

        DocumentReference doc = db.Collection("private").Document();
        // TODO(mikelehen): Check for permission_denied once we plumb errors through somehow.
        AssertException(typeof(Exception), () => Await(doc.SetAsync(data)));

        Await(firebaseAuth.SignInAnonymouslyAsync());

        // Write should now succeed.
        Await(doc.SetAsync(data));

        firebaseAuth.SignOut();

        // TODO(b/138616555): We shouldn't need this sleep and extra SignOut(), but it seems like
        // Auth sometimes sends us an extra event.
        System.Threading.Thread.Sleep(1000);
        firebaseAuth.SignOut();
      });
    }

    Task TestDocumentListen() {
      return Async(() => {
        var doc = TestDocument();
        var initialData = TestData(1);
        var newData = TestData(2);

        Await(doc.SetAsync(initialData));

        var accumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        var registration = doc.Listen(accumulator.Listener);

        // Ensure that the Task from the ListenerRegistration is in the correct state.
        AssertTaskIsPending(registration.ListenerTask);

        // Wait for the first snapshot.
        DocumentSnapshot snapshot = accumulator.Await();
        AssertDeepEq("Initial snapshot matches", snapshot.ToDictionary(), initialData);
        AssertEq(snapshot.Metadata.IsFromCache, true);
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Write new data and wait for the resulting snapshot.
        doc.SetAsync(newData);
        snapshot = accumulator.Await();
        AssertDeepEq("Second snapshot matches written data",
            snapshot.ToDictionary(), newData);
        AssertEq(snapshot.Metadata.HasPendingWrites, true);

        // Remove the listener and make sure we don't get events anymore.
        accumulator.ThrowOnAnyEvent();
        registration.Stop();
        AssertTaskSucceeds(registration.ListenerTask);
        Await(doc.SetAsync(TestData(3)));

        // Ensure that the Task from the ListenerRegistration correctly fails with an error.
        var docWithInvalidName = TestCollection().Document("__badpath__");
        var callbackInvoked = false;
        var registration2 = docWithInvalidName.Listen(snap => { callbackInvoked = true; });
        AssertTaskFaults(registration2.ListenerTask, FirestoreError.InvalidArgument, "__badpath__");
        registration2.Stop();
        System.Threading.Thread.Sleep(50);
        Assert("The callback should not have been invoked", !callbackInvoked);
      });
    }

    Task TestDocumentListenWithMetadataChanges() {
      return Async(() => {
        var doc = TestDocument();
        var initialData = TestData(1);
        var newData = TestData(2);

        Await(doc.SetAsync(initialData));

        var accumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        var registration = doc.Listen(MetadataChanges.Include, accumulator.Listener);

        // Ensure that the Task from the ListenerRegistration is in the correct state.
        AssertTaskIsPending(registration.ListenerTask);

        // Wait for the first snapshot.
        DocumentSnapshot snapshot = accumulator.Await();
        AssertDeepEq("Initial snapshot matches", snapshot.ToDictionary(), initialData);
        AssertEq(snapshot.Metadata.IsFromCache, true);
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Wait for new snapshot once we're synced with the backend.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.IsFromCache, false);

        // Write new data and wait for the resulting snapshot.
        doc.SetAsync(newData);
        snapshot = accumulator.Await();
        AssertDeepEq("Second snapshot matches written data",
            snapshot.ToDictionary(), newData);
        AssertEq(snapshot.Metadata.HasPendingWrites, true);

        // Wait for new snapshot once write completes.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Remove the listener and make sure we don't get events anymore.
        accumulator.ThrowOnAnyEvent();
        registration.Stop();
        AssertTaskSucceeds(registration.ListenerTask);
        Await(doc.SetAsync(TestData(3)));

        // Ensure that the Task from the ListenerRegistration correctly fails with an error.
        var docWithInvalidName = TestCollection().Document("__badpath__");
        var callbackInvoked = false;
        var registration2 =
            docWithInvalidName.Listen(MetadataChanges.Include, snap => { callbackInvoked = true; });
        AssertTaskFaults(registration2.ListenerTask, FirestoreError.InvalidArgument, "__badpath__");
        registration2.Stop();
        System.Threading.Thread.Sleep(50);
        Assert("The callback should not have been invoked", !callbackInvoked);
      });
    }

    Task TestQueryListen() {
      return Async(() => {
        var collection = TestCollection();
        var firstDoc = TestData(1);
        var secondDoc = TestData(2);

        Await(collection.Document("a").SetAsync(firstDoc));

        var accumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        var registration = collection.Listen(accumulator.Listener);

        // Ensure that the Task from the ListenerRegistration is in the correct state.
        AssertTaskIsPending(registration.ListenerTask);

        // Wait for the first snapshot.
        QuerySnapshot snapshot = accumulator.Await();
        AssertEq("Expected one document", 1, snapshot.Count);
        AssertDeepEq("First doc matches", snapshot[0].ToDictionary(), firstDoc);
        AssertEq(snapshot.Metadata.IsFromCache, true);
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Write a new document and wait for the resulting snapshot.
        collection.Document("b").SetAsync(secondDoc);
        snapshot = accumulator.Await();
        AssertEq("Expected two documents.", 2, snapshot.Count);
        AssertDeepEq("First doc matches", snapshot[0].ToDictionary(), firstDoc);
        AssertDeepEq("Second doc matches", snapshot[1].ToDictionary(), secondDoc);
        AssertEq(snapshot.Metadata.HasPendingWrites, true);

        // Remove the listener and make sure we don't get events anymore.
        accumulator.ThrowOnAnyEvent();
        registration.Stop();
        AssertTaskSucceeds(registration.ListenerTask);
        Await(collection.Document("c").SetAsync(TestData(3)));

        // Ensure that the Task from the ListenerRegistration correctly fails with an error.
        var collectionWithInvalidName = TestCollection().Document("__badpath__").Collection("sub");
        var callbackInvoked = false;
        var registration2 = collectionWithInvalidName.Listen(snap => { callbackInvoked = true; });
        AssertTaskFaults(registration2.ListenerTask, FirestoreError.InvalidArgument, "__badpath__");
        registration2.Stop();
        System.Threading.Thread.Sleep(50);
        Assert("The callback should not have been invoked", !callbackInvoked);
      });
    }

    Task TestQueryListenWithMetadataChanges() {
      return Async(() => {
        var collection = TestCollection();

        var firstDoc = TestData(1);
        var secondDoc = TestData(2);

        Await(collection.Document("a").SetAsync(firstDoc));

        var accumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        var registration = collection.Listen(MetadataChanges.Include, accumulator.Listener);

        // Ensure that the Task from the ListenerRegistration is in the correct state.
        AssertTaskIsPending(registration.ListenerTask);

        // Wait for the first snapshot.
        QuerySnapshot snapshot = accumulator.Await();
        AssertEq("Expected one document", 1, snapshot.Count);
        AssertDeepEq("First doc matches", snapshot[0].ToDictionary(), firstDoc);
        AssertEq(snapshot.Metadata.IsFromCache, true);
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Wait for new snapshot once we're synced with the backend.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.IsFromCache, false);

        // Write a new document and wait for the resulting snapshot.
        collection.Document("b").SetAsync(secondDoc);
        snapshot = accumulator.Await();
        AssertEq("Expected two documents.", 2, snapshot.Count);
        AssertDeepEq("First doc matches", snapshot[0].ToDictionary(), firstDoc);
        AssertDeepEq("Second doc matches", snapshot[1].ToDictionary(), secondDoc);
        AssertEq(snapshot.Metadata.HasPendingWrites, true);

        // Wait for new snapshot once write completes.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Remove the listener and make sure we don't get events anymore.
        accumulator.ThrowOnAnyEvent();
        registration.Stop();
        AssertTaskSucceeds(registration.ListenerTask);
        Await(collection.Document("c").SetAsync(TestData(3)));

        // Ensure that the Task from the ListenerRegistration correctly fails with an error.
        var collectionWithInvalidName = TestCollection().Document("__badpath__").Collection("sub");
        var callbackInvoked = false;
        var registration2 = collectionWithInvalidName.Listen(MetadataChanges.Include,
                                                             snap => { callbackInvoked = true; });
        AssertTaskFaults(registration2.ListenerTask, FirestoreError.InvalidArgument, "__badpath__");
        registration2.Stop();
        System.Threading.Thread.Sleep(50);
        Assert("The callback should not have been invoked", !callbackInvoked);
      });
    }

    Task TestQuerySnapshotChanges() {
      return Async(() => {
        var collection = TestCollection();

        var initialData = TestData(1);
        var updatedData = TestData(2);

        Await(collection.Document("a").SetAsync(initialData));

        var accumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        var registration = collection.Listen(MetadataChanges.Include, accumulator.Listener);

        // Wait for the first snapshot.
        QuerySnapshot snapshot = accumulator.Await();
        AssertEq("Expected one document", 1, snapshot.Count);
        AssertDeepEq("First snapshot matches initial data", snapshot[0].ToDictionary(), initialData);
        AssertDeepEq("Can be converted to an array", snapshot.ToArray()[0].ToDictionary(), initialData);
        AssertEq(snapshot.Metadata.IsFromCache, true);
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        // Wait for new snapshot once we're synced with the backend.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.IsFromCache, false);

        // Update the document and wait for the resulting snapshot.
        collection.Document("a").SetAsync(updatedData);
        snapshot = accumulator.Await();
        AssertDeepEq("New snapshot contains updated data", snapshot[0].ToDictionary(), updatedData);
        AssertEq(snapshot.Metadata.HasPendingWrites, true);

        // Wait for snapshot indicating the write has completed.
        snapshot = accumulator.Await();
        AssertEq(snapshot.Metadata.HasPendingWrites, false);

        var changes = snapshot.GetChanges();
        AssertEq("Expected zero documents.", 0, changes.Count());

        var changesIncludingMetadata = snapshot.GetChanges(MetadataChanges.Include);
        AssertEq("Expected one document.", 1, changesIncludingMetadata.Count());

        var changedDocument = changesIncludingMetadata.First().Document;
        AssertEq(changedDocument.Metadata.HasPendingWrites, false);

        // Remove the listener and make sure we don't get events anymore.
        accumulator.ThrowOnAnyEvent();
        registration.Stop();
        Await(collection.Document("c").SetAsync(TestData(3)));
      });
    }

    Task TestQueries() {
      return Async(() => {
        // Initialize collection with a few test documents to query against.
        var c = TestCollection();
        Await(c.Document("a").SetAsync(new Dictionary<string, object> {
          { "num", 1 },
          { "state", "created" },
          { "active", true },
          { "nullable", "value" },
        }));
        Await(c.Document("b").SetAsync(new Dictionary<string, object> {
          { "num", 2 },
          { "state", "done" },
          { "active", false },
          { "nullable", null },
        }));
        Await(c.Document("c").SetAsync(new Dictionary<string, object> {
          { "num", 3 },
          { "state", "done" },
          { "active", true },
          { "nullable", null },
        }));
        // Put in a nested collection (with same ID) for testing collection group queries.
        Await(c.Document("d")
                  .Collection(c.Id)
                  .Document("d-nested")
                  .SetAsync(new Dictionary<string, object> {
                    { "num", 4 },
                    { "state", "created" },
                    { "active", false },
                    { "nullable", null },
                  }));

        AssertQueryResults(
            desc: "EqualTo",
            query: c.WhereEqualTo("num", 1),
            docIds: AsList("a"));
        AssertQueryResults(
            desc: "EqualTo (FieldPath)",
            query: c.WhereEqualTo(new FieldPath("num"), 1),
            docIds: AsList("a"));

        AssertQueryResults(desc: "NotEqualTo", query: c.WhereNotEqualTo("num", 1),
                           docIds: AsList("b", "c"));
        AssertQueryResults(desc: "NotEqualTo (FieldPath)",
                           query: c.WhereNotEqualTo(new FieldPath("num"), 1),
                           docIds: AsList("b", "c"));
        AssertQueryResults(desc: "NotEqualTo (FieldPath) on nullable",
                           query: c.WhereNotEqualTo(new FieldPath("nullable"), null),
                           docIds: AsList("a"));

        AssertQueryResults(desc: "LessThanOrEqualTo", query: c.WhereLessThanOrEqualTo("num", 2),
                           docIds: AsList("a", "b"));
        AssertQueryResults(
            desc: "LessThanOrEqualTo (FieldPath)",
            query: c.WhereLessThanOrEqualTo(new FieldPath("num"), 2),
            docIds: AsList("a", "b"));

        AssertQueryResults(
            desc: "LessThan",
            query: c.WhereLessThan("num", 2),
            docIds: AsList("a"));
        AssertQueryResults(
            desc: "LessThan (FieldPath)",
            query: c.WhereLessThan(new FieldPath("num"), 2),
            docIds: AsList("a"));

        AssertQueryResults(
            desc: "GreaterThanOrEqualTo",
            query: c.WhereGreaterThanOrEqualTo("num", 2),
            docIds: AsList("b", "c"));
        AssertQueryResults(
            desc: "GreaterThanOrEqualTo (FieldPath)",
            query: c.WhereGreaterThanOrEqualTo(new FieldPath("num"), 2),
            docIds: AsList("b", "c"));

        AssertQueryResults(
            desc: "GreaterThan",
            query: c.WhereGreaterThan("num", 2),
            docIds: AsList("c"));
        AssertQueryResults(
            desc: "GreaterThan (FieldPath)",
            query: c.WhereGreaterThan(new FieldPath("num"), 2),
            docIds: AsList("c"));

        AssertQueryResults(
            desc: "two EqualTos",
            query: c.WhereEqualTo("state", "done").WhereEqualTo("active", false),
            docIds: AsList("b"));

        AssertQueryResults(
            desc: "OrderBy, Limit",
            query: c.OrderBy("num").Limit(2),
            docIds: AsList("a", "b"));
        AssertQueryResults(
            desc: "OrderBy, Limit (FieldPath)",
            query: c.OrderBy(new FieldPath("num")).Limit(2),
            docIds: AsList("a", "b"));

        AssertQueryResults(
            desc: "OrderByDescending, Limit",
            query: c.OrderByDescending("num").Limit(2),
            docIds: AsList("c", "b"));
        AssertQueryResults(
            desc: "OrderByDescending, Limit (FieldPath)",
            query: c.OrderByDescending(new FieldPath("num")).Limit(2),
            docIds: AsList("c", "b"));

        AssertQueryResults(
            desc: "StartAfter",
            query: c.OrderBy("num").StartAfter(2),
            docIds: AsList("c"));
        AssertQueryResults(
            desc: "EndBefore",
            query: c.OrderBy("num").EndBefore(2),
            docIds: AsList("a"));
        AssertQueryResults(
            desc: "StartAt, EndAt",
            query: c.OrderBy("num").StartAt(2).EndAt(2),
            docIds: AsList("b"));

        // Collection Group Query
        AssertQueryResults(
          desc: "CollectionGroup",
          query: db.CollectionGroup(c.Id),
          docIds: AsList("a", "b", "c", "d-nested")
        );
      });
    }

    Task TestLimitToLast() {
      return Async(() => {
        var c = TestCollection();
        // TODO(b/149105903): Uncomment this line when exception can be raised from SWIG and below.
        // AssertException(typeof(Exception), () => Await(c.LimitToLast(2).GetSnapshotAsync()));

        // Initialize data with a few test documents to query against.
        Await(c.Document("a").SetAsync(new Dictionary<string, object> {
            {"k", "a"},
            {"sort", 0},
        }));
        Await(c.Document("b").SetAsync(new Dictionary<string, object> {
            {"k", "b"},
            {"sort", 1},
        }));
        Await(c.Document("c").SetAsync(new Dictionary<string, object> {
            {"k", "c"},
            {"sort", 1},
        }));
        Await(c.Document("d").SetAsync(new Dictionary<string, object> {
            {"k", "d"},
            {"sort", 2},
        }));

        // Setup `limit` query.
        var limit = c.Limit(2).OrderBy("sort");
        var limitAccumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        var limitReg = limit.Listen(limitAccumulator.Listener);

        // Setup mirroring `limitToLast` query.
        var limitToLast = c.LimitToLast(2).OrderByDescending("sort");
        var limitToLastAccumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        var limitToLastReg = limitToLast.Listen(limitToLastAccumulator.Listener);

        // Verify both query get expected result.
        var data = QuerySnapshotToValues(limitAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "a"}, { "sort", 0L}},
                             new Dictionary<string, object>{ {"k", "b"}, { "sort", 1L}}
                           });
        data = QuerySnapshotToValues(limitToLastAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "b"}, { "sort", 1L}},
                             new Dictionary<string, object>{ {"k", "a"}, { "sort", 0L}}
                           });

        // Unlisten then re-listen limit query.
        limitReg.Stop();
        limit.Listen(limitAccumulator.Listener);

        // Verify `limit` query still works.
        data = QuerySnapshotToValues(limitAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "a"}, { "sort", 0L}},
                             new Dictionary<string, object>{ {"k", "b"}, { "sort", 1L}}
                           });

        // Add a document that would change the result set.
        Await(c.Document("d").SetAsync(new Dictionary<string, object> {
            {"k", "e"},
            {"sort", -1},
        }));

        // Verify both query get expected result.
        data = QuerySnapshotToValues(limitAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "e"}, { "sort", -1L}},
                             new Dictionary<string, object>{ {"k", "a"}, { "sort", 0L}}
                           });
        data = QuerySnapshotToValues(limitToLastAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "a"}, { "sort", 0L}},
                             new Dictionary<string, object>{ {"k", "e"}, { "sort", -1L}}
                           });

        // Unlisten to limitToLast, update a doc, then relisten to limitToLast
        limitToLastReg.Stop();
        Await(c.Document("a").UpdateAsync("sort", -2));
        limitToLast.Listen(limitToLastAccumulator.Listener);

        // Verify both query get expected result.
        data = QuerySnapshotToValues(limitAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "a"}, { "sort", -2L}},
                             new Dictionary<string, object>{ {"k", "e"}, { "sort", -1L}}
                           });
        data = QuerySnapshotToValues(limitToLastAccumulator.Await());
        AssertDeepEq(data, new List<Dictionary<string, object>>
                           { new Dictionary<string, object>{ {"k", "e"}, { "sort", -1L}},
                             new Dictionary<string, object>{ {"k", "a"}, { "sort", -2L}}
                           });
      });
    }

    Task TestArrayContains() {
      return Async(() => {
        // Initialize collection with a few test documents to query against.
        var c = TestCollection();
        Await(c.Document("a").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> { 42 }},
        }));

        Await(c.Document("b").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {"a", 42, "c"}},
        }));

        Await(c.Document("c").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {
              41.999,
              "42",
              new Dictionary<string, object> {
                {"array", new List<object> { 42 }}}}}}));

        Await(c.Document("d").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {42}},
            {"array2", new List<object> {"bingo"}},
        }));

        AssertQueryResults(
            desc: "ArrayContains",
            query: c.WhereArrayContains(new FieldPath("array"), 42),
            docIds: AsList("a", "b", "d"));
        AssertQueryResults(
            desc: "ArrayContains",
            query: c.WhereArrayContains("array", 42),
            docIds: AsList("a", "b", "d"));
      });
    }

    Task TestArrayContainsAny() {
      return Async(() => {
        // Initialize collection with a few test documents to query against.
        var c = TestCollection();
        Await(c.Document("a").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> { 42 }},
        }));

        Await(c.Document("b").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {"a", 42, "c"}},
        }));

        Await(c.Document("c").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {
              41.999,
              "42",
              new Dictionary<string, object> {
                {"array", new List<object> { 42 }}}}}}));

        Await(c.Document("d").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {42}},
            {"array2", new List<object> {"bingo"}},
        }));

        Await(c.Document("e").SetAsync(new Dictionary<string, object> {
            {"array", new List<object> {43}}
        }));

        Await(c.Document("f").SetAsync(new Dictionary<string, object> {
            {"array",
                new List<object> {
                  new Dictionary<string, object> {
                    {"a", 42}}}}}));

        Await(c.Document("g").SetAsync(new Dictionary<string, object> {
            {"array", 42},
        }));

        AssertQueryResults(
            desc: "ArrayContainsAny",
            query: c.WhereArrayContainsAny("array", new List<object> { 42, 43 }),
            docIds: AsList("a", "b", "d", "e"));

        AssertQueryResults(
            desc: "ArrayContainsAnyObject",
            query: c.WhereArrayContainsAny(
                new FieldPath("array"),
                new List<object> {
                  new Dictionary<string, object> {
                    {"a", 42}} }),
            docIds: AsList("f"));
      });
    }

    Task TestInQueries() {
      return Async(() => {
        // Initialize collection with a few test documents to query against.
        var c = TestCollection();
        Await(c.Document("a").SetAsync(new Dictionary<string, object> {
          { "zip", 98101 },
          { "nullable", null },
        }));
        Await(c.Document("b").SetAsync(new Dictionary<string, object> {
          { "zip", 98102 },
          { "nullable", "value" },
        }));
        Await(c.Document("c").SetAsync(new Dictionary<string, object> {
          { "zip", 98103 },
          { "nullable", null },
        }));
        Await(c.Document("d").SetAsync(new Dictionary<string, object> {
            {"zip", new List<object>{98101} }
        }));
        Await(c.Document("e").SetAsync(new Dictionary<string, object> {
            {"zip", new List<object>{"98101", new Dictionary<string, object> {{"zip", 98101} } } }
        }));
        Await(c.Document("f").SetAsync(new Dictionary<string, object> {
          { "zip", new Dictionary<string, object> { { "code", 500 } } },
          { "nullable", 123 },
        }));
        Await(c.Document("g").SetAsync(new Dictionary<string, object> {
          { "zip", new List<object> { 98101, 98102 } },
          { "nullable", null },
        }));

        AssertQueryResults(
            desc: "InQuery",
            query: c.WhereIn("zip",
                             new List<object> { 98101, 98103, new List<object> { 98101, 98102 } }),
            docIds: AsList("a", "c", "g"));

        AssertQueryResults(
            desc: "InQueryWithObject",
            query: c.WhereIn(new FieldPath("zip"),
                             new object[] { new Dictionary<string, object> { { "code", 500 } } }),
            docIds: AsList("f"));

        AssertQueryResults(
            desc: "InQueryWithDocIds",
            query: c.WhereIn(FieldPath.DocumentId,
                             new object[] { "c", "e" }),
            docIds: AsList("c", "e"));

        AssertQueryResults(
            desc: "NotInQuery",
            query: c.WhereNotIn(
                "zip", new List<object> { 98101, 98103, new List<object> { 98101, 98102 } }),
            docIds: AsList("b", "d", "e", "f"));

        AssertQueryResults(
            desc: "NotInQueryWithObject",
            query: c.WhereNotIn(
                new FieldPath("zip"),
                new object[] { new List<object> { 98101, 98102 },
                               new Dictionary<string, object> { { "code", 500 } } }),
            docIds: AsList("a", "b", "c", "d", "e"));

        AssertQueryResults(
            desc: "NotInQueryWithDocIds",
            query: c.WhereNotIn(FieldPath.DocumentId, new object[] { "a", "c", "e" }),
            docIds: AsList("b", "d", "f", "g"));

        AssertQueryResults(
            desc: "NotInQueryWithNulls",
            query: c.WhereNotIn(new FieldPath("nullable"), new object[] { null }),
            docIds: new List<String> {});
      });
    }

    Task TestDefaultInstanceStable() {
      return Async(() => {
        FirebaseFirestore db1 = FirebaseFirestore.DefaultInstance;
        FirebaseFirestore db2 = FirebaseFirestore.DefaultInstance;
        AssertEq("FirebaseFirestore.DefaultInstance's not stable", db1, db2);
      });
    }

    // Tests that DocumentReference and Query respect the Source parameter passed to
    // GetSnapshotAsync().  We don't exhaustively test the behavior. We just do enough
    // checks to verify that cache, default, and server produce distinct results.
    Task TestGetAsyncSource() {
      return Async(() => {
        DocumentReference doc = TestDocument();

        Await(doc.SetAsync(TestData()));

        // Verify FromCache gives us cached results even when online.
        DocumentSnapshot docSnap = Await(doc.GetSnapshotAsync(Source.Cache));
        AssertEq(docSnap.Metadata.IsFromCache, true);
        QuerySnapshot querySnap = Await(doc.Parent.GetSnapshotAsync(Source.Cache));
        AssertEq(querySnap.Metadata.IsFromCache, true);

        // Verify Default gives us non-cached results when online.
        docSnap = Await(doc.GetSnapshotAsync(Source.Default));
        AssertEq(docSnap.Metadata.IsFromCache, false);
        querySnap = Await(doc.Parent.GetSnapshotAsync(Source.Default));
        AssertEq(querySnap.Metadata.IsFromCache, false);

        bool cleanup = true;
        try {
          // Disable network so we can test offline behavior.
          Await(doc.Firestore.DisableNetworkAsync());

          // Verify Default gives us cached results when offline.
          docSnap = Await(doc.GetSnapshotAsync(Source.Default));
          AssertEq(docSnap.Metadata.IsFromCache, true);
          querySnap = Await(doc.Parent.GetSnapshotAsync(Source.Default));
          AssertEq(querySnap.Metadata.IsFromCache, true);

          // Verify Server gives us an error when offline even if there's data in cache..
          // TODO(mikelehen): Check for unavailable error once we plumb errors through somehow.
          AssertException(typeof(Exception), () => Await(doc.GetSnapshotAsync(Source.Server)));
          AssertException(typeof(Exception), () => Await(doc.Parent.GetSnapshotAsync(
              Source.Server)));
        } catch (ThreadAbortException) {
          // Don't try to do anything else on this thread if it was aborted, or else it will hang.
          cleanup = false;
        } finally {
          if (cleanup) {
            Await(doc.Firestore.EnableNetworkAsync());
          }
        }
      });
    }

    Task TestWaitForPendingWrites() {
      return Async(() => {
        DocumentReference doc = TestDocument();

        Await(doc.Firestore.DisableNetworkAsync());

        // Returns immediately because there is no pending writes.
        var pendingWrites1 = doc.Firestore.WaitForPendingWritesAsync();
        doc.SetAsync(new Dictionary<string, object> {
            {"zip", 98101}
        });
        var pendingWrites2 = doc.Firestore.WaitForPendingWritesAsync();

        // `pendingWrites1` completes immediately because there are no pending writes at
        // the time it is created.
        AssertTaskSucceeds(pendingWrites1);
        AssertTaskIsPending(pendingWrites2);

        Await(doc.Firestore.EnableNetworkAsync());
        AssertTaskSucceeds(pendingWrites2);
      });
    }

    // TODO(wuandy): Add a test to verify WaitForPendingWritesAsync task fails in user change.
    // It requires to create underlying firestore instance with
    // a MockCredentialProvider first.

    Task TestTerminate() {
      return Async(() => {
        var db1 = NonDefaultFirestore("TestTerminate");
        FirebaseApp app = db1.App;
        var doc = db1.Document("ColA/DocA/ColB/DocB");
        var doc2 = db1.Document("ColA/DocA/ColB/DocC");
        var collection = doc.Parent;
        var writeBatch = db1.StartBatch();
        var accumulator = new EventAccumulator<DocumentSnapshot>(MainThreadId, FailTest);
        var registration = doc.Listen(accumulator.Listener);

        // Multiple calls to terminate should go through.
        AssertTaskSucceeds(db1.TerminateAsync());
        AssertTaskSucceeds(db1.TerminateAsync());

        // Can call registration.Stop multiple times even after termination.
        registration.Stop();
        registration.Stop();

        var taskCompletionSource = new TaskCompletionSource<string>();
        taskCompletionSource.SetException(new InvalidOperationException("forced exception"));
        Task sampleUntypedTask = taskCompletionSource.Task;
        Task<string> sampleTypedTask = taskCompletionSource.Task;

        // Verify that the `App` property is still valid after `TerminateAsync()`.
        Assert("App property should not have changed", db1.App == app);

        // Verify that synchronous methods in `FirebaseFirestore` still return values after
        // `TerminateAsync()`.
        AssertNotNull("Collection() returned null", db1.Collection("a"));
        AssertNotNull("CollectionGroup() returned null", db1.CollectionGroup("c"));
        AssertNotNull("Document() returned null", db1.Document("a/b"));
        AssertNotNull("StartBatch() returned null", db1.StartBatch());

        // Verify that adding a listener via `ListenForSnapshotsInSync()` is not notified
        // after `TerminateAsync()`.
        bool snapshotsInSyncListenerInvoked = false;
        Action snapshotsInSyncListener = () => { snapshotsInSyncListenerInvoked = true; };
#if UNITY_ANDROID
        AssertException(typeof(InvalidOperationException),
                        () => db1.ListenForSnapshotsInSync(snapshotsInSyncListener));
#else
        // TODO(b/201438171) `ListenForSnapshotsInSync()` should throw `InvalidOperationException`,
        // like it does on Android and like `CollectionReference.Listen()` and
        // `DocumentReference.Listen()` do.
        db1.ListenForSnapshotsInSync(snapshotsInSyncListener);
#endif

        // Verify that all non-static methods in `FirebaseFirestore` that start a `Task`, other
        // than `ClearPersistenceAsync()` fail.
        AssertTaskFaults(typeof(InvalidOperationException),
                         db1.RunTransactionAsync(transaction => sampleUntypedTask));
        AssertTaskFaults(typeof(InvalidOperationException),
                         db1.RunTransactionAsync<string>(transaction => sampleTypedTask));
        // TODO(b/201098348) Change `AssertException()` to `AssertTaskFaults()` for the 5 bundle
        // loading methods below, since they should be consistent with the rest of the methods.
        AssertException(typeof(InvalidOperationException), () => db1.LoadBundleAsync("BundleData"));
        AssertException(typeof(InvalidOperationException),
                        () => db1.LoadBundleAsync("BundleData", (sender, progress) => {}));
        AssertException(typeof(InvalidOperationException), () => db1.LoadBundleAsync(new byte[0]));
        AssertException(typeof(InvalidOperationException),
                        () => db1.LoadBundleAsync(new byte[0], (sender, progress) => {}));
        AssertException(typeof(InvalidOperationException),
                        () => db1.GetNamedQueryAsync("QueryName"));
        AssertTaskFaults(typeof(InvalidOperationException), db1.DisableNetworkAsync());
        AssertTaskFaults(typeof(InvalidOperationException), db1.EnableNetworkAsync());
        AssertTaskFaults(typeof(InvalidOperationException), db1.WaitForPendingWritesAsync());

        // Verify the behavior of methods in `CollectionReference` after `TerminateAsync()`.
        Assert("collection.Firestore is not correct", collection.Firestore == db1);
        AssertEq(collection.Id, "ColB");
        AssertEq(collection.Path, "ColA/DocA/ColB");
        bool collectionListenerInvoked = false;
        Action<QuerySnapshot> collectionListener = snap => { collectionListenerInvoked = true; };
        AssertException(typeof(InvalidOperationException),
                        () => collection.Listen(collectionListener));
        AssertNotNull("Collection.Document() returned null", collection.Document());
        AssertNotNull("Collection.Document(string) returned null", collection.Document("abc"));
        AssertTaskFaults(typeof(InvalidOperationException), collection.AddAsync(TestData(1)));
        // TODO(b/201438328) Change `AssertException()` to `AssertTaskFaults()` for
        // `GetSnapshotAsync()`, since it should be consistent with other methods.
        AssertException(typeof(InvalidOperationException),
                        () => collection.GetSnapshotAsync(Source.Default));

        // Verify the behavior of methods in `DocumentReference` after `TerminateAsync()`.
        Assert("doc.Firestore is not correct", doc.Firestore == db1);
        AssertEq(doc.Id, "DocB");
        AssertEq(doc.Path, "ColA/DocA/ColB/DocB");
        AssertEq(doc.Parent, collection);
        bool docListenerInvoked = false;
        Action<DocumentSnapshot> docListener = snap => { docListenerInvoked = true; };
        AssertException(typeof(InvalidOperationException), () => doc.Listen(docListener));
        AssertNotNull("DocumentReference.Collection() returned null", doc.Collection("zzz"));
        AssertTaskFaults(typeof(InvalidOperationException), doc.DeleteAsync());
        AssertTaskFaults(typeof(InvalidOperationException), doc.SetAsync(TestData(1)));
        AssertTaskFaults(typeof(InvalidOperationException), doc.UpdateAsync("life", 42));
        // TODO(b/201438328) Change `AssertException()` to `AssertTaskFaults()` for
        // `GetSnapshotAsync()`, since it should be consistent with other methods.
        AssertException(typeof(InvalidOperationException),
                        () => doc.GetSnapshotAsync(Source.Default));

        // Verify the behavior of methods in `WriteBatch` after `TerminateAsync()`.
        writeBatch.Delete(doc);
        writeBatch.Set(doc2, TestData(1), null);
        AssertTaskFaults(typeof(InvalidOperationException), writeBatch.CommitAsync());

        // Verify that the listeners were not notified. Do it here instead of above to allow
        // some time to pass for unexpected notifications to be received.
        Assert("The listener registered with FirebaseFirestore.ListenForSnapshotsInSync() " +
                   "should not be notified after TerminateAsync()",
               !snapshotsInSyncListenerInvoked);
        Assert("The listener registered with CollectionReference.Listen() " +
                   "should not be notified after TerminateAsync()",
               !collectionListenerInvoked);
        Assert("The listener registered with DocumentReference.Listen() " +
                   "should not be notified after TerminateAsync()",
               !docListenerInvoked);

        // Create a new functional instance.
        var db2 = FirebaseFirestore.GetInstance(app);
        Assert("Should create a new instance.", db1 != db2);
        AssertTaskSucceeds(db2.DisableNetworkAsync());
        AssertTaskSucceeds(db2.EnableNetworkAsync());

        app.Dispose();
      });
    }

    Task TestClearPersistence() {
      return Async(() => {
        var defaultOptions = TestCollection().Firestore.App.Options;
        string path;

        // Verify that ClearPersistenceAsync() succeeds when invoked on a newly-created
        // FirebaseFirestore instance.
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          AssertTaskSucceeds(db.ClearPersistenceAsync());
          app.Dispose();
        }

        // Create a document to use to verify the behavior of ClearPersistenceAsync().
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          var docContents = new Dictionary<string, object> { { "foo", 42 } };

          var doc = db.Collection("TestCollection").Document();
          path = doc.Path;
          // It is not necessary to Await on the Task returned from SetAsync() below. This is
          // because the document has already been written to persistence by the time that
          // SetAsync() returns.
          doc.SetAsync(docContents);
          AssertTaskSucceeds(db.TerminateAsync());
          app.Dispose();
        }

        // As a sanity check, verify that the document created in the previous block exists.
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          var doc = db.Document(path);
          AssertTaskSucceeds(doc.GetSnapshotAsync(Source.Cache));
          app.Dispose();
        }

        // Call ClearPersistenceAsync() after TerminateAsync().
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          AssertTaskSucceeds(db.TerminateAsync());
          AssertTaskSucceeds(db.ClearPersistenceAsync());
          app.Dispose();
        }

        // Verify that ClearPersistenceAsync() deleted the document that was created above.
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          var doc = db.Document(path);
          AssertTaskFaults(doc.GetSnapshotAsync(Source.Cache), FirestoreError.Unavailable);
          AssertTaskSucceeds(db.TerminateAsync());
          app.Dispose();
        }

        // Verify that ClearPersistenceAsync() fails if invoked after the first operation and
        // before a call to TerminateAsync().
        {
          var app = FirebaseApp.Create(defaultOptions, "TestClearPersistenceApp");
          var db = FirebaseFirestore.GetInstance(app);
          Await(db.EnableNetworkAsync());
          AssertTaskFaults(db.ClearPersistenceAsync(), FirestoreError.FailedPrecondition);
          AssertTaskSucceeds(db.TerminateAsync());
          app.Dispose();
        }

      });
    }

    Task TestDocumentReferenceTaskFailures() {
      return Async(() => {
        var docWithInvalidName = TestCollection().Document("__badpath__");
        var fieldPathData = new Dictionary<FieldPath, object>{ { new FieldPath("key"), 42} };

        {
          Task task = docWithInvalidName.DeleteAsync();
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task task = docWithInvalidName.UpdateAsync(TestData(0));
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task task = docWithInvalidName.UpdateAsync("fieldName", 42);
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task task = docWithInvalidName.UpdateAsync(fieldPathData);
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task task = docWithInvalidName.SetAsync(TestData(), SetOptions.MergeAll);
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task<DocumentSnapshot> task = docWithInvalidName.GetSnapshotAsync();
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task<DocumentSnapshot> task = docWithInvalidName.GetSnapshotAsync(Source.Default);
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          ListenerRegistration listenerRegistration = docWithInvalidName.Listen(snap => { });
          AssertTaskFaults(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                           "__badpath__");
          listenerRegistration.Stop();
        }
      });
    }

    Task TestCollectionReferenceTaskFailures() {
      return Async(() => {
        var collectionWithInvalidName = TestCollection().Document("__badpath__").Collection("sub");
        {
          Task<QuerySnapshot> task = collectionWithInvalidName.GetSnapshotAsync();
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task<QuerySnapshot> task = collectionWithInvalidName.GetSnapshotAsync(Source.Default);
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          Task<DocumentReference> task = collectionWithInvalidName.AddAsync(TestData(0));
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "__badpath__");
        }
        {
          ListenerRegistration listenerRegistration = collectionWithInvalidName.Listen(snap => { });
          AssertTaskFaults(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                           "__badpath__");
          listenerRegistration.Stop();
        }
        {
          ListenerRegistration listenerRegistration = collectionWithInvalidName.Listen(
              MetadataChanges.Include, snap => { });
          AssertTaskFaults(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                           "__badpath__");
          listenerRegistration.Stop();
        }
      });
    }

    // Performs a smoke test using a custom class with (nested) [DocumentId] and [ServerTimestamp]
    // attributes.
    Task TestObjectMappingSmokeTest() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        var data = new SampleDataWithNestedDocumentId() {
          OuterName = "outer",
          Nested = new SampleDataWithDocumentId() {
            Name = "name",
            Value = 10
          },
        };

        Await(doc.SetAsync(data));
        DocumentSnapshot snap = Await(doc.GetSnapshotAsync());

        var result = snap.ConvertTo<SampleDataWithNestedDocumentId>();
        AssertEq(result.OuterName, "outer");
        AssertEq(result.Nested.Name, "name");
        AssertEq(result.Nested.Value, 10);
        AssertEq(result.Nested.DocumentId, doc.Id);
        AssertEq(result.DocumentId, doc.Id);

        // Verify a valid timestamp (within 1hr of now) was written for the server timestamp.
        TimeSpan duration = result.ServerTimestamp - DateTime.Now.ToUniversalTime();
        Assert("ServerTimestamp is within 1hr of now", Math.Abs(duration.TotalHours) < 1);
      });
    }

    // Verify Firestore instances are singletons.
    Task TestFirestoreSingleton() {
      return Async(() => {
        FirebaseFirestore db2 = FirebaseFirestore.DefaultInstance;
        Assert("FirebaseFirestore.DefaultInstance returns a singleton", db == db2);
        Assert("Query.Firestore returns the same instance",
            db == db.Collection("a").WhereEqualTo("x", 1).Firestore);
        Assert("DocumentReference.Firestore returns the same instance",
            db == db.Document("a/b").Firestore);
      });
    }

    Task TestFirestoreSettings() {
      return Async(() => {
        // Verify that ToString() returns a meaningful value.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-tostring-test");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          customDb.Settings.Host = "a.b.c";
          customDb.Settings.SslEnabled = true;
          customDb.Settings.PersistenceEnabled = false;
          customDb.Settings.CacheSizeBytes = 9876543;
          AssertStringContainsNoCase(customDb.Settings.ToString(), "FirebaseFirestoreSettings");
          AssertStringContainsNoCase(customDb.Settings.ToString(), "Host=a.b.c");
          AssertStringContainsNoCase(customDb.Settings.ToString(), "SslEnabled=true");
          AssertStringContainsNoCase(customDb.Settings.ToString(), "PersistenceEnabled=false");
          AssertStringContainsNoCase(customDb.Settings.ToString(), "CacheSizeBytes=9876543");
          customApp.Dispose();
        }

        // Verify the default FirebaseFirestoreSettings values.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-defaults-test");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          AssertEq(customDb.Settings.Host, "firestore.googleapis.com");
          AssertEq(customDb.Settings.SslEnabled, true);
          AssertEq(customDb.Settings.PersistenceEnabled, true);
          AssertEq(customDb.Settings.CacheSizeBytes, 100 * 1024 * 1024);
          customApp.Dispose();
        }

        // Verify that the FirebaseFirestoreSettings written are read back.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-readwrite-test");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);

          customDb.Settings.Host = "a.b.c";
          AssertEq<string>(customDb.Settings.Host, "a.b.c");
          customDb.Settings.Host = "d.e.f";
          AssertEq<string>(customDb.Settings.Host, "d.e.f");

          customDb.Settings.SslEnabled = true;
          AssertEq<bool>(customDb.Settings.SslEnabled, true);
          customDb.Settings.SslEnabled = false;
          AssertEq<bool>(customDb.Settings.SslEnabled, false);

          customDb.Settings.PersistenceEnabled = true;
          AssertEq<bool>(customDb.Settings.PersistenceEnabled, true);
          customDb.Settings.PersistenceEnabled = false;
          AssertEq<bool>(customDb.Settings.PersistenceEnabled, false);

          customDb.Settings.CacheSizeBytes = 9876543;
          AssertEq<long>(customDb.Settings.CacheSizeBytes, 9876543);
          customDb.Settings.CacheSizeBytes = 1234567;
          AssertEq<long>(customDb.Settings.CacheSizeBytes, 1234567);

          customApp.Dispose();
        }

        // Verify the FirebaseFirestoreSettings behavior after the FirebaseFirestore is disposed.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-dispose-test");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          FirebaseFirestoreSettings settings = customDb.Settings;

          var oldHost = settings.Host;
          var oldSslEnabled = settings.SslEnabled;
          var oldPersistenceEnabled = settings.PersistenceEnabled;
          var oldCacheSizeBytes = settings.CacheSizeBytes;

          customApp.Dispose();

          AssertException(typeof(InvalidOperationException), () => { settings.Host = "a.b.c"; });
          AssertException(typeof(InvalidOperationException),
                          () => { settings.SslEnabled = false; });
          AssertException(typeof(InvalidOperationException),
                          () => { settings.PersistenceEnabled = false; });
          AssertException(typeof(InvalidOperationException),
                          () => { settings.CacheSizeBytes = 9876543; });

          // Test "getting" the values after verifying that setting throws an exception to ensure
          // that the values were not actually changed by the exception-throwing set operation.
          AssertEq<string>(settings.Host, oldHost);
          AssertEq<bool>(settings.SslEnabled, oldSslEnabled);
          AssertEq<bool>(settings.PersistenceEnabled, oldPersistenceEnabled);
          AssertEq<long>(settings.CacheSizeBytes, oldCacheSizeBytes);
        }

        // Verify the FirebaseFirestoreSettings behavior after the FirebaseFirestore is used.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-toolate-test");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          var oldHost = customDb.Settings.Host;
          var oldSslEnabled = customDb.Settings.SslEnabled;
          var oldPersistenceEnabled = customDb.Settings.PersistenceEnabled;
          var oldCacheSizeBytes = customDb.Settings.CacheSizeBytes;

          // "Use" the settings to lock them in, so that future "set" operations will fail.
          customDb.Collection("a");

          AssertException(typeof(InvalidOperationException),
                          () => { customDb.Settings.Host = "a.b.c"; });
          AssertException(typeof(InvalidOperationException),
                          () => { customDb.Settings.SslEnabled = false; });
          AssertException(typeof(InvalidOperationException),
                          () => { customDb.Settings.PersistenceEnabled = false; });
          AssertException(typeof(InvalidOperationException),
                          () => { customDb.Settings.CacheSizeBytes = 9876543; });

          // Test "getting" the values after verifying that setting throws an exception to ensure
          // that the values were not actually changed by the exception-throwing set operation.
          AssertEq<string>(customDb.Settings.Host, oldHost);
          AssertEq<bool>(customDb.Settings.SslEnabled, oldSslEnabled);
          AssertEq<bool>(customDb.Settings.PersistenceEnabled, oldPersistenceEnabled);
          AssertEq<long>(customDb.Settings.CacheSizeBytes, oldCacheSizeBytes);

          customApp.Dispose();
        }

        // Verify that FirebaseFirestoreSettings.PersistenceEnabled is respected.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "settings-persistence-test");
          string docPath;
          {
            FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
            customDb.Settings.PersistenceEnabled = true;
            DocumentReference doc = customDb.Collection("settings-persistence-test").Document();
            docPath = doc.Path;
            AssertTaskSucceeds(doc.SetAsync(TestData(1)));
            AssertTaskSucceeds(doc.GetSnapshotAsync(Source.Cache));
            AssertTaskSucceeds(customDb.TerminateAsync());
          }
          {
            FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
            customDb.Settings.PersistenceEnabled = false;
            DocumentReference doc = customDb.Document(docPath);
            AssertTaskSucceeds(doc.SetAsync(TestData(1)));
            AssertTaskFaults(doc.GetSnapshotAsync(Source.Cache));
            AssertTaskSucceeds(customDb.TerminateAsync());
          }
          customApp.Dispose();
        }
      });
    }

    // Runs a bunch of serialization test cases (borrowed from google-cloud-dotnet).
    // We first write the (custom) object to Firestore and verify that reading it back out as native
    // C# types returns the correct value. Then we read it out again as the original (custom) object
    // type and verify it equals the original input object.
    Task TestObjectMapping() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        bool cleanup = true;

        try {
          Await(doc.Firestore.DisableNetworkAsync());

          foreach (SerializationTestData.TestCase test in SerializationTestData.TestData(
                       doc.Firestore)) {
            AssertEq(SerializationTestData.TestCase.TestOutcome.Success, test.Result);
            var input = test.Input;
            var expectedOutputWithRawFirestoreTypes = test.ExpectedRawOutput;

            object actualOutputWithRawFirestoreTypes, actualOutputWithInputTypes;
            RoundtripValue(doc, input, out actualOutputWithRawFirestoreTypes,
                           out actualOutputWithInputTypes);

            // This logging can be handy for debugging.
            // if (input != null) {
            //   DebugLog("input: " + input + " [" + input.GetType() + "]");
            //   DebugLog("native types: " + actualOutputWithRawFirestoreTypes + " [" +
            //   actualOutputWithRawFirestoreTypes.GetType() + "]"); DebugLog("converted to original
            //   type: " + actualOutputWithInputTypes + " [" + actualOutputWithInputTypes.GetType()
            //   + "]");
            // }

            AssertDeepEq("Deserialized value with Firestore Raw types does not match expected",
                         actualOutputWithRawFirestoreTypes, expectedOutputWithRawFirestoreTypes);
            AssertDeepEq("Deserialized value with input types does not match the input",
                         actualOutputWithInputTypes, input);
          }

        } catch (ThreadAbortException) {
          // Don't try to do anything else on this thread if it was aborted, or else it will hang.
          cleanup = false;

        } finally {
          if (cleanup) {
            // TODO(mikelehen): Ideally we would use Terminate() or something to avoid actually
            // writing all of our mutations (doesn't really matter, but it takes some time).
            Await(doc.Firestore.EnableNetworkAsync());
          }
        }
      });
    }

    // Tests NotSupportedException is thrown for the type of object mapping
    // we cannot support (yet).
    Task TestNotSupportedObjectMapping() {
      return Async(() => {
        DocumentReference doc = TestDocument();
        bool cleanup = true;

        try {
          Await(doc.Firestore.DisableNetworkAsync());

          foreach (SerializationTestData.TestCase test in SerializationTestData
                       .UnsupportedTestData()) {
            var input = test.Input;
            var expectedWithFirestoreRawTypes = test.ExpectedRawOutput;

            object actualWithFirestoreRawTypes, actualWithInputTypes;
            switch (test.Result) {
              case SerializationTestData.TestCase.TestOutcome.Unsupported:
                AssertException("When serializing: ", typeof(NotSupportedException),
                                () => SerializeToDoc(doc, input));

                // Write the doc with expected output, then deserialize it back to input type.
                SerializeToDoc(doc, expectedWithFirestoreRawTypes);
                AssertException(
                    "When deserializing to input types: ", typeof(NotSupportedException),
                    () => { DeserializeWithInputTypes(doc, input, out actualWithInputTypes); });
                break;
              case SerializationTestData.TestCase.TestOutcome.ReadToInputTypesNotSupported:
                SerializeToDoc(doc, input);

                DeserializeWithRawFirestoreTypes(doc, out actualWithFirestoreRawTypes);
                AssertDeepEq("Deserialized value with Firestore Raw types does not match expected",
                             actualWithFirestoreRawTypes, expectedWithFirestoreRawTypes);

                AssertException(
                    "When deserializing to input types: ", typeof(NotSupportedException),
                    () => { DeserializeWithInputTypes(doc, input, out actualWithInputTypes); });

                break;
              case SerializationTestData.TestCase.TestOutcome.Success:
                FailTest("Expecting a failing outcome with the test case, got 'Success'");
                break;
            }
          }
        } catch (ThreadAbortException) {
          // Don't try to do anything else on this thread if it was aborted, or else it will hang.
          cleanup = false;
        } finally {
          if (cleanup) {
            Await(doc.Firestore.EnableNetworkAsync());
          }
        }
      });
    }

    Task TestAssertTaskFaults() {
      return Async(() => {
        testRunner.DisablePostTestIgnoredFailureCheck();

        // Verify that AssertTaskFaults() passes if the Task faults with a FirestoreException with
        // the expected error code.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetException(new FirestoreException(FirestoreError.Unavailable));
          AssertTaskFaults(tcs.Task, FirestoreError.Unavailable);
        }

        // Verify that AssertTaskFaults() fails if the Task faults with an AggregateException that,
        // when flattened, resolves to a FirestoreException with the expected error code.
        {
          var tcs = new TaskCompletionSource<object>();
          var firestoreException = new FirestoreException(FirestoreError.Unavailable);
          var aggregateException1 = new AggregateException(new[] { firestoreException });
          var aggregateException2 = new AggregateException(new[] { aggregateException1 });
          tcs.SetException(aggregateException2);
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Unavailable);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the AggregateException" +
                "has multiple nested AggregateException instances.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task faults with a FirestoreException with
        // an unexpected error code.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetResult(new FirestoreException(FirestoreError.Unavailable));
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an incorrect error code.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task completes successfully.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetResult(null);
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task completed " +
                "successfully.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task fails with an exception other than
        // FirestoreException.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetException(new InvalidOperationException());
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an incorrect exception type.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task fails with an AggregateException
        // that, when flattened, resolves to an unexpected exception.
        {
          var tcs = new TaskCompletionSource<object>();
          var exception1 = new InvalidOperationException();
          var exception2 = new AggregateException(new[] { exception1 });
          var exception3 = new AggregateException(new[] { exception2 });
          tcs.SetException(exception3);
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an AggregateException that flattened to an unexpected exception.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task fails with an AggregateException that
        // has more than one InnerException.
        {
          var tcs = new TaskCompletionSource<object>();
          var exception1 = new InvalidOperationException();
          var exception2 = new InvalidOperationException();
          var exception3 = new AggregateException(new[] { exception1, exception2 });
          tcs.SetException(exception3);
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok);
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an AggregateException that could not be fully flattened.");
          }
        }

        // Verify that AssertTaskFaults() fails if the Task faults with a FirestoreException with
        // no message but a regular expression for the message is specified.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetException(new FirestoreException(FirestoreError.Ok));
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok, "SomeMessageRegex");
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an exception that does not have a message.");
          } else if (!thrownException.Message.Contains("SomeMessageRegex")) {
            FailTest(
                "AssertTaskFaults() threw an exception (as expected); however, its message was " +
                "incorrect: " + thrownException.Message);
          }
        }

        // Verify that AssertTaskFaults() fails if the Task faults with a FirestoreException with
        // a message that does not match the given regular expression.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetException(new FirestoreException(FirestoreError.Ok, "TheActualMessage"));
          Exception thrownException = null;
          try {
            AssertTaskFaults(tcs.Task, FirestoreError.Ok, "The.*MeaningOfLife");
          } catch (Exception e) {
            thrownException = e;
          }
          if (thrownException == null) {
            FailTest(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an exception that does not have a message.");
          } else if (!(thrownException.Message.Contains("TheActualMessage") &&
                       thrownException.Message.Contains("The.*MeaningOfLife"))) {
            FailTest(
                "AssertTaskFaults() threw an exception (as expected); however, its message was " +
                "incorrect: " + thrownException.Message);
          }
        }

        // Verify that AssertTaskFaults() passes if the Task faults with a FirestoreException with
        // a message that matches the given regular expression.
        {
          var tcs = new TaskCompletionSource<object>();
          tcs.SetException(new FirestoreException(FirestoreError.Ok, "TheActualMessage"));
          AssertTaskFaults(tcs.Task, FirestoreError.Ok, "The.*Message");
        }
      });
    }

    // Intentionally misuse the API to trigger internal assertions and make sure they are thrown as
    // C# exceptions of the expected type.
    Task TestInternalExceptions() {
      return Async(() => {
        Exception exception = null;

        // Invalid argument.
        try {
          var db1 = FirebaseFirestore.DefaultInstance;
          var db2 = NonDefaultFirestore("InvalidArgument");

          var batch = db1.StartBatch();
          var doc = db2.Collection("foo").Document("bar");
          batch.Delete(doc);

        } catch (Exception e) {
          exception = e;

        } finally {
          Assert("Expected an exception to be thrown", exception != null);
          Assert("Expected an ArgumentException, but received " +
              exception, exception is ArgumentException);
        }

        // Illegal state.
        exception = null;
        try {
          var db = NonDefaultFirestore("IllegalState");
          db.Settings.SslEnabled = false;
          // Make sure the Firestore client is initialized.
          db.Collection("foo").Document("bar");

        } catch (Exception e) {
          exception = e;

        } finally {
          Assert("Expected an exception to be thrown", exception != null);
          Assert("Expected an InvalidOperationException, but received " +
              exception, exception is InvalidOperationException);
        }

        // Exception in an async method.
        exception = null;

        {
          var db1 = FirebaseFirestore.DefaultInstance;
          var db2 = NonDefaultFirestore("InternalAssertion");

          DocumentReference doc1 = db1.Collection("foo").Document("bar");
          DocumentReference doc2 = db2.Collection("x").Document("y");
          var data = new Dictionary<string, object>{
            {"f1", doc2},
          };
          exception = AssertTaskFaults(doc1.SetAsync(data));

          Assert("Expected an exception to be thrown", exception != null);
          Assert("Expected an ArgumentException, but received " +
              exception, exception is ArgumentException);
        }
      });
    }

    Task TestListenerRegistrationDispose() {
      return Async(() => {
        var doc = TestDocument();
        var initialData = TestData(1);
        AssertTaskSucceeds(doc.SetAsync(initialData));
        var registration = doc.Listen((snap) => { });
        AssertTaskIsPending(registration.ListenerTask);
        registration.Dispose();
        // The task should have transitioned to TaskStatus.RanToCompletion after
        // Dispose() is called on ListenerRegistration.
        AssertTaskSucceeds(registration.ListenerTask);

        // The callback should not be called after Dispose() is called on
        // ListenerRegistration.
        var doc2 = TestDocument();
        var callbackInvoked = false;
        var registration2 = doc2.Listen(snap => { callbackInvoked = true; });
        registration2.Dispose();
        Await(doc2.SetAsync(initialData));
        System.Threading.Thread.Sleep(50);
        Assert("The callback should not have been invoked", !callbackInvoked);
      });
    }

    Task TestLoadBundles() {
      return Async(() => {
        var db = NonDefaultFirestore("TestLoadBundles");
        AssertTaskSucceeds(db.ClearPersistenceAsync());

        string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
        var progresses = new ThreadSafeList<LoadBundleTaskProgress>();
        object eventSender = null;
        var loadTask = db.LoadBundleAsync(Encoding.UTF8.GetBytes(bundle), (sender, progress) => {
          eventSender = sender;
          progresses.Add(progress);
        });

        // clang-format off
        LoadBundleTaskProgress finalProgress = Await(loadTask);
        AssertEq(finalProgress.State, LoadBundleTaskProgress.LoadBundleTaskState.Success);

        AssertListCountReaches(progresses, 4);
        AssertEq(progresses[0].DocumentsLoaded, 0);
        AssertEq(progresses[0].State, LoadBundleTaskProgress.LoadBundleTaskState.InProgress);
        AssertEq(progresses[1].DocumentsLoaded, 1);
        AssertEq(progresses[1].State, LoadBundleTaskProgress.LoadBundleTaskState.InProgress);
        AssertEq(progresses[2].DocumentsLoaded, 2);
        AssertEq(progresses[2].State, LoadBundleTaskProgress.LoadBundleTaskState.InProgress);
        AssertEq(progresses[3], finalProgress);

        Assert("Expecting eventSender to be db", eventSender == db);

        VerifyBundledQueryResults(db);
        // clang-format on
      });
    }

    Task TestLoadBundlesForASecondTime_ShouldSkip() {
      return Async(() => {
        var db = NonDefaultFirestore("TestLoadBundlesForASecondTime_ShouldSkip");
        AssertTaskSucceeds(db.ClearPersistenceAsync());

        string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
        var loadTask = db.LoadBundleAsync(bundle);
        LoadBundleTaskProgress finalProgress = Await(loadTask);
        AssertEq(finalProgress.State, LoadBundleTaskProgress.LoadBundleTaskState.Success);

        var progresses = new ThreadSafeList<LoadBundleTaskProgress>();
        object eventSender = null;
        loadTask = db.LoadBundleAsync(Encoding.UTF8.GetBytes(bundle), (sender, progress) => {
          eventSender = sender;
          progresses.Add(progress);
        });

        // clang-format off
        finalProgress = Await(loadTask);
        AssertEq(finalProgress.State, LoadBundleTaskProgress.LoadBundleTaskState.Success);

        AssertListCountReaches(progresses, 1);
        AssertEq(progresses[0], finalProgress);

        Assert("Expecting eventSender to be db", eventSender == db);

        VerifyBundledQueryResults(db);
        // clang-format on
      });
    }

    Task TestLoadBundlesFromOtherProjects_ShouldFail() {
      return Async(() => {
        var db = NonDefaultFirestore("TestLoadBundlesFromOtherProjects_ShouldFail");
        Await(db.ClearPersistenceAsync());
        string bundle = BundleBuilder.CreateBundle("other-project");

        // clang-format off
        var progresses = new ThreadSafeList<LoadBundleTaskProgress>();
        var loadTask =
            db.LoadBundleAsync(bundle, (sender, progress) => { progresses.Add(progress); });
        AssertTaskFaults(loadTask);

        AssertListCountReaches(progresses, 2);
        AssertEq(progresses[0].State, LoadBundleTaskProgress.LoadBundleTaskState.InProgress);
        AssertEq(progresses[1].State, LoadBundleTaskProgress.LoadBundleTaskState.Error);
        // clang-format on
      });
    }

    Task LoadedBundleDocumentsAlreadyPulledFromBackend_ShouldNotOverwrite() {
      return Async(() => {
        var db = FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance);
        Await(db.TerminateAsync());
        Await(db.ClearPersistenceAsync());
        db = FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance);

        var collection = db.Collection("coll-1");
        Await(collection.Document("a").SetAsync(new Dictionary<string, object> {
          { "k", "a" },
          { "bar", "newValueA" },
        }));
        Await(collection.Document("b").SetAsync(new Dictionary<string, object> {
          { "k", "b" },
          { "bar", "newValueB" },
        }));

        string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
        var loadTask = db.LoadBundleAsync(bundle);
        var finalProgress = Await(loadTask);
        AssertEq(finalProgress.State, LoadBundleTaskProgress.LoadBundleTaskState.Success);

        // Verify documents are not overwritten
        {
          QuerySnapshot snapshot = Await(db.Collection("coll-1").GetSnapshotAsync(Source.Cache));
          AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
            new Dictionary<string, object> { { "k", "a" }, { "bar", "newValueA" } },
            new Dictionary<string, object> { { "k", "b" }, { "bar", "newValueB" } }
          });
        }

        // Read documents using named query
        {
          Query limitQuery = Await(db.GetNamedQueryAsync("limit"));
          QuerySnapshot snapshot = Await(limitQuery.GetSnapshotAsync(Source.Cache));
          AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
            new Dictionary<string, object> { { "k", "b" }, { "bar", "newValueB" } }
          });
        }

        {
          Query limitToLastQuery = Await(db.GetNamedQueryAsync("limit-to-last"));
          QuerySnapshot snapshot = Await(limitToLastQuery.GetSnapshotAsync(Source.Cache));
          AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
            new Dictionary<string, object> { { "k", "a" }, { "bar", "newValueA" } }
          });
        }
      });
    }

    void VerifyBundledQueryResults(FirebaseFirestore firebaseFirestore) {
      // Verify documents are saved
      {
        QuerySnapshot snapshot =
            Await(firebaseFirestore.Collection("coll-1").GetSnapshotAsync(Source.Cache));
        AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
          new Dictionary<string, object> { { "k", "a" }, { "bar", 1L } },
          new Dictionary<string, object> { { "k", "b" }, { "bar", 2L } }
        });
      }

      // Read documents using named query
      {
        Query limitQuery = Await(firebaseFirestore.GetNamedQueryAsync("limit"));
        QuerySnapshot snapshot = Await(limitQuery.GetSnapshotAsync(Source.Cache));
        AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
          new Dictionary<string, object> { { "k", "b" }, { "bar", 2L } }
        });
      }

      {
        Query limitToLastQuery = Await(firebaseFirestore.GetNamedQueryAsync("limit-to-last"));
        QuerySnapshot snapshot = Await(limitToLastQuery.GetSnapshotAsync(Source.Cache));
        AssertDeepEq(QuerySnapshotToValues(snapshot), new List<Dictionary<string, object>> {
          new Dictionary<string, object> { { "k", "a" }, { "bar", 1L } }
        });
      }
    }

    Task TestQueryEqualsAndGetHashCode() {
      return Async(() => {
        var collection = TestCollection();

        var query1 = collection.WhereEqualTo("num", 1);
        var query2 = collection.WhereEqualTo("num", 1);
        AssertEq(query1.Equals(query2), true);
        AssertEq(query1.GetHashCode(), query2.GetHashCode());

        var query3 = collection.WhereNotEqualTo("num", 1);
        AssertEq(query1.Equals(query3), false);
        AssertNe(query1.GetHashCode(), query3.GetHashCode());

        var query4 = collection.WhereEqualTo("state", "done");
        AssertEq(query1.Equals(query4), false);
        AssertNe(query1.GetHashCode(), query4.GetHashCode());

        var query5 = collection.OrderBy("num");
        var query6 = collection.Limit(2);
        var query7 = collection.OrderBy("num").Limit(2);
        AssertEq(query5.Equals(query6), false);
        AssertEq(query5.Equals(query7), false);
        AssertEq(query6.Equals(query7), false);
        AssertEq(query1.Equals(null), false);

        AssertNe(query5.GetHashCode(), query6.GetHashCode());
        AssertNe(query5.GetHashCode(), query7.GetHashCode());
        AssertNe(query6.GetHashCode(), query7.GetHashCode());
      });
    }

    Task TestQuerySnapshotEqualsAndGetHashCode() {
      return Async(() => {
        var collection = TestCollection();

        AssertTaskSucceeds(collection.Document("a").SetAsync(TestData(1)));
        QuerySnapshot snapshot1 = AssertTaskSucceeds(collection.GetSnapshotAsync());
        QuerySnapshot snapshot2 = AssertTaskSucceeds(collection.GetSnapshotAsync());
        QuerySnapshot snapshot3 = AssertTaskSucceeds(collection.GetSnapshotAsync());

        AssertTaskSucceeds(collection.Document("b").SetAsync(TestData(2)));
        QuerySnapshot snapshot4 = AssertTaskSucceeds(collection.GetSnapshotAsync());

        AssertEq(snapshot1.Equals(snapshot1), true);
        // Note: snapshot1 is not equal to snapshot2 as snapshot1 contains a document with
        // `DocumentState` of `kHasCommittedMutations`, but snapshot2 has the same document with
        // `DocumentState` of `kSynced`.
        AssertEq(snapshot1.Equals(snapshot2), false);
        AssertEq(snapshot2.Equals(snapshot3), true);
        AssertEq(snapshot3.Equals(snapshot4), false);
        AssertEq(snapshot1.Equals(null), false);

        AssertEq(snapshot1.GetHashCode(), snapshot1.GetHashCode());
        AssertEq(snapshot2.GetHashCode(), snapshot3.GetHashCode());
        AssertNe(snapshot3.GetHashCode(), snapshot4.GetHashCode());
      });
    }

    Task TestDocumentSnapshotEqualsAndGetHashCode() {
      return Async(() => {
        DocumentReference doc1 = db.Collection("col2").Document();
        DocumentReference doc2 = db.Collection("col2").Document();
        var data1 = TestData();
        var data2 = TestData(2);

        AssertTaskSucceeds(doc1.SetAsync(data1));
        DocumentSnapshot snapshot1 = AssertTaskSucceeds(doc1.GetSnapshotAsync());

        AssertTaskSucceeds(doc1.SetAsync(data2));
        DocumentSnapshot snapshot2 = AssertTaskSucceeds(doc1.GetSnapshotAsync());

        AssertTaskSucceeds(doc2.SetAsync(data1));
        DocumentSnapshot snapshot3 = AssertTaskSucceeds(doc2.GetSnapshotAsync());
        DocumentSnapshot snapshot4 = AssertTaskSucceeds(doc2.GetSnapshotAsync());
        DocumentSnapshot snapshot5 = AssertTaskSucceeds(doc2.GetSnapshotAsync());

        AssertEq(snapshot1.Equals(snapshot1), true);
        AssertEq(snapshot1.Equals(snapshot2), false);
        AssertEq(snapshot1.Equals(snapshot3), false);
        AssertEq(snapshot1.Equals(null), false);

        // Note: snapshot3 is not equal to snapshot4 as snapshot3 has `DocumentState` of
        // `kHasCommittedMutations`, but snapshot4 has `DocumentState` of `kSynced`.
        // TODO(b/204238341) Remove this #if once the Android and iOS implementations converge.
#if UNITY_ANDROID
        AssertEq(snapshot3.Equals(snapshot4), false);
#else
        AssertEq(snapshot3.Equals(snapshot4), true);
#endif

        AssertEq(snapshot4.Equals(snapshot5), true);

        AssertEq(snapshot1.GetHashCode(), snapshot1.GetHashCode());

#if UNITY_ANDROID
        // TODO(b/198001784): Re-enable the following check once the iOS implementation is fixed.
        // Note: the non-Android implementation calculates the hash based on the document key (and
        // ignores the document contents), and so it produces the same hash for snapshot1 and
        // snapshot2.
        AssertNe(snapshot1.GetHashCode(), snapshot2.GetHashCode());
#endif

        AssertNe(snapshot1.GetHashCode(), snapshot3.GetHashCode());
        AssertEq(snapshot4.GetHashCode(), snapshot5.GetHashCode());
      });
    }

    Task TestDocumentChangeEqualsAndGetHashCode() {
      return Async(() => {
        var collection = TestCollection();

        AssertTaskSucceeds(collection.Document("a").SetAsync(TestData(1)));

        var accumulator = new EventAccumulator<QuerySnapshot>(MainThreadId, FailTest);
        collection.Listen(MetadataChanges.Include, accumulator.Listener);

        // Wait for the first snapshot.
        QuerySnapshot snapshot1 = accumulator.Await();
        var changes = snapshot1.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 1);
        var change1 = changes.First();

        // Wait for new snapshot once we're synced with the backend.
        var snapshot2 = accumulator.Await();
        // This is expected to be empty.
        changes = snapshot2.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 0);

        // Update the document and wait for the resulting snapshot.
        collection.Document("a").SetAsync(TestData(2));
        var snapshot3 = accumulator.Await();
        changes = snapshot3.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 1);
        var change2 = changes.First();

        // Wait for snapshot indicating the write has completed.
        var snapshot4 = accumulator.Await();
        changes = snapshot4.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 1);
        var change3 = changes.First();

        QuerySnapshot snapshot5 = AssertTaskSucceeds(collection.GetSnapshotAsync());
        changes = snapshot5.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 1);
        var change4 = changes.First();
        QuerySnapshot snapshot6 = AssertTaskSucceeds(collection.GetSnapshotAsync());
        changes = snapshot6.GetChanges(MetadataChanges.Include);
        AssertEq(changes.Count(), 1);
        var change5 = changes.First();

        AssertEq(change1.Equals(change1), true);
        AssertEq(change1.Equals(change2), false);
        AssertEq(change1.Equals(change3), false);
        AssertEq(change1.Equals(null), false);
        AssertEq(change4.Equals(change5), true);

        AssertEq(change1.GetHashCode(), change1.GetHashCode());
        AssertNe(change1.GetHashCode(), change2.GetHashCode());
        AssertNe(change1.GetHashCode(), change3.GetHashCode());
        AssertEq(change4.GetHashCode(), change5.GetHashCode());
      });
    }

    Task TestInvalidArgumentAssertions() {
      return Async(() => {
        foreach (var test in InvalidArgumentsTest.TestCases) {
          bool pass = false;
          try {
            test.action(this);
            pass = true;
          } finally {
            if (!pass) {
              DebugLog("InvalidArgumentsTest test FAILED: " + test.name);
            }
          }
        }
      });
    }

    Task TestFirestoreDispose() {
      return Async(() => {
        // Verify that disposing the `FirebaseApp` in turn disposes the `FirebaseFirestore` object.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "dispose-app-to-firestore");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          customApp.Dispose();
          Assert("App property should be null", customDb.App == null);
        }

        // Verify that all non-static methods in `FirebaseFirestore` throw if invoked after
        // the instance is disposed.
        {
          var taskCompletionSource = new TaskCompletionSource<string>();
          taskCompletionSource.SetException(new InvalidOperationException("forced exception"));
          Task sampleUntypedTask = taskCompletionSource.Task;
          Task<string> sampleTypedTask = taskCompletionSource.Task;

          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "dispose-exceptions");
          FirebaseFirestore customDb = FirebaseFirestore.GetInstance(customApp);
          var doc = customDb.Document("ColA/DocA/ColB/DocB");
          var doc2 = customDb.Document("ColA/DocA/ColB/DocC");
          var collection = doc.Parent;
          var writeBatch = customDb.StartBatch();
          customApp.Dispose();

          // Verify that the `App` property is null valid after `FirebaseFirestore` is disposed.
          Assert("App property should be null", customDb.App == null);

          // Verify that all non-static methods in `FirebaseFirestore` throw after it is disposed.
          AssertException(typeof(InvalidOperationException), () => customDb.Collection("a"));
          AssertException(typeof(InvalidOperationException), () => customDb.Document("a/b"));
          AssertException(typeof(InvalidOperationException), () => customDb.CollectionGroup("c"));
          AssertException(typeof(InvalidOperationException), () => customDb.StartBatch());
          AssertException(typeof(InvalidOperationException),
                          () => customDb.RunTransactionAsync(transaction => sampleUntypedTask));
          AssertException(
              typeof(InvalidOperationException),
              () => customDb.RunTransactionAsync<string>(transaction => sampleTypedTask));
          bool snapshotsInSyncListenerInvoked = false;
          Action snapshotsInSyncListener = () => { snapshotsInSyncListenerInvoked = true; };
          AssertException(typeof(InvalidOperationException),
                          () => customDb.ListenForSnapshotsInSync(snapshotsInSyncListener));
          AssertException(typeof(InvalidOperationException),
                          () => customDb.LoadBundleAsync("BundleData"));
          AssertException(typeof(InvalidOperationException),
                          () => customDb.LoadBundleAsync("BundleData", (sender, progress) => {}));
          AssertException(typeof(InvalidOperationException),
                          () => customDb.LoadBundleAsync(new byte[0]));
          AssertException(typeof(InvalidOperationException),
                          () => customDb.LoadBundleAsync(new byte[0], (sender, progress) => {}));
          AssertException(typeof(InvalidOperationException),
                          () => customDb.GetNamedQueryAsync("QueryName"));
          AssertException(typeof(InvalidOperationException), () => customDb.DisableNetworkAsync());
          AssertException(typeof(InvalidOperationException), () => customDb.EnableNetworkAsync());
          AssertException(typeof(InvalidOperationException),
                          () => customDb.WaitForPendingWritesAsync());
          AssertException(typeof(InvalidOperationException), () => customDb.TerminateAsync());
          AssertException(typeof(InvalidOperationException),
                          () => customDb.ClearPersistenceAsync());

          // Verify the behavior of methods in `CollectionReference` after `FirebaseFirestore` is
          // disposed.
          Assert("collection.Firestore is not correct", collection.Firestore == customDb);
          AssertEq(collection.Id, "");
          AssertEq(collection.Path, "");
          bool collectionListenerInvoked = false;
          Action<QuerySnapshot> collectionListener = snap => { collectionListenerInvoked = true; };
          // TODO(b/201440423) `Listen()` should throw `InvalidOperationException`, not succeed.
          AssertNotNull("Collection.Listen()", collection.Listen(collectionListener));
          AssertNotNull("Collection.Document() returned null", collection.Document());
          AssertNotNull("Collection.Document(string) returned null", collection.Document("abc"));
          AssertTaskFaults(collection.AddAsync(TestData(1)), FirestoreError.FailedPrecondition);
          AssertTaskFaults(collection.GetSnapshotAsync(Source.Default),
                           FirestoreError.FailedPrecondition);

          // Verify the behavior of methods in `DocumentReference` after `FirebaseFirestore` is
          // disposed.
          Assert("doc.Firestore is not correct", doc.Firestore == customDb);
          AssertEq(doc.Id, "");
          AssertEq(doc.Path, "");
          AssertEq(doc.Parent, collection);
          bool docListenerInvoked = false;
          Action<DocumentSnapshot> docListener = snap => { docListenerInvoked = true; };
          // TODO(b/201440423) `Listen()` should throw `InvalidOperationException`, not succeed.
          AssertNotNull("DocumentReference.Listen()", doc.Listen(docListener));
          AssertNotNull("DocumentReference.Collection() returned null", doc.Collection("zzz"));
          AssertTaskFaults(doc.DeleteAsync(), FirestoreError.FailedPrecondition);
          AssertTaskFaults(doc.SetAsync(TestData(1)), FirestoreError.FailedPrecondition);
          AssertTaskFaults(doc.UpdateAsync("life", 42), FirestoreError.FailedPrecondition);
          AssertTaskFaults(doc.GetSnapshotAsync(Source.Default), FirestoreError.FailedPrecondition);

          // Verify the behavior of methods in `WriteBatch` after `FirebaseFirestore` is disposed.
          writeBatch.Delete(doc);
          writeBatch.Set(doc2, TestData(1), null);
          AssertTaskFaults(writeBatch.CommitAsync(), FirestoreError.FailedPrecondition);

          // Verify that the listeners were not notified. Do it here instead of above to allow
          // some time to pass for unexpected notifications to be received.
          Assert("The listener registered with FirebaseFirestore.ListenForSnapshotsInSync() " +
                     "should not be notified after TerminateAsync()",
                 !snapshotsInSyncListenerInvoked);
          Assert("The listener registered with CollectionReference.Listen() " +
                     "should not be notified after TerminateAsync()",
                 !collectionListenerInvoked);
          Assert("The listener registered with DocumentReference.Listen() " +
                     "should not be notified after TerminateAsync()",
                 !docListenerInvoked);
        }
      });
    }

    Task TestFirestoreGetInstance() {
      return Async(() => {
        // Verify that invoking `FirebaseFirestore.GetInstance()` with the same `FirebaseApp`
        // returns the exact same `FirebaseFirestore` instance.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "getinstance-same-instance");
          FirebaseFirestore customDb1 = FirebaseFirestore.GetInstance(customApp);
          FirebaseFirestore customDb2 = FirebaseFirestore.GetInstance(customApp);
          Assert("GetInstance() should return the same instance", customDb1 == customDb2);
          customApp.Dispose();
        }

        // Verify that invoking `FirebaseFirestore.GetInstance()` with different `FirebaseApp`
        // instances return distinct and consistent `FirebaseFirestore` instances.
        {
          FirebaseApp customAppA = FirebaseApp.Create(db.App.Options, "getinstance-multi-a");
          FirebaseApp customAppB = FirebaseApp.Create(db.App.Options, "getinstance-multi-b");
          FirebaseFirestore customDbA1 = FirebaseFirestore.GetInstance(customAppA);
          FirebaseFirestore customDbB1 = FirebaseFirestore.GetInstance(customAppB);
          FirebaseFirestore customDbA2 = FirebaseFirestore.GetInstance(customAppA);
          FirebaseFirestore customDbB2 = FirebaseFirestore.GetInstance(customAppB);
          Assert("GetInstance() should return the same instance A", customDbA1 == customDbA2);
          Assert("GetInstance() should return the same instance B", customDbB1 == customDbB2);
          Assert("GetInstance() should return distinct instances", customDbA1 != customDbB1);
          customAppB.Dispose();
          customAppA.Dispose();
        }

        // Verify that invoking `FirebaseFirestore.GetInstance()` with a disposed `FirebaseApp`
        // does not crash.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "getinstance-disposed-app");
          FirebaseFirestore.GetInstance(customApp);
          customApp.Dispose();
          AssertException(typeof(ArgumentException),
                          () => FirebaseFirestore.GetInstance(customApp));
        }

        // Verify that invoking `FirebaseFirestore.GetInstance()` after `TerminateAsync()` results
        // in a distinct, functional `FirebaseFirestore` instance.
        {
          FirebaseApp customApp = FirebaseApp.Create(db.App.Options, "getinstance-after-terminate");
          FirebaseFirestore customDbBefore = FirebaseFirestore.GetInstance(customApp);
          Task terminateTask = customDbBefore.TerminateAsync();
          FirebaseFirestore customDbAfter = FirebaseFirestore.GetInstance(customApp);
          FirebaseFirestore customDbAfter2 = FirebaseFirestore.GetInstance(customApp);
          // Wait for completion of the `Task` returned from `TerminateAsync()` *after* calling
          // `GetInstance()` to ensure that `TerminateAsync()` *synchronously* evicts the
          // "firestore" objects from both the C++ and C# instance caches (as opposed to evicting
          // from the caches *asynchronously*).
          AssertTaskSucceeds(terminateTask);
          Assert("GetInstance() should return a new instance", customDbBefore != customDbAfter);
          Assert("GetInstance() should return the same instance", customDbAfter == customDbAfter2);
          DocumentReference doc = customDbAfter.Collection("a").Document();
          AssertTaskSucceeds(doc.SetAsync(TestData(1)));
          customApp.Dispose();
        }
      });
    }

    private void RoundtripValue(DocumentReference doc, object input, out object nativeOutput, out object convertedOutput) {
      SerializeToDoc(doc, input);

      DeserializeWithRawFirestoreTypes(doc, out nativeOutput);

      DeserializeWithInputTypes(doc, input, out convertedOutput);
    }

    private void SerializeToDoc(DocumentReference doc, object input) {
      // Wrap in a dictionary so we can write it as a document even if input is a primitive type.
      var docData = new Dictionary<string, object> { { "field", input } };

      // Write doc and read the field back out as native types.
      doc.SetAsync(docData);
    }

    private void DeserializeWithRawFirestoreTypes(DocumentReference doc, out object rawOutput) {
      var docSnap = Await(doc.GetSnapshotAsync(Source.Cache));
      rawOutput = docSnap.GetValue<object>("field");
    }

    private void DeserializeWithInputTypes(DocumentReference doc, object input,
                                           out object outputWithInputTypes) {
      var docSnap = Await(doc.GetSnapshotAsync(Source.Cache));
      // To get the converted value out, we have to use reflection to call GetValue<> with
      // input.GetType() as the generic parameter.
      MethodInfo method = typeof(DocumentSnapshot).GetMethod("GetValue", new Type[] { typeof(string), typeof(ServerTimestampBehavior) });
      MethodInfo genericMethod = method.MakeGenericMethod(input != null ? input.GetType() : typeof(object));
      outputWithInputTypes =
          genericMethod.Invoke(docSnap, new object[] { "field", ServerTimestampBehavior.None });
    }

    // Unity on iOS can't JIT compile code, which breaks some usages of
    // reflection such as the way RoundtripValue() calls the
    // DocumentSnapshot.GetValue<>() method with dynamic generic types. As a
    // workaround, we have to enumerate all the different versions that we need.
    // Normal users won't have to do this.
    // See https://docs.unity3d.com/Manual/ScriptingRestrictions.html for more details.
    private void UnityCompileHack() {
      // NOTE: This never actually runs. It just forces the compiler to generate
      // the code we need.
      DocumentSnapshot x = null;
      x.GetValue<bool>("");
      x.GetValue<string>("");
      x.GetValue<byte>("");
      x.GetValue<sbyte>("");
      x.GetValue<short>("");
      x.GetValue<ushort>("");
      x.GetValue<int>("");
      x.GetValue<uint>("");
      x.GetValue<long>("");
      x.GetValue<ulong>("");
      x.GetValue<float>("");
      x.GetValue<double>("");
      x.GetValue<Timestamp>("");
      x.GetValue<DateTime>("");
      x.GetValue<DateTimeOffset>("");
      x.GetValue<Blob>("");
      x.GetValue<GeoPoint>("");
      x.GetValue<SerializationTestData.StructModel>("");
      x.GetValue<SerializationTestData.ByteEnum>("");
      x.GetValue<SerializationTestData.SByteEnum>("");
      x.GetValue<SerializationTestData.Int16Enum>("");
      x.GetValue<SerializationTestData.UInt16Enum>("");
      x.GetValue<SerializationTestData.Int32Enum>("");
      x.GetValue<SerializationTestData.UInt32Enum>("");
      x.GetValue<SerializationTestData.Int64Enum>("");
      x.GetValue<SerializationTestData.UInt64Enum>("");
      x.GetValue<SerializationTestData.CustomConversionEnum>("");
      x.GetValue<SerializationTestData.CustomValueType>("");
      x.GetValue<Dictionary<string, SerializationTestData.GameResult>>("");
    }

    private void AssertQueryResults(string desc, Query query, List<string> docIds) {
      var snapshot = Await(query.GetSnapshotAsync());

      AssertEq(desc + ": Query result count", snapshot.Count, docIds.Count);
      for (int i = 0; i < snapshot.Count; i++) {
        AssertEq(desc + ": Document ID " + i, docIds[i], snapshot[i].Id);
      }
    }

    private FirebaseFirestore NonDefaultFirestore(string appName) {
      var appOptions = new AppOptions();
      // Setting a ProjectId is required (b/158838266).
      appOptions.ProjectId = appName;
      var app = FirebaseApp.Create(appOptions, appName);
      return FirebaseFirestore.GetInstance(app);
    }

    /// Wraps IEnumerator in an exception handling Task.
    Task ToTask(IEnumerator ienum) {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      mainThreadDispatcher.RunOnMainThread(() => {
        StartThrowingCoroutine(ienum, ex => {
          if (ex == null) {
            if (previousTask.IsFaulted) {
              tcs.TrySetException(previousTask.Exception);
            } else if (previousTask.IsCanceled) {
              tcs.TrySetCanceled();
            } else {
              tcs.TrySetResult(true);
            }
          } else {
            tcs.TrySetException(ex);
          }
        });
      });
      return tcs.Task;
    }

    /// Start a coroutine that might throw an exception. Call the callback with the exception if it
    /// does or null if it finishes without throwing an exception.
    public Coroutine StartThrowingCoroutine(IEnumerator enumerator, Action<Exception> done) {
      return StartCoroutine(RunThrowingIterator(enumerator, done));
    }

    /// Run an iterator function that might throw an exception. Call the callback with the exception
    /// if it does or null if it finishes without throwing an exception.
    public static IEnumerator RunThrowingIterator(IEnumerator enumerator, Action<Exception> done) {
      while (true) {
        object current;
        try {
          if (enumerator.MoveNext() == false) {
            break;
          }
          current = enumerator.Current;
        } catch (Exception ex) {
          done(ex);
          yield break;
        }
        yield return current;
      }
      done(null);
    }

    [FirestoreData]
    private class SampleDataWithDocumentId {
      [FirestoreDocumentId]
      public string DocumentId { get; set; }

      [FirestoreProperty]
      public string Name { get; set; }

      [FirestoreProperty]
      public int Value { get; set; }
    }

    [FirestoreData]
    private class SampleDataWithNestedDocumentId {
      [FirestoreDocumentId]
      public string DocumentId { get; set; }

      [FirestoreProperty]
      public string OuterName { get; set; }

      [FirestoreProperty]
      public SampleDataWithDocumentId Nested { get; set; }

      [FirestoreProperty]
      [ServerTimestamp]
      public DateTime ServerTimestamp { get; set; }
    }

    // A minimal port of the .NET `System.Threading.Barrier` class.
    // TODO: Delete this class and use the `Barrier` class from the standard library once .NET 3.5
    // support is dropped.
    private sealed class BarrierCompat {
      private readonly object _lock = new object();
      private readonly int _participantCount;
      private int _waitingParticipantCount = 0;

      internal BarrierCompat(int participantCount) {
        if (participantCount < 2) {
          throw new ArgumentException("invalid participantCount: " + participantCount);
        }
        _participantCount = participantCount;
      }

      internal void SignalAndWait() {
        lock (_lock) {
          _waitingParticipantCount++;
          if (_waitingParticipantCount == _participantCount) {
            _waitingParticipantCount = 0;
            Monitor.PulseAll(_lock);
          } else {
            Monitor.Wait(_lock);
          }
        }
      }
    }

    // A minimal implementation of a "list" that is safe for concurrent access
    // from multiple threads.
    private sealed class ThreadSafeList<T> {
      private readonly List<T> elements = new List<T>();

      public int Count {
        get {
          lock (this) {
            return elements.Count;
          }
        }
      }

      public T this[int index] {
        get {
          lock (this) {
            return elements[index];
          }
        }
      }

      public void Add(T element) {
        lock (this) {
          elements.Add(element);
          Monitor.PulseAll(this);
        }
      }
    }
  }
}
