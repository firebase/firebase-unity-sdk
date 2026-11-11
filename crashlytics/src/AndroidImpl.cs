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

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Firebase.Platform;
using UnityEngine;

namespace Firebase.Crashlytics {
  /// <summary>
  /// Android-specific implementation of the Crashlytics API.
  /// All methods call through to the implementations in the native Java SDK.
  /// See Firebase Crashlytics for API docs.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal sealed class AndroidImpl : IDisposable {
    private const int JniVersion16 = 0x00010006, JniDetached = -2;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int JniGetEnvDelegate(IntPtr vm, out IntPtr env, int version);

    private static volatile int s_mainThreadId;
    private static JniGetEnvDelegate s_jniGetEnv;
    private readonly object _lock = new object();
    private AndroidJavaObject _crashlytics;
    private IntPtr _coreGlobalRef, _arbiterGlobalRef, _logFatalMethodId, _isAutoCollectionMethodId;
    private volatile bool _cachedCollectionEnabled = true;
    private int _disposed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CaptureMainThread() => s_mainThreadId = Thread.CurrentThread.ManagedThreadId;

    internal AndroidImpl() {
      _ = RunWithJniAttachment(() => {
        try {
          using var cls = new AndroidJavaClass("com.google.firebase.crashlytics.FirebaseCrashlytics");
          _crashlytics = cls.CallStatic<AndroidJavaObject>("getInstance");
          if (_crashlytics == null) { Impl.SafeLog(PlatformLogLevel.Warning, "Unable to create FirebaseCrashlytics instance."); return false; }
          TryCacheInternalHandles(_crashlytics.GetRawObject());
          TryInstallNdkSignalHandler();
          string metadataJson = MetadataBuilder.GenerateMetadataJSON();
          if (!string.IsNullOrEmpty(metadataJson)) SetCustomKey(MetadataBuilder.METADATA_KEY, metadataJson);
        } catch (Exception ex) { CheckAndClearJniException(); Impl.SafeLog(PlatformLogLevel.Error, $"Failed to initialize Android FirebaseCrashlytics: {ex.Message}"); }
        return true;
      });
    }

    internal bool IsInitialized { get { lock (_lock) return _disposed == 0 && _crashlytics != null; } }
    internal bool DidCrashOnPreviousExecution() => RunCall("DidCrashOnPreviousExecution", false, c => c.Call<bool>("didCrashOnPreviousExecution"));
    internal void Log(string message) {
      if (message == null) throw new ArgumentNullException(nameof(message));
      if (_cachedCollectionEnabled) RunCall("Log", true, c => { c.Call("log", message); return true; });
    }
    internal void SetCustomKey(string key, string value) {
      if (key == null || value == null) throw new ArgumentNullException(key == null ? nameof(key) : nameof(value));
      if (_cachedCollectionEnabled) RunCall("SetCustomKey", true, c => { c.Call("setCustomKey", key, value); return true; });
    }
    internal void SetUserId(string identifier) {
      if (identifier == null) throw new ArgumentNullException(nameof(identifier));
      if (_cachedCollectionEnabled) RunCall("SetUserId", true, c => { c.Call("setUserId", identifier); return true; });
    }
    internal bool IsCrashlyticsCollectionEnabled() => RunCall("IsCrashlyticsCollectionEnabled", false, _ => {
      if (_arbiterGlobalRef != IntPtr.Zero && _isAutoCollectionMethodId != IntPtr.Zero) {
        bool enabled = AndroidJNI.CallBooleanMethod(_arbiterGlobalRef, _isAutoCollectionMethodId, Array.Empty<jvalue>());
        if (!CheckAndClearJniException()) { _cachedCollectionEnabled = enabled; return enabled; }
      }
      return _cachedCollectionEnabled;
    });
    internal void SetCrashlyticsCollectionEnabled(bool enabled) => RunCall("SetCrashlyticsCollectionEnabled", true, c => {
      c.Call("setCrashlyticsCollectionEnabled", enabled);
      if (!CheckAndClearJniException()) _cachedCollectionEnabled = enabled;
      return true;
    });

