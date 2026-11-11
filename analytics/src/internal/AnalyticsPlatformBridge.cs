// Copyright 2026 Google LLC
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

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.Analytics.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class AnalyticsPlatformBridge {
    internal sealed class SessionState {
      internal readonly string AppName, AppId;
      internal volatile bool Disposed;
      internal SessionState(FirebaseApp app) =>
          (AppName, AppId) = (app?.Name ?? FirebaseApp.DefaultName, app?.Options?.AppId ?? string.Empty);
    }

    internal sealed class LoggedEventRecord {
      internal string Name { get; }
      internal IReadOnlyDictionary<string, object> Parameters { get; }
      internal LoggedEventRecord(string name, IReadOnlyDictionary<string, object> parameters) => (Name, Parameters) = (name, parameters);
    }

    private static readonly FirebaseInstanceCache<SessionState> _sessions = new(OnSessionEvicted);
    private static volatile SessionState _activeSession;
    private static volatile int _mainThreadId;
    private static volatile string _dataPath = string.Empty, _appIdentifier = string.Empty;

    internal static Action<LoggedEventRecord> EventSinkForTesting { get; set; }
    internal static int MainThreadId => _mainThreadId;
    internal static string CachedDataPath => _dataPath;
    internal static string CachedAppIdentifier => _appIdentifier;

    static AnalyticsPlatformBridge() => FirebaseInstanceCache.RegisterResetHook(ResetForReload);

#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() => CaptureMainThreadContext();
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void OnEditorLoad() => CaptureMainThreadContext();
#endif

    internal static void CaptureMainThreadContext() {
      _mainThreadId = Thread.CurrentThread.ManagedThreadId;
#if UNITY_5_3_OR_NEWER
      try {
        _dataPath = UnityEngine.Application.dataPath ?? string.Empty;
        _appIdentifier = UnityEngine.Application.identifier ?? string.Empty;
      } catch (Exception ex) {
        Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Debug, "Analytics metadata capture skipped: " + ex.Message);
      }
#endif
    }

    internal static void ResetForReload() {
      _activeSession = null;
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
      ResetAppleResetterState();
#elif !(UNITY_ANDROID && !UNITY_EDITOR)
      ResetDesktopState();
#endif
    }

    private static void OnSessionEvicted(SessionState state) {
      if (state == null) return;
      state.Disposed = true;
      Interlocked.CompareExchange(ref _activeSession, null, state);
#if !((UNITY_ANDROID || UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR)
      MeleeNativeLoader.NotifyTerminationIfLoaded();
#endif
    }

    internal static SessionState EnsureSession() {
      SessionState fast = _activeSession;
      if (fast != null && !fast.Disposed) return fast;
      FirebaseApp app = FirebaseApp.DefaultInstance ?? throw new InvalidOperationException("FirebaseApp.DefaultInstance is unavailable.");
      SessionState resolved = _sessions.GetOrCreate(app, () => {
        var created = new SessionState(app);
#if !((UNITY_ANDROID || UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR)
        bool enabled;
        lock (_stateLock) enabled = _collectionEnabled;
        MeleeNativeLoader.TryInitialize(created.AppId, _appIdentifier, _dataPath, enabled);
#endif
        return created;
      });
      _activeSession = resolved;
      return resolved;
    }

    internal static void LogEventEnumerable(string name, IEnumerable<Parameter> parameters) => LogEventParameters(name, MaterializeParameters(parameters));
    internal static void SetDefaultEventParametersEnumerable(IEnumerable<Parameter> parameters) => SetDefaultEventParameters(MaterializeParameters(parameters));

    private static Parameter[] MaterializeParameters(IEnumerable<Parameter> parameters) {
      if (parameters == null || parameters is Parameter[]) return parameters as Parameter[];
      var list = new List<Parameter>();
      foreach (Parameter p in parameters) if (p != null) list.Add(p);
      return list.Count == 0 ? Array.Empty<Parameter>() : list.ToArray();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    internal static void LogEventSingle(string name, string paramName, ParameterKind kind, long iVal, double dVal, string sVal) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      RunJniVoid(fa => {
        using var bundle = new UnityEngine.AndroidJavaObject("android.os.Bundle");
        if (paramName != null) {
          if (kind == ParameterKind.Int64) bundle.Call("putLong", paramName, iVal);
          else if (kind == ParameterKind.Double) bundle.Call("putDouble", paramName, dVal);
          else CallJniStr(bundle, "putString", paramName, sVal, true);
        }
        LogAndroidBundle(fa, name, bundle);
      });
    }
    internal static void LogEventParameters(string name, Parameter[] parameters) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      RunJniVoid(fa => {
        using var bundle = new UnityEngine.AndroidJavaObject("android.os.Bundle");
        if (parameters != null) for (int i = 0; i < parameters.Length; i++) if (parameters[i] != null) PutParamInBundle(bundle, parameters[i]);
        LogAndroidBundle(fa, name, bundle);
      });
    }
    private static void LogAndroidBundle(UnityEngine.AndroidJavaObject fa, string name, UnityEngine.AndroidJavaObject bundle) {
      IntPtr jName = UnityEngine.AndroidJNI.NewStringUTF(name);
      try { CallJniVoidRaw(fa, "logEvent", "(Ljava/lang/String;Landroid/os/Bundle;)V", jName, bundle.GetRawObject(), true); }
      finally { if (jName != IntPtr.Zero) UnityEngine.AndroidJNI.DeleteLocalRef(jName); }
    }
    internal static void SetDefaultEventParameters(Parameter[] parameters) {
      EnsureSession();
      RunJniVoid(fa => {
        if (parameters == null || parameters.Length == 0) { CallJniVoidRaw(fa, "setDefaultEventParameters", "(Landroid/os/Bundle;)V", IntPtr.Zero, IntPtr.Zero, false); return; }
        using var bundle = new UnityEngine.AndroidJavaObject("android.os.Bundle");
        for (int i = 0; i < parameters.Length; i++) if (parameters[i] != null) PutParamInBundle(bundle, parameters[i]);
        CallJniVoidRaw(fa, "setDefaultEventParameters", "(Landroid/os/Bundle;)V", bundle.GetRawObject(), IntPtr.Zero, false);
      });
    }
    internal static void SetAnalyticsCollectionEnabled(bool enabled) { EnsureSession(); RunJniVoid(fa => fa.Call("setAnalyticsCollectionEnabled", enabled)); }
    internal static void SetConsent(IDictionary<ConsentType, ConsentStatus> settings) { EnsureSession(); RunJniVoid(fa => SetAndroidConsent(fa, settings)); }
    internal static void SetSessionTimeoutDuration(long ms) { EnsureSession(); RunJniVoid(fa => fa.Call("setSessionTimeoutDuration", ms)); }
    internal static void SetUserId(string userId) { EnsureSession(); RunJniVoid(fa => CallJniStr(fa, "setUserId", userId, null, false)); }
    internal static void SetUserProperty(string name, string prop) { EnsureSession(); if (!string.IsNullOrEmpty(name)) RunJniVoid(fa => CallJniStr(fa, "setUserProperty", name, prop, true)); }
    internal static void ResetAnalyticsData() { EnsureSession(); RunJniVoid(fa => fa.Call("resetAnalyticsData")); }
    internal static Task LogAppleTransactionAsync(string transactionId) { EnsureSession(); return Task.CompletedTask; }
    internal static void InitiateOnDeviceConversionString(bool isEmail, string value) => EnsureSession();
    internal static void InitiateOnDeviceConversionBytes(bool isEmail, byte[] bytes) => EnsureSession();
    internal static Task<string> GetAnalyticsInstanceIdAsync() {
      EnsureSession();
      return RunJniAsync(fa => InvokeAndroidTask(fa, "getAppInstanceId", res =>
          res != null && res.GetRawObject() != IntPtr.Zero ? res.Call<string>("toString") : throw new FirebaseException(-1, "Analytics app instance ID is unavailable.")));
    }
    internal static Task<long> GetSessionIdAsync() {
      EnsureSession();
      return RunJniAsync(fa => InvokeAndroidTask(fa, "getSessionId", res =>
          res != null && res.GetRawObject() != IntPtr.Zero ? res.Call<long>("longValue") : throw new FirebaseException(-2, "AnalyticsStorage consent is set to Denied, or session is expired.")));
    }

    private sealed class AndroidTaskListener<T> : UnityEngine.AndroidJavaProxy {
      private readonly TaskCompletionSource<T> _tcs;
      private readonly Func<UnityEngine.AndroidJavaObject, T> _extract;
      internal AndroidTaskListener(TaskCompletionSource<T> tcs, Func<UnityEngine.AndroidJavaObject, T> extract)
          : base("com.google.android.gms.tasks.OnCompleteListener") => (_tcs, _extract) = (tcs, extract);
      [UnityEngine.Scripting.Preserve]
      public void onComplete(UnityEngine.AndroidJavaObject task) {
        try {
          using (task) {
            if (task != null && task.Call<bool>("isSuccessful")) { using var res = task.Call<UnityEngine.AndroidJavaObject>("getResult"); _tcs.TrySetResult(_extract(res)); }
            else { using var ex = task?.Call<UnityEngine.AndroidJavaObject>("getException"); _tcs.TrySetException(new FirebaseException(-1, ex?.Call<string>("getMessage") ?? "Android Analytics task failed.")); }
          }
        } catch (FirebaseException fex) { _tcs.TrySetException(fex); }
        catch (Exception e) { _tcs.TrySetException(new FirebaseException(-1, e.Message, e)); }
      }
    }

    private static Task<T> InvokeAndroidTask<T>(UnityEngine.AndroidJavaObject fa, string method, Func<UnityEngine.AndroidJavaObject, T> extract) {
      using var gmsTask = fa.Call<UnityEngine.AndroidJavaObject>(method);
      if (gmsTask == null) return Task.FromException<T>(new FirebaseException(-1, $"Android {method} returned null task."));
      var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
      using var reg = gmsTask.Call<UnityEngine.AndroidJavaObject>("addOnCompleteListener", new AndroidTaskListener<T>(tcs, extract));
      return tcs.Task;
    }
    private static void RunJniVoid(Action<UnityEngine.AndroidJavaObject> work) {
      if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId) { WithAndroidAnalytics(fa => { work(fa); return true; }, false); return; }
      Task.Run(() => {
        UnityEngine.AndroidJNI.AttachCurrentThread();
        try { WithAndroidAnalytics(fa => { work(fa); return true; }, false); } finally { UnityEngine.AndroidJNI.DetachCurrentThread(); }
      }).GetAwaiter().GetResult();
    }
    private static Task<T> RunJniAsync<T>(Func<UnityEngine.AndroidJavaObject, Task<T>> work) {
      Task<T> err = Task.FromException<T>(new FirebaseException(-1, "Android FirebaseAnalytics instance is unavailable."));
      if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId) return WithAndroidAnalytics(work, err);
      return Task.Run(async () => {
        Task<T> pending;
        UnityEngine.AndroidJNI.AttachCurrentThread();
        try { pending = WithAndroidAnalytics(work, err); } finally { UnityEngine.AndroidJNI.DetachCurrentThread(); }
        return await pending.ConfigureAwait(false);
      });
    }
    private static T WithAndroidAnalytics<T>(Func<UnityEngine.AndroidJavaObject, T> fn, T fallback) {
      using var unityPlayer = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
      using var activity = unityPlayer.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
      if (activity == null) return fallback;
      using var faCls = new UnityEngine.AndroidJavaClass("com.google.firebase.analytics.FirebaseAnalytics");
      using var fa = faCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", activity);
      return fa != null ? fn(fa) : fallback;
    }
    private static void SetAndroidConsent(UnityEngine.AndroidJavaObject fa, IDictionary<ConsentType, ConsentStatus> settings) {
      using var map = new UnityEngine.AndroidJavaObject("java.util.HashMap");
      using var typeCls = new UnityEngine.AndroidJavaClass("com.google.firebase.analytics.FirebaseAnalytics$ConsentType");
      using var statusCls = new UnityEngine.AndroidJavaClass("com.google.firebase.analytics.FirebaseAnalytics$ConsentStatus");
      foreach (KeyValuePair<ConsentType, ConsentStatus> kv in settings) {
        string tName = kv.Key switch {
          ConsentType.AdStorage => "AD_STORAGE", ConsentType.AnalyticsStorage => "ANALYTICS_STORAGE",
          ConsentType.AdUserData => "AD_USER_DATA", ConsentType.AdPersonalization => "AD_PERSONALIZATION", _ => null,
        };
        string sName = kv.Value switch { ConsentStatus.Granted => "GRANTED", ConsentStatus.Denied => "DENIED", _ => null };
        if (tName == null || sName == null) return;
        using var jType = typeCls.GetStatic<UnityEngine.AndroidJavaObject>(tName);
        using var jStatus = statusCls.GetStatic<UnityEngine.AndroidJavaObject>(sName);
        using var prev = map.Call<UnityEngine.AndroidJavaObject>("put", jType, jStatus);
      }
      fa.Call("setConsent", map);
    }
    private static void PutParamInBundle(UnityEngine.AndroidJavaObject bundle, Parameter p) {
      if (string.IsNullOrEmpty(p.Name)) return;
      if (p.Kind == ParameterKind.String) CallJniStr(bundle, "putString", p.Name, (string)p.RefValue, true);
      else if (p.Kind == ParameterKind.Int64) bundle.Call("putLong", p.Name, p.Int64Value);
      else if (p.Kind == ParameterKind.Double) bundle.Call("putDouble", p.Name, p.DoubleValue);
      else PutObjectInBundle(bundle, p.Name, p.RefValue);
    }
    private static UnityEngine.AndroidJavaObject MapToAndroidBundle(IDictionary<string, object> map) {
      var bundle = new UnityEngine.AndroidJavaObject("android.os.Bundle");
      foreach (KeyValuePair<string, object> kv in map) if (!string.IsNullOrEmpty(kv.Key)) PutObjectInBundle(bundle, kv.Key, kv.Value);
      return bundle;
    }
    private static void PutObjectInBundle(UnityEngine.AndroidJavaObject bundle, string key, object val) {
      if (val == null) bundle.Call("putLong", key, 0L);
      else if (val is string s) CallJniStr(bundle, "putString", key, s, true);
      else if (TryConvertToInt64(val, out long lVal)) bundle.Call("putLong", key, lVal);
      else if (TryConvertToDouble(val, out double dVal)) bundle.Call("putDouble", key, dVal);
      else if (val is IDictionary<string, object> subMap) { using var inner = MapToAndroidBundle(subMap); bundle.Call("putBundle", key, inner); }
      else if (val is IEnumerable<IDictionary<string, object>> seq) {
        using var list = new UnityEngine.AndroidJavaObject("java.util.ArrayList");
        foreach (IDictionary<string, object> item in seq) if (item != null) { using var inner = MapToAndroidBundle(item); list.Call<bool>("add", inner); }
        bundle.Call("putParcelableArrayList", key, list);
      } else if (val is IEnumerable<Parameter> subParams) {
        using var inner = new UnityEngine.AndroidJavaObject("android.os.Bundle");
        foreach (Parameter sp in subParams) if (sp != null) PutParamInBundle(inner, sp);
        bundle.Call("putBundle", key, inner);
      }
    }
    private static void CallJniStr(UnityEngine.AndroidJavaObject target, string method, string a0, string a1, bool twoArgs) {
      IntPtr j0 = a0 != null ? UnityEngine.AndroidJNI.NewStringUTF(a0) : IntPtr.Zero;
      IntPtr j1 = twoArgs && a1 != null ? UnityEngine.AndroidJNI.NewStringUTF(a1) : IntPtr.Zero;
      try { CallJniVoidRaw(target, method, twoArgs ? "(Ljava/lang/String;Ljava/lang/String;)V" : "(Ljava/lang/String;)V", j0, j1, twoArgs); }
      finally { if (j1 != IntPtr.Zero) UnityEngine.AndroidJNI.DeleteLocalRef(j1); if (j0 != IntPtr.Zero) UnityEngine.AndroidJNI.DeleteLocalRef(j0); }
    }
    private static void CallJniVoidRaw(UnityEngine.AndroidJavaObject target, string method, string sig, IntPtr a0, IntPtr a1, bool twoArgs) {
      IntPtr mid = UnityEngine.AndroidJNIHelper.GetMethodID(target.GetRawClass(), method, sig, false);
      var args = new UnityEngine.jvalue[twoArgs ? 2 : 1];
      args[0].l = a0;
      if (twoArgs) args[1].l = a1;
      UnityEngine.AndroidJNI.CallVoidMethod(target.GetRawObject(), mid, args);
      IntPtr ex = UnityEngine.AndroidJNI.ExceptionOccurred();
      if (ex != IntPtr.Zero) {
        UnityEngine.AndroidJNI.ExceptionClear();
        UnityEngine.AndroidJNI.DeleteLocalRef(ex);
        Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Warning, $"Android JNI exception cleared in {method}.");
      }
    }
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    private const string LibObjC = "/usr/lib/libobjc.A.dylib", LibSystem = "/usr/lib/libSystem.B.dylib";
    private const int BlockHasCopyDispose = 1 << 25;
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockDescriptor { public IntPtr reserved, size, copy, dispose; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockLiteral { public IntPtr isa; public int flags, reserved; public IntPtr invoke, descriptor, context; }
    private sealed class AppleBlockBox<T> {
      internal readonly TaskCompletionSource<T> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
      internal void CancelIfIncomplete() => Tcs.TrySetException(new FirebaseException(-1, "Objective-C completion block released without invocation."));
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BlockCopyDelegate(IntPtr dst, IntPtr src);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BlockDisposeDelegate(IntPtr block);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SessionIdBlock(IntPtr block, long sessionId, IntPtr errObj);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void BoolBlock(IntPtr block, [MarshalAs(UnmanagedType.I1)] bool isFound);

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(LibObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(LibSystem)] private static extern IntPtr dlsym(IntPtr handle, string symbol);
    [DllImport(LibSystem)] private static extern IntPtr _Block_copy(ref ObjCBlockLiteral block);
    [DllImport(LibSystem)] private static extern void _Block_release(IntPtr block);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrNUInt(IntPtr r, IntPtr s, nuint cap);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrBytes(IntPtr r, IntPtr s, byte[] b, nuint len);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrLong(IntPtr r, IntPtr s, long v);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrDouble(IntPtr r, IntPtr s, double v);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBoolSel(IntPtr r, IntPtr s, IntPtr sel);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool v);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidDouble(IntPtr r, IntPtr s, double v);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr2(IntPtr r, IntPtr s, IntPtr a, IntPtr b);

    private static readonly int BlockCtxOffset = Marshal.OffsetOf<ObjCBlockLiteral>(nameof(ObjCBlockLiteral.context)).ToInt32();
    private static readonly BlockCopyDelegate BlockCopyCb = OnBlockCopy;
    private static readonly BlockDisposeDelegate BlockDisposeCb = OnBlockDispose;
    private static readonly SessionIdBlock SessionIdCb = OnSessionIdCompleted;
    private static readonly BoolBlock AppleTxCb = OnAppleTransactionCompleted;
    private static IntPtr _blockDescriptorPtr;
    private static readonly object _appleResetLock = new();
    private static int _appleResetState;
    private static long _appleResetStamp;
    private static string _applePreResetInstanceId = string.Empty;

    private static void ResetAppleResetterState() { lock (_appleResetLock) { _appleResetState = 0; _appleResetStamp = 0; _applePreResetInstanceId = string.Empty; } }
    internal static void LogEventSingle(string name, string paramName, ParameterKind kind, long iVal, double dVal, string sVal) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      WithFirAnalytics(cls => {
        IntPtr dict = SendPtrNUInt(objc_getClass("NSMutableDictionary"), sel_registerName("dictionaryWithCapacity:"), paramName != null ? (nuint)1 : 0);
        if (paramName != null) {
          IntPtr numCls = objc_getClass("NSNumber");
          IntPtr v = kind == ParameterKind.Int64 ? SendPtrLong(numCls, sel_registerName("numberWithLongLong:"), iVal)
              : (kind == ParameterKind.Double ? SendPtrDouble(numCls, sel_registerName("numberWithDouble:"), dVal) : CreateNSString(sVal));
          if (v != IntPtr.Zero) SendVoidPtr2(dict, sel_registerName("setObject:forKey:"), v, CreateNSString(paramName));
        }
        SendVoidPtr2(cls, sel_registerName("logEventWithName:parameters:"), CreateNSString(name), dict);
      });
    }
    internal static void LogEventParameters(string name, Parameter[] parameters) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      WithFirAnalytics(cls => {
        IntPtr dict = SendPtrNUInt(objc_getClass("NSMutableDictionary"), sel_registerName("dictionaryWithCapacity:"), parameters != null ? (nuint)parameters.Length : 0);
        if (parameters != null) for (int i = 0; i < parameters.Length; i++) if (parameters[i] != null) PutParamInNSDictionary(dict, parameters[i], false);
        SendVoidPtr2(cls, sel_registerName("logEventWithName:parameters:"), CreateNSString(name), dict);
      });
    }
    internal static void SetDefaultEventParameters(Parameter[] parameters) {
      EnsureSession();
      WithFirAnalytics(cls => {
        if (parameters == null || parameters.Length == 0) { SendVoidPtr(cls, sel_registerName("setDefaultEventParameters:"), IntPtr.Zero); return; }
        IntPtr dict = SendPtrNUInt(objc_getClass("NSMutableDictionary"), sel_registerName("dictionaryWithCapacity:"), (nuint)parameters.Length);
        for (int i = 0; i < parameters.Length; i++) if (parameters[i] != null) PutParamInNSDictionary(dict, parameters[i], true);
        SendVoidPtr(cls, sel_registerName("setDefaultEventParameters:"), dict);
      });
    }
    internal static void SetAnalyticsCollectionEnabled(bool enabled) { EnsureSession(); WithFirAnalytics(cls => SendVoidBool(cls, sel_registerName("setAnalyticsCollectionEnabled:"), enabled)); }
    internal static void SetConsent(IDictionary<ConsentType, ConsentStatus> settings) { EnsureSession(); WithFirAnalytics(cls => SetAppleConsent(cls, settings)); }
    internal static void SetSessionTimeoutDuration(long ms) { EnsureSession(); WithFirAnalytics(cls => SendVoidDouble(cls, sel_registerName("setSessionTimeoutInterval:"), ms / 1000.0)); }
    internal static void SetUserId(string userId) { EnsureSession(); WithFirAnalytics(cls => SendVoidPtr(cls, sel_registerName("setUserID:"), userId != null ? CreateNSString(userId) : IntPtr.Zero)); }
    internal static void SetUserProperty(string name, string prop) {
      EnsureSession();
      if (!string.IsNullOrEmpty(name)) WithFirAnalytics(cls => SendVoidPtr2(cls, sel_registerName("setUserPropertyString:forName:"), prop != null ? CreateNSString(prop) : IntPtr.Zero, CreateNSString(name)));
    }
    internal static void ResetAnalyticsData() {
      EnsureSession();
      WithFirAnalytics(cls => {
        lock (_appleResetLock) { _appleResetStamp = Stopwatch.GetTimestamp(); _appleResetState = 1; _applePreResetInstanceId = ReadNSString(SendPtr(cls, sel_registerName("appInstanceID"))); SendVoid(cls, sel_registerName("resetAnalyticsData")); }
      });
    }
    internal static Task<string> GetAnalyticsInstanceIdAsync() => GetAppleInstanceIdAsync(EnsureSession());
    internal static Task<long> GetSessionIdAsync() { EnsureSession(); return InvokeAppleBlockAsync<long>("FIRAnalytics", "sessionIDWithCompletion:", null, SessionIdCb); }
    internal static Task LogAppleTransactionAsync(string txId) {
      EnsureSession();
      return txId == null ? Task.FromException(new FirebaseException(-1, "Transaction ID is null")) : InvokeAppleBlockAsync<bool>("FirebaseUnityAppleTransactionVerifier", "verifyWithTransactionId:completion:", txId, AppleTxCb);
    }
    internal static void InitiateOnDeviceConversionString(bool isEmail, string value) {
      EnsureSession();
      string selName = isEmail ? "initiateOnDeviceConversionMeasurementWithEmailAddress:" : "initiateOnDeviceConversionMeasurementWithPhoneNumber:";
      WithFirAnalytics(cls => { IntPtr sel = sel_registerName(selName); if (SendBoolSel(cls, sel_registerName("respondsToSelector:"), sel)) SendVoidPtr(cls, sel, CreateNSString(value)); });
    }
    internal static void InitiateOnDeviceConversionBytes(bool isEmail, byte[] bytes) {
      EnsureSession();
      string selName = isEmail ? "initiateOnDeviceConversionMeasurementWithHashedEmailAddress:" : "initiateOnDeviceConversionMeasurementWithHashedPhoneNumber:";
      WithFirAnalytics(cls => { IntPtr sel = sel_registerName(selName); if (SendBoolSel(cls, sel_registerName("respondsToSelector:"), sel)) SendVoidPtr(cls, sel, SendPtrBytes(objc_getClass("NSData"), sel_registerName("dataWithBytes:length:"), bytes, (nuint)bytes.Length)); });
    }
    private static void WithFirAnalytics(Action<IntPtr> action) {
      IntPtr pool = objc_autoreleasePoolPush();
      try { IntPtr cls = objc_getClass("FIRAnalytics"); if (cls != IntPtr.Zero) action(cls); }
      finally { if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool); }
    }
    private static IntPtr CreateNSString(string value) {
      string safe = value ?? string.Empty;
      byte[] rented = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(safe.Length) + 1);
      GCHandle pin = default;
      try {
        rented[Encoding.UTF8.GetBytes(safe, 0, safe.Length, rented, 0)] = 0;
        pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
        return SendPtrPtr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), pin.AddrOfPinnedObject());
      } finally { if (pin.IsAllocated) pin.Free(); ArrayPool<byte>.Shared.Return(rented); }
    }
    private static string ReadNSString(IntPtr ns) =>
        ns != IntPtr.Zero && SendPtr(ns, sel_registerName("UTF8String")) is IntPtr p && p != IntPtr.Zero ? (Firebase.Internal.FirebaseInterops.PtrToStringUtf8(p) ?? string.Empty) : string.Empty;
    private static IntPtr CreateHeapBlock(IntPtr invokePtr, IntPtr contextPtr) {
      IntPtr stackIsa = dlsym(new IntPtr(-2), "_NSConcreteStackBlock");
      if (stackIsa == IntPtr.Zero) return IntPtr.Zero;
      if (_blockDescriptorPtr == IntPtr.Zero) {
        var desc = new ObjCBlockDescriptor { reserved = IntPtr.Zero, size = (IntPtr)Marshal.SizeOf<ObjCBlockLiteral>(), copy = Marshal.GetFunctionPointerForDelegate(BlockCopyCb), dispose = Marshal.GetFunctionPointerForDelegate(BlockDisposeCb) };
        IntPtr d = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockDescriptor>());
        Marshal.StructureToPtr(desc, d, false);
        if (Interlocked.CompareExchange(ref _blockDescriptorPtr, d, IntPtr.Zero) != IntPtr.Zero) Marshal.FreeHGlobal(d);
      }
      var lit = new ObjCBlockLiteral { isa = stackIsa, flags = BlockHasCopyDispose, invoke = invokePtr, descriptor = _blockDescriptorPtr, context = contextPtr };
      return _Block_copy(ref lit);
    }
    [AOT.MonoPInvokeCallback(typeof(BlockCopyDelegate))] private static void OnBlockCopy(IntPtr dst, IntPtr src) { }
    [AOT.MonoPInvokeCallback(typeof(BlockDisposeDelegate))]
    private static void OnBlockDispose(IntPtr block) {
      try {
        IntPtr ctx = Marshal.ReadIntPtr(block, BlockCtxOffset);
        if (ctx == IntPtr.Zero) return;
        Marshal.WriteIntPtr(block, BlockCtxOffset, IntPtr.Zero);
        GCHandle gch = GCHandle.FromIntPtr(ctx);
        Action cancel = gch.Target as Action;
        gch.Free();
        cancel?.Invoke();
      } catch (Exception ex) { Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Debug, "Analytics ObjC block dispose error: " + ex.Message); }
    }
    private static void PutParamInNSDictionary(IntPtr dict, Parameter p, bool allowNullReset) {
      if (string.IsNullOrEmpty(p.Name)) return;
      IntPtr numCls = objc_getClass("NSNumber");
      IntPtr valObj = p.Kind switch {
        ParameterKind.String when allowNullReset && p.RefValue == null => SendPtr(objc_getClass("NSNull"), sel_registerName("null")),
        ParameterKind.String => CreateNSString((string)p.RefValue),
        ParameterKind.Int64 => SendPtrLong(numCls, sel_registerName("numberWithLongLong:"), p.Int64Value),
        ParameterKind.Double => SendPtrDouble(numCls, sel_registerName("numberWithDouble:"), p.DoubleValue),
        ParameterKind.Map when p.RefValue is IDictionary<string, object> m => MapToNSDictionary(m),
        ParameterKind.MapSequence when p.RefValue is IEnumerable<IDictionary<string, object>> s => SeqToNSArray(s),
        _ when p.RefValue == null => SendPtrLong(numCls, sel_registerName("numberWithLongLong:"), 0L),
        _ => IntPtr.Zero,
      };
      if (valObj != IntPtr.Zero) SendVoidPtr2(dict, sel_registerName("setObject:forKey:"), valObj, CreateNSString(p.Name));
    }
    private static IntPtr SeqToNSArray(IEnumerable<IDictionary<string, object>> seq) {
      IntPtr arr = SendPtrNUInt(objc_getClass("NSMutableArray"), sel_registerName("arrayWithCapacity:"), 4), addSel = sel_registerName("addObject:");
      foreach (IDictionary<string, object> item in seq) if (item != null && MapToNSDictionary(item) is IntPtr sub && sub != IntPtr.Zero) SendVoidPtr(arr, addSel, sub);
      return arr;
    }
    private static IntPtr MapToNSDictionary(IDictionary<string, object> map) {
      IntPtr dict = SendPtrNUInt(objc_getClass("NSMutableDictionary"), sel_registerName("dictionaryWithCapacity:"), (nuint)map.Count);
      IntPtr setSel = sel_registerName("setObject:forKey:"), numCls = objc_getClass("NSNumber");
      foreach (KeyValuePair<string, object> kv in map) {
        if (string.IsNullOrEmpty(kv.Key)) continue;
        IntPtr v = kv.Value == null ? SendPtrLong(numCls, sel_registerName("numberWithLongLong:"), 0L)
            : (kv.Value is string s ? CreateNSString(s)
            : (TryConvertToInt64(kv.Value, out long l) ? SendPtrLong(numCls, sel_registerName("numberWithLongLong:"), l)
            : (TryConvertToDouble(kv.Value, out double d) ? SendPtrDouble(numCls, sel_registerName("numberWithDouble:"), d)
            : (kv.Value is IDictionary<string, object> subMap ? MapToNSDictionary(subMap)
            : (kv.Value is IEnumerable<IDictionary<string, object>> subSeq ? SeqToNSArray(subSeq) : IntPtr.Zero)))));
        if (v != IntPtr.Zero) SendVoidPtr2(dict, setSel, v, CreateNSString(kv.Key));
      }
      return dict;
    }
    private static void SetAppleConsent(IntPtr cls, IDictionary<ConsentType, ConsentStatus> settings) {
      IntPtr dict = SendPtrNUInt(objc_getClass("NSMutableDictionary"), sel_registerName("dictionaryWithCapacity:"), (nuint)settings.Count);
      IntPtr setSel = sel_registerName("setObject:forKey:");
      foreach (KeyValuePair<ConsentType, ConsentStatus> kv in settings) {
        string k = kv.Key switch { ConsentType.AdStorage => "ad_storage", ConsentType.AnalyticsStorage => "analytics_storage", ConsentType.AdUserData => "ad_user_data", ConsentType.AdPersonalization => "ad_personalization", _ => null };
        string s = kv.Value switch { ConsentStatus.Granted => "granted", ConsentStatus.Denied => "denied", _ => null };
        if (k == null || s == null) return;
        SendVoidPtr2(dict, setSel, CreateNSString(s), CreateNSString(k));
      }
      SendVoidPtr(cls, sel_registerName("setConsent:"), dict);
    }
    private static async Task<string> GetAppleInstanceIdAsync(SessionState session) {
      long startStamp = Stopwatch.GetTimestamp();
      while (!session.Disposed) {
        string current = string.Empty, resolved = string.Empty;
        bool hasClass = false;
        WithFirAnalytics(cls => { hasClass = true; current = ReadNSString(SendPtr(cls, sel_registerName("appInstanceID"))); });
        if (!hasClass) throw new FirebaseException(-1, "FIRAnalytics unavailable.");
        lock (_appleResetLock) {
          long elapsedMs = (Stopwatch.GetTimestamp() - _appleResetStamp) * 1000L / Stopwatch.Frequency;
          if (_appleResetState == 1 && elapsedMs >= 1000L && (string.IsNullOrEmpty(current) || current == _applePreResetInstanceId)) {
            _appleResetState = 2; _appleResetStamp = Stopwatch.GetTimestamp();
            WithFirAnalytics(cls => SendVoid(cls, sel_registerName("resetAnalyticsData")));
          } else if (!((_appleResetState == 1 || _appleResetState == 2) && (string.IsNullOrEmpty(current) || current == _applePreResetInstanceId) && elapsedMs < 5000L) && !string.IsNullOrEmpty(current)) {
            _appleResetState = 0; _applePreResetInstanceId = current; resolved = current;
          }
        }
        if (!string.IsNullOrEmpty(resolved)) return resolved;
        if ((Stopwatch.GetTimestamp() - startStamp) * 1000L / Stopwatch.Frequency >= 5000L) throw new FirebaseException(-1, "Analytics app instance ID is unavailable.");
        await Task.Delay(100).ConfigureAwait(false);
      }
      throw new FirebaseException(-1, "Analytics session was disposed.");
    }
    private static Task<T> InvokeAppleBlockAsync<T>(string clsName, string selName, string optFirstStrArg, Delegate cb) {
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        IntPtr cls = objc_getClass(clsName), sel = sel_registerName(selName);
        if (cls == IntPtr.Zero || !SendBoolSel(cls, sel_registerName("respondsToSelector:"), sel)) {
          return Task.FromException<T>(new FirebaseException(-1, optFirstStrArg != null ? "StoreKit 2 transaction not found." : $"{clsName} unavailable."));
        }
        var box = new AppleBlockBox<T>();
        Action holder = box.CancelIfIncomplete;
        GCHandle gch = GCHandle.Alloc(holder);
        IntPtr heapBlock = CreateHeapBlock(Marshal.GetFunctionPointerForDelegate(cb), GCHandle.ToIntPtr(gch));
        if (heapBlock == IntPtr.Zero) { gch.Free(); return Task.FromException<T>(new FirebaseException(-1, "Block allocation failed.")); }
        try {
          if (optFirstStrArg != null) SendVoidPtr2(cls, sel, CreateNSString(optFirstStrArg), heapBlock);
          else SendVoidPtr(cls, sel, heapBlock);
          return box.Tcs.Task;
        } finally { _Block_release(heapBlock); }
      } finally { if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool); }
    }
    private static TaskCompletionSource<T> PeekBlockTcs<T>(IntPtr block) =>
        Marshal.ReadIntPtr(block, BlockCtxOffset) is IntPtr ctx && ctx != IntPtr.Zero && GCHandle.FromIntPtr(ctx).Target is Action a && a.Target is AppleBlockBox<T> box ? box.Tcs : null;
    [AOT.MonoPInvokeCallback(typeof(SessionIdBlock))]
    private static void OnSessionIdCompleted(IntPtr block, long sessionId, IntPtr errorObj) {
      IntPtr pool = objc_autoreleasePoolPush();
      TaskCompletionSource<long> tcs = null;
      try {
        tcs = PeekBlockTcs<long>(block);
        if (errorObj != IntPtr.Zero) {
          string msg = ReadNSString(SendPtr(errorObj, sel_registerName("localizedDescription")));
          tcs?.TrySetException(new FirebaseException(-1, string.IsNullOrEmpty(msg) ? "Failed to retrieve session ID." : msg));
        } else tcs?.TrySetResult(sessionId);
      } catch (Exception ex) { tcs?.TrySetException(new FirebaseException(-1, ex.Message, ex)); }
      finally { if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool); }
    }
    [AOT.MonoPInvokeCallback(typeof(BoolBlock))]
    private static void OnAppleTransactionCompleted(IntPtr block, bool isFound) {
      IntPtr pool = objc_autoreleasePoolPush();
      TaskCompletionSource<bool> tcs = null;
      try {
        tcs = PeekBlockTcs<bool>(block);
        if (isFound) tcs?.TrySetResult(true);
        else tcs?.TrySetException(new FirebaseException(-1, "StoreKit 2 transaction not found."));
      } catch (Exception ex) { tcs?.TrySetException(new FirebaseException(-1, ex.Message, ex)); }
      finally { if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool); }
    }
