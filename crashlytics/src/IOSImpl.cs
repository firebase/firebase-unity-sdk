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

namespace Firebase.Crashlytics
{
  using System;
  using System.Runtime.InteropServices;
  using System.Diagnostics;
  using System.Collections.Generic;
  using System.Linq;
  using UnityEngine;

  
  [StructLayout(LayoutKind.Sequential)]
  internal struct Frame
  {
    public string library;
    public string symbol;
    public string fileName;
    public string lineNumber;
  }
  

  // Stub implementation that is not used on iOS or tvOS.
  internal class AndroidImpl : Impl {}

  //TODO(b/112043008): These extern symbols aren't available for Android builds.
  internal class IOSImpl : Impl
  {
    #region DLL Imports
    [DllImport("__Internal")]
    private static extern bool CLUIsInitialized();

    [DllImport("__Internal")]
    private static extern void CLULog(string msg);

    [DllImport("__Internal")]
    private static extern void CLUSetKeyValue(string key, string value);

    [DllImport("__Internal")]
    private static extern void CLUSetInternalKeyValue(string key, string value);

    [DllImport("__Internal")]
    private static extern void CLUSetUserIdentifier(string identifier);

    [DllImport("__Internal")]
    private static extern void CLUSetDevelopmentPlatform(string name, string version);

    [DllImport("__Internal")]
    private static extern void CLURecordCustomException(string name, string reason,
                                                        Frame[] frames, int frameCount, bool isOnDemand);

    [DllImport("__Internal")]
    private static extern bool CLUIsCrashlyticsCollectionEnabled();

    [DllImport("__Internal")]
    private static extern void CLUSetCrashlyticsCollectionEnabled(bool enabled);
    #endregion

    public IOSImpl() {
      CLUSetDevelopmentPlatform("Unity", Application.unityVersion);
      CLUSetInternalKeyValue(MetadataBuilder.METADATA_KEY, MetadataBuilder.GenerateMetadataJSON());
    }

    public override bool IsSDKInitialized() {
      return CLUIsInitialized();
    }

    public override void Log(string message)
    {
      CLULog(message);
    }

    public override void SetCustomKey(string key, string value)
    {
      CLUSetKeyValue(key, value);
    }

    public override void SetUserId(string identifier)
    {
      CLUSetUserIdentifier(identifier);
    }

    public override void LogException(Exception exception)
    {
      var loggedException = LoggedException.FromException(exception);
      RecordCustomException(loggedException, false);
    }

    public override void LogExceptionAsFatal(Exception exception) {
      var loggedException = LoggedException.FromException(exception);
      RecordCustomException(loggedException, true);
    }

    public override bool IsCrashlyticsCollectionEnabled() {
      return CLUIsCrashlyticsCollectionEnabled();
    }

    public override void SetCrashlyticsCollectionEnabled(bool enabled) {
      CLUSetCrashlyticsCollectionEnabled(enabled);
    }

    // private void RecordCustomException(string name, string reason, string stackTraceString)
    private void RecordCustomException(LoggedException loggedException, bool isOnDemand) {
      Dictionary<string, string>[] parsedStackTrace = loggedException.ParsedStackTrace;

      if (isOnDemand && parsedStackTrace.Length == 0) {
        // if for some reason we don't get stack trace from exception, we add current stack trace in
        var currentStackTrace = System.Environment.StackTrace;
        LoggedException loggedExceptionWithCurrentStackTrace = new LoggedException(loggedException.Name, loggedException.Message, currentStackTrace);
        parsedStackTrace = loggedExceptionWithCurrentStackTrace.ParsedStackTrace;

        if (parsedStackTrace.Length > 2) {
          // remove RecordCustomException and System.Environment.StackTrace frame for fault blame on crashlytics sdk
          var slicedParsedStackTrace = parsedStackTrace.Skip(2).Take(parsedStackTrace.Length - 2).ToArray();
          parsedStackTrace = slicedParsedStackTrace;
        }
      }

      List<Frame> frames = new List<Frame>();
      foreach (Dictionary<string, string> frame in parsedStackTrace) {
        frames.Add(new Frame {
          library = frame["class"],
          symbol = frame["method"],
          fileName = frame["file"],
          lineNumber = frame["line"],
        });
      }

      CLURecordCustomException(loggedException.Name, loggedException.Message, frames.ToArray(), frames.Count, isOnDemand);
    }
  }
}
