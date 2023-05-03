/*
 * Copyright 2023 Google LLC
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
 * limitations under the License. */

using System.Collections;
using System.Collections.Generic;

namespace Firebase.RemoteConfig {
  // Object passed to ConfigUpdateEventHandlers that contains ConfigUpdate arguments.
  public sealed class ConfigUpdateEventArgs : System.EventArgs {
    // The keys that have changes in the config.
    public IEnumerable<string> UpdatedKeys { get; set; }
    
    // Remote Config Errors that may have come up while listening for updates.
    public RemoteConfigError Error { get; set; }
  }
}

