// Copyright 2017 Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Firebase.Sample.DynamicLinks {
  using Firebase.Extensions;
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Text.RegularExpressions;
  using System.Threading.Tasks;
  using UnityEngine;

  using Firebase.DynamicLinks;

  public class UIHandlerAutomated : UIHandler {
    public string kAutomatedDomainUriPrefix = "REPLACE_WITH_YOUR_URI_PREFIX";

    private string urlHost;
    private Firebase.Sample.AutomatedTestRunner testRunner;

    public override void Start() {
      Func<Task>[] tests = {
        TestCreateLongLinkAsync,
// TODO(b/264533368) Enable theses tests when the issue has been fixed.
#if (UNITY_ANDROID || UNITY_IOS)
        // Link shortening does not work on desktop builds, so only test that for mobile.
        TestCreateShortLinkAsync,
        TestCreateUnguessableShortLinkAsync,
#endif  // !UNITY_EDITOR
      };
      testRunner = AutomatedTestRunner.CreateTestRunner(
        testsToRun: tests,
        logFunc: DebugLog
      );

      urlHost = kAutomatedDomainUriPrefix;
      base.kDomainUriPrefix = kAutomatedDomainUriPrefix;
      base.DisableUI();
      base.Start();
    }

    public override void Update() {
      base.Update();
      if (firebaseInitialized) testRunner.Update();
    }

    Task TestCreateLongLinkAsync() {
      Func<string, Dictionary<string, string>> extractUrlParams = (url) => {
        var pattern = new Regex("[?&](?<name>[^=]+)=(?<value>[^&]+)");
        var result = new Dictionary<string, string>();
        foreach (Match m in pattern.Matches(url)) {
          result.Add(m.Groups["name"].Value, m.Groups["value"].Value);
        }
        return result;
      };

      // Dynamic Links uses the Application identifier for some of the fields.
      // Note that on Windows and Linux desktop, this field will likely be empty.
      string identifier = Application.identifier;

      // This is taken from the values given in UIHandler.
      var expected =
        urlHost + "/?afl=https://google.com/fallback&" +
        "amv=12&apn=" + identifier + "&at=abcdefg&ct=hijklmno&" +
        "ibi=" + identifier + "&ifl=https://google.com/fallback&imv=1.2.3&" +
        "ipbi=" + identifier + "&" +
        "ipfl=https://google.com/fallbackipad&ius=mycustomscheme&link=https://google.com/abc&" +
        "pt=pq-rstuv&sd=My app is awesome!&si=https://google.com/someimage.jpg&st=My App!&" +
        "utm_campaign=mycampaign&utm_content=mycontent&utm_medium=mymedium&utm_source=mysource&" +
        "utm_term=myterm";
      // The order of URL parameters is different between desktop and mobile implementations, and
      // can't be relied upon.
      var expectedParams = extractUrlParams(expected);

      var source = new TaskCompletionSource<string>();
      try {
        var result = Uri.UnescapeDataString(CreateAndDisplayLongLink().ToString());
        var resultHost = new Regex("/\\?").Split(result)[0];
        var sameHost = resultHost == urlHost;
        var resultParams = extractUrlParams(result);
        var sameParams = expectedParams.Keys.All(k => resultParams.ContainsKey(k) &&
            object.Equals(expectedParams[k], resultParams[k]));

        if (sameHost && sameParams) {
          source.TrySetResult(result);
        } else {
          List<string> differences = new List<string>();
          differences.Add("The generated long link doesn't match the expected result.");
          if (!sameHost) {
            differences.Add(String.Format(
                "Url Host:\n" +
                "  Expected: {0}\n" +
                "  Actual: {1}", urlHost, resultHost));
          }
          if (!sameParams) {
            var diffParams = expectedParams.Keys.Where(k => !(resultParams.ContainsKey(k) &&
                object.Equals(expectedParams[k], resultParams[k])));
            foreach (var key in diffParams) {
              differences.Add(String.Format(
                  "{0}: \n" +
                  "  Expected: {1}\n" +
                  "  Actual: {2}", key, expectedParams[key],
                  resultParams.ContainsKey(key) ? resultParams[key] : "(missing key)"));
            }
          }
          source.TrySetException(new Exception(String.Join("\n", differences.ToArray())));
        }
      } catch (Exception e) {
        source.TrySetException(e);
      }

      return source.Task;
    }

    // This is an ad-hoc sanity check that the shortened URL looks like the correct host followed
    // by a bunch of alphanumeric characters as path.
    void CheckShortLinkIsValid(string result) {
      if (!Regex.IsMatch(result, urlHost + "/[a-zA-Z0-9]+")) {
        throw new Exception(
            String.Format("The generated link {0} doesn't look right", result));
      }
    }

    Task TestCreateShortLinkAsync() {
      return CreateAndDisplayShortLinkAsync().ContinueWithOnMainThread(task => {
        var result = task.Result.Url.ToString();
        CheckShortLinkIsValid(result);
        DebugLog(String.Format("TestCreateShortLink: {0}", result));
      });
    }

    Task TestCreateUnguessableShortLinkAsync() {
      return CreateAndDisplayUnguessableShortLinkAsync().ContinueWithOnMainThread(task => {
        var result = task.Result.Url.ToString();
        CheckShortLinkIsValid(result);
        DebugLog(String.Format("TestCreateUnguessableShortLink: {0}", result));
      });
    }
  }
}
