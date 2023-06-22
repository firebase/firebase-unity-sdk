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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Tests.TestAsserts;

namespace Tests {
  // Tests that passing invalid input to SWIG-wrapped functions results in a C# exception instead
  // of a crash.
  public class InvalidArgumentsTest : FirestoreIntegrationTests {
    [Test]
    public void CollectionReference_AddAsync_NullDocumentData() {
      CollectionReference collection = db.Collection("a");
      Assert.Throws<ArgumentNullException>(() => collection.AddAsync(null));
    }

    [UnityTest]
    public IEnumerator CollectionReference_AddAsync_DocumentDataWithEmptyKey() {
      CollectionReference collection = db.Collection("a");
      yield return AwaitFaults(collection.AddAsync(new Dictionary<string, object> { { "", 42 } }));
    }

    [UnityTest]
    public IEnumerator CollectionReference_AddAsync_InvalidDocumentDataType() {
      CollectionReference collection = db.Collection("a");
      yield return AwaitFaults(collection.AddAsync(42));
    }

    [Test]
    public void CollectionReference_Document_NullStringPath() {
      CollectionReference collection = TestCollection();
      Assert.Throws<ArgumentNullException>(() => collection.Document(null));
    }

    [Test]
    public void CollectionReference_Document_EmptyStringPath() {
      CollectionReference collection = TestCollection();
      Assert.Throws<ArgumentException>(() => collection.Document(""));
    }

    [Test]
    public void CollectionReference_Document_EvenNumberOfPathSegments() {
      CollectionReference collection = TestCollection();
      Assert.Throws<ArgumentException>(() => collection.Document("b/c"));
    }

