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

namespace Firebase.Crashlytics {
  using Firebase;
  using System;
  using System.Diagnostics;

  /// <summary>
  /// Base implementation of the Crashlytics API.
  ///
  /// Used for platforms where a native implementation is not available.
  /// </summary>
  internal class Impl {

    // Constant log messages.
    private static readonly string LogString =
      "Would log custom message if running on a physical device: {0}";
    private static readonly string SetKeyValueString =
      "Would set key-value if running on a physical device: {0}:{1}";
    private static readonly string SetUserIdentifierString =
      "Would set user identifier if running on a physical device: {0}";
    private static readonly string LogExceptionString =
      "Would log exception if running on a physical device: {0}";
    private static readonly string LogExceptionAsFatalString =
      "Would log exception as fatal if running on a physical device: {0}";
    private static readonly string IsCrashlyticsCollectionEnabledString =
      "Would get Crashlytics data collection if running on a physical device";
    private static readonly string SetCrashlyticsCollectionEnabledString =
      "Would set Crashlytics data collection if running on a physical device: {0}";

    public static Impl Make() {
      if (Firebase.Platform.PlatformInformation.IsIOS) {
        return new IOSImpl();
      }
      if (Firebase.Platform.PlatformInformation.IsAndroid) {
        return new AndroidImpl();
      }
      return new Impl();
    }

    public virtual bool IsSDKInitialized() {
      return false;
    }

    public virtual void Log(string message) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(LogString, message));
    }

    public virtual void SetCustomKey(string key, string value) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(SetKeyValueString, key, value));
    }

    public virtual void SetUserId(string identifier) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(SetUserIdentifierString, identifier));
    }

    public virtual void LogException(Exception exception) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(LogExceptionString, exception));
    }

    public virtual void LogExceptionAsFatal(Exception exception) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(LogExceptionAsFatalString, exception));
    }

    public virtual bool IsCrashlyticsCollectionEnabled() {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(IsCrashlyticsCollectionEnabledString));
      return true;
    }

    public virtual void SetCrashlyticsCollectionEnabled(bool enabled) {
      LogUtil.LogMessage(LogLevel.Debug, String.Format(SetCrashlyticsCollectionEnabledString, enabled));
    }
  }
}
