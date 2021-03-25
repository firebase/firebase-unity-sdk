/*
 * Copyright 2017 Google LLC
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

using Firebase.Platform.Default;
using Firebase.Platform;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Collections;

namespace Firebase.Unity {
  internal class UnityPlatformServices {
    // This takes about 150 ms. on a Pixel, most of which is the first
    // access to Services since it causes the static constructor to be called.
    public static void SetupServices() {
      Services.AppConfig = UnityConfigExtensions.DefaultInstance;
      Services.Logging = UnityLoggingService.Instance;
    }
  }
}
