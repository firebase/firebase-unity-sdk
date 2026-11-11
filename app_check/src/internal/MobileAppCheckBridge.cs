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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;

namespace Firebase.AppCheck.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class MobileAppCheckBridge {
    internal static T ThrowUnsupported<T>(string providerName, string platformName, bool logError = true) {
      if (logError) LogUtil.LogMessage(LogLevel.Error, $"{providerName} is not supported on this platform.");
      throw new FirebaseException((int)AppCheckError.UnsupportedProvider, $"{providerName} is only supported on {platformName} platforms.");
    }
    internal static void ValidateAppOptions(FirebaseApp app) {
      AppOptions opts = app?.Options;
      if (string.IsNullOrWhiteSpace(opts?.ProjectId) || string.IsNullOrWhiteSpace(opts?.AppId) || string.IsNullOrWhiteSpace(opts?.ApiKey)) {
        throw new FirebaseException((int)AppCheckError.InvalidConfiguration, "FirebaseApp Options must specify ProjectId, AppId, and ApiKey for App Check.");
      }
    }
    internal static AppCheckError MapAppleErrorDomainAndCode(string domain, int code) =>
        (domain == "com.google.app_check_core" || domain == "com.firebase.appCheck")
            ? code switch { 1 => AppCheckError.ServerUnreachable, 2 => AppCheckError.InvalidConfiguration, 3 => AppCheckError.SystemKeychain, 4 => AppCheckError.UnsupportedProvider, _ => AppCheckError.Unknown }
            : AppCheckError.Unknown;
    internal static T GetAndroidFactory<T>(ref T slot, Func<T> init) where T : class =>
#if UNITY_ANDROID && !UNITY_EDITOR
        LazyInitializer.EnsureInitialized(ref slot, init);
#else
        ThrowUnsupported<T>("Play Integrity", "Android");
#endif
    internal static IAppCheckProvider GetAndroidProvider(FirebaseInstanceCache<IAppCheckProvider> cache, FirebaseApp app) =>
        app == null ? throw new ArgumentNullException(nameof(app)) :
#if UNITY_ANDROID && !UNITY_EDITOR
        cache.GetOrCreate(app, () => CreatePlayIntegrityProvider(app));
#else
        ThrowUnsupported<IAppCheckProvider>("Play Integrity", "Android", logError: false);
#endif
    internal static T GetIosFactory<T>(ref T slot, Func<T> init, string name) where T : class =>
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        LazyInitializer.EnsureInitialized(ref slot, init);
#else
        ThrowUnsupported<T>(name, "iOS+");
#endif
    internal static IAppCheckProvider GetIosProvider(FirebaseInstanceCache<IAppCheckProvider> cache, FirebaseApp app, string name, string objcCls) =>
        app == null ? throw new ArgumentNullException(nameof(app)) :
#if (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
        cache.GetOrCreate(app, () => CreateIosProvider(app, objcCls));
#else
        ThrowUnsupported<IAppCheckProvider>(name, "iOS+", logError: false);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    private const string PlayIntegrityFactoryClass = "com.google.firebase.appcheck.playintegrity.PlayIntegrityAppCheckProviderFactory";
    private static volatile int s_unityMainThreadId;
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CaptureUnityMainThread() => s_unityMainThreadId = Thread.CurrentThread.ManagedThreadId;
    private static T RunJniSafe<T>(Func<T> jniWork) =>
        s_unityMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == s_unityMainThreadId ? jniWork()
        : Task.Run(() => { _ = UnityEngine.AndroidJNI.AttachCurrentThread(); try { return jniWork(); } finally { _ = UnityEngine.AndroidJNI.DetachCurrentThread(); } }).GetAwaiter().GetResult();

