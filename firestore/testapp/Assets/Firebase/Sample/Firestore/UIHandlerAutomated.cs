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
    private int mainThreadId;

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
        TestGetKnownValue,
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
        TestTransactionPermanentError,
        // Waiting for all retries is slow, so we usually leave this test disabled.
        //TestTransactionMaxRetryFailure,
        TestSetOptions,
        TestCanTraverseCollectionsAndDocuments,
        TestCanTraverseCollectionAndDocumentParents,
        TestDocumentSnapshot,
        TestDocumentSnapshotIntegerIncrementBehavior,
        TestDocumentSnapshotDoubleIncrementBehavior,
        TestDocumentSnapshotServerTimestampBehavior,
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
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
      });

      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
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

    // Throw when condition is false.
    private void Assert(string message, bool condition) {
      if (!condition)
        throw new Exception(String.Format("Assertion failed ({0}): {1}",
                                          testRunner.CurrentTestDescription, message));
    }

    // Throw when value1 != value2 (using direct .Equals() check).
    private void AssertEq<T>(string message, T value1, T value2) {
      if (!(object.Equals(value1, value2))) {
        throw new Exception(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                                          testRunner.CurrentTestDescription, value1, value2,
                                          message));
      }
    }

    // Throw when value1 != value2 (using direct .Equals() check).
    private void AssertEq<T>(T value1, T value2) {
      AssertEq("Values not equal", value1, value2);
    }

    // Throw when value1 != value2 (traversing the values recursively, including arrays and maps).
    private void AssertDeepEq<T>(string message, T value1, T value2) {
      if (!ObjectDeepEquals(value1, value2)) {
        throw new Exception(String.Format("Assertion failed ({0}): {1} != {2} ({3})",
                                          testRunner.CurrentTestDescription,
                                          ObjectToString(value1),
                                          ObjectToString(value2),
                                          message));
      }
    }

    // Throw when value1 != value2 (traversing the values recursively, including arrays and maps).
    private void AssertDeepEq<T>(T value1, T value2) {
      AssertDeepEq("Values not equal", value1, value2);
    }

    private void AssertIsType(object obj, Type expectedType) {
      if (!expectedType.IsInstanceOfType(obj)) {
        throw new Exception("object has type " + obj.GetType() + " but expected "
                            + expectedType + " (" + obj + ")");
      }
    }

    private void AssertException(Type exceptionType, Action a) {
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
          throw new Exception(
              String.Format("{0}\nException of wrong type was thrown. Expected {1} but got: {2}",
                            context, exceptionType, e));
        }
        return;
      }
      throw new Exception(String.Format("{0}\nExpected exception was not thrown ({1})", context,
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

    private void AssertTaskSucceeds(Task t) {
      t.Wait();
      AssertTaskProperties(t, isCompleted: true, isFaulted: false, isCanceled: false);
    }

    private Exception AssertTaskFaults(Task t) {
      var exception = AwaitException(t);
      AssertTaskProperties(t, isCompleted: true, isFaulted: true, isCanceled: false);
      return exception;
    }

    private FirestoreException AssertTaskFaults(Task t, FirestoreError expectedError,
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

    private T Await<T>(Task<T> t) {
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
      throw new Exception("AwaitException() task succeeded rather than throwing an exception.");
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

    private CollectionReference TestCollection() {
      return db.Collection("test-collection_" + AutoId());
    }

    private DocumentReference TestDocument() {
      return TestCollection().Document();
    }

    /// Runs the given action in a task to avoid blocking the main thread.
    private Task Async(Action action) {
      return Task.Run(action);
    }

    private List<Dictionary<string, object>> QuerySnapshotToValues(QuerySnapshot snap) {
      List<Dictionary<string, object>> res = new List<Dictionary<string, object>>();
      foreach (DocumentSnapshot doc in snap) {
        res.Add(doc.ToDictionary());
      }
      return res;
    }

    private Dictionary<string, object> TestData(long n = 1) {
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

    /**
     * Tests a *very* basic trip through the Firestore API.
     */
    Task TestGetKnownValue() {
      return ToTask(GetKnownValue()).ContinueWithOnMainThread((task) => {
        DocumentSnapshot snap = (previousTask as Task<DocumentSnapshot>).Result;

        var dict = snap.ToDictionary();
        Assert("Resulting document is missing 'field1' field.", dict.ContainsKey("field1"));
        AssertEq("'field1' is not equal to 'value1'", dict["field1"].ToString(), "value1");

        return task;
      }).Unwrap();
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
        var docAccumulator = new EventAccumulator<DocumentSnapshot>(mainThreadId);
        var docListener = doc.Listen(snapshot => {
          events.Add("doc");
          docAccumulator.Listener(snapshot);
        });

        Await(doc.SetAsync(TestData(1)));

        docAccumulator.Await();
        events.Clear();

        var syncAccumulator = new EventAccumulator<string>();
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

        var db1SyncAccumulator = new EventAccumulator<string>();
        var db1SyncListener = db1.ListenForSnapshotsInSync(() => {
          db1SyncAccumulator.Listener("db1 in sync");
        });
        db1SyncAccumulator.Await();

        var db2SyncAccumulator = new EventAccumulator<string>();
        db2.ListenForSnapshotsInSync(() => {
          db2SyncAccumulator.Listener("db2 in sync");
        });
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

        var db1DocAccumulator = new EventAccumulator<DocumentSnapshot>();
        db1Doc.Listen(db1DocAccumulator.Listener);
        db1DocAccumulator.Await();

        var db2DocAccumulator = new EventAccumulator<DocumentSnapshot>();
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

        var db1CollAccumulator = new EventAccumulator<QuerySnapshot>();
        db1Coll.Listen(db1CollAccumulator.Listener);
        db1CollAccumulator.Await();

        var db2CollAccumulator = new EventAccumulator<QuerySnapshot>();
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
        // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
        AssertTaskFaults(commitWithInvalidDocTask, FirestoreError.InvalidArgument, "invalid");
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
          AssertEq(mainThreadId, Thread.CurrentThread.ManagedThreadId);
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

        var accumulator = new EventAccumulator<DocumentSnapshot>(mainThreadId);
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

        var accumulator = new EventAccumulator<DocumentSnapshot>();
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

        var accumulator = new EventAccumulator<QuerySnapshot>(mainThreadId);
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

        var accumulator = new EventAccumulator<QuerySnapshot>();
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

        var accumulator = new EventAccumulator<QuerySnapshot>();
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
        var limitAccumulator = new EventAccumulator<QuerySnapshot>();
        var limitReg = limit.Listen(limitAccumulator.Listener);

        // Setup mirroring `limitToLast` query.
        var limitToLast = c.LimitToLast(2).OrderByDescending("sort");
        var limitToLastAccumulator = new EventAccumulator<QuerySnapshot>();
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
                             new List<object> { new Dictionary<string, object> { { "code", 500 } } }),
            docIds: AsList("f"));

        AssertQueryResults(
            desc: "InQueryWithDocIds",
            query: c.WhereIn(FieldPath.DocumentId,
                             new List<object> { "c", "e" }),
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
                new List<object> { new List<object> { 98101, 98102 },
                                   new Dictionary<string, object> { { "code", 500 } } }),
            docIds: AsList("a", "b", "c", "d", "e"));

        AssertQueryResults(
            desc: "NotInQueryWithDocIds",
            query: c.WhereNotIn(FieldPath.DocumentId, new List<object> { "a", "c", "e" }),
            docIds: AsList("b", "d", "f", "g"));

        AssertQueryResults(
            desc: "NotInQueryWithNulls",
            query: c.WhereNotIn(new FieldPath("nullable"), new List<object> { null }),
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
        var doc = db1.Document("abc/123");
        var accumulator = new EventAccumulator<DocumentSnapshot>();
        var registration = doc.Listen(accumulator.Listener);

        // Multiple calls to terminate should go through.
        Await(db1.TerminateAsync());
        Await(db1.TerminateAsync());

        // Can call registration.Stop multiple times even after termination.
        registration.Stop();
        registration.Stop();

        // TODO(b/149105903) Uncomment this line when a C# exception can be thrown here.
        // AssertException(typeof(Exception), () => Await(db2.DisableNetworkAsync()));

        // Create a new functional instance.
        var db2 = FirebaseFirestore.GetInstance(app);
        Assert("Should create a new instance.", db1 != db2);
        Await(db2.DisableNetworkAsync());
        Await(db2.EnableNetworkAsync());

        app.Dispose();
        // TODO(wuandy): App.Dispose really should leads to Firestore terminated, a NRE here is
        // not ideal, but serves the purpose for now. Ideally, it should throw an exception
        // telling user it is terminated.
        AssertException(typeof(NullReferenceException), () => Await(db2.DisableNetworkAsync()));
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
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
        }
        {
          Task task = docWithInvalidName.UpdateAsync(TestData(0));
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
        }
        {
          Task task = docWithInvalidName.UpdateAsync("fieldName", 42);
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
        }
        {
          Task task = docWithInvalidName.UpdateAsync(fieldPathData);
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
        }
        {
          Task task = docWithInvalidName.SetAsync(TestData(), SetOptions.MergeAll);
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
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
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(task, FirestoreError.InvalidArgument, "invalid");
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
          // TODO(b/193834769) Assert "__badpath__" is in the message once the message is fixed.
          AssertTaskFaults(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                           "invalid");
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
        FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
        DocumentReference doc = db.Collection("coll").Document();
        var data = new Dictionary<string, object>{
          {"f1", "v1"},
        };
        Await(doc.SetAsync(data));

        // Verify it can get snapshot from cache, this is the behavior when
        // persistence is set to true in settings, which is the default setting.
        Await(doc.GetSnapshotAsync(Source.Cache));

        // Terminate the current instance and create a new one (via DefaultInstance) such that
        // we can apply a new FirebaseFirestoreSettings.
        Await(db.TerminateAsync());
        db = FirebaseFirestore.DefaultInstance;
        db.Settings = new FirebaseFirestoreSettings { PersistenceEnabled = false };

        doc = db.Collection("coll").Document();
        Await(doc.SetAsync(data));

        // Verify it cannot get snapshot from cache, this behavior only exists with memory
        // persistence.
        AssertException(typeof(Exception), () => Await(doc.GetSnapshotAsync(Source.Cache)));

        // Restart SDK again to test mutating existing settings.
        Await(db.TerminateAsync());
        db = FirebaseFirestore.DefaultInstance;
        db.Settings.PersistenceEnabled = false;

        doc = db.Collection("coll").Document();
        data = new Dictionary<string, object>{
          {"f1", "v1"},
        };
        Await(doc.SetAsync(data));

        // Verify it cannot get snapshot from cache, this behavior only exists with memory
        // persistence.
        AssertException(typeof(Exception), () => Await(doc.GetSnapshotAsync(Source.Cache)));

        Await(db.TerminateAsync());
        db = FirebaseFirestore.DefaultInstance;
        long fiveMb = 5 * 1024 * 1024;
        db.Settings = new FirebaseFirestoreSettings { CacheSizeBytes = fiveMb };
        AssertEq(db.Settings.CacheSizeBytes, fiveMb);
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
                throw new Exception(
                    "Expecting a failing outcome with the test case, got 'Success'");
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
            throw new Exception(
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
            throw new Exception(
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
            throw new Exception(
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
            throw new Exception(
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
            throw new Exception(
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
            throw new Exception(
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
            throw new Exception(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an exception that does not have a message.");
          } else if (!thrownException.Message.Contains("SomeMessageRegex")) {
            throw new Exception(
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
            throw new Exception(
                "AssertTaskFaults() should have thrown an exception because the task faulted " +
                "with an exception that does not have a message.");
          } else if (!(thrownException.Message.Contains("TheActualMessage") &&
                       thrownException.Message.Contains("The.*MeaningOfLife"))) {
            throw new Exception(
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
          db.Settings = new FirebaseFirestoreSettings{SslEnabled = false};
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
  }
}
