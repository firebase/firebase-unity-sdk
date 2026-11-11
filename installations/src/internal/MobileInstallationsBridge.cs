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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Installations.Internal {
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  internal static class MobileInstallationsBridge {
    internal const int OpGetId = 0, OpGetToken = 1, OpDelete = 2;
    private static volatile int _mainThreadId;
    internal static Func<FirebaseApp, int, bool, Task<string>> OverrideForTesting { get; set; }
    internal static void CaptureMainThread() => _mainThreadId = Thread.CurrentThread.ManagedThreadId;
    internal static Task<string> TryGetIdAsync(FirebaseApp app, InstallationsClient client) => Invoke(app, OpGetId, false, client);
    internal static Task<string> TryGetTokenAsync(FirebaseApp app, bool forceRefresh, InstallationsClient client) => Invoke(app, OpGetToken, forceRefresh, client);
    internal static Task TryDeleteAsync(FirebaseApp app, InstallationsClient client) => Invoke(app, OpDelete, false, client);
    internal static bool IsValidAppleApiKey(string k) {
      if (k == null || k.Length != 39 || k[0] != 'A') return false;
      for (int i = 0; i < 39; i++) { char c = k[i]; if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false; }
      return true;
    }
    private static Task<string> Invoke(FirebaseApp app, int op, bool forceRefresh, InstallationsClient client) {
      if (OverrideForTesting is Func<FirebaseApp, int, bool, Task<string>> hook) return hook(app, op, forceRefresh);
#if UNITY_ANDROID && !UNITY_EDITOR
      return TryInvokeAndroid(app, op, forceRefresh, client);
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
      return TryInvokeApple(app, op, forceRefresh);
#else
      return null;
#endif
    }
#if UNITY_ANDROID && !UNITY_EDITOR
    private static volatile int _androidProbeState;
    private sealed class AndroidTaskListener : UnityEngine.AndroidJavaProxy {
      private readonly TaskCompletionSource<string> _tcs;
      private readonly int _op;
      internal AndroidTaskListener(TaskCompletionSource<string> tcs, int op) : base("com.google.android.gms.tasks.OnCompleteListener") => (_tcs, _op) = (tcs, op);
      public void onComplete(UnityEngine.AndroidJavaObject task) {
        try {
          using (task) {
            if (task != null && task.Call<bool>("isSuccessful")) {
              using var res = task.Call<UnityEngine.AndroidJavaObject>("getResult");
              _tcs.TrySetResult((_op == OpGetToken ? res?.Call<string>("getToken") : (_op == OpGetId ? res?.Call<string>("toString") : string.Empty)) ?? string.Empty);
            } else {
              using var ex = task?.Call<UnityEngine.AndroidJavaObject>("getException");
              _tcs.TrySetException(new FirebaseException((int)Error.Unknown, ex?.Call<string>("getMessage") ?? "Android FirebaseInstallations task failed."));
            }
          }
        } catch (Exception e) { _tcs.TrySetException(new FirebaseException((int)Error.Unknown, e.Message, e)); }
      }
    }
    private static Task<string> TryInvokeAndroid(FirebaseApp app, int op, bool forceRefresh, InstallationsClient client) {
      if (_androidProbeState < 0 || app == null) return null;
      if (_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId) return TryInvokeAndroidCore(app, op, forceRefresh);
      return Task.Run(async () => {
        Task<string> nativeTask;
        UnityEngine.AndroidJNI.AttachCurrentThread();
        try { nativeTask = TryInvokeAndroidCore(app, op, forceRefresh); } finally { UnityEngine.AndroidJNI.DetachCurrentThread(); }
        if (nativeTask != null) return await nativeTask.ConfigureAwait(false);
        if (op != OpDelete) return await (op == OpGetId ? client.PureGetIdAsync() : client.PureGetTokenAsync(forceRefresh)).ConfigureAwait(false);
        await client.EnqueueDeleteAsync().ConfigureAwait(false);
        return string.Empty;
      });
    }
    private static Task<string> TryInvokeAndroidCore(FirebaseApp app, int op, bool forceRefresh) {
      try {
        using var fisCls = new UnityEngine.AndroidJavaClass("com.google.firebase.installations.FirebaseInstallations");
        using var appCls = new UnityEngine.AndroidJavaClass("com.google.firebase.FirebaseApp");
        _androidProbeState = 1;
        bool isDefault = string.IsNullOrEmpty(app.Name) || app.Name == FirebaseApp.DefaultName;
        using var jApp = isDefault ? appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance") : appCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", app.Name);
        using var fis = jApp != null ? fisCls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance", jApp) : null;
        if (fis == null) return null;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var gmsTask = op == OpGetId ? fis.Call<UnityEngine.AndroidJavaObject>("getId") : (op == OpGetToken ? fis.Call<UnityEngine.AndroidJavaObject>("getToken", forceRefresh) : fis.Call<UnityEngine.AndroidJavaObject>("delete"));
        using var _ = gmsTask.Call<UnityEngine.AndroidJavaObject>("addOnCompleteListener", new AndroidTaskListener(tcs, op));
        return tcs.Task;
      } catch (Exception) { if (_androidProbeState == 0) _androidProbeState = -1; return null; }
    }
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
    private const string LibObjC = "/usr/lib/libobjc.A.dylib", LibSystem = "/usr/lib/libSystem.B.dylib";
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockDescriptor { public IntPtr reserved, size; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockLiteral { public IntPtr isa; public int flags, reserved; public IntPtr invoke, descriptor, context; }
    private sealed class BlockContext { internal TaskCompletionSource<string> Tcs; internal int Op; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void TwoArgBlock(IntPtr block, IntPtr resObj, IntPtr errObj);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void OneArgBlock(IntPtr block, IntPtr errObj);
    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(LibObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(LibSystem)] private static extern IntPtr dlsym(IntPtr handle, string symbol);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr r, IntPtr s);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrArg(IntPtr r, IntPtr s, IntPtr a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrUtf8(IntPtr r, IntPtr s, byte[] u);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidBlock(IntPtr r, IntPtr s, IntPtr b);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern void SendVoidBoolBlock(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool f, IntPtr b);
    private static readonly int ContextOffset = Marshal.OffsetOf<ObjCBlockLiteral>(nameof(ObjCBlockLiteral.context)).ToInt32();
    private static readonly TwoArgBlock TwoArgCb = OnTwoArgCompleted;
    private static readonly OneArgBlock OneArgCb = OnDeleteCompleted;
    private static IntPtr _descriptorPtr;
    private static volatile int _appleProbeState;

    private static Task<string> TryInvokeApple(FirebaseApp app, int op, bool forceRefresh) {
      if (_appleProbeState < 0 || app == null) return null;
      IntPtr pool = IntPtr.Zero;
      try {
        IntPtr clsFis = objc_getClass("FIRInstallations"), clsApp = objc_getClass("FIRApp"), stackIsa = dlsym(new IntPtr(-2), "_NSConcreteStackBlock");
        if (clsFis == IntPtr.Zero || clsApp == IntPtr.Zero || stackIsa == IntPtr.Zero) { _appleProbeState = -1; return null; }
        _appleProbeState = 1;
        if (string.IsNullOrEmpty(app.Name) || !IsValidAppleApiKey(app.Options?.ApiKey)) return null;
        pool = objc_autoreleasePoolPush();
        bool isDefault = app.Name == FirebaseApp.DefaultName;
        IntPtr firApp = isDefault ? SendPtr(clsApp, sel_registerName("defaultApp")) : SendPtrArg(clsApp, sel_registerName("appNamed:"), SendPtrUtf8(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), Encoding.UTF8.GetBytes(app.Name + "\0")));
        IntPtr fis = (firApp != IntPtr.Zero && IsValidFirOptions(SendPtr(firApp, sel_registerName("options")))) ? SendPtrArg(clsFis, sel_registerName("installationsWithApp:"), firApp) : IntPtr.Zero;
        if (fis == IntPtr.Zero) return null;
        var ctx = new BlockContext { Tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously), Op = op };
        GCHandle gch = GCHandle.Alloc(ctx);
        IntPtr blockPtr = AllocStackBlock(stackIsa, op == OpDelete ? Marshal.GetFunctionPointerForDelegate(OneArgCb) : Marshal.GetFunctionPointerForDelegate(TwoArgCb), GCHandle.ToIntPtr(gch));
        try {
          if (op == OpGetId) SendVoidBlock(fis, sel_registerName("installationIDWithCompletion:"), blockPtr);
          else if (op == OpGetToken) SendVoidBoolBlock(fis, sel_registerName("authTokenForcingRefresh:completion:"), forceRefresh, blockPtr);
          else SendVoidBlock(fis, sel_registerName("deleteWithCompletion:"), blockPtr);
          return ctx.Tcs.Task;
        } catch (Exception) { if (gch.IsAllocated) gch.Free(); throw; } finally { Marshal.FreeHGlobal(blockPtr); }
      } catch (Exception) { if (_appleProbeState == 0) _appleProbeState = -1; return null; }
      finally { if (pool != IntPtr.Zero) objc_autoreleasePoolPop(pool); }
    }
    private static bool IsValidFirOptions(IntPtr o) => o != IntPtr.Zero && IsValidAppleApiKey(ReadNSString(SendPtr(o, sel_registerName("APIKey")))) && ReadNSString(SendPtr(o, sel_registerName("googleAppID"))).Length > 0 && ReadNSString(SendPtr(o, sel_registerName("projectID"))).Length > 0;
    private static IntPtr AllocStackBlock(IntPtr isa, IntPtr invokePtr, IntPtr context) {
      if (_descriptorPtr == IntPtr.Zero) {
        IntPtr d = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockDescriptor>());
        Marshal.StructureToPtr(new ObjCBlockDescriptor { reserved = IntPtr.Zero, size = (IntPtr)Marshal.SizeOf<ObjCBlockLiteral>() }, d, false);
        if (Interlocked.CompareExchange(ref _descriptorPtr, d, IntPtr.Zero) != IntPtr.Zero) Marshal.FreeHGlobal(d);
      }
      IntPtr m = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockLiteral>());
      Marshal.StructureToPtr(new ObjCBlockLiteral { isa = isa, invoke = invokePtr, descriptor = _descriptorPtr, context = context }, m, false);
      return m;
    }
    private static string ReadNSString(IntPtr ns) => ns != IntPtr.Zero && SendPtr(ns, sel_registerName("UTF8String")) is IntPtr p && p != IntPtr.Zero ? (Firebase.Internal.FirebaseInterops.PtrToStringUtf8(p) ?? string.Empty) : string.Empty;
    private static BlockContext PopContext(IntPtr b) { GCHandle g = GCHandle.FromIntPtr(Marshal.ReadIntPtr(b, ContextOffset)); var c = (BlockContext)g.Target; g.Free(); return c; }
    [AOT.MonoPInvokeCallback(typeof(TwoArgBlock))] private static void OnTwoArgCompleted(IntPtr b, IntPtr res, IntPtr err) { BlockContext c = PopContext(b); IntPtr p = objc_autoreleasePoolPush(); try { if (err != IntPtr.Zero) c.Tcs.TrySetException(new FirebaseException((int)Error.Unknown, ReadNSString(SendPtr(err, sel_registerName("localizedDescription"))))); else c.Tcs.TrySetResult(ReadNSString(c.Op == OpGetToken && res != IntPtr.Zero ? SendPtr(res, sel_registerName("authToken")) : res)); } finally { if (p != IntPtr.Zero) objc_autoreleasePoolPop(p); } }
    [AOT.MonoPInvokeCallback(typeof(OneArgBlock))] private static void OnDeleteCompleted(IntPtr b, IntPtr err) { BlockContext c = PopContext(b); IntPtr p = objc_autoreleasePoolPush(); try { if (err != IntPtr.Zero) c.Tcs.TrySetException(new FirebaseException((int)Error.Unknown, ReadNSString(SendPtr(err, sel_registerName("localizedDescription"))))); else c.Tcs.TrySetResult(string.Empty); } finally { if (p != IntPtr.Zero) objc_autoreleasePoolPop(p); } }
#endif
  }
}
