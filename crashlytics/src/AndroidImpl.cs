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

#if !CRASHLYTICS_IOS
namespace Firebase.Crashlytics
{
  using System;
  using System.Diagnostics;
  using System.Collections.Generic;
  using System.Linq;

  using UnityEngine;

  // Stub implementation that is not used on Android.
  internal class IOSImpl : Impl {}

  /// <summary>
  /// Android-specific implementation of the Crashlytics API.
  /// All methods call through to the implementations in the native Java SDK.
  /// See Firebase Crashlytics for API docs.
  /// </summary>
  internal class AndroidImpl : Impl {

    // Proxy for the C++ firebase::crashlytics::Crashlytics object.
    private FirebaseCrashlyticsInternal crashlyticsInternal;

    // Proxy for the C++ firebase::app:App object.
    private readonly FirebaseApp firebaseApp;

    internal AndroidImpl() {
      firebaseApp = FirebaseApp.DefaultInstance;

      InitResult initResult;
      crashlyticsInternal =
        FirebaseCrashlyticsInternal.GetInstance(firebaseApp, out initResult);

      if (initResult != InitResult.Success) {
        throw new Firebase.InitializationException(
            initResult,
            Firebase.ErrorMessages.DependencyNotFoundErrorMessage);
      } else if (crashlyticsInternal == null) {
        LogUtil.LogMessage(LogLevel.Warning,
            "Unable to create FirebaseCrashlytics instance.");
        return;
      }

      SetCustomKey(MetadataBuilder.METADATA_KEY, MetadataBuilder.GenerateMetadataJSON());
    }

    ~AndroidImpl() {
      Dispose();
    }

    private void Dispose() {
      lock(this) {
        System.GC.SuppressFinalize(this);
        if (crashlyticsInternal != null) {
          crashlyticsInternal.Dispose();
          crashlyticsInternal = null;
        }
      }
    }

    public override bool IsSDKInitialized() {
      return crashlyticsInternal != null;
    }

    // Log a warning about failing to perform the specified operation.
    private void LogOperationFailedWarningDueToShutdown(string operation) {
      LogUtil.LogMessage(LogLevel.Warning, String.Format("Crashlytics is shut down, {0} failed",
                                                         operation));
    }

    // Call a method on the proxy object while holding the global dispose lock.
    // If the proxy has already been disposed log a warning and return the specified error value.
    private T CallInternalMethod<T>(Func<T> methodCall, string operation,
                                    T errorValue = default(T)) {
      lock (FirebaseApp.disposeLock) {
        if (crashlyticsInternal != null && !crashlyticsInternal.IsDisposed) {
          return methodCall();
        }
      }
      LogOperationFailedWarningDueToShutdown(operation);
      return errorValue;
    }

    // Call a method on the proxy object while holding the global dispose lock.
    // If the proxy has already been disposed log a warning.
    private void CallInternalMethod(Action methodCall, string operation) {
      lock (FirebaseApp.disposeLock) {
        if (crashlyticsInternal != null && !crashlyticsInternal.IsDisposed) {
          methodCall();
          return;
        }
      }
      LogOperationFailedWarningDueToShutdown(operation);
    }

    public override void Log(string message) {
      if (message == null) throw new ArgumentNullException("message should not be null");
      CallInternalMethod(() => { crashlyticsInternal.Log(message); }, "Log");
    }

    public override void SetCustomKey(string key, string value) {
      if (key == null || value == null) {
        throw new ArgumentNullException("key and value should not be null");
      }
      CallInternalMethod(() => { crashlyticsInternal.SetCustomKey(key, value); }, "SetCustomKey");
    }

    public override void SetUserId(string identifier) {
      if (identifier == null) throw new ArgumentNullException("identifier should not be null");
      CallInternalMethod(() => { crashlyticsInternal.SetUserId(identifier); }, "SetUserId");
    }

    public override void LogException(Exception exception) {
      var loggedException = LoggedException.FromException(exception);
      StackFrames frames = new StackFrames();
      foreach (Dictionary<string, string> frame in loggedException.ParsedStackTrace) {
        frames.Add(new FirebaseCrashlyticsFrame {
          library = frame["class"],
          symbol = frame["method"],
          fileName = frame["file"],
          lineNumber = frame["line"],
        });
      }
      CallInternalMethod(() => {
          crashlyticsInternal.LogException(loggedException.Name, loggedException.Message, frames);
        }, "LogException");
    }

    public override void LogExceptionAsFatal(Exception exception) {
      var loggedException = LoggedException.FromException(exception);
      Dictionary<string, string>[] parsedStackTrace = loggedException.ParsedStackTrace;

      if (parsedStackTrace.Length == 0) {
        // if for some reason we don't get stack trace from exception, we add current stack trace in
        var currentStackTrace = System.Environment.StackTrace;
        LoggedException loggedExceptionWithCurrentStackTrace = new LoggedException(loggedException.Name, loggedException.Message, currentStackTrace);
        parsedStackTrace = loggedExceptionWithCurrentStackTrace.ParsedStackTrace;

        if (parsedStackTrace.Length > 3) {
          // remove AndroidImpl frames for fault blame on crashlytics sdk
          var slicedParsedStackTrace = parsedStackTrace.Skip(3).Take(parsedStackTrace.Length - 3).ToArray();
          parsedStackTrace = slicedParsedStackTrace;
        }
      }

      StackFrames frames = new StackFrames();
      foreach (Dictionary<string, string> frame in parsedStackTrace) {
        frames.Add(new FirebaseCrashlyticsFrame {
          library = frame["class"],
          symbol = frame["method"],
          fileName = frame["file"],
          lineNumber = frame["line"],
        });
      }

      CallInternalMethod(() => {
          crashlyticsInternal.LogExceptionAsFatal(loggedException.Name,
              loggedException.Message, frames);
        }, "LogExceptionAsFatal");
    }

    public override bool IsCrashlyticsCollectionEnabled() {
      return CallInternalMethod(() => {
          return crashlyticsInternal.IsCrashlyticsCollectionEnabled();
        }, "IsCrashlyticsCollectionEnabled", false);
    }

    public override void SetCrashlyticsCollectionEnabled(bool enabled) {
      CallInternalMethod(() => {
          crashlyticsInternal.SetCrashlyticsCollectionEnabled(enabled);
        }, "SetCrashlyticsCollectionEnabled");
    }
  }
}
#endif
