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

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Firebase.Crashlytics {
  internal readonly struct StackFrameData {
    internal readonly string ClassName, MethodName, FileName;
    internal readonly int LineNumber;
    internal readonly ulong Address;
    internal StackFrameData(string className, string methodName, string fileName, int lineNumber, ulong address = 0UL) {
      ClassName = className ?? string.Empty; MethodName = methodName ?? string.Empty;
      FileName = fileName ?? string.Empty; LineNumber = lineNumber; Address = address;
    }
    internal bool IsAddressOnly => Address != 0UL && MethodName.Length == 0;
  }

  /// <summary>
  /// Unity allows the user to log Exceptions, such that they get passed as
  /// strings to the logMessageReceived handler. This class allows us to wrap
  /// the strings passed to that handler back up as an Exception.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class StackTraceParser {
    internal const int MaxParsedFrames = 1024;
    internal static Dictionary<string, string>[] ParseStackTraceString(string stackTrace) => ToDictionaries(ParseFrames(stackTrace));

    internal static Dictionary<string, string>[] ToDictionaries(StackFrameData[] frames) {
      if (frames == null || frames.Length == 0) return Array.Empty<Dictionary<string, string>>();
      var result = new Dictionary<string, string>[frames.Length];
      for (int i = 0; i < frames.Length; i++) {
        StackFrameData f = frames[i];
        result[i] = new Dictionary<string, string>(4, StringComparer.Ordinal) {
          { "class", f.ClassName }, { "method", f.MethodName }, { "file", f.FileName },
          { "line", f.LineNumber == 0 ? "0" : f.LineNumber.ToString(CultureInfo.InvariantCulture) },
        };
      }
      return result;
    }

    internal static StackFrameData[] ParseFrames(string stackTrace, int skipLeadingFrames = 0) {
      if (string.IsNullOrEmpty(stackTrace)) return Array.Empty<StackFrameData>();
      List<StackFrameData> list = null;
      int skipped = 0, pos = 0;
      ReadOnlySpan<char> span = stackTrace.AsSpan();
      while (pos < span.Length) {
        int lineEnd = pos;
        while (lineEnd < span.Length && span[lineEnd] != '\n' && span[lineEnd] != '\r') lineEnd++;
        ReadOnlySpan<char> line = span.Slice(pos, lineEnd - pos).Trim();
        pos = lineEnd + 1;
        if (lineEnd < span.Length && span[lineEnd] == '\r' && pos < span.Length && span[pos] == '\n') pos++;
        if (line.Length == 0 || !TryParseFrame(line, out StackFrameData frame)) continue;
        if (skipped < skipLeadingFrames) { skipped++; continue; }
        (list ??= new List<StackFrameData>(16)).Add(frame);
        if (list.Count >= MaxParsedFrames) break;
      }
      return list == null ? Array.Empty<StackFrameData>() : list.ToArray();
    }

    internal static bool TryParseFrame(ReadOnlySpan<char> line, out StackFrameData frame) {
      frame = default;
      if (line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("Rethrow as ", StringComparison.Ordinal)) return false;
      if (line.StartsWith("at ", StringComparison.Ordinal)) line = line.Slice(3).TrimStart();
      int openParen = line.IndexOf('(');
      if (openParen <= 0) return TryParseNativeAddressFrame(line, out frame);
      ReadOnlySpan<char> target = line.Slice(0, openParen).TrimEnd();
      if (target.Length == 0 || HasUnbracketedWhitespace(target)) return TryParseNativeAddressFrame(line, out frame);

      int depth = 0, closeParen = -1;
      for (int i = openParen; i < line.Length; i++) {
        if (line[i] == '(') depth++;
        else if (line[i] == ')' && --depth == 0) { closeParen = i; break; }
      }
      if (closeParen < 0 || !TrySplitClassAndMethod(target, out ReadOnlySpan<char> classSpan, out ReadOnlySpan<char> methodSpan)) return false;

      string className = classSpan.ToString(), methodName = methodSpan.ToString(), fileName = className;
      int lineNumber = 0;
      ReadOnlySpan<char> rest = line.Slice(closeParen + 1).Trim();
      // If the symbols are unknown or stripped, it will be formatted differently.
      // See https://github.com/mono/mono/blob/b7a308f660de8174b64697a422abfc7315d07b8c/mcs/class/corlib/System.Diagnostics/StackFrame.cs#L146
      if (rest.Length > 0 && TryExtractFileAndLine(rest, out ReadOnlySpan<char> fileSpan, out int parsedLine) &&
          !(fileSpan.Length >= 2 && fileSpan[0] == '<' && fileSpan[fileSpan.Length - 1] == '>')) {
        fileName = fileSpan.ToString(); lineNumber = parsedLine;
      }
      frame = new StackFrameData(className, methodName, fileName, lineNumber);
      return true;
    }

    private static bool HasUnbracketedWhitespace(ReadOnlySpan<char> s) {
      int angle = 0, bracket = 0;
      for (int i = 0; i < s.Length; i++) {
        char c = s[i];
        if (c == '<') angle++;
        else if (c == '>' && angle > 0) angle--;
        else if (c == '[') bracket++;
        else if (c == ']' && bracket > 0) bracket--;
        else if ((c == ' ' || c == '\t') && angle == 0 && bracket == 0) return true;
      }
      return false;
    }

    private static bool TrySplitClassAndMethod(ReadOnlySpan<char> target, out ReadOnlySpan<char> classSpan, out ReadOnlySpan<char> methodSpan) {
      classSpan = methodSpan = default;
      if (target.EndsWith("..ctor", StringComparison.Ordinal) && target.Length > 6) { classSpan = target.Slice(0, target.Length - 6); methodSpan = ".ctor".AsSpan(); return true; }
      if (target.EndsWith("..cctor", StringComparison.Ordinal) && target.Length > 7) { classSpan = target.Slice(0, target.Length - 7); methodSpan = ".cctor".AsSpan(); return true; }

      int bracketDepth = 0, angleDepth = 0;
      for (int i = target.Length - 1; i >= 0; i--) {
        char c = target[i];
        if (c == ']') bracketDepth++;
        else if (c == '[') bracketDepth = Math.Max(0, bracketDepth - 1);
        else if (c == '>') angleDepth++;
        else if (c == '<') angleDepth = Math.Max(0, angleDepth - 1);
        else if ((c == '.' || c == ':') && bracketDepth == 0 && angleDepth == 0) {
          int classEnd = c == ':' && i > 1 && target[i - 1] == ':' ? i - 1 : i;
          if (classEnd <= 0 || i >= target.Length - 1) return false;
          classSpan = target.Slice(0, classEnd); methodSpan = target.Slice(i + 1);
          return true;
        }
      }
      return false;
    }

    private static bool TryExtractFileAndLine(ReadOnlySpan<char> rest, out ReadOnlySpan<char> fileSpan, out int lineNumber) {
      fileSpan = default; lineNumber = 0;
      if (rest[0] == '(' && rest[rest.Length - 1] == ')') rest = rest.Slice(1, rest.Length - 2).Trim();
      if (rest.Length > 0 && rest[0] == '[') {
        int closeBracket = rest.IndexOf(']');
        if (closeBracket > 0) rest = rest.Slice(closeBracket + 1).TrimStart();
      }
      if (rest.StartsWith("in ", StringComparison.Ordinal) || rest.StartsWith("at ", StringComparison.Ordinal)) rest = rest.Slice(3).TrimStart();
      else {
        int inIdx = rest.IndexOf(" in ", StringComparison.Ordinal);
        int marker = inIdx >= 0 ? inIdx : rest.IndexOf(" at ", StringComparison.Ordinal);
        if (marker < 0) return false;
        rest = rest.Slice(marker + 4).TrimStart();
      }
      int lastColon = rest.LastIndexOf(':');
      if (lastColon <= 0 || lastColon >= rest.Length - 1) return false;
      ReadOnlySpan<char> linePart = rest.Slice(lastColon + 1).TrimEnd(')').TrimStart();
      if (linePart.StartsWith("line ", StringComparison.OrdinalIgnoreCase)) linePart = linePart.Slice(5).TrimStart();
      if (!int.TryParse(linePart, NumberStyles.None, CultureInfo.InvariantCulture, out lineNumber)) return false;
      ReadOnlySpan<char> rawPath = rest.Slice(0, lastColon);
      int lastSlash = rawPath.LastIndexOfAny('/', '\\');
      fileSpan = lastSlash >= 0 ? rawPath.Slice(lastSlash + 1) : rawPath;
      return fileSpan.Length > 0;
    }

    private static bool TryParseNativeAddressFrame(ReadOnlySpan<char> line, out StackFrameData frame) {
      frame = default;
      int hexIdx = line.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
      if (hexIdx < 0) return false;
      ReadOnlySpan<char> afterHex = line.Slice(hexIdx + 2);
      int hexLen = 0;
      while (hexLen < afterHex.Length && Uri.IsHexDigit(afterHex[hexLen])) hexLen++;
      if (hexLen == 0 || !ulong.TryParse(afterHex.Slice(0, hexLen), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong addr) || addr == 0UL) return false;
      string lib = "Native";
      if (hexIdx > 0) {
        ReadOnlySpan<char> prefix = line.Slice(0, hexIdx).Trim();
        int space = prefix.IndexOfAny(' ', '\t');
        ReadOnlySpan<char> libToken = space >= 0 ? prefix.Slice(space + 1).Trim() : prefix;
        if (libToken.Length > 0) lib = libToken.ToString();
      }
      frame = new StackFrameData(lib, string.Empty, lib, 0, addr);
      return true;
    }
  }
}
