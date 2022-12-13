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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Runtime.InteropServices;

namespace Firebase.TestLab {
  /// <summary>
  /// Provides access to the native iOS/tvOS plugin for Game Loops.
  /// </summary>
  internal sealed class ApplePluginWrapper {
    public static int GetScenario() {
      // Not yet hooked up to a native plugin. Return 1 for now, so that we treat the testapps
      // as always running on Game Loops. This is 'harmless', since this assumption currently
      // only results in the writing of a log file.
      return 1;
    }
  }
}
