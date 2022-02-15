/*
 * Copyright 2019 Google LLC
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

namespace Firebase.Editor {
    using System;
    using System.IO;
    using System.Collections.Generic;

    // For CommandLine.
    using GooglePlayServices;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Finds a Python script.
    /// </summary>
    internal class PythonExecutor {

        /// <summary>
        /// Path to the generic and window python script relative to the Unity project
        /// directory.
        /// </summary>
        private string scriptPath;

        /// <summary>
        /// Filename of the script without the path.
        /// </summary>
        private string scriptFilename;

        /// <summary>
        /// GUID of the script if it isn't found in scriptPath.
        /// </summary>
        private string scriptGuid;

        /// <summary>
        /// Filename of the windows script runner.
        /// </summary>
        private string windowsScriptFilename;

        /// <summary>
        /// GUID of the windows script runner if it isn't found in scriptPath.
        /// </summary>
        private string windowsScriptGuid;

        /// <summary>
        /// Construct a python script executor.
        /// </summary>
        /// <param name="scriptPath">Path of scriptFilename and windowsScriptFilename
        /// </param>
        /// <param name="scriptFilename">Filename of the python script under scriptPath.
        /// </param>
        /// <param name="scriptGuid">Asset GUID of scriptFilename.</param>
        /// <param name="windowsScriptFilename">Filename of the python script on Windows
        /// under scriptPath.</param>
        /// <param name="windowsScriptGuid">Asset GUID of windowsScriptFilename.</param>
        public PythonExecutor(string scriptPath, string scriptFilename, string scriptGuid,
                              string windowsScriptFilename, string windowsScriptGuid) {
            this.scriptPath = scriptPath;
            this.scriptFilename = scriptFilename;
            this.scriptGuid = scriptGuid;
            this.windowsScriptFilename = windowsScriptFilename;
            this.windowsScriptGuid = windowsScriptGuid;
        }

        /// <summary>
        /// Search for a script by asset GUID, falling back to the specified filename.
        /// </summary>
        /// <param name="guid">GUID of the asset to search for.</param>
        /// <param name="filename">Fallback filename of the asset.</param>
        /// <returns>Path to the script if found in the asset database or the full path to the
        /// script based upon the specified filename, falling back to the expected relative path
        /// in the project if the file isn't found.</returns>
        public string FindScript(string guid, string filename) {
            string path = Path.Combine(scriptPath, filename);
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!String.IsNullOrEmpty(assetPath)) path = assetPath;
            return File.Exists(path) ? Path.GetFullPath(path) : path;
        }

        /// <summary>
        /// Get / find the Windows script.
        /// </summary>
        /// <returns>Path to the Windows executable if found, null otherwise.</returns>
        private string WindowsScriptPath {
            get {
                if (Application.platform == RuntimePlatform.WindowsEditor) {
                    string path = FindScript(windowsScriptGuid, windowsScriptFilename);
                    if (File.Exists(path)) return path;
                }
                return null;
            }
        }

        /// <summary>
        /// Get / find the script.
        /// </summary>
        public string ScriptPath {
            get {
                string path = WindowsScriptPath;
                return String.IsNullOrEmpty(path) ? FindScript(scriptGuid, scriptFilename) : path;
            }
        }

        private static string PYTHON_INTERPRETER
        {
            get
            {
                // Default using 'python'
                string result = "python";

                if (UnityEngine.SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX)
                {
                    // Currently Unity API return Fixed format, so just remove that part, may have better solution
                    var versionCodeString = UnityEngine.SystemInfo.operatingSystem.Replace("Mac OS X ", "").Split('.').First();
                    // Default is 10, due to before Big Sur is 10.x
                    int versionCode = 10;
                    if (int.TryParse(versionCodeString, out versionCode))
                    {
                        // If versionCode >= 12 means it is macOS Monterey, we can use python3 instead
                        // Since after 12.3 there is no 'python' inside /usr/bin, Apple make it to two different files 'python3' and 'python2.7' (and 'python2.7' may be removed in future macOS release)
                        if (versionCode >= 12)
                        {
                            result = "python3";
                        }
                    }
                }

                return result;
            }
        }


        /// <summary>
        /// Get the executable to run the script.
        /// </summary>
        public string Executable {
            get {
                return Application.platform == RuntimePlatform.WindowsEditor ?
                    ScriptPath : PYTHON_INTERPRETER;
            }
        }

        /// <summary>
        /// Build an argument list to run the script.
        /// </summary>
        /// <param name="arguments">List of arguments to pass to the script.</param>
        /// <returns>List of arguments to pass to the Executable to run the script.</returns>
        public IEnumerable<string> GetArguments(IEnumerable<string> arguments) {
            if (Executable != PYTHON_INTERPRETER) return arguments;
            var argsWithScript = new List<string>();
            argsWithScript.Add(String.Format("\"{0}\"", ScriptPath));
            argsWithScript.AddRange(arguments);
            return argsWithScript;
        }

        /// <summary>
        /// Get a command line string from a set of arguments.
        /// </summary>
        /// <param name="arguments">List of arguments to pass to the script.</param>
        /// <returns>Command line string for debugging purposes.</returns>
        public string GetCommand(IEnumerable<string> arguments) {
            var executableWithArgs = new List<string>();
            executableWithArgs.Add(String.Format("\"{0}\"", Executable));
            executableWithArgs.AddRange(GetArguments(arguments));
            return String.Join(" ", executableWithArgs.ToArray());
        }

        /// <summary>
        /// If execution fails on Windows 7/8, suggest potential remidies.
        /// </summary>
        private CommandLine.Result SuggestWorkaroundsOnFailure(CommandLine.Result result) {
            if (result.exitCode != 0 && Executable != PYTHON_INTERPRETER) {
                Debug.LogWarning(String.Format(DocRef.PythonScriptExecutionFailed, result.message,
                                               Executable));
            }
            return result;
        }

        /// <summary>
        /// Execute the script.
        /// </summary>
        /// <param name="arguments">Arguments to pass to the script, built with
        /// GetArguments().</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="envVars">Additional environment variables to set for the command.</param>
        /// <param name="ioHandler">Allows a caller to provide interactive input and also handle
        /// both output and error streams from a single delegate.</param>
        /// <returns>CommandLineTool result if successful, raises an exception if it's not
        /// possible to execute the tool.</returns>
        public CommandLine.Result Run(IEnumerable<string> arguments,
                                      string workingDirectory = null,
                                      Dictionary<string, string> envVars = null,
                                      CommandLine.IOHandler ioHandler = null) {
            return SuggestWorkaroundsOnFailure(
                CommandLine.Run(
                    Executable,
                    String.Join(" ", (new List<string>(arguments)).ToArray()),
                    workingDirectory: workingDirectory, envVars: envVars,
                    ioHandler: ioHandler));
        }

        /// <summary>
        /// Execute the script.
        /// </summary>
        /// <param name="arguments">Arguments to pass to the script, built with
        /// GetArguments().</param>
        /// <param name="completionDelegate">Called when the tool completes.  This is always
        /// called from the main thread.</param>
        /// <param name="workingDirectory">Directory to execute the tool from.</param>
        /// <param name="envVars">Additional environment variables to set for the command.</param>
        /// <param name="ioHandler">Allows a caller to provide interactive input and also handle
        /// both output and error streams from a single delegate.</param>
        public void RunAsync(IEnumerable<string> arguments,
                             CommandLine.CompletionHandler completionDelegate,
                             string workingDirectory = null,
                             Dictionary<string, string> envVars = null,
                             CommandLine.IOHandler ioHandler = null) {
            CommandLine.RunAsync(
                Executable,
                String.Join(" ", (new List<string>(arguments)).ToArray()),
                (CommandLine.Result result) => {
                    completionDelegate(SuggestWorkaroundsOnFailure(result));
                },
                workingDirectory: workingDirectory,
                envVars: envVars, ioHandler: ioHandler);
        }
    }
}
