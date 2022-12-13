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

using UnityEngine;

namespace Firebase.TestLab {
  /// <summary>
  /// Interface for Firebase Test Lab's Game Loops.
  /// </summary>
  public abstract class TestLabManager {

    public const int NO_SCENARIO_PRESENT = -1;

    public bool IsTestingScenario {
      get { return ScenarioNumber > NO_SCENARIO_PRESENT; }
    }

    public abstract int ScenarioNumber { get; }

    private bool isFinished = false;

    /// <summary>
    /// Use when the scenario is finished and the app can be closed. Safe to call multiple times.
    /// </summary>
    public void NotifyHarnessTestIsComplete() {
      // This function wraps the platform-specific logic to ensure it only gets called once.
      if (!isFinished) {
        OnFinishTest();
      }
      isFinished = true;
    }

    /// <summary>
    /// Informs the Game Loop runner that the test is finished, and that the testapp should be
    /// closed.
    /// </summary>
    protected abstract void OnFinishTest();

    public abstract void LogToResults(string s);

    /// <summary>
    /// Creates an appropriate implementation based on the environment. Will determine if FTL
    /// is being used and is supported by the current platform. If so, will return a
    /// platform-specific implementation. Otherwise, will return a dummy that can safely absorb
    /// API calls.
    /// </summary>
    public static TestLabManager Instantiate() {
      if (Application.platform == RuntimePlatform.Android) {
        return AndroidTestLabManager.Create();
      } else if (IsApplePlatform(Application.platform)) {
        return AppleTestLabManager.Create();
      } else if (IsDesktopPlatform(Application.platform)) {
        return DesktopTestLabManager.Create();
      } else {
        return new DummyTestLabManager();
      }
    }

    static bool IsApplePlatform(RuntimePlatform platform) {
      return platform == RuntimePlatform.IPhonePlayer
        || platform == RuntimePlatform.tvOS;
    }

    static bool IsDesktopPlatform(RuntimePlatform platform) {
      return platform == RuntimePlatform.OSXPlayer
        || platform == RuntimePlatform.WindowsPlayer
        || platform == RuntimePlatform.LinuxPlayer;
    }
  }
}
