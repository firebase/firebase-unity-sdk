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
using UnityEngine;

namespace Firebase.TestLab {
  class AndroidTestLabManager : TestLabManager {

    public override int ScenarioNumber { get { return scenario; } }

    readonly AndroidJavaObject activity;
    readonly AndroidJavaObject intent;
    readonly int fileDescriptor;
    readonly int scenario;

    private AndroidTestLabManager(
      AndroidJavaObject intent, AndroidJavaObject activity, int fileDescriptor, int scenario) {
      this.intent = intent;
      this.activity = activity;
      this.fileDescriptor = fileDescriptor;
      this.scenario = scenario;
    }

    protected override void OnFinishTest() {
      activity.Call("finish");
    }

    public override void LogToResults(string s) {
      LibcWrapper.WriteToFileDescriptor(fileDescriptor, s);
    }

    /// <summary>
    /// Attempts to create a TestLabManager for an Android build. If running with Game Loops,
    /// and all necessary information is found in the intent, this will return a functional
    /// AndroidTestLabManager. If not running with Game Loops, this will return a dummy.
    /// If running with Game Loops but one or more critical pieces of data cannot be found in
    /// the intent, e.g. the file descriptor, an exception will be thrown.
    /// </summary>
    public static TestLabManager Create() {
      AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
      AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
      AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
      int scenario = CheckIntentForScenario(intent);
      if (scenario == -1) {  // Not using Game Loops: return dummy.
        return new DummyTestLabManager();
      }
      int fileDescriptor = GetLogFileDescriptor(intent, activity);
      return new AndroidTestLabManager(intent, activity, fileDescriptor, scenario);
    }

    private static int CheckIntentForScenario(AndroidJavaObject intent) {
      // Whether Game Loops is being used is determined by the action: only Game Loops uses this.
      // Thus once we find this action, any missing data needed to run with Game Loops
      // is treated as an error.
      string action = intent.Call<string>("getAction");
      if (action != "com.google.intent.action.TEST_LOOP") {
        return -1;
      }

      int scenarioNumber = intent.Call<int>("getIntExtra", "scenario", NO_SCENARIO_PRESENT);
      if (scenarioNumber == -1) {
        throw new InvalidOperationException("Scenario not found in Game Loop's intent.");
      }

      Debug.LogFormat("Fetched scenario: {0}", scenarioNumber);
      return scenarioNumber;
    }

    private static int GetLogFileDescriptor(AndroidJavaObject intent, AndroidJavaObject activity) {
      AndroidJavaObject logFileUri = intent.Call<AndroidJavaObject>("getData");
      if (logFileUri == null) {
        throw new InvalidOperationException(
          "Unable to extract file descriptor: intent's 'getData' returned null.");
      }
      try {
        int fd = activity
          .Call<AndroidJavaObject>("getContentResolver")
          .Call<AndroidJavaObject>("openAssetFileDescriptor", logFileUri, "w")
          .Call<AndroidJavaObject>("getParcelFileDescriptor")
          .Call<int>("getFd");
        return LibcWrapper.dup(fd);
      }
      catch {
        Debug.LogError("Exception thrown while fetching log file descriptor.");
        throw;
      }
    }
  }
}
