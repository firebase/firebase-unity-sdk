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

using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine.TestTools;
using NUnit.Framework;
using static Tests.TestAsserts;

namespace Tests {
  // Tests that do not belong to other well categorized testing classes.
  public class MiscellaneousTests : FirestoreIntegrationTests {
    [Test]
    public void TestCanTraverseCollectionsAndDocuments() {
      // doc path from root Firestore.
      Assert.That(db.Document("a/b/c/d").Path, Is.EqualTo("a/b/c/d"));

      // collection path from root Firestore.
      Assert.That(db.Collection("a/b/c").Document("d").Path, Is.EqualTo("a/b/c/d"));

      // doc path from CollectionReference.
      Assert.That(db.Collection("a").Document("b/c/d").Path, Is.EqualTo("a/b/c/d"));

      // collection path from DocumentReference.
      Assert.That(db.Document("a/b").Collection("c/d/e").Path, Is.EqualTo("a/b/c/d/e"));
    }

    [Test]
    public void TestCanTraverseCollectionAndDocumentParents() {
      CollectionReference collection = db.Collection("a/b/c");
      Assert.That(collection.Path, Is.EqualTo("a/b/c"));

      DocumentReference doc = collection.Parent;
      Assert.That(doc.Path, Is.EqualTo("a/b"));

      collection = doc.Parent;
      Assert.That(collection.Path, Is.EqualTo("a"));

      DocumentReference invalidDoc = collection.Parent;
      Assert.That(invalidDoc, Is.Null);
    }

    [Test]
    public void Firestore_DefaultInstanceShouldBeStable() {
      FirebaseFirestore db1 = FirebaseFirestore.DefaultInstance;
      FirebaseFirestore db2 = FirebaseFirestore.DefaultInstance;
      Assert.That(db1, Is.SameAs(db2));
      Assert.That(db1, Is.SameAs(db1.Collection("a").WhereEqualTo("x", 1).Firestore));
      Assert.That(db2, Is.SameAs(db2.Document("a/b").Firestore));
    }

    [UnityTest]
    public IEnumerator DocumentReference_TasksFailProperly() {
      var docWithInvalidName = TestCollection().Document("__badpath__");
      var fieldPathData = new Dictionary<FieldPath, object> { { new FieldPath("key"), 42 } };

      {
        Task task = docWithInvalidName.DeleteAsync();
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task task = docWithInvalidName.UpdateAsync(TestData(0));
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task task = docWithInvalidName.UpdateAsync("fieldName", 42);
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task task = docWithInvalidName.UpdateAsync(fieldPathData);
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task task = docWithInvalidName.SetAsync(TestData(), SetOptions.MergeAll);
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task<DocumentSnapshot> task = docWithInvalidName.GetSnapshotAsync();
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task<DocumentSnapshot> task = docWithInvalidName.GetSnapshotAsync(Source.Default);
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        ListenerRegistration listenerRegistration = docWithInvalidName.Listen(snap => {});
        yield return AwaitCompletion(listenerRegistration.ListenerTask);
        AssertTaskFaulted(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                          "__badpath__");
        listenerRegistration.Stop();
      }
    }

    [UnityTest]
    public IEnumerator CollectionReference_TasksFailProperly() {
      var collectionWithInvalidName = TestCollection().Document("__badpath__").Collection("sub");
      {
        Task<QuerySnapshot> task = collectionWithInvalidName.GetSnapshotAsync();
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task<QuerySnapshot> task = collectionWithInvalidName.GetSnapshotAsync(Source.Default);
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        Task<DocumentReference> task = collectionWithInvalidName.AddAsync(TestData(0));
        yield return AwaitCompletion(task);
        AssertTaskFaulted(task, FirestoreError.InvalidArgument, "__badpath__");
      }
      {
        ListenerRegistration listenerRegistration = collectionWithInvalidName.Listen(snap => {});
        yield return AwaitCompletion(listenerRegistration.ListenerTask);
        AssertTaskFaulted(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                          "__badpath__");
        listenerRegistration.Stop();
      }
      {
        ListenerRegistration listenerRegistration =
            collectionWithInvalidName.Listen(MetadataChanges.Include, snap => {});
        yield return AwaitCompletion(listenerRegistration.ListenerTask);
        AssertTaskFaulted(listenerRegistration.ListenerTask, FirestoreError.InvalidArgument,
                          "__badpath__");
        listenerRegistration.Stop();
      }
    }
  }
}