    internal void RecordException(LoggedException loggedException, bool isFatal) {
      if (loggedException == null || !_cachedCollectionEnabled) return;
      StackFrameData[] frames = loggedException.Frames;
      // if for some reason we don't get stack trace from exception, we add current stack trace in
      // remove AndroidImpl frames for fault blame on crashlytics sdk
      if (isFatal && frames.Length == 0) frames = StackTraceParser.ParseFrames(Environment.StackTrace, skipLeadingFrames: 3);
      string message = $"{loggedException.Name} : {loggedException.Message}";
      RunCall(isFatal ? "LogExceptionAsFatal" : "LogException", true, c => {
        using var javaException = new AndroidJavaObject("java.lang.Exception", message);
        PopulateJavaStackTrace(javaException.GetRawObject(), frames);
        if (isFatal && _coreGlobalRef != IntPtr.Zero && _logFatalMethodId != IntPtr.Zero) {
          AndroidJNI.CallVoidMethod(_coreGlobalRef, _logFatalMethodId, new[] { new jvalue { l = javaException.GetRawObject() } });
          CheckAndClearJniException();
        } else c.Call("recordException", javaException);
        return true;
      });
    }

    private static void PopulateJavaStackTrace(IntPtr javaExceptionPtr, StackFrameData[] frames) {
      IntPtr steClass = IntPtr.Zero, throwableClass = IntPtr.Zero, arrayPtr = IntPtr.Zero;
      try {
        steClass = AndroidJNI.FindClass("java/lang/StackTraceElement");
        throwableClass = AndroidJNI.FindClass("java/lang/Throwable");
        if (CheckAndClearJniException() || steClass == IntPtr.Zero || throwableClass == IntPtr.Zero) return;
        IntPtr steCtor = AndroidJNI.GetMethodID(steClass, "<init>", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;I)V");
        IntPtr setStackTrace = AndroidJNI.GetMethodID(throwableClass, "setStackTrace", "([Ljava/lang/StackTraceElement;)V");
        if (CheckAndClearJniException() || steCtor == IntPtr.Zero || setStackTrace == IntPtr.Zero) return;
        arrayPtr = AndroidJNI.NewObjectArray(frames.Length, steClass, IntPtr.Zero);
        if (CheckAndClearJniException() || arrayPtr == IntPtr.Zero) return;
        var args = new jvalue[4];
        for (int i = 0; i < frames.Length; i++) {
          StackFrameData f = frames[i];
          IntPtr cls = IntPtr.Zero, sym = IntPtr.Zero, file = IntPtr.Zero, elem = IntPtr.Zero;
          try {
            args[0].l = cls = AndroidJNI.NewStringUTF(f.ClassName ?? string.Empty);
            args[1].l = sym = AndroidJNI.NewStringUTF(f.MethodName ?? string.Empty);
            args[2].l = file = AndroidJNI.NewStringUTF(f.FileName ?? string.Empty);
            args[3].i = f.LineNumber;
            elem = AndroidJNI.NewObject(steClass, steCtor, args);
            if (!CheckAndClearJniException() && elem != IntPtr.Zero) { AndroidJNI.SetObjectArrayElement(arrayPtr, i, elem); CheckAndClearJniException(); }
          } finally { DeleteLocals(elem, file, sym, cls); }
        }
        AndroidJNI.CallVoidMethod(javaExceptionPtr, setStackTrace, new[] { new jvalue { l = arrayPtr } });
        CheckAndClearJniException();
      } finally { DeleteLocals(arrayPtr, throwableClass, steClass); }
    }

    private void TryCacheInternalHandles(IntPtr crashlyticsObj) {
      IntPtr fcCls = IntPtr.Zero, coreLoc = IntPtr.Zero, coreCls = IntPtr.Zero, arbLoc = IntPtr.Zero, arbCls = IntPtr.Zero;
      try {
        fcCls = AndroidJNI.GetObjectClass(crashlyticsObj);
        IntPtr coreField = fcCls != IntPtr.Zero ? AndroidJNI.GetFieldID(fcCls, "core", "Lcom/google/firebase/crashlytics/internal/common/CrashlyticsCore;") : IntPtr.Zero;
        if (CheckAndClearJniException() || coreField == IntPtr.Zero) return;
        coreLoc = AndroidJNI.GetObjectField(crashlyticsObj, coreField);
        if (CheckAndClearJniException() || coreLoc == IntPtr.Zero) return;
        _coreGlobalRef = AndroidJNI.NewGlobalRef(coreLoc);
        coreCls = AndroidJNI.GetObjectClass(coreLoc);
        _logFatalMethodId = AndroidJNI.GetMethodID(coreCls, "logFatalException", "(Ljava/lang/Throwable;)V");
        CheckAndClearJniException();
        IntPtr arbField = AndroidJNI.GetFieldID(coreCls, "dataCollectionArbiter", "Lcom/google/firebase/crashlytics/internal/common/DataCollectionArbiter;");
        if (CheckAndClearJniException() || arbField == IntPtr.Zero) return;
        arbLoc = AndroidJNI.GetObjectField(coreLoc, arbField);
        if (CheckAndClearJniException() || arbLoc == IntPtr.Zero) return;
        _arbiterGlobalRef = AndroidJNI.NewGlobalRef(arbLoc);
        arbCls = AndroidJNI.GetObjectClass(arbLoc);
        _isAutoCollectionMethodId = AndroidJNI.GetMethodID(arbCls, "isAutomaticDataCollectionEnabled", "()Z");
        if (!CheckAndClearJniException() && _isAutoCollectionMethodId != IntPtr.Zero) {
          _cachedCollectionEnabled = AndroidJNI.CallBooleanMethod(_arbiterGlobalRef, _isAutoCollectionMethodId, Array.Empty<jvalue>());
          CheckAndClearJniException();
        }
      } finally { DeleteLocals(arbCls, arbLoc, coreCls, coreLoc, fcCls); }
    }

