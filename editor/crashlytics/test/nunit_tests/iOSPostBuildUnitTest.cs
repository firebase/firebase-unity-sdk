/*
 * Copyright 2018 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

namespace Firebase.Crashlytics.Editor {
  using System;
  using System.Collections.Generic;
  using UnityEngine;
  using NUnit.Framework;

  /// <summary>
  /// Set up cases to put the iOSPostBuild through its paces.
  /// </summary>
  [TestFixture(false, "chmod u+x \"${PROJECT_DIR}/Pods/FirebaseCrashlytics/run\"\n" +
          "chmod u+x \"${PROJECT_DIR}/Pods/FirebaseCrashlytics/upload-symbols\"\n" +
          "\"${PROJECT_DIR}/Pods/FirebaseCrashlytics/run\" -gsp \"${PROJECT_DIR}/GoogleService-Info.plist\"")]
  public class iOSPostBuildUnitTest : CrashlyticsEditorTestBase {

    IFirebaseConfigurationStorage _storage;
    string _expRunScript;

    /// <summary>
    /// Setup the test with a particular apiKey setup
    /// </summary>
    public iOSPostBuildUnitTest(bool isDataSufficient, string expectedRunScript) : base() {
      _storage = new MockFirebaseConfigurationStore();
      _expRunScript = expectedRunScript;
    }

    /// <summary>
    /// Test that when we get the runscript body, given the context of the
    /// test setup that we properly insert or don't insert the api key.
    /// </summary>
    [Test]
    public void TestGetRunScriptBody() {
      string actualRunScript = iOSPostBuild.GetRunScriptBody(_storage);
      Assert.AreEqual(_expRunScript, actualRunScript);
    }
  }

  [TestFixture(true, "AbSoLUt.3RubBish")]
  [TestFixture(true, "The split delim is a period")]
  [TestFixture(true, "5.10.33.11")]
  [TestFixture(true, "2018.23f")]
  [TestFixture(false, "2019.1b2.bla")]
  [TestFixture(false, "2020.012.thefuture")]
  public class iOSPostBuildUnitBuildPhaseTest : CrashlyticsEditorTestBase {

    string TestGUID = "TestGUID";
    string TestRunScriptBody = "TestRunScriptBody";

    bool _isOldVersion;
    string _unityVersion;

    public iOSPostBuildUnitBuildPhaseTest(bool isOldVersion, string unityVersion) : base() {
      _isOldVersion = isOldVersion;
      _unityVersion = unityVersion;
    }

    /// <summary>
    /// Ensure we get the right Add Run Script method to call on PBXProj via
    /// reflection
    /// </summary>
    [Test]
    public void TestGetAddRunScriptMethodName() {
      iOSBuildPhaseMethodCall methodCall = iOSPostBuild.GetBuildPhaseMethodCall(_unityVersion, TestGUID, TestRunScriptBody);

      if (_isOldVersion) {
        Assert.AreEqual(methodCall.MethodName, "AppendShellScriptBuildPhase");
        Assert.AreEqual(methodCall.ArgumentTypes[0], typeof(IEnumerable<string>));

      } else {
        Assert.AreEqual(methodCall.MethodName, "AddShellScriptBuildPhase");
        Assert.AreEqual(methodCall.ArgumentTypes[0], typeof(string));
      }
    }
  }
}
