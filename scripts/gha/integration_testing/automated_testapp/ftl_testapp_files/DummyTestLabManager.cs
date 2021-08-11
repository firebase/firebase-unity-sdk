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

namespace Firebase.TestLab {
  /// <summary>
  /// Absorbs API calls without side effect or errors. Allows integrating
  /// the test lab manager without conditionally removing API calls when not using the test lab.
  /// </summary>
  class DummyTestLabManager : TestLabManager {
    public override int ScenarioNumber { get { return NO_SCENARIO_PRESENT; } }

    protected override void OnFinishTest() { }

    public override void LogToResults(string s) { }
  }
}