    private static IAppCheckProvider CreatePlayIntegrityProvider(FirebaseApp app) {
      ValidateAppOptions(app);
      return RunJniSafe<IAppCheckProvider>(() => {
        using var fbAppClass = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
        bool isDefault = string.IsNullOrEmpty(app.Name) || app.Name == FirebaseApp.DefaultName || app.Name == "[DEFAULT]";
        using UnityEngine.AndroidJavaObject javaApp = isDefault ? fbAppClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance") : fbAppClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", app.Name);
        if (javaApp == null) throw new FirebaseException((int)AppCheckError.InvalidConfiguration, $"Android FirebaseApp '{app.Name}' not found.");
        using var factoryClass = new UnityEngine.AndroidJavaClass(PlayIntegrityFactoryClass);
        using UnityEngine.AndroidJavaObject javaFactory = factoryClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
        return new AndroidAppCheckProvider(javaFactory?.Call<UnityEngine.AndroidJavaObject>("create", javaApp)
            ?? throw new FirebaseException((int)AppCheckError.UnsupportedProvider, "Failed to create Android PlayIntegrity AppCheckProvider."));
      });
    }
    internal static void SyncNativeFactory(IAppCheckProviderFactory factory) {
      if (!(factory is PlayIntegrityProviderFactory)) return;
      try {
        RunJniSafe(() => {
          using var fbAppClass = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
          using var acClass = new UnityEngine.AndroidJavaClass("com.google.firebase.appcheck.FirebaseAppCheck");
          using var factoryClass = new UnityEngine.AndroidJavaClass(PlayIntegrityFactoryClass);
          using UnityEngine.AndroidJavaObject javaApp = fbAppClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
          using UnityEngine.AndroidJavaObject javaAc = acClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", javaApp);
          using UnityEngine.AndroidJavaObject javaFactory = factoryClass.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
          javaAc?.Call("installAppCheckProviderFactory", javaFactory);
          return true;
        });
      } catch (Exception ex) { LogUtil.LogMessage(LogLevel.Debug, $"Native Android FirebaseAppCheck sync skipped: {ex.Message}"); }
    }
    private sealed class AndroidAppCheckProvider : IAppCheckProvider, IDisposable {
      private UnityEngine.AndroidJavaObject _javaProvider;
      private int _disposed;
      internal AndroidAppCheckProvider(UnityEngine.AndroidJavaObject javaProvider) => _javaProvider = javaProvider;
      public Task<AppCheckToken> GetTokenAsync() => CallProviderTaskAsync("getToken");
      public Task<AppCheckToken> GetLimitedUseTokenAsync() => CallProviderTaskAsync("getLimitedUseToken");
      private async Task<AppCheckToken> CallProviderTaskAsync(string methodName) {
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(AndroidAppCheckProvider));
        var tcs = new TaskCompletionSource<AppCheckToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        void AttachListener() {
          UnityEngine.AndroidJavaObject provider = _javaProvider ?? throw new ObjectDisposedException(nameof(AndroidAppCheckProvider));
          using UnityEngine.AndroidJavaObject gmsTask = provider.Call<UnityEngine.AndroidJavaObject>(methodName);
          using UnityEngine.AndroidJavaObject reg = gmsTask.Call<UnityEngine.AndroidJavaObject>("addOnCompleteListener", new GmsTaskCompleteListener(tcs));
        }
        try {
          if (s_unityMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == s_unityMainThreadId) AttachListener();
          else await Task.Run(() => { _ = UnityEngine.AndroidJNI.AttachCurrentThread(); try { AttachListener(); } finally { _ = UnityEngine.AndroidJNI.DetachCurrentThread(); } }).ConfigureAwait(false);
        } catch (ObjectDisposedException) { throw; }
        catch (Exception ex) { tcs.TrySetException(new FirebaseException((int)AppCheckError.Unknown, ex.Message, ex)); }
        return await tcs.Task.ConfigureAwait(false);
      }
      public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && Interlocked.Exchange(ref _javaProvider, null) is var p && p != null) RunJniSafe(() => { p.Dispose(); return true; });
      }
    }
    private sealed class GmsTaskCompleteListener : UnityEngine.AndroidJavaProxy {
      private readonly TaskCompletionSource<AppCheckToken> _tcs;
      internal GmsTaskCompleteListener(TaskCompletionSource<AppCheckToken> tcs) : base("com.google.android.gms.tasks.OnCompleteListener") => _tcs = tcs;
      [UnityEngine.Scripting.Preserve]
      public void onComplete(UnityEngine.AndroidJavaObject completedTask) {
        try {
          using (completedTask) {
            if (completedTask != null && completedTask.Call<bool>("isSuccessful")) {
              using UnityEngine.AndroidJavaObject tokObj = completedTask.Call<UnityEngine.AndroidJavaObject>("getResult");
              _tcs.TrySetResult(AppCheckToken.FromUnixTimeMilliseconds(tokObj?.Call<string>("getToken") ?? string.Empty, tokObj != null ? tokObj.Call<long>("getExpireTimeMillis") : 0L));
            } else {
              using UnityEngine.AndroidJavaObject exObj = completedTask?.Call<UnityEngine.AndroidJavaObject>("getException");
              using UnityEngine.AndroidJavaObject clsObj = exObj?.Call<UnityEngine.AndroidJavaObject>("getClass");
              string clsName = clsObj?.Call<string>("getName") ?? string.Empty;
              AppCheckError err = clsName.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0 ? AppCheckError.ServerUnreachable : AppCheckError.Unknown;
              _tcs.TrySetException(new FirebaseException((int)err, exObj?.Call<string>("getMessage") ?? "Android App Check attestation failed."));
            }
          }
        } catch (Exception ex) { _tcs.TrySetException(new FirebaseException((int)AppCheckError.Unknown, ex.Message, ex)); }
      }
    }
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib", SystemLib = "/usr/lib/libSystem.B.dylib";
    private const int BlockHasSignature = 1 << 30;
    private static readonly IntPtr RtldDefault = new(-2);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCCompletionDelegate(IntPtr block, IntPtr tokenObj, IntPtr errorObj);
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockDescriptor { public nuint Reserved, Size; public IntPtr Signature; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockLiteral { public IntPtr Isa; public int Flags, Reserved; public IntPtr Invoke, Descriptor; public int RequestKey; }
    [DllImport(ObjCLib)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjCLib)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjCLib)] private static extern void objc_release(IntPtr obj);
    [DllImport(ObjCLib)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjCLib)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr receiver, IntPtr selector);
    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern nint SendNInt(IntPtr receiver, IntPtr selector);
    [DllImport(SystemLib)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private static readonly ObjCCompletionDelegate s_completionDelegate = OnObjCTokenCompletion;
    private static readonly IntPtr s_invokePtr = Marshal.GetFunctionPointerForDelegate(s_completionDelegate);
    private static readonly IntPtr s_stackBlockIsa = dlsym(RtldDefault, "_NSConcreteStackBlock");
    private static readonly IntPtr s_descriptorPtr = CreateBlockDescriptor();
    private static readonly int s_requestKeyOffset = (int)Marshal.OffsetOf<ObjCBlockLiteral>(nameof(ObjCBlockLiteral.RequestKey));
    private static readonly Dictionary<int, TaskCompletionSource<AppCheckToken>> s_pending = new();
    private static int s_nextKey;

    private static IntPtr CreateBlockDescriptor() {
      var desc = new ObjCBlockDescriptor { Size = (nuint)Marshal.SizeOf<ObjCBlockLiteral>(), Signature = Marshal.StringToHGlobalAnsi("v24@?0@8@16") };
      IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockDescriptor>());
      Marshal.StructureToPtr(desc, ptr, false);
      return ptr;
    }
    private static IAppCheckProvider CreateIosProvider(FirebaseApp app, string objcProviderClassName) {
      ValidateAppOptions(app);
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        IntPtr cls = objc_getClass(objcProviderClassName);
        if (cls == IntPtr.Zero) throw new FirebaseException((int)AppCheckError.UnsupportedProvider, $"{objcProviderClassName} is not linked.");
        IntPtr provider = SendPtrPtr(SendPtr(cls, sel_registerName("alloc")), sel_registerName("initWithApp:"), ResolveFirApp(app));
        return provider != IntPtr.Zero ? new IosObjCAppCheckProvider(provider)
            : throw new FirebaseException((int)AppCheckError.InvalidConfiguration, $"Failed to initialize {objcProviderClassName}.");
      } finally { objc_autoreleasePoolPop(pool); }
    }
    internal static void SyncNativeFactory(IAppCheckProviderFactory factory) {
      string cls = factory is AppAttestProviderFactory ? "FIRAppAttestProviderFactory" : factory is DeviceCheckProviderFactory ? "FIRDeviceCheckProviderFactory" : null;
      if (cls == null) return;
      IntPtr pool = objc_autoreleasePoolPush();
      try {
        IntPtr acClass = objc_getClass("FIRAppCheck"), fClass = objc_getClass(cls);
        if (acClass == IntPtr.Zero || fClass == IntPtr.Zero) return;
        IntPtr fObj = SendPtr(SendPtr(fClass, sel_registerName("alloc")), sel_registerName("init"));
        if (fObj != IntPtr.Zero) { SendVoidPtr(acClass, sel_registerName("setAppCheckProviderFactory:"), fObj); objc_release(fObj); }
      } finally { objc_autoreleasePoolPop(pool); }
    }
    private static IntPtr ResolveFirApp(FirebaseApp app) {
      IntPtr firAppClass = objc_getClass("FIRApp");
      if (firAppClass == IntPtr.Zero) throw new FirebaseException((int)AppCheckError.InvalidConfiguration, "FIRApp class is not available.");
      IntPtr firApp;
      if (string.IsNullOrEmpty(app.Name) || app.Name == FirebaseApp.DefaultName || app.Name == "__FIRAPP_DEFAULT") {
        firApp = SendPtr(firAppClass, sel_registerName("defaultApp"));
      } else {
        byte[] nameBytes = Encoding.UTF8.GetBytes(app.Name);
        IntPtr utf8 = Marshal.AllocHGlobal(nameBytes.Length + 1);
        try {
          Marshal.Copy(nameBytes, 0, utf8, nameBytes.Length);
          Marshal.WriteByte(utf8, nameBytes.Length, 0);
          firApp = SendPtrPtr(firAppClass, sel_registerName("appNamed:"), SendPtrPtr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), utf8));
        } finally { Marshal.FreeHGlobal(utf8); }
      }
      return firApp != IntPtr.Zero ? firApp : throw new FirebaseException((int)AppCheckError.InvalidConfiguration, $"FIRApp '{app.Name}' is not configured.");
    }
    [AOT.MonoPInvokeCallback(typeof(ObjCCompletionDelegate))]
    private static void OnObjCTokenCompletion(IntPtr block, IntPtr tokenObj, IntPtr errorObj) {
      IntPtr pool = objc_autoreleasePoolPush();
      TaskCompletionSource<AppCheckToken> tcs = null;
      try {
        int key = Marshal.ReadInt32(block, s_requestKeyOffset);
        lock (s_pending) { if (!s_pending.TryGetValue(key, out tcs)) return; s_pending.Remove(key); }
        if (errorObj != IntPtr.Zero) {
          string domain = ReadNSString(SendPtr(errorObj, sel_registerName("domain")));
          AppCheckError mapped = MapAppleErrorDomainAndCode(domain, (int)SendNInt(errorObj, sel_registerName("code")));
          tcs.TrySetException(new FirebaseException((int)mapped, ReadNSString(SendPtr(errorObj, sel_registerName("localizedDescription"))) ?? "iOS App Check error."));
          return;
        }
        if (tokenObj == IntPtr.Zero) { tcs.TrySetException(new FirebaseException((int)AppCheckError.Unknown, "FIRAppCheckProvider returned an empty token.")); return; }
        IntPtr nsDate = SendPtr(tokenObj, sel_registerName("expirationDate"));
        double epochSec = nsDate != IntPtr.Zero ? SendDouble(nsDate, sel_registerName("timeIntervalSince1970")) : 0.0;
        tcs.TrySetResult(AppCheckToken.FromUnixTimeMilliseconds(ReadNSString(SendPtr(tokenObj, sel_registerName("token"))) ?? string.Empty, (long)(epochSec * 1000.0)));
      } catch (Exception ex) {
        tcs?.TrySetException(new FirebaseException((int)AppCheckError.Unknown, ex.Message, ex));
      } finally { objc_autoreleasePoolPop(pool); }
    }
    private static string ReadNSString(IntPtr ns) =>
        ns == IntPtr.Zero ? null : (SendPtr(ns, sel_registerName("UTF8String")) is IntPtr p && p != IntPtr.Zero ? Firebase.Internal.FirebaseInterops.PtrToStringUtf8(p) : null);
    private sealed class IosObjCAppCheckProvider : IAppCheckProvider, IDisposable {
      private IntPtr _provider;
      internal IosObjCAppCheckProvider(IntPtr retainedProvider) => _provider = retainedProvider;
      public Task<AppCheckToken> GetTokenAsync() => InvokeAsync("getTokenWithCompletion:");
      public Task<AppCheckToken> GetLimitedUseTokenAsync() => InvokeAsync("getLimitedUseTokenWithCompletion:");
      private Task<AppCheckToken> InvokeAsync(string selectorName) {
        IntPtr target = Volatile.Read(ref _provider);
        if (target == IntPtr.Zero) throw new ObjectDisposedException(nameof(IosObjCAppCheckProvider));
        var tcs = new TaskCompletionSource<AppCheckToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        int key;
        lock (s_pending) s_pending[key = ++s_nextKey] = tcs;
        var block = new ObjCBlockLiteral { Isa = s_stackBlockIsa, Flags = BlockHasSignature, Invoke = s_invokePtr, Descriptor = s_descriptorPtr, RequestKey = key };
        IntPtr pool = objc_autoreleasePoolPush(), blockPtr = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockLiteral>());
        try { Marshal.StructureToPtr(block, blockPtr, false); SendVoidPtr(target, sel_registerName(selectorName), blockPtr); }
        finally { Marshal.FreeHGlobal(blockPtr); objc_autoreleasePoolPop(pool); }
        return tcs.Task;
      }
      public void Dispose() { IntPtr prev = Interlocked.Exchange(ref _provider, IntPtr.Zero); if (prev != IntPtr.Zero) objc_release(prev); }
    }
#else
    internal static void SyncNativeFactory(IAppCheckProviderFactory factory) {
      // Desktop and Editor builds manage App Check exclusively in managed C#.
    }
#endif
  }
}
