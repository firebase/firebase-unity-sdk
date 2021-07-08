/*
 * Copyright 2019 Google LLC
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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NUnit.Framework;
using Firebase.Crashlytics.Editor;

namespace Firebase.Crashlytics.Editor {

  /// <summary>
  /// Test cases for Version functions
  /// </summary>
  public class VersionInfoTest : CrashlyticsEditorTestBase {

    /// <summary>
    /// Ensure we correctly parse Application.unityVersion
    /// </summary>
    [Test]
    public void TestGetUnityMajorVersion() {
      Assert.AreEqual(VersionInfo.GetUnityMajorVersion("2019.1b2.stuff"), 2019);
      Assert.AreEqual(VersionInfo.GetUnityMajorVersion("5.12.01"), 5);
      Assert.AreEqual(VersionInfo.GetUnityMajorVersion("AbSoLUt.3RubBish"), -1);
      Assert.AreEqual(VersionInfo.GetUnityMajorVersion("The split delim is a period"), -1);
    }
  }
}
