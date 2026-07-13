/*
 * Copyright 2018 Google LLC
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Firebase.Crashlytics.Test")]

namespace Firebase.Crashlytics {
  using System;
  using System.Collections.Generic;
  using System.Text.RegularExpressions;

  /// <summary>
  /// Unity allows the user to log Exceptions, such that they get passed as
  /// strings to the logMessageReceived handler. This class allows us to wrap
  /// the strings passed to that handler back up as an Exception.
  /// </summary>
  internal class StackTraceParser {

    // Matches e.g. "Method(string arg1, int arg2)" or "Method (string arg1, int arg2)"
    private static readonly string FrameArgsRegex = @"\s?\([^\)]*\)";

    // Matches e.g. "at Assets.Src.Gui.Menus.GameMenuController.ShowSkilltreeScreen (string arg1)"
    //      or e.g. "UnityEngine.Debug:Log(Object)"
    private static readonly string FrameRegexWithoutFileInfo =
      @"(?<class>[^\s]+)\.(?<method>[^\s\.]+)" + FrameArgsRegex;

    // Matches e.g.:
    //   "at Assets.Src.Gui.Menus.GameMenuController.ShowSkilltreeScreen () (at C:/Game_Project/Assets/Src/Gui/GameMenuController.cs:154)"
    //   "at SampleApp.Buttons.CrashlyticsButtons.CauseDivByZero () [0x00000] in /Some/Dir/Program.cs:567"
    //   "at UnityEngine.EventSystems.ExecuteEvents.Execute[T] (...) [0x00000] in <00000000000000000000000000000000>:0"
    private static readonly string FrameRegexWithFileInfo =
      FrameRegexWithoutFileInfo + @" (?:.*[/|\\]|.*(?:in|at) )(?<file>[^:]+):(?<line>\d+)";

    private static readonly string[] StringDelimiters = new string[] { Environment.NewLine };

    public static Dictionary<string, string>[] ParseStackTraceString(string stackTrace) {
      string regex = null;
      var result = new List< Dictionary<string, string> >();
      string[] splitStackTrace = stackTrace.Split(StringDelimiters, StringSplitOptions.None);

      if (splitStackTrace.Length < 1) {
        return result.ToArray();
      }

      foreach (var frameString in splitStackTrace) {
        var parsedDict = ParseFrameString(regex, frameString);
        if (parsedDict != null) {
          result.Add(parsedDict);
        }
      }

      return result.ToArray();
    }

    private static Dictionary<string, string> ParseFrameString(string frameString) {
      // First, check for file info.
      var match = Regex.Match(frameString, FrameRegexWithFileInfo);

      // If that failed, check without file info.
      if (!match.Success) {
        match = Regex.Match(frameString, FrameRegexWithoutFileInfo);
      }

      // If that also failed, this isn't a valid frame.
      if (!match.Success) {
        return null;
      }

      if (!match.Groups["class"].Success || !match.Groups["method"].Success) {
        return null;
      }

      string file = match.Groups["file"].Success ? match.Groups["file"].Value : match.Groups["class"].Value;
      string line = match.Groups["line"].Success ? match.Groups["line"].Value : "0";

      // If the symbols are unknown or stripped, it will be formatted differently.
      // See https://github.com/mono/mono/blob/b7a308f660de8174b64697a422abfc7315d07b8c/mcs/class/corlib/System.Diagnostics/StackFrame.cs#L146
      if (file.StartsWith('<') && file.EndsWith('>')) {
        file = match.Groups["class"].Value;
        line = "0";
      }

      var dict = new Dictionary<string, string>();
      dict.Add("class", match.Groups["class"].Value);
      dict.Add("method", match.Groups["method"].Value);
      dict.Add("file", file);
      dict.Add("line", line);

      return dict;
    }
  }
}
