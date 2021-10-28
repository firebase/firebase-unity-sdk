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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Firebase;
using Firebase.Firestore;
using Firebase.Sample.Firestore;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static Tests.TestAsserts;

namespace Tests {
  internal static class BundleBuilder {
    private static List<string> BundleTemplate() {
      string metadata =
          @"{
   ""metadata"":{
      ""id"":""test-bundle"",
      ""createTime"":{
        ""seconds"":1001,
        ""nanos"":9999
      },
      ""version"":1,
      ""totalDocuments"":2,
      ""totalBytes"":{totalBytes}
    }
  }";
      string namedQuery1 =
          @"{
   ""namedQuery"":{
      ""name"":""limit"",
      ""readTime"":{
        ""seconds"":1000,
        ""nanos"":9999
      },
      ""bundledQuery"":{
        ""parent"":""projects/{projectId}/databases/(default)/documents"",
        ""structuredQuery"":{
          ""from"":[
          {
            ""collectionId"":""coll-1""
          }
          ],
          ""orderBy"":[
          {
            ""field"":{
              ""fieldPath"":""bar""
            },
            ""direction"":""DESCENDING""
          },
          {
            ""field"":{
              ""fieldPath"":""__name__""
            },
            ""direction"":""DESCENDING""
          }
          ],
          ""limit"":{
            ""value"":1
          }
        },
        ""limitType"":""FIRST""
      }
    }
  }";
      string namedQuery2 =
          @"{
   ""namedQuery"":{
      ""name"":""limit-to-last"",
      ""readTime"":{
        ""seconds"":1000,
        ""nanos"":9999
      },
      ""bundledQuery"":{
        ""parent"":""projects/{projectId}/databases/(default)/documents"",
        ""structuredQuery"":{
          ""from"":[
          {
            ""collectionId"":""coll-1""
          }
          ],
          ""orderBy"":[
          {
            ""field"":{
              ""fieldPath"":""bar""
            },
            ""direction"":""DESCENDING""
          },
          {
            ""field"":{
              ""fieldPath"":""__name__""
            },
            ""direction"":""DESCENDING""
          }
          ],
          ""limit"":{
            ""value"":1
          }
        },
        ""limitType"":""LAST""
      }
    }
  }";
      string documentMetadata1 =
          @"{
      ""documentMetadata"":{
        ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/a"",
        ""readTime"":{
          ""seconds"":1000,
          ""nanos"":9999
        },
        ""exists"":true
      }
    }";

      string document1 =
          @"{
   ""document"":{
      ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/a"",
      ""createTime"":{
        ""seconds"":1,
        ""nanos"":9
      },
      ""updateTime"":{
        ""seconds"":1,
        ""nanos"":9
      },
      ""fields"":{
        ""k"":{
          ""stringValue"":""a""
        },
        ""bar"":{
          ""integerValue"":1
        }
      }
    }
  }";

      string documentMetadata2 =
          @"{
   ""documentMetadata"":{
      ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/b"",
      ""readTime"":{
        ""seconds"":1000,
        ""nanos"":9999
      },
      ""exists"":true
    }
  }";

      string document2 =
          @"{
   ""document"":{
      ""name"":""projects/{projectId}/databases/(default)/documents/coll-1/b"",
      ""createTime"":{
        ""seconds"":1,
        ""nanos"":9
      },
      ""updateTime"":{
        ""seconds"":1,
        ""nanos"":9
      },
      ""fields"":{
        ""k"":{
          ""stringValue"":""b""
        },
        ""bar"":{
          ""integerValue"":2
        }
      }
    }
  }";

      return new List<string> { metadata,  namedQuery1,       namedQuery2, documentMetadata1,
                                document1, documentMetadata2, document2 };
    }

    internal static string CreateBundle(string projectId) {
      StringBuilder stringBuilder = new StringBuilder();

      var template = BundleTemplate();
      for (int i = 1; i < template.Count; ++i) {
        string element = template [i].Replace("{projectId}", projectId);
        stringBuilder.Append(Encoding.UTF8.GetBytes(element).Length);
        stringBuilder.Append(element);
      }

      string content = stringBuilder.ToString();
      string metadata =
          template [0].Replace("{totalBytes}", Encoding.UTF8.GetBytes(content).Length.ToString());
      return Encoding.UTF8.GetBytes(metadata).Length.ToString() + metadata + content;
    }
  }

  public class BundlesTests : FirestoreIntegrationTests {
    private void VerifySuccessProgress(LoadBundleTaskProgress progress) {
      Assert.That(progress.State, Is.EqualTo(LoadBundleTaskProgress.LoadBundleTaskState.Success));
      Assert.That(progress.BytesLoaded, Is.EqualTo(progress.TotalBytes));
      Assert.That(progress.DocumentsLoaded, Is.EqualTo(progress.TotalDocuments));
    }

    private void VerifyErrorProgress(LoadBundleTaskProgress progress) {
      Assert.That(progress.State, Is.EqualTo(LoadBundleTaskProgress.LoadBundleTaskState.Error));
      Assert.That(progress.BytesLoaded, Is.EqualTo(0));
      Assert.That(progress.DocumentsLoaded, Is.EqualTo(0));
    }

    private void VerifyInProgressProgress(LoadBundleTaskProgress progress, int expectedDocuments) {
      Assert.That(progress.State,
                  Is.EqualTo(LoadBundleTaskProgress.LoadBundleTaskState.InProgress));
      Assert.That(progress.DocumentsLoaded, Is.EqualTo(expectedDocuments));
      Assert.That(progress.BytesLoaded, Is.LessThanOrEqualTo(progress.TotalBytes));
      Assert.That(progress.DocumentsLoaded, Is.LessThanOrEqualTo(progress.TotalDocuments));
    }

    private IEnumerator VerifyQueryResults() {
      {
        var snapshotTask = db.Collection("coll-1").GetSnapshotAsync(Source.Cache);
        yield return AwaitCompletion(snapshotTask);
        Assert.That(QuerySnapshotToValues(snapshotTask.Result),
                    Is.EquivalentTo(new List<Dictionary<string, object>> {
                      new Dictionary<string, object> { { "k", "a" }, { "bar", 1L } },
                      new Dictionary<string, object> { { "k", "b" }, { "bar", 2L } }
                    }));
      }

      {
        var limitQueryTask = db.GetNamedQueryAsync("limit");
        yield return AwaitSuccess(limitQueryTask);
        var querySnapshotTask = limitQueryTask.Result.GetSnapshotAsync(Source.Cache);
        yield return AwaitSuccess(querySnapshotTask);
        Assert.That(QuerySnapshotToValues(querySnapshotTask.Result),
                    Is.EquivalentTo(new List<Dictionary<string, object>> {
                      new Dictionary<string, object> { { "k", "b" }, { "bar", 2L } }
                    }));
      }

      {
        var limitToLastQueryTask = db.GetNamedQueryAsync("limit-to-last");
        yield return AwaitSuccess(limitToLastQueryTask);
        var querySnapshotTask = limitToLastQueryTask.Result.GetSnapshotAsync(Source.Cache);
        yield return AwaitSuccess(querySnapshotTask);
        Assert.That(QuerySnapshotToValues(querySnapshotTask.Result),
                    Is.EquivalentTo(new List<Dictionary<string, object>> {
                      new Dictionary<string, object> { { "k", "a" }, { "bar", 1L } }
                    }));
      }
    }

    protected new FirebaseFirestore db { get;
    set;
  }
  = FirebaseFirestore.DefaultInstance;

  [UnitySetUp]
  private IEnumerator ClearPersistenceAndRestart() {
    // Clear local persistence cache to make sure bundles loaded previously do not
    // interfere with current test case.
    yield return AwaitSuccess(db.TerminateAsync());
    yield return AwaitSuccess(db.ClearPersistenceAsync());
    db = FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance);
  }

  [UnityTest]
  public IEnumerator LoadBundleWithoutProgressUpdate_ShouldSucceed() {
    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var loadTask = db.LoadBundleAsync(bundle);
    yield return AwaitSuccess(loadTask);

    VerifySuccessProgress(loadTask.Result);
    yield return VerifyQueryResults();
  }

  [UnityTest]
  public IEnumerator LoadBundleWithProgressUpdate_ShouldSucceed() {
    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var progresses = new List<LoadBundleTaskProgress>();
    object eventSender = null;
    var loadTask = db.LoadBundleAsync(Encoding.UTF8.GetBytes(bundle), (sender, progress) => {
      eventSender = sender;
      progresses.Add(progress);
    });

    yield return AwaitSuccess(loadTask);
    Assert.That(eventSender, Is.SameAs(db));
    VerifySuccessProgress(loadTask.Result);

    Assert.That(progresses.Count, Is.EqualTo(4));
    VerifyInProgressProgress(progresses[0], 0);
    VerifyInProgressProgress(progresses[1], 1);
    VerifyInProgressProgress(progresses[2], 2);
    Assert.That(progresses[3], Is.EqualTo(loadTask.Result));

    yield return VerifyQueryResults();
  }

  [UnityTest]
  public IEnumerator LoadBundle_CanDeleteFirestoreFromProgressUpdate() {
    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var progresses = new List<LoadBundleTaskProgress>();

    var source = new TaskCompletionSource<bool>();
    try {
      var loadTask = db.LoadBundleAsync(bundle, (sender, progress) => {
        progresses.Add(progress);

        // Delete firestore before the final progress.
        if (progresses.Count == 3) {
          source.SetResult(true);
          db.App.Dispose();
        }
      });

      yield return AwaitSuccess(source.Task);

      Assert.That(progresses.Count, Is.EqualTo(3));
      VerifyInProgressProgress(progresses[0], 0);
      VerifyInProgressProgress(progresses[1], 1);
      VerifyInProgressProgress(progresses[2], 2);

    } finally {
      // Recreate DB such that other tests can run.
      db = FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance);
    }
  }

  [UnityTest]
  public IEnumerator LoadBundleASecondTime_Skips() {
    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var firstLoadTask = db.LoadBundleAsync(bundle);
    yield return AwaitSuccess(firstLoadTask);
    VerifySuccessProgress(firstLoadTask.Result);

    var progresses = new List<LoadBundleTaskProgress>();
    var secondLoadTask =
        db.LoadBundleAsync(bundle, (sender, progress) => { progresses.Add(progress); });

    yield return AwaitSuccess(secondLoadTask);
    VerifySuccessProgress(secondLoadTask.Result);

    Assert.That(progresses.Count, Is.EqualTo(1));
    Assert.That(progresses[0], Is.EqualTo(firstLoadTask.Result));

    yield return VerifyQueryResults();
  }

  [UnityTest]
  public IEnumerator LoadInvalidBundle_ReportsErrorProgress() {
    var invalidBundles = new List<string> { "invalid bundle obviously", "\"(╯°□°）╯︵ ┻━┻\"",
                                            Encoding.UTF8.GetString(new byte[] { 0xc3, 0x28 }) };

    foreach (var bundle in invalidBundles) {
      var progresses = new List<LoadBundleTaskProgress>();
      var loadTask =
          db.LoadBundleAsync(bundle, (sender, progress) => { progresses.Add(progress); });

      yield return AwaitFaults(loadTask);

      Assert.That(progresses.Count, Is.EqualTo(1));
      VerifyErrorProgress(progresses[0]);
    }
  }

  [UnityTest]
  public IEnumerator LoadedDocumentsAlreadyPulledFromBackend_ShouldNotOverwriteNewerVersion() {
    var collection = db.Collection("coll-1");
    collection.Document("a").SetAsync(new Dictionary<string, object> {
      { "k", "a" },
      { "bar", "newValueA" },
    });
    var writeTask = collection.Document("b").SetAsync(new Dictionary<string, object> {
      { "k", "b" },
      { "bar", "newValueB" },
    });
    yield return AwaitSuccess(writeTask);

    var accumulator = new EventAccumulator<QuerySnapshot>();
    collection.Listen(accumulator.Listener);
    yield return AwaitSuccess(accumulator.LastEventAsync());

    // The test bundle is holding ancient documents, so no events are generated as
    // a result. The case where a bundle has newer doc than cache can only be
    // tested in spec tests.
    accumulator.ThrowOnAnyEvent();

    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var loadTask = db.LoadBundleAsync(bundle);
    yield return AwaitSuccess(loadTask);

    VerifySuccessProgress(loadTask.Result);
    {
      var snapshotTask = collection.GetSnapshotAsync(Source.Cache);
      yield return AwaitCompletion(snapshotTask);
      Assert.That(QuerySnapshotToValues(snapshotTask.Result),
                  Is.EquivalentTo(new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { { "k", "a" }, { "bar", "newValueA" } },
                    new Dictionary<string, object> { { "k", "b" }, { "bar", "newValueB" } }
                  }));
    }

    {
      var limitQueryTask = db.GetNamedQueryAsync("limit");
      yield return AwaitSuccess(limitQueryTask);
      var querySnapshotTask = limitQueryTask.Result.GetSnapshotAsync(Source.Cache);
      yield return AwaitSuccess(querySnapshotTask);
      Assert.That(QuerySnapshotToValues(querySnapshotTask.Result),
                  Is.EquivalentTo(new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { { "k", "b" }, { "bar", "newValueB" } }
                  }));
    }

    {
      var limitToLastQueryTask = db.GetNamedQueryAsync("limit-to-last");
      yield return AwaitSuccess(limitToLastQueryTask);
      var querySnapshotTask = limitToLastQueryTask.Result.GetSnapshotAsync(Source.Cache);
      yield return AwaitSuccess(querySnapshotTask);
      Assert.That(QuerySnapshotToValues(querySnapshotTask.Result),
                  Is.EquivalentTo(new List<Dictionary<string, object>> {
                    new Dictionary<string, object> { { "k", "a" }, { "bar", "newValueA" } }
                  }));
    }
  }

  [UnityTest]
  public IEnumerator LoadedDocuments_ShouldNotBeGarbageCollectedRightAway() {
    db.Settings.PersistenceEnabled = false;

    string bundle = BundleBuilder.CreateBundle(db.App.Options.ProjectId);
    var loadTask = db.LoadBundleAsync(bundle);
    yield return AwaitSuccess(loadTask);
    VerifySuccessProgress(loadTask.Result);

    // Read a different collection. This will trigger GC.
    yield return AwaitSuccess(db.Collection("coll-other").GetSnapshotAsync());

    // Read the loaded documents, expecting them to exist in cache. With memory
    // GC, the documents would get GC-ed if we did not hold the document keys in
    // an "umbrella" target. See LocalStore for details.
    yield return VerifyQueryResults();
  }

  [UnityTest]
  public IEnumerator LoadBundleFromOtherProject_ShouldFail() {
    string bundle = BundleBuilder.CreateBundle("other-project");

    var progresses = new List<LoadBundleTaskProgress>();
    var loadTask = db.LoadBundleAsync(bundle, (sender, progress) => { progresses.Add(progress); });
    yield return AwaitFaults(loadTask);

    Assert.That(progresses.Count, Is.EqualTo(2));
    VerifyInProgressProgress(progresses[0], 0);
    VerifyErrorProgress(progresses[1]);
  }

  [UnityTest]
  public IEnumerator GetInvalideNamedQuery_ShouldReturnNull() {
    {
      var task = db.GetNamedQueryAsync("DOES_NOT_EXIST");
      yield return AwaitSuccess(task);
      Assert.That(task.Result, Is.Null);
    }
    {
      var task = db.GetNamedQueryAsync("");
      yield return AwaitSuccess(task);
      Assert.That(task.Result, Is.Null);
    }
    {
      var task = db.GetNamedQueryAsync("\xc3\x28");
      yield return AwaitSuccess(task);
      Assert.That(task.Result, Is.Null);
    }
  }

}
}