    [Test]
    public void DocumentReference_Collection_NullStringPath() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => doc.Collection(null));
    }

    [Test]
    public void DocumentReference_Collection_EmptyStringPath() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentException>(() => doc.Collection(""));
    }

    [Test]
    public void DocumentReference_Collection_EvenNumberOfPathSegments() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentException>(() => doc.Collection("a/b"));
    }

    [Test]
    public void DocumentReference_Listen_1Arg_NullCallback() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => doc.Listen(null));
    }

    [Test]
    public void DocumentReference_Listen_2Args_NullCallback() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => doc.Listen(MetadataChanges.Include, null));
    }

    [Test]
    public void DocumentReference_SetAsync_NullDocumentData() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => doc.SetAsync(null, null));
    }

    [UnityTest]
    public IEnumerator DocumentReference_SetAsync_DocumentDataWithEmptyKey() {
      DocumentReference doc = TestDocument();
      yield return AwaitFaults(doc.SetAsync(new Dictionary<string, object> { { "", 42 } }, null));
    }

    [UnityTest]
    public IEnumerator DocumentReference_SetAsync_InvalidDocumentDataType() {
      DocumentReference doc = TestDocument();
      yield return AwaitFaults(doc.SetAsync(42, null));
    }

    [Test]
    public void DocumentReference_UpdateAsync_NullStringKeyedDictionary() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() =>
                                               doc.UpdateAsync((IDictionary<string, object>)null));
    }

    [UnityTest]
    public IEnumerator DocumentReference_UpdateAsync_EmptyStringKeyedDictionary() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitSuccess(doc.UpdateAsync(new Dictionary<string, object>()));
    }

    [UnityTest]
    public IEnumerator DocumentReference_UpdateAsync_StringKeyedDictionaryWithEmptyKey() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitFaults(doc.UpdateAsync(new Dictionary<string, object> { { "", 42 } }));
    }

    [Test]
    public void DocumentReference_UpdateAsync_NullStringField() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => doc.UpdateAsync(null, 42));
    }

    [UnityTest]
    public IEnumerator DocumentReference_UpdateAsync_EmptyStringField() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitFaults(doc.UpdateAsync("", 42));
    }

    [Test]
    public void DocumentReference_UpdateAsync_NullFieldPathKeyedDictionary() {
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(
          () => doc.UpdateAsync((IDictionary<FieldPath, object>)null));
    }

    [UnityTest]
    public IEnumerator DocumentReference_UpdateAsync_EmptyFieldPathKeyedDictionary() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitSuccess(doc.UpdateAsync(new Dictionary<FieldPath, object>()));
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_ContainsField_NullStringPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        Assert.Throws<ArgumentNullException>(() => snapshot.ContainsField((string)null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_ContainsField_EmptyStringPath() {
      var steps = RunWithTestDocumentSnapshot(
          snapshot => { Assert.Throws<ArgumentException>(() => snapshot.ContainsField("")); });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_ContainsField_NullFieldPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        Assert.Throws<ArgumentNullException>(() => snapshot.ContainsField((FieldPath)null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_GetValue_NullStringPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        Assert.Throws<ArgumentNullException>(
            () => snapshot.GetValue<object>((string)null, ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_GetValue_EmptyStringPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        Assert.Throws<ArgumentException>(
            () => snapshot.GetValue<object>("", ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_GetValue_NullFieldPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        Assert.Throws<ArgumentNullException>(
            () => snapshot.GetValue<object>((FieldPath)null, ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_TryGetValue_NullStringPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        object value = null;
        Assert.Throws<ArgumentNullException>(
            () => snapshot.TryGetValue((string)null, out value, ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_TryGetValue_EmptyStringPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        object value = null;
        Assert.Throws<ArgumentException>(
            () => snapshot.TryGetValue("", out value, ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator DocumentSnapshot_TryGetValue_NullFieldPath() {
      var steps = RunWithTestDocumentSnapshot(snapshot => {
        object value = null;
        Assert.Throws<ArgumentNullException>(
            () => snapshot.TryGetValue((FieldPath)null, out value, ServerTimestampBehavior.None));
      });
      foreach (var step in steps) yield return step;
    }

    /**
     * Creates a `DocumentSnapshot` then invokes the given action with it synchronously in the
     * calling thread. This enables the caller to use standard asserts since any exceptions
     * they throw will be thrown in the calling thread's context and bubble up to the test runner.
     */
    private IEnumerable RunWithTestDocumentSnapshot(Action<DocumentSnapshot> action) {
      DocumentReference doc = TestDocument();
      doc.SetAsync(TestData());
      Task<DocumentSnapshot> task = doc.GetSnapshotAsync(Source.Cache);
      yield return AwaitSuccess(task);
      DocumentSnapshot snapshot = task.Result;
      action(snapshot);
    }

    [Test]
    public void FieldPath_Constructor_NullStringArray() {
      Assert.Throws<ArgumentNullException>(() => new FieldPath(null));
    }

    [Test]
    public void FieldPath_Constructor_StringArrayWithNullElement() {
      Assert.Throws<ArgumentException>(() => new FieldPath(new string[] { null }));
    }

    [Test]
    public void FieldPath_Constructor_EmptyStringArray() {
      Assert.Throws<ArgumentException>(() => new FieldPath(new string[0]));
    }

    [Test]
    public void FieldPath_Constructor_StringArrayWithEmptyString() {
      Assert.Throws<ArgumentException>(() => new FieldPath(new string[] { "" }));
    }

    [Test]
    public void FieldValue_ArrayRemove_NullArray() {
      Assert.Throws<ArgumentNullException>(() => FieldValue.ArrayRemove(null));
    }

    [Test]
    public void FieldValue_ArrayUnion_NullArray() {
      Assert.Throws<ArgumentNullException>(() => FieldValue.ArrayUnion(null));
    }

    [Test]
    public void FirebaseFirestore_GetInstance_Null_App() {
      Assert.Throws<ArgumentNullException>(() => FirebaseFirestore.GetInstance((FirebaseApp)null));
    }
    
    [Test]
    public void FirebaseFirestore_GetInstance_Null_Database_Name() {
      Assert.Throws<ArgumentNullException>(() => FirebaseFirestore.GetInstance((string)null));
    }

    [Test]
    public void FirebaseFirestore_GetInstance_Null_App_With_Database_Name() {
      Assert.Throws<ArgumentNullException>(() => FirebaseFirestore.GetInstance((FirebaseApp)null,"a"));
    }

    [Test]
    public void FirebaseFirestore_GetInstance_App_With_Null_Database_Name() {
      FirebaseApp app = FirebaseApp.DefaultInstance;
      Assert.Throws<ArgumentNullException>(() => FirebaseFirestore.GetInstance(app,(string)null));
    }

    [Test]
    public void FirebaseFirestore_GetInstance_DisposedApp() {
      FirebaseApp disposedApp = FirebaseApp.Create(db.App.Options, "test-getinstance-disposedapp");
      disposedApp.Dispose();
      Assert.Throws<ArgumentException>(() => FirebaseFirestore.GetInstance(disposedApp));
    }

    [Test]
    public void FirebaseFirestore_Collection_NullStringPath() {
      Assert.Throws<ArgumentNullException>(() => db.Collection(null));
    }

    [Test]
    public void FirebaseFirestore_Collection_EmptyStringPath() {
      Assert.Throws<ArgumentException>(() => db.Collection(""));
    }

    [Test]
    public void FirebaseFirestore_Collection_EvenNumberOfPathSegments() {
      Assert.Throws<ArgumentException>(() => db.Collection("a/b"));
    }

    [Test]
    public void FirebaseFirestore_CollectionGroup_NullCollectionId() {
      Assert.Throws<ArgumentNullException>(() => db.CollectionGroup(null));
    }

    [Test]
    public void FirebaseFirestore_CollectionGroup_EmptyCollectionId() {
      Assert.Throws<ArgumentException>(() => db.CollectionGroup(""));
    }

    [Test]
    public void FirebaseFirestore_CollectionGroup_CollectionIdContainsSlash() {
      Assert.Throws<ArgumentException>(() => db.CollectionGroup("a/b"));
    }

    [Test]
    public void FirebaseFirestore_Document_NullStringPath() {
      Assert.Throws<ArgumentNullException>(() => db.Document(null));
    }

    [Test]
    public void FirebaseFirestore_Document_EmptyStringPath() {
      Assert.Throws<ArgumentException>(() => db.Document(""));
    }

    [Test]
    public void FirebaseFirestore_Document_OddNumberOfPathSegments() {
      Assert.Throws<ArgumentException>(() => db.Document("a/b/c"));
    }

    [Test]
    public void FirebaseFirestore_ListenForSnapshotsInSync_NullCallback() {
      Assert.Throws<ArgumentNullException>(() => db.ListenForSnapshotsInSync(null));
    }

    [Test]
    public void FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_NullCallback() {
      Assert.Throws<ArgumentNullException>(() => db.RunTransactionAsync(null));
    }

    [Test]
    public void FirebaseFirestore_RunTransactionAsync_WithTypeParameter_NullCallback() {
      Assert.Throws<ArgumentNullException>(() => db.RunTransactionAsync<object>(null));
    }

    [Test]
    public void FirebaseFirestoreSettings_Host_Null() {
      FirebaseFirestoreSettings settings = db.Settings;
      Assert.Throws<ArgumentNullException>(() => settings.Host = null);
    }

    [Test]
    public void FirebaseFirestoreSettings_Host_EmptyString() {
      FirebaseFirestoreSettings settings = db.Settings;
      Assert.Throws<ArgumentException>(() => settings.Host = "");
    }

    [Test]
    public void Query_EndAt_NullDocumentSnapshot() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.EndAt((DocumentSnapshot)null));
    }

    [Test]
    public void Query_EndAt_NullArray() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.EndAt((object[])null));
    }

    [Test]
    public void Query_EndAt_ArrayWithNullElement() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.EndAt(new object[] { null }));
    }

    [Test]
    public void Query_EndBefore_NullDocumentSnapshot() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.EndBefore((DocumentSnapshot)null));
    }

    [Test]
    public void Query_EndBefore_NullArray() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.EndBefore((object[])null));
    }

    [Test]
    public void Query_Limit_0() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.Limit(0));
    }

    [Test]
    public void Query_Limit_Negative1() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.Limit(-1));
    }

    [Test]
    public void Query_LimitToLast_0() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.LimitToLast(0));
    }

    [Test]
    public void Query_LimitToLast_Negative1() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.LimitToLast(-1));
    }

    [Test]
    public void Query_OrderBy_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.OrderBy((string)null));
    }

    [Test]
    public void Query_OrderBy_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.OrderBy(""));
    }

    [Test]
    public void Query_OrderBy_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.OrderBy((FieldPath)null));
    }

    [Test]
    public void Query_OrderByDescending_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.OrderByDescending((string)null));
    }

    [Test]
    public void Query_OrderByDescending_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.OrderByDescending(""));
    }

    [Test]
    public void Query_OrderByDescending_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.OrderByDescending((FieldPath)null));
    }

    [Test]
    public void Query_StartAfter_NullDocumentSnapshot() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.StartAfter((DocumentSnapshot)null));
    }

    [Test]
    public void Query_StartAfter_NullArray() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.StartAfter((object[])null));
    }

    [Test]
    public void Query_StartAt_NullDocumentSnapshot() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.StartAt((DocumentSnapshot)null));
    }

    [Test]
    public void Query_StartAt_NullArray() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.StartAt((object[])null));
    }

    [Test]
    public void Query_StartAt_ArrayWithNullElement() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.StartAt(new object[] { null }));
    }

    [Test]
    public void Query_WhereArrayContains_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereArrayContains((FieldPath)null, ""));
    }

    [Test]
    public void Query_WhereArrayContains_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereArrayContains((string)null, ""));
    }

    [Test]
    public void Query_WhereArrayContains_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereArrayContains("", 42));
    }

    [Test]
    public void Query_WhereArrayContainsAny_NullFieldPath() {
      Query query = TestCollection();
      List<object> values = new List<object> { "" };
      Assert.Throws<ArgumentNullException>(
          () => query.WhereArrayContainsAny((FieldPath)null, values));
    }

    [Test]
    public void Query_WhereArrayContainsAny_NonNullFieldPath_NullValues() {
      Query query = TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      Assert.Throws<ArgumentNullException>(() => query.WhereArrayContainsAny(fieldPath, null));
    }

    [Test]
    public void Query_WhereArrayContainsAny_NullPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { "" };
      Assert.Throws<ArgumentNullException>(() => query.WhereArrayContainsAny((string)null, values));
    }

    [Test]
    public void Query_WhereArrayContainsAny_EmptyPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { "" };
      Assert.Throws<ArgumentException>(() => query.WhereArrayContainsAny("", values));
    }

    [Test]
    public void Query_WhereArrayContainsAny_NonNullPathString_NullValues() {
      Query query = TestCollection();
      string pathString = "a/b";
      Assert.Throws<ArgumentNullException>(() => query.WhereArrayContainsAny(pathString, null));
    }

    [Test]
    public void Query_WhereEqualTo_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereEqualTo((string)null, 42));
    }

    [Test]
    public void Query_WhereEqualTo_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereEqualTo("", 42));
    }

    [Test]
    public void Query_WhereEqualTo_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereEqualTo((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereGreaterThan_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereGreaterThan((string)null, 42));
    }

    [Test]
    public void Query_WhereGreaterThan_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereGreaterThan("", 42));
    }

    [Test]
    public void Query_WhereGreaterThan_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereGreaterThan((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereGreaterThanOrEqualTo_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereGreaterThanOrEqualTo((string)null, 42));
    }

    [Test]
    public void Query_WhereGreaterThanOrEqualTo_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereGreaterThanOrEqualTo("", 42));
    }

    [Test]
    public void Query_WhereGreaterThanOrEqualTo_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(
          () => query.WhereGreaterThanOrEqualTo((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereIn_NullFieldPath() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentNullException>(() => query.WhereIn((FieldPath)null, values));
    }

    [Test]
    public void Query_WhereIn_NonNullFieldPath_NullValues() {
      Query query = TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      Assert.Throws<ArgumentNullException>(() => query.WhereIn(fieldPath, null));
    }

    [Test]
    public void Query_WhereIn_NullPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentNullException>(() => query.WhereIn((string)null, values));
    }

    [Test]
    public void Query_WhereIn_EmptyPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentException>(() => query.WhereIn("", values));
    }

    [Test]
    public void Query_WhereIn_NonNullPathString_NullValues() {
      Query query = TestCollection();
      string fieldPath = "a/b";
      Assert.Throws<ArgumentNullException>(() => query.WhereIn(fieldPath, null));
    }

    [Test]
    public void Query_WhereLessThan_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereLessThan((string)null, 42));
    }

    [Test]
    public void Query_WhereLessThan_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereLessThan("", 42));
    }

    [Test]
    public void Query_WhereLessThan_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereLessThan((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereLessThanOrEqualTo_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereLessThanOrEqualTo((string)null, 42));
    }

    [Test]
    public void Query_WhereLessThanOrEqualTo_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereLessThanOrEqualTo("", 42));
    }

    [Test]
    public void Query_WhereLessThanOrEqualTo_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereLessThanOrEqualTo((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereNotEqualTo_NullPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereNotEqualTo((string)null, 42));
    }

    [Test]
    public void Query_WhereNotEqualTo_EmptyPathString() {
      Query query = TestCollection();
      Assert.Throws<ArgumentException>(() => query.WhereNotEqualTo("", 42));
    }

    [Test]
    public void Query_WhereNotEqualTo_NullFieldPath() {
      Query query = TestCollection();
      Assert.Throws<ArgumentNullException>(() => query.WhereNotEqualTo((FieldPath)null, 42));
    }

    [Test]
    public void Query_WhereNotIn_NullFieldPath() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentNullException>(() => query.WhereNotIn((FieldPath)null, values));
    }

    [Test]
    public void Query_WhereNotIn_NonNullFieldPath_NullValues() {
      Query query = TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      Assert.Throws<ArgumentNullException>(() => query.WhereNotIn(fieldPath, null));
    }

    [Test]
    public void Query_WhereNotIn_NullPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentNullException>(() => query.WhereNotIn((string)null, values));
    }

    [Test]
    public void Query_WhereNotIn_EmptyPathString() {
      Query query = TestCollection();
      List<object> values = new List<object> { 42 };
      Assert.Throws<ArgumentException>(() => query.WhereNotIn("", values));
    }

    [Test]
    public void Query_WhereNotIn_NonNullPathString_NullValues() {
      Query query = TestCollection();
      string fieldPath = "a/b";
      Assert.Throws<ArgumentNullException>(() => query.WhereNotIn(fieldPath, null));
    }

    [UnityTest]
    public IEnumerator Transaction_Delete_NullDocumentReference() {
      IEnumerable steps = RunWithTransaction(
          transaction => { Assert.Throws<ArgumentNullException>(() => transaction.Delete(null)); });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_GetSnapshotAsync_NullDocumentReference() {
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(() => transaction.GetSnapshotAsync(null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Set_NullDocumentReference() {
      object documentData = TestData();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(() => transaction.Set(null, documentData, null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Set_NullDocumentData() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(() => transaction.Set(doc, null, null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Set_DocumentDataWithEmptyKey() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentException>(
            () => transaction.Set(doc, new Dictionary<string, object> { { "", 42 } }, null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Set_InvalidDocumentDataType() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentException>(() => transaction.Set(doc, 42, null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NullDocumentReference_NonNullStringKeyDictionary() {
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(
            () => transaction.Update(null, new Dictionary<string, object> { { "key", 42 } }));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NonNullDocumentReference_NullStringKeyDictionary() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(
            () => transaction.Update(doc, (IDictionary<string, object>)null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NonNullDocumentReference_EmptyStringKeyDictionary() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitSuccess(db.RunTransactionAsync(transaction => {
        return transaction.GetSnapshotAsync(doc).ContinueWith(
            snapshot => { transaction.Update(doc, new Dictionary<string, object>()); });
      }));
    }

    [UnityTest]
    public IEnumerator
    Transaction_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentException>(
            () => transaction.Update(doc, new Dictionary<string, object> { { "", 42 } }));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NullDocumentReference_NonNullFieldString() {
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(() => transaction.Update(null, "fieldName", 42));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NonNullDocumentReference_NullFieldString() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(() => transaction.Update(doc, (string)null, 42));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NullDocumentReference_NonNullFieldPathKeyDictionary() {
      var nonNullFieldPathKeyDictionary =
          new Dictionary<FieldPath, object> { { new FieldPath(new string[] { "a", "b" }), 42 } };
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(
            () => transaction.Update(null, nonNullFieldPathKeyDictionary));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NonNullDocumentReference_NullFieldPathKeyDictionary() {
      DocumentReference doc = TestDocument();
      IEnumerable steps = RunWithTransaction(transaction => {
        Assert.Throws<ArgumentNullException>(
            () => transaction.Update(doc, (IDictionary<FieldPath, object>)null));
      });
      foreach (var step in steps) yield return step;
    }

    [UnityTest]
    public IEnumerator Transaction_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary() {
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      yield return AwaitSuccess(db.RunTransactionAsync(transaction => {
        return transaction.GetSnapshotAsync(doc).ContinueWith(
            snapshot => { transaction.Update(doc, new Dictionary<FieldPath, object>()); });
      }));
    }

    /**
     * Starts a transaction and invokes the given action with the `Transaction` object synchronously
     * in the calling thread. This enables the caller to use standard asserts since any exceptions
     * they throw will be thrown in the calling thread's context and bubble up to the test runner.
     */
    private IEnumerable RunWithTransaction(Action<Transaction> action) {
      DocumentReference doc = TestDocument();
      var taskCompletionSource = new TaskCompletionSource<object>();
      Transaction capturedTransaction = null;

      Task transactionTask = db.RunTransactionAsync(lambdaTransaction => {
        Interlocked.Exchange(ref capturedTransaction, lambdaTransaction);
        return taskCompletionSource.Task;
      });

      try {
        Transaction transaction = null;
        while (true) {
          transaction = Interlocked.Exchange(ref capturedTransaction, null);
          if (transaction != null) {
            break;
          }
          yield return null;
        }
        action(transaction);
      } finally {
        taskCompletionSource.SetResult(null);
      }

      yield return AwaitSuccess(transactionTask);
    }

    [Test]
    public void WriteBatch_Delete_NullDocumentReference() {
      WriteBatch writeBatch = db.StartBatch();
      Assert.Throws<ArgumentNullException>(() => writeBatch.Delete(null));
    }

    [Test]
    public void WriteBatch_Set_NullDocumentReference() {
      WriteBatch writeBatch = db.StartBatch();
      var nonNullDocumentData = TestData();
      Assert.Throws<ArgumentNullException>(() => writeBatch.Set(null, nonNullDocumentData, null));
    }

    [Test]
    public void WriteBatch_Set_NullDocumentData() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => writeBatch.Set(doc, null, null));
    }

    [Test]
    public void WriteBatch_Set_DocumentDataWithEmptyKey() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentException>(
          () => writeBatch.Set(doc, new Dictionary<string, object> { { "", 42 } }, null));
    }

    [Test]
    public void WriteBatch_Set_InvalidDocumentDataType() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentException>(() => writeBatch.Set(doc, 42, null));
    }

    [Test]
    public void WriteBatch_Update_NullDocumentReference_NonNullStringKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      Assert.Throws<ArgumentNullException>(
          () => writeBatch.Update(null, new Dictionary<string, object> { { "key", 42 } }));
    }

    [Test]
    public void WriteBatch_Update_NonNullDocumentReference_NullStringKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(
          () => writeBatch.Update(doc, (IDictionary<string, object>)null));
    }

    [UnityTest]
    public IEnumerator WriteBatch_Update_NonNullDocumentReference_EmptyStringKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      writeBatch.Update(doc, new Dictionary<string, object>());
      yield return AwaitSuccess(writeBatch.CommitAsync());
    }

    [Test]
    public void WriteBatch_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentException>(
          () => writeBatch.Update(doc, new Dictionary<string, object> { { "", 42 } }));
    }

    [Test]
    public void WriteBatch_Update_NullDocumentReference_NonNullFieldString() {
      WriteBatch writeBatch = db.StartBatch();
      Assert.Throws<ArgumentNullException>(() => writeBatch.Update(null, "fieldName", 42));
    }

    [Test]
    public void WriteBatch_Update_NonNullDocumentReference_NullFieldString() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(() => writeBatch.Update(doc, (string)null, 42));
    }

    [Test]
    public void WriteBatch_Update_NullDocumentReference_NonNullFieldPathKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      var nonNullFieldPathKeyDictionary =
          new Dictionary<FieldPath, object> { { new FieldPath(new string[] { "a", "b" }), 42 } };
      Assert.Throws<ArgumentNullException>(
          () => writeBatch.Update(null, nonNullFieldPathKeyDictionary));
    }

    [Test]
    public void WriteBatch_Update_NonNullDocumentReference_NullFieldPathKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      Assert.Throws<ArgumentNullException>(
          () => writeBatch.Update(doc, (IDictionary<FieldPath, object>)null));
    }

    [UnityTest]
    public IEnumerator WriteBatch_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary() {
      WriteBatch writeBatch = db.StartBatch();
      DocumentReference doc = TestDocument();
      yield return AwaitSuccess(doc.SetAsync(TestData(), null));
      writeBatch.Update(doc, new Dictionary<FieldPath, object>());
      yield return AwaitSuccess(writeBatch.CommitAsync());
    }

    [Test]
    public void FirebaseFirestore_LoadBundleAsync_NullBundle() {
      Assert.Throws<ArgumentNullException>(() => db.LoadBundleAsync(null as string));
      Assert.Throws<ArgumentNullException>(
          () => db.LoadBundleAsync(null as string, (sender, progress) => {}));
      Assert.Throws<ArgumentNullException>(() => db.LoadBundleAsync(null as byte[]));
      Assert.Throws<ArgumentNullException>(
          () => db.LoadBundleAsync(null as byte[], (sender, progress) => {}));
    }

    [Test]
    public void FirebaseFirestore_LoadBundleAsync_NonNullBundle_NullHandler() {
      Assert.Throws<ArgumentNullException>(() => db.LoadBundleAsync("", null));
      Assert.Throws<ArgumentNullException>(() => db.LoadBundleAsync(new byte[] {}, null));
    }

    [Test]
    public void FirebaseFirestore_GetNamedQueryAsync_NullQueryName() {
      Assert.Throws<ArgumentNullException>(() => db.GetNamedQueryAsync(null));
    }
  }
}
