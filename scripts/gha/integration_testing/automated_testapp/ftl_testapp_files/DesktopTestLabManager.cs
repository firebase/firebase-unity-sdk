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
  /// <summary>
  /// Provides support for automated testing on desktop. Note that this doesn't actually use
  /// Game Loops or Firebase Test Lab at all, but for the sake of simplicity is defined as part
  /// of the same interface since the usage is the same.
  /// </summary>
  internal sealed class DesktopTestLabManager : TestLabManager {

    public override int ScenarioNumber { get { return scenario; } }

    readonly int scenario;
    readonly StreamWriter logWriter;

    const string LOG_PATH_COMMAND_LINE_ARG = "-TestLabManager.logPath";

    private DesktopTestLabManager(int scenario, StreamWriter logWriter) {
      this.scenario = scenario;
      this.logWriter = logWriter;
    }

    protected override void OnFinishTest() {
      logWriter.Close();
      Application.Quit();
    }

    public override void LogToResults(string s) {
      logWriter.Write(s);
      logWriter.Flush();
    }

    /// <summary>
    /// Attempts to create a test manager for desktop. Requires a command line argument
    /// "TestLabManager.logPath", otherwise will supply a dummy implementation. This is done so
    /// that these testapps can be run locally without having to supply any flags.
    /// </summary>
    public static TestLabManager Create() {
      string logPath = ReadLogPathCommandLineArg();
      if (logPath == null) {
        Debug.LogFormat(
          "{0} flag not supplied, using dummy test lab manager.", LOG_PATH_COMMAND_LINE_ARG);
        return new DummyTestLabManager();
      }
      // Hard-coded scenario number. May be replaced with a number supplied via command line
      // flag if multiple scenarios become required for desktop.
      return new DesktopTestLabManager(1, File.AppendText(logPath));
    }

    private static string ReadLogPathCommandLineArg() {
      string[] args = System.Environment.GetCommandLineArgs();
      for (int i = 0; i < args.Length; i++) {
        if (args[i] == LOG_PATH_COMMAND_LINE_ARG) {
          return args[++i];
        }
      }
      return null;
    }
  }
}
