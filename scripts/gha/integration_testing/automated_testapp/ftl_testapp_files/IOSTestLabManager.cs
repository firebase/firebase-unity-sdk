// Copyright 2019 Google Inc. All rights reserved.
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

using System;
using System.IO;
using UnityEngine;

namespace Firebase.TestLab {
  internal sealed class IOSTestLabManager : TestLabManager {

    public override int ScenarioNumber { get { return scenario; } }

    readonly int scenario;
    readonly StreamWriter logWriter;

    private IOSTestLabManager(int scenario, StreamWriter logWriter) {
      this.scenario = scenario;
      this.logWriter = logWriter;
    }

    protected override void OnFinishTest() {
      logWriter.Close();
      Application.OpenURL("firebase-game-loop-complete://");
    }

    public override void LogToResults(string s) {
      logWriter.Write(s);
      logWriter.Flush();
    }

    /// <summary>
    /// Attempts to create a TestLabManager for an iOS build. If running with Game Loops,
    /// and all necessary information is found in the custom url, this will return a functional
    /// IOSTestLabManager. If not running with Game Loops, this will return a dummy.
    /// </summary>
    public static TestLabManager Create() {
      int scenario = IOSPluginWrapper.GetScenario();
      if (scenario == -1) {  // Not using Game Loops: return dummy.
        return new DummyTestLabManager();
      }
      string logDir = Application.persistentDataPath + "/GameLoopResults";
      string logPath = string.Format("{0}/Results{1}.json", logDir, scenario);
      Directory.CreateDirectory(logDir);
      // Logs will be appended, so we need to clear the file first.
      File.Delete(logPath);
      return new IOSTestLabManager(scenario, File.AppendText(logPath));
    }
  }
}