    private static void TryInstallNdkSignalHandler() {
      try {
        using var ndkClass = new AndroidJavaClass("com.google.firebase.crashlytics.ndk.FirebaseCrashlyticsNdk");
        using AndroidJavaObject ndk = ndkClass.CallStatic<AndroidJavaObject>("getInstance");
        ndk?.Call("installSignalHandler");
      } catch (Exception) { CheckAndClearJniException(); }
    }

    private T RunCall<T>(string op, T fallback, Func<AndroidJavaObject, T> action) => RunWithJniAttachment(() => {
      lock (_lock) {
        if (_disposed != 0 || _crashlytics == null) { Impl.SafeLog(PlatformLogLevel.Warning, $"Crashlytics is shut down, {op} failed"); return fallback; }
        try { return action(_crashlytics); }
        catch (Exception ex) { CheckAndClearJniException(); Impl.SafeLog(PlatformLogLevel.Error, $"Crashlytics.{op} failed: {ex.Message}"); return fallback; }
      }
    });

    private static T RunWithJniAttachment<T>(Func<T> work) {
      bool attached = EnsureThreadAttachedToJni();
      try { return work(); } finally { if (attached) _ = AndroidJNI.DetachCurrentThread(); }
    }

    private static bool EnsureThreadAttachedToJni() {
      if (s_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == s_mainThreadId) return false;
      IntPtr vm = AndroidJNI.GetJavaVM();
      if (vm == IntPtr.Zero) return false;
      JniGetEnvDelegate getEnv = s_jniGetEnv ??= Marshal.GetDelegateForFunctionPointer<JniGetEnvDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(vm), 6 * IntPtr.Size));
      int status = getEnv(vm, out IntPtr env, JniVersion16);
      return (status == JniDetached || env == IntPtr.Zero) && AndroidJNI.AttachCurrentThread() == 0;
    }

    private static bool CheckAndClearJniException() {
      IntPtr ex = AndroidJNI.ExceptionOccurred();
      if (ex == IntPtr.Zero) return false;
      AndroidJNI.ExceptionClear(); DeleteLocals(ex); return true;
    }

    private static void DeleteLocals(IntPtr a, IntPtr b = default, IntPtr c = default, IntPtr d = default, IntPtr e = default) {
      if (a != IntPtr.Zero) AndroidJNI.DeleteLocalRef(a);
      if (b != IntPtr.Zero) AndroidJNI.DeleteLocalRef(b);
      if (c != IntPtr.Zero) AndroidJNI.DeleteLocalRef(c);
      if (d != IntPtr.Zero) AndroidJNI.DeleteLocalRef(d);
      if (e != IntPtr.Zero) AndroidJNI.DeleteLocalRef(e);
    }

    public void Dispose() {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      _ = RunWithJniAttachment(() => {
        lock (_lock) {
          if (_arbiterGlobalRef != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(_arbiterGlobalRef); _arbiterGlobalRef = IntPtr.Zero; }
          if (_coreGlobalRef != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(_coreGlobalRef); _coreGlobalRef = IntPtr.Zero; }
          if (_crashlytics != null) { _crashlytics.Dispose(); _crashlytics = null; }
        }
        return true;
      });
    }
  }
}
#endif
