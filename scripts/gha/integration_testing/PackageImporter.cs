// Copyright 2021 Google LLC
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

/* Offers functionality to import Unity packages via the Package Manager in 2019.3 and above.
 *
 * This installs Unity package tarballs present on the local filesystem, as an alternative
 * to importing them as plugins. Contains one method:
 *
 * Import()
 *
 * One flag is required when calling this method:
 *
 * -PackageImporter.package
 *
 * This should be a full path to a local Unity tarball.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

[InitializeOnLoad]
public class PackageImporter {
  static readonly string package;

  static PackageImporter() {
    string[] args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-PackageImporter.package") {
        package = "file:" + args[++i];
        continue;
      }
    }
  }

  public static void Import() {
    if (package == null) {
      throw new InvalidOperationException("Must specify package via -PackageImporter.package flag");
    }
    Debug.LogFormat("Adding package: {0}", package);
    AddRequest request = Client.Add(package);
    int secondsSlept = 0;
    while (!request.IsCompleted) {
      Thread.Sleep(1000); // Wait 1 second.
      secondsSlept++;
      // Protect against infinite loop in case request cannot complete.
      if (secondsSlept > 200) {
        Debug.LogError("Took longer than 200 seconds to install a package.");
        EditorApplication.Exit(1);
      }
    }
    if (request.Error != null) {
        Debug.LogError("ERROR: Adding package failed. Scroll up for details.");
        EditorApplication.Exit(1);
    }
  }
}
