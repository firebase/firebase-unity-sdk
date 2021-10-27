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
using System.Threading;
using System.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Firestore;
using NUnit.Framework;
using UnityEngine.TestTools;

using static Tests.TestAsserts;

namespace Tests {

  public abstract class FirestoreIntegrationTests {
    private const int AUTO_ID_LENGTH = 20;
    private const string AUTO_ID_ALPHABET =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private static System.Random random = new System.Random();
    private static string AutoId() {
      string result = "";
      for (int i = 0; i < AUTO_ID_LENGTH; i++) {
        result += AUTO_ID_ALPHABET[random.Next(0, AUTO_ID_ALPHABET.Length)];
      }

      return result;
    }

    protected int mainThreadId = -1;
    [UnitySetUp]
    protected IEnumerator CheckAndFixDependencies() {
      yield return AwaitSuccess(Firebase.FirebaseApp.CheckAndFixDependenciesAsync());
    }

    [UnitySetUp]
    protected IEnumerator InitializeMainThreadId() {
      yield return AwaitSuccess(Task.Run((() => {})).ContinueWithOnMainThread((task => {
        mainThreadId = Thread.CurrentThread.ManagedThreadId;
      })));
    }

    protected FirebaseFirestore db => FirebaseFirestore.DefaultInstance;

    protected CollectionReference TestCollection() {
      return db.Collection("test-collection_" + AutoId());
    }

    protected DocumentReference TestDocument() {
      return TestCollection().Document();
    }

    protected Dictionary<string, object> TestData(long n = 1) {
      return new Dictionary<string, object> {
        { "name", "room " + n },
        { "active", true },
        { "list", new List<object> { "one", 2L, "three", 4L } },
        { "metadata", new Dictionary<string, object> { { "createdAt", n },
                                                       { "deep",
                                                         new Dictionary<string, object> {
                                                           { "field", "deep-field-" + n },
                                                           { "nested-list",
                                                             new List<object> { "a", "b", "c" } },
                                                         } } } }
        // TODO(b/181775697): Add other types here too.
      };
    }

    protected static List<Dictionary<string, object>> QuerySnapshotToValues(QuerySnapshot snap) {
      List<Dictionary<string, object>> result = new List<Dictionary<string, object>>();
      foreach (DocumentSnapshot doc in snap) {
        result.Add(doc.ToDictionary());
      }
      return result;
    }
  }
}
