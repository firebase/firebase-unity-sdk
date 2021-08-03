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

/* Offers functionality to enable/disable assemblies.
 *
 * This class was written to workaround an issue where plugins are supposed
 * to be enabled in the Editor, but aren't. It takes a list of plugins
 * and enables or disables each in turn. This class has two exposed methods:
 *
 * Enable()
 * Disable()
 *
 * These method will enable/disable all plugins specified by the following flag:
 *
 * -PluginToggler.plugins
 *
 * This is a comma separated list of asset paths. i.e. each entry should be
 * a path relative to the Asset path in the Unity project folder. A full asset
 * path looks like Assets/Parse/Plugins/Unity.Compat.dll. Partial asset paths
 * will also work if they match the path, e.g. Unity.Compat.dll and
 * Plugins/Unity.Compat.dll will both match the full path given. Note however
 * that if a partial path matches more than one plugin, an
 * InvalidOperationException will be thrown.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class PluginToggler {
  static readonly IEnumerable<string> plugins;

  static PluginToggler() {
    string[] args = Environment.GetCommandLineArgs();
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == "-PluginToggler.plugins") {
        plugins = Array.AsReadOnly(GetAssemblies(args[++i]));
        continue;
      }
    }
  }

  /// <summary>
  /// Enables the plugins specified by the PluginToggler.plugins command line flag.
  /// </summary>
  public static void Enable() {
    Toggle(plugins, enabled:true);
  }

  /// <summary>
  /// Disables the plugins specified by the PluginToggler.plugins command line flag.
  /// </summary>
  public static void Disable() {
    Toggle(plugins, enabled:false);
  }

  private static void Toggle(IEnumerable<string> assemblies, bool enabled) {
    foreach (string assembly in assemblies) {
      UnityEngine.Debug.LogFormat(
        "{0} {1}", enabled ? "Enabling" : "Disabling", assembly);
      PluginImporter importer = GetImporter(assembly);
      importer.SetCompatibleWithEditor(enabled);
      importer.SaveAndReimport();
    }
  }

  private static string[] GetAssemblies(string pluginCSV) {
    return pluginCSV.Split(new [] {","}, StringSplitOptions.RemoveEmptyEntries);
  }

  private static PluginImporter GetImporter(string targetPlugin) {
    PluginImporter[] matchingImporters =
      PluginImporter.GetAllImporters()
                    .Where(imp => imp.assetPath.Contains(targetPlugin))
                    .ToArray();
    if (matchingImporters.Length == 0) {
      throw new InvalidOperationException("Plugin not found: " + targetPlugin);
    }
    if (matchingImporters.Length > 1) {
      string paths = string.Join(", ", matchingImporters.Select(imp => imp.assetPath)
                                                        .ToArray());
      string errorMessage = string.Format(
        "Asset path {0} matched multiple plugins: {1}", targetPlugin, paths);
      throw new InvalidOperationException(errorMessage);
    }

    return matchingImporters[0];
  }
}