#else
    private const long StubBaseSessionId = 0x5E5510171D570BL, DefaultSessionTimeoutMs = 1800000L;
    private static readonly object _stateLock = new();
    private static bool _collectionEnabled = true;
    private static string _userId, _lastEventName;
    private static long _sessionTimeoutMs = DefaultSessionTimeoutMs, _loggedEventCount, _onDeviceConversionCount;
    private static int _instanceSequence;
    private static readonly Dictionary<string, string> _userProperties = new(StringComparer.Ordinal);
    private static readonly Dictionary<ConsentType, ConsentStatus> _consentSettings = new();
    private static readonly Dictionary<string, object> _defaultParameters = new(StringComparer.Ordinal);
    private static volatile bool _hasDefaultParameters;

    internal static bool CollectionEnabled { get { lock (_stateLock) return _collectionEnabled; } }
    internal static string UserId { get { lock (_stateLock) return _userId; } }
    internal static long SessionTimeoutMs { get { lock (_stateLock) return _sessionTimeoutMs; } }
    internal static long LoggedEventCount => Interlocked.Read(ref _loggedEventCount);
    internal static long OnDeviceConversionCount => Interlocked.Read(ref _onDeviceConversionCount);
    internal static string LastEventName { get { lock (_stateLock) return _lastEventName; } }
    internal static bool IsNativeDesktopLoaded => MeleeNativeLoader.IsLoaded;
    internal static Dictionary<string, string> GetUserPropertiesSnapshot() { lock (_stateLock) return new Dictionary<string, string>(_userProperties, StringComparer.Ordinal); }
    internal static Dictionary<ConsentType, ConsentStatus> GetConsentSnapshot() { lock (_stateLock) return new Dictionary<ConsentType, ConsentStatus>(_consentSettings); }
    internal static Dictionary<string, object> GetDefaultParametersSnapshot() { lock (_stateLock) return new Dictionary<string, object>(_defaultParameters, StringComparer.Ordinal); }

    private static void ResetDesktopState() {
      MeleeNativeLoader.NotifyTerminationIfLoaded();
      lock (_stateLock) {
        _collectionEnabled = true; _userId = null; _sessionTimeoutMs = DefaultSessionTimeoutMs;
        _instanceSequence = 0; _lastEventName = null; _hasDefaultParameters = false;
        _userProperties.Clear(); _consentSettings.Clear(); _defaultParameters.Clear();
      }
      Interlocked.Exchange(ref _loggedEventCount, 0L);
      Interlocked.Exchange(ref _onDeviceConversionCount, 0L);
    }
    internal static void LogEventSingle(string name, string paramName, ParameterKind kind, long iVal, double dVal, string sVal) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      if (paramName == null) { LogEventParameters(name, null); return; }
      if (MeleeNativeLoader.IsLoaded) MeleeNativeLoader.TryLogEvent(name, new[] { MakeParam(paramName, kind, iVal, dVal, sVal) });
      Interlocked.Increment(ref _loggedEventCount);
      Action<LoggedEventRecord> sink = EventSinkForTesting;
      if (sink == null) { lock (_stateLock) _lastEventName = name; return; }
      Dictionary<string, object> dict = CreateEventDictionary(name, 1);
      dict[paramName] = kind == ParameterKind.Int64 ? (object)iVal : (kind == ParameterKind.Double ? dVal : sVal);
      sink(new LoggedEventRecord(name, dict));
    }
    internal static void LogEventParameters(string name, Parameter[] parameters) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      if (parameters != null) for (int i = 0; i < parameters.Length; i++) ValidateParameterTree(parameters[i]);
      MeleeNativeLoader.TryLogEvent(name, parameters);
      Interlocked.Increment(ref _loggedEventCount);
      Action<LoggedEventRecord> sink = EventSinkForTesting;
      if (sink == null) { lock (_stateLock) _lastEventName = name; return; }
      Dictionary<string, object> dict = CreateEventDictionary(name, parameters?.Length ?? 0);
      if (parameters != null) for (int i = 0; i < parameters.Length; i++) if (parameters[i] is Parameter p && !string.IsNullOrEmpty(p.Name)) dict[p.Name] = ExtractValue(p);
      sink(new LoggedEventRecord(name, dict));
    }
    internal static void SetDefaultEventParameters(Parameter[] parameters) {
      EnsureSession();
      lock (_stateLock) {
        if (parameters == null || parameters.Length == 0) { _defaultParameters.Clear(); _hasDefaultParameters = false; return; }
        for (int i = 0; i < parameters.Length; i++) {
          if (parameters[i] is not Parameter p || string.IsNullOrEmpty(p.Name)) continue;
          if (p.Kind == ParameterKind.String && p.RefValue == null) _defaultParameters.Remove(p.Name);
          else _defaultParameters[p.Name] = ExtractValue(p);
        }
        _hasDefaultParameters = _defaultParameters.Count > 0;
      }
    }
    internal static void SetAnalyticsCollectionEnabled(bool enabled) { EnsureSession(); lock (_stateLock) _collectionEnabled = enabled; MeleeNativeLoader.TrySetCollectionEnabled(enabled); }
    internal static void SetConsent(IDictionary<ConsentType, ConsentStatus> s) { EnsureSession(); lock (_stateLock) foreach (KeyValuePair<ConsentType, ConsentStatus> kv in s) _consentSettings[kv.Key] = kv.Value; }
    internal static void SetSessionTimeoutDuration(long ms) { EnsureSession(); lock (_stateLock) _sessionTimeoutMs = ms; }
    internal static void SetUserId(string userId) { EnsureSession(); lock (_stateLock) _userId = userId; MeleeNativeLoader.TrySetUserId(userId); }
    internal static void SetUserProperty(string name, string prop) {
      EnsureSession();
      if (string.IsNullOrEmpty(name)) return;
      lock (_stateLock) { if (prop == null) _userProperties.Remove(name); else _userProperties[name] = prop; }
      MeleeNativeLoader.TrySetUserProperty(name, prop);
    }
    internal static void ResetAnalyticsData() {
      EnsureSession();
      lock (_stateLock) { _instanceSequence++; _userId = null; _userProperties.Clear(); _defaultParameters.Clear(); _hasDefaultParameters = false; }
      MeleeNativeLoader.TryResetAnalyticsData();
    }
    internal static Task<string> GetAnalyticsInstanceIdAsync() { EnsureSession(); int seq; lock (_stateLock) seq = _instanceSequence; return Task.FromResult("FakeAnalyticsInstanceId" + seq.ToString(CultureInfo.InvariantCulture)); }
    internal static Task<long> GetSessionIdAsync() { EnsureSession(); int seq; lock (_stateLock) seq = _instanceSequence; return Task.FromResult(StubBaseSessionId + seq); }
    internal static Task LogAppleTransactionAsync(string txId) { EnsureSession(); return Task.CompletedTask; }
    internal static void InitiateOnDeviceConversionString(bool isEmail, string value) { EnsureSession(); Interlocked.Increment(ref _onDeviceConversionCount); }
    internal static void InitiateOnDeviceConversionBytes(bool isEmail, byte[] bytes) { EnsureSession(); Interlocked.Increment(ref _onDeviceConversionCount); }

    private static Parameter MakeParam(string n, ParameterKind k, long i, double d, string s) => k switch { ParameterKind.Int64 => new Parameter(n, i), ParameterKind.Double => new Parameter(n, d), _ => new Parameter(n, s) };
    private static Dictionary<string, object> CreateEventDictionary(string name, int cap) {
      lock (_stateLock) { _lastEventName = name; return _hasDefaultParameters ? new Dictionary<string, object>(_defaultParameters, StringComparer.Ordinal) : new Dictionary<string, object>(cap, StringComparer.Ordinal); }
    }
    private static object ExtractValue(Parameter p) => p.Kind switch { ParameterKind.Int64 => p.Int64Value, ParameterKind.Double => p.DoubleValue, _ => p.RefValue };
    private static void ValidateParameterTree(Parameter p) {
      if (p?.Kind == ParameterKind.Map) TouchMap(p.RefValue as IDictionary<string, object>);
      else if (p?.Kind == ParameterKind.MapSequence && p.RefValue is IList<IDictionary<string, object>> list) for (int i = 0; i < list.Count; i++) TouchMap(list[i]);
      else if (p?.Kind == ParameterKind.MapSequence && p.RefValue is IEnumerable<IDictionary<string, object>> seq) foreach (IDictionary<string, object> item in seq) TouchMap(item);
    }
    private static void TouchMap(IDictionary<string, object> map) {
      if (map is Dictionary<string, object> concrete) foreach (KeyValuePair<string, object> kv in concrete) _ = kv.Key;
      else if (map != null) foreach (KeyValuePair<string, object> kv in map) _ = kv.Key;
    }

    internal static class MeleeNativeLoader {
      private static readonly string[] KnownWindowsDllSha256 = {
        "c1b9ff6e9119c30bbeb7472326dcde418f45682e6b822e25eed922fe6e3cc698", "13ae5f9349b24186f1f3667b52832076e8d14ad9656c3546b1b7fca79ac8144b",
        "3f1fb1bb21bce0061c4b89bb674d3b6c94eaea2c8de98802198a35ea94c97900", "1e944cd4a2b8d115a32d01f4cc900f23805934b5587b55305df9cd189f9d78d5",
        "9127cffeb617f07197e13b40b09f756ae856f3a4f066115e6fc9d45d33293e7d",
      };
      [StructLayout(LayoutKind.Sequential)]
      internal struct NativeGaOptions {
        public IntPtr app_id, package_name;
        [MarshalAs(UnmanagedType.I1)] public bool analytics_collection_enabled_at_first_launch;
        public IntPtr app_data_directory;
        public IntPtr reserved;
      }
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FnCreate();
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnPtr(IntPtr ptr);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnBoolPtr(IntPtr p);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnIsInit();
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnInsInt(IntPtr m, IntPtr k, long v);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnInsDbl(IntPtr m, IntPtr k, double v);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnInsPtr(IntPtr m, IntPtr k, IntPtr v);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private delegate bool FnInsItem(IntPtr vec, IntPtr item);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnPtr2(IntPtr a, IntPtr b);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnVoid();
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnBool([MarshalAs(UnmanagedType.I1)] bool val);
      [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void FnInt(int state);
      [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryW(string path);
      [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] private static extern IntPtr GetProcAddress(IntPtr mod, string name);
      [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlopen(string path, int mode);
      [DllImport("/usr/lib/libSystem.B.dylib")] private static extern IntPtr dlsym(IntPtr handle, string sym);
      private static readonly object _loadLock = new();
      private static int _probeState;
      private static IntPtr _pinnedAppId, _pinnedPkgName;
      private static FnCreate _optionsCreate, _itemCreate, _itemVecCreate, _paramsCreate;
      private static FnPtr _optionsDestroy, _itemDestroy, _itemVecDestroy, _paramsDestroy, _setUserId;
      private static FnBoolPtr _initialize;
      private static FnIsInit _isInitialized;
      private static FnInsInt _itemInsertInt, _paramsInsertInt;
      private static FnInsDbl _itemInsertDouble, _paramsInsertDouble;
      private static FnInsPtr _itemInsertStr, _paramsInsertStr, _paramsInsertItemVec;
      private static FnInsItem _itemVecInsertItem;
      private static FnPtr2 _logEvent, _setUserProp;
      private static FnVoid _resetData;
      private static FnBool _setEnabled;
      private static FnInt _notifyLifecycle;

      internal static bool IsLoaded => Volatile.Read(ref _probeState) == 1;
      internal static void NotifyTerminationIfLoaded() { if (IsLoaded) _notifyLifecycle?.Invoke((int)AppLifecycleState.Termination); }
      internal static void TrySetCollectionEnabled(bool enabled) { if (IsLoaded) _setEnabled?.Invoke(enabled); }
      internal static void TryResetAnalyticsData() { if (IsLoaded) _resetData?.Invoke(); }
      internal static void TrySetUserId(string userId) { if (IsLoaded && _setUserId != null) { IntPtr u = userId != null ? AllocUtf8(userId) : IntPtr.Zero; try { _setUserId(u); } finally { FreeUtf8(ref u); } } }
      internal static void TrySetUserProperty(string name, string prop) { if (IsLoaded && _setUserProp != null) { IntPtr k = AllocUtf8(name), v = prop != null ? AllocUtf8(prop) : IntPtr.Zero; try { _setUserProp(k, v); } finally { FreeUtf8(ref v); FreeUtf8(ref k); } } }

      internal static void TryInitialize(string appId, string pkgName, string dataPath, bool enabled) {
        lock (_loadLock) {
          if (_probeState == -1) return;
          if (_probeState == 0) _probeState = ProbeAndBind(dataPath) ? 1 : -1;
          if (_probeState != 1 || (_isInitialized != null && _isInitialized()) || string.IsNullOrEmpty(appId)) return;
          IntPtr optsPtr = _optionsCreate();
          if (optsPtr == IntPtr.Zero) return;
          bool consumed = false;
          try {
            FreeUtf8(ref _pinnedAppId); FreeUtf8(ref _pinnedPkgName);
            _pinnedAppId = AllocUtf8(appId); _pinnedPkgName = AllocUtf8(string.IsNullOrEmpty(pkgName) ? "com.google.firebase.unity" : pkgName);
            NativeGaOptions nativeOpts = Marshal.PtrToStructure<NativeGaOptions>(optsPtr);
            nativeOpts.app_id = _pinnedAppId; nativeOpts.package_name = _pinnedPkgName; nativeOpts.analytics_collection_enabled_at_first_launch = enabled;
            Marshal.StructureToPtr(nativeOpts, optsPtr, false);
            consumed = true;
            _initialize(optsPtr);
          } finally { if (!consumed) _optionsDestroy?.Invoke(optsPtr); }
        }
      }
      internal static void TryLogEvent(string name, Parameter[] parameters) {
        if (!IsLoaded || _logEvent == null) return;
        IntPtr pMap = IntPtr.Zero, nPtr = IntPtr.Zero;
        bool logged = false;
        try {
          if (parameters != null && parameters.Length > 0) {
            pMap = _paramsCreate();
            if (pMap == IntPtr.Zero) return;
            for (int i = 0; i < parameters.Length; i++) InsertParameter(pMap, parameters[i]);
          }
          nPtr = AllocUtf8(name);
          logged = true;
          _logEvent(nPtr, pMap);
        } finally { FreeUtf8(ref nPtr); if (!logged && pMap != IntPtr.Zero) _paramsDestroy?.Invoke(pMap); }
      }
      private static void InsertParameter(IntPtr pMap, Parameter p) {
        if (p == null || string.IsNullOrEmpty(p.Name)) return;
        IntPtr kPtr = AllocUtf8(p.Name);
        try {
          if (p.Kind == ParameterKind.Int64) _paramsInsertInt(pMap, kPtr, p.Int64Value);
          else if (p.Kind == ParameterKind.Double) _paramsInsertDouble(pMap, kPtr, p.DoubleValue);
          else if (p.Kind == ParameterKind.String) { IntPtr vPtr = AllocUtf8((string)p.RefValue ?? string.Empty); try { _paramsInsertStr(pMap, kPtr, vPtr); } finally { FreeUtf8(ref vPtr); } }
          else if (p.Kind == ParameterKind.Map && p.RefValue is IDictionary<string, object> dict && dict.Count > 0) InsertItems(pMap, kPtr, new[] { dict });
          else if (p.Kind == ParameterKind.MapSequence && p.RefValue is IEnumerable<IDictionary<string, object>> seq) InsertItems(pMap, kPtr, seq);
        } finally { FreeUtf8(ref kPtr); }
      }
      private static void InsertItems(IntPtr pMap, IntPtr kPtr, IEnumerable<IDictionary<string, object>> items) {
        IntPtr vec = _itemVecCreate();
        if (vec == IntPtr.Zero) return;
        bool anyAdded = false;
        foreach (IDictionary<string, object> dict in items) {
          if (dict == null || dict.Count == 0) continue;
          IntPtr item = _itemCreate();
          if (item == IntPtr.Zero) continue;
          bool propAdded = false;
          foreach (KeyValuePair<string, object> kv in dict) {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            IntPtr ik = AllocUtf8(kv.Key);
            try {
              if (TryConvertToInt64(kv.Value, out long l)) propAdded |= _itemInsertInt(item, ik, l);
              else if (TryConvertToDouble(kv.Value, out double d)) propAdded |= _itemInsertDouble(item, ik, d);
              else if (kv.Value is string s) { IntPtr sv = AllocUtf8(s); try { propAdded |= _itemInsertStr(item, ik, sv); } finally { FreeUtf8(ref sv); } }
            } finally { FreeUtf8(ref ik); }
          }
          if (propAdded) { _itemVecInsertItem(vec, item); anyAdded = true; } else _itemDestroy(item);
        }
        if (anyAdded) _paramsInsertItemVec(pMap, kPtr, vec); else _itemVecDestroy(vec);
      }
      private static bool ProbeAndBind(string dataPath) {
        try {
          bool isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows), isMac = !isWin && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
          if (!isWin && !isMac) return false;
          IntPtr handle = IntPtr.Zero;
          foreach (string p in CandidatePaths(dataPath, isWin ? "google_analytics.dll" : "libgoogle_analytics.dylib")) {
            if (!File.Exists(p)) continue;
            if (!isWin) { handle = dlopen(p, 2); break; }
            using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sha = SHA256.Create();
            string hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty);
            for (int i = 0; i < KnownWindowsDllSha256.Length; i++) if (string.Equals(hex, KnownWindowsDllSha256[i], StringComparison.OrdinalIgnoreCase)) { handle = LoadLibraryW(p); break; }
            if (handle != IntPtr.Zero) break;
          }
          if (handle == IntPtr.Zero) return false;
          Func<string, IntPtr> sym = n => isWin ? GetProcAddress(handle, n) : dlsym(handle, n);
          _optionsCreate = Bind<FnCreate>(sym, "GoogleAnalytics_Options_Create"); _optionsDestroy = Bind<FnPtr>(sym, "GoogleAnalytics_Options_Destroy");
          _itemCreate = Bind<FnCreate>(sym, "GoogleAnalytics_Item_Create"); _itemDestroy = Bind<FnPtr>(sym, "GoogleAnalytics_Item_Destroy");
          _itemInsertInt = Bind<FnInsInt>(sym, "GoogleAnalytics_Item_InsertInt"); _itemInsertDouble = Bind<FnInsDbl>(sym, "GoogleAnalytics_Item_InsertDouble"); _itemInsertStr = Bind<FnInsPtr>(sym, "GoogleAnalytics_Item_InsertString");
          _itemVecCreate = Bind<FnCreate>(sym, "GoogleAnalytics_ItemVector_Create"); _itemVecInsertItem = Bind<FnInsItem>(sym, "GoogleAnalytics_ItemVector_InsertItem"); _itemVecDestroy = Bind<FnPtr>(sym, "GoogleAnalytics_ItemVector_Destroy");
          _paramsCreate = Bind<FnCreate>(sym, "GoogleAnalytics_EventParameters_Create"); _paramsDestroy = Bind<FnPtr>(sym, "GoogleAnalytics_EventParameters_Destroy");
          _paramsInsertInt = Bind<FnInsInt>(sym, "GoogleAnalytics_EventParameters_InsertInt"); _paramsInsertDouble = Bind<FnInsDbl>(sym, "GoogleAnalytics_EventParameters_InsertDouble");
          _paramsInsertStr = Bind<FnInsPtr>(sym, "GoogleAnalytics_EventParameters_InsertString"); _paramsInsertItemVec = Bind<FnInsPtr>(sym, "GoogleAnalytics_EventParameters_InsertItemVector");
          _initialize = Bind<FnBoolPtr>(sym, "GoogleAnalytics_Initialize"); _isInitialized = Bind<FnIsInit>(sym, "GoogleAnalytics_IsInitialized");
          _logEvent = Bind<FnPtr2>(sym, "GoogleAnalytics_LogEvent"); _setUserProp = Bind<FnPtr2>(sym, "GoogleAnalytics_SetUserProperty");
          _setUserId = Bind<FnPtr>(sym, "GoogleAnalytics_SetUserId"); _resetData = Bind<FnVoid>(sym, "GoogleAnalytics_ResetAnalyticsData");
          _setEnabled = Bind<FnBool>(sym, "GoogleAnalytics_SetAnalyticsCollectionEnabled"); _notifyLifecycle = Bind<FnInt>(sym, "GoogleAnalytics_NotifyAppLifecycleChange");
          return _optionsCreate != null && _optionsDestroy != null && _initialize != null && _paramsCreate != null && _paramsDestroy != null && _logEvent != null &&
                 _itemCreate != null && _itemVecCreate != null && _setUserProp != null && _setUserId != null && _resetData != null && _setEnabled != null && _notifyLifecycle != null;
        } catch (Exception ex) {
          Firebase.Platform.FirebaseLogger.LogMessage(Firebase.Platform.PlatformLogLevel.Debug, "Desktop Google Analytics native probe skipped: " + ex.Message);
          return false;
        }
      }
      private static IEnumerable<string> CandidatePaths(string dataPath, string fileName) {
        if (!string.IsNullOrEmpty(AppDomain.CurrentDomain.BaseDirectory)) yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        if (string.IsNullOrEmpty(dataPath)) yield break;
        if (Path.GetDirectoryName(dataPath) is string exeDir && !string.IsNullOrEmpty(exeDir)) yield return Path.Combine(exeDir, fileName);
        yield return Path.Combine(dataPath, "Plugins", "x86_64", fileName);
        yield return Path.Combine(dataPath, "Plugins", fileName);
        yield return Path.Combine(dataPath, "Firebase", "Plugins", "x86_64", fileName);
      }
      private static T Bind<T>(Func<string, IntPtr> sym, string name) where T : class =>
          sym(name) is IntPtr p && p != IntPtr.Zero ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
      private static IntPtr AllocUtf8(string s) {
        string val = s ?? string.Empty;
        byte[] rented = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(val.Length) + 1);
        try {
          int len = Encoding.UTF8.GetBytes(val, 0, val.Length, rented, 0);
          rented[len] = 0;
          IntPtr mem = Marshal.AllocHGlobal(len + 1);
          Marshal.Copy(rented, 0, mem, len + 1);
          return mem;
        } finally { ArrayPool<byte>.Shared.Return(rented); }
      }
      private static void FreeUtf8(ref IntPtr ptr) { if (ptr != IntPtr.Zero) { Marshal.FreeHGlobal(ptr); ptr = IntPtr.Zero; } }
    }
#endif

    internal static bool TryConvertToInt64(object v, out long result) {
      (bool ok, long val) = v switch {
        long l => (true, l), int i => (true, i), bool b => (true, b ? 1L : 0L), short s => (true, s),
        byte by => (true, by), sbyte sb => (true, sb), ushort us => (true, us), uint ui => (true, ui), ulong ul => (true, unchecked((long)ul)), _ => (false, 0L),
      };
      result = val;
      return ok;
    }
    internal static bool TryConvertToDouble(object v, out double result) {
      (bool ok, double val) = v switch { double d => (true, d), float f => (true, f), decimal m => (true, (double)m), _ => (false, 0.0) };
      result = val;
      return ok;
    }
  }
}
