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
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Platform;

namespace Firebase.Messaging.Internal {

#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
internal static class MobileMessagingBridge {
#if UNITY_ANDROID && !UNITY_EDITOR
  private const string FcmClass = "com.google.firebase.messaging.FirebaseMessaging", PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";
  [ThreadStatic] private static bool t_onJavaCallbackThread;
  private static readonly object s_bridgeLock = new object();
  private static readonly HashSet<object> s_activeProxies = new HashSet<object>();
  private static int s_initialized, s_pendingAutoInit, s_pendingBigQuery;
  private static string s_lastIntentMessageId;

  internal static void EnsureInitialized() {
    if (Interlocked.CompareExchange(ref s_initialized, 1, 0) != 0) return;
    if (MessagingRuntime.IsOnUnityMainThread) OnMainThreadReady();
    else _ = FirebaseHandler.RunOnMainThreadAsync(() => { if (Volatile.Read(ref s_initialized) != 0) OnMainThreadReady(); return true; });
  }
  internal static void OnMainThreadReady() {
    UnityEngine.Application.focusChanged -= OnFocusChanged; UnityEngine.Application.focusChanged += OnFocusChanged;
    TryInstallServiceListener(); CheckActivityIntent(); if (IsAutoInitEnabled()) _ = RefreshTokenQuietlyAsync();
  }
  internal static void ResetForReload() {
    Interlocked.Exchange(ref s_initialized, 0); s_pendingAutoInit = 0; s_pendingBigQuery = 0;
    if (MessagingRuntime.IsOnUnityMainThread) UnityEngine.Application.focusChanged -= OnFocusChanged;
    lock (s_bridgeLock) s_activeProxies.Clear();
  }
  private static void OnFocusChanged(bool hasFocus) { if (hasFocus) CheckActivityIntent(); }
  private static T RunJni<T>(Func<T> work) =>
      MessagingRuntime.IsOnUnityMainThread || t_onJavaCallbackThread ? work()
      : Task.Run(() => { _ = UnityEngine.AndroidJNI.AttachCurrentThread(); try { return work(); } finally { _ = UnityEngine.AndroidJNI.DetachCurrentThread(); } }).GetAwaiter().GetResult();
  private static T WithFcm<T>(Func<UnityEngine.AndroidJavaObject, T> fn, T fallback) {
    try { return RunJni(() => { using var cls = new UnityEngine.AndroidJavaClass(FcmClass); using UnityEngine.AndroidJavaObject fm = cls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance"); return fn(fm); }); }
    catch (Exception ex) { MessagingRuntime.LogDebug(ex); return fallback; }
  }
  private static async Task<string> RunJniTaskAsync(string method, object arg, bool expectString, Error failErr) {
    var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var proxy = new GmsTaskListener(tcs, expectString, failErr); lock (s_bridgeLock) s_activeProxies.Add(proxy);
    void Attach() {
      using var cls = new UnityEngine.AndroidJavaClass(FcmClass); using UnityEngine.AndroidJavaObject fm = cls.CallStatic<UnityEngine.AndroidJavaObject>("getInstance");
      using UnityEngine.AndroidJavaObject task = arg == null ? fm.Call<UnityEngine.AndroidJavaObject>(method) : fm.Call<UnityEngine.AndroidJavaObject>(method, arg);
      using UnityEngine.AndroidJavaObject reg = task.Call<UnityEngine.AndroidJavaObject>("addOnCompleteListener", proxy);
    }
    try {
      if (MessagingRuntime.IsOnUnityMainThread || t_onJavaCallbackThread) Attach();
      else await Task.Run(() => { _ = UnityEngine.AndroidJNI.AttachCurrentThread(); try { Attach(); } finally { _ = UnityEngine.AndroidJNI.DetachCurrentThread(); } }).ConfigureAwait(false);
      return await tcs.Task.ConfigureAwait(false);
    } catch (FirebaseException) { throw; }
    catch (Exception ex) {
      Error code = ex.Message != null && ex.Message.IndexOf("IllegalArgumentException", StringComparison.Ordinal) >= 0 ? Error.InvalidTopicName : failErr;
      throw new FirebaseException((int)code, ex.Message, ex);
    } finally { lock (s_bridgeLock) s_activeProxies.Remove(proxy); }
  }
  internal static bool HasTopicRegistration() => MessagingRuntime.HasActiveRegistration;
  internal static bool IsAutoInitEnabled() => WithFcm(fm => fm.Call<bool>("isAutoInitEnabled"), s_pendingAutoInit != -1);
  internal static void SetAutoInitEnabled(bool enabled) {
    s_pendingAutoInit = enabled ? 1 : -1;
    if (WithFcm(fm => { bool was = fm.Call<bool>("isAutoInitEnabled"); fm.Call("setAutoInitEnabled", enabled); return !was && enabled; }, false)) _ = RefreshTokenQuietlyAsync();
  }
  internal static bool IsDeliveryMetricsExportToBigQueryEnabled() => WithFcm(fm => fm.Call<bool>("deliveryMetricsExportToBigQueryEnabled"), s_pendingBigQuery == 1);
  internal static void SetDeliveryMetricsExportToBigQueryEnabled(bool enabled) {
    s_pendingBigQuery = enabled ? 1 : -1; _ = WithFcm(fm => { fm.Call("setDeliveryMetricsExportToBigQuery", enabled); return true; }, false);
  }
  internal static async Task<string> GetTokenAsync() {
    string token = await RunJniTaskAsync("getToken", null, expectString: true, Error.Unknown).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(token)) MessagingRuntime.NotifyTokenReceived(token);
    return token;
  }
  internal static Task DeleteTokenAsync() => RunJniTaskAsync("deleteToken", null, false, Error.Unknown);
  internal static async Task RegisterAsync() { _ = await RunJniTaskAsync("register", null, false, Error.Unknown).ConfigureAwait(false); MessagingRuntime.MarkRegistered(); }
  internal static Task UnregisterAsync() => RunJniTaskAsync("unregister", null, false, Error.Unknown);
  internal static Task SubscribeAsync(string topic) => RunJniTaskAsync("subscribeToTopic", topic, false, Error.Unknown);
  internal static Task UnsubscribeAsync(string topic) => RunJniTaskAsync("unsubscribeFromTopic", topic, false, Error.Unknown);
  internal static async Task RequestPermissionAsync() {
    if (RunJni(() => { using var ver = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION"); return ver.GetStatic<int>("SDK_INT"); }) < 33) return;
    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    _ = FirebaseHandler.RunOnMainThreadAsync(() => {
      if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(PostNotificationsPermission)) { tcs.TrySetResult(true); return true; }
      var cb = new UnityEngine.Android.PermissionCallbacks(); lock (s_bridgeLock) s_activeProxies.Add(cb);
      void Done() { lock (s_bridgeLock) s_activeProxies.Remove(cb); tcs.TrySetResult(true); }
      cb.PermissionGranted += _ => Done(); cb.PermissionDenied += _ => Done();
#if !UNITY_6000_0_OR_NEWER
      cb.PermissionDeniedAndDontAskAgain += _ => Done();
#endif
      UnityEngine.Android.Permission.RequestUserPermission(PostNotificationsPermission, cb); return true;
    });
    await tcs.Task.ConfigureAwait(false);
  }
  private static async Task RefreshTokenQuietlyAsync() { try { _ = await GetTokenAsync().ConfigureAwait(false); } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } }
  private static void TryInstallServiceListener() {
    try { RunJni(() => { using var svc = new UnityEngine.AndroidJavaClass("com.google.firebase.MessagingUnityService"); var l = new ServiceListenerProxy(); lock (s_bridgeLock) s_activeProxies.Add(l); svc.CallStatic("setListener", l); return true; }); }
    catch (Exception ex) { MessagingRuntime.LogDebug(ex); }
  }
  private static void CheckActivityIntent() {
    try {
      FirebaseMessage parsed = RunJni(() => {
        using var up = new UnityEngine.AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using UnityEngine.AndroidJavaObject act = up.GetStatic<UnityEngine.AndroidJavaObject>("currentActivity");
        using UnityEngine.AndroidJavaObject intent = act?.Call<UnityEngine.AndroidJavaObject>("getIntent");
        using UnityEngine.AndroidJavaObject extras = intent?.Call<UnityEngine.AndroidJavaObject>("getExtras");
        if (extras == null) return null;
        string msgId = extras.Call<string>("getString", "google.message_id") ?? extras.Call<string>("getString", "message_id"), from = extras.Call<string>("getString", "from");
        if (string.IsNullOrEmpty(msgId) || string.IsNullOrEmpty(from)) return null;
        lock (s_bridgeLock) { if (string.Equals(s_lastIntentMessageId, msgId, StringComparison.Ordinal)) return null; s_lastIntentMessageId = msgId; }
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        using (UnityEngine.AndroidJavaObject keySet = extras.Call<UnityEngine.AndroidJavaObject>("keySet"))
        using (UnityEngine.AndroidJavaObject iter = keySet?.Call<UnityEngine.AndroidJavaObject>("iterator"))
          while (iter != null && iter.Call<bool>("hasNext")) { string k = iter.Call<string>("next"); if (k != null) dict[k] = extras.Call<string>("getString", k); }
        if ((!dict.TryGetValue("google.ttl", out string ttl) || string.IsNullOrEmpty(ttl)) && extras.Call<int>("getInt", "google.ttl", 0) is int ttlInt && ttlInt > 0)
          dict["google.ttl"] = ttlInt.ToString(CultureInfo.InvariantCulture);
        using UnityEngine.AndroidJavaObject dataUri = intent.Call<UnityEngine.AndroidJavaObject>("getData");
        intent.Call("removeExtra", "google.message_id"); intent.Call("removeExtra", "message_id");
        return MessagingRuntime.ParseAndroidExtras(dict, dataUri?.Call<string>("toString"), notificationOpened: true);
      });
      if (parsed != null) MessagingRuntime.NotifyMessageReceived(parsed);
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); }
  }
  private sealed class GmsTaskListener : UnityEngine.AndroidJavaProxy {
    private readonly TaskCompletionSource<string> _tcs; private readonly bool _expectString; private readonly Error _failError;
    internal GmsTaskListener(TaskCompletionSource<string> tcs, bool expectString, Error failError) : base("com.google.android.gms.tasks.OnCompleteListener") { _tcs = tcs; _expectString = expectString; _failError = failError; }
    [UnityEngine.Scripting.Preserve]
    public void onComplete(UnityEngine.AndroidJavaObject task) {
      bool prev = t_onJavaCallbackThread; t_onJavaCallbackThread = true;
      try {
        using (task) {
          if (task != null && task.Call<bool>("isSuccessful")) { _tcs.TrySetResult(_expectString ? task.Call<string>("getResult") ?? string.Empty : string.Empty); return; }
          using UnityEngine.AndroidJavaObject ex = task?.Call<UnityEngine.AndroidJavaObject>("getException");
          using UnityEngine.AndroidJavaObject cls = ex?.Call<UnityEngine.AndroidJavaObject>("getClass");
          Error err = (cls?.Call<string>("getName") ?? string.Empty).EndsWith("IllegalArgumentException", StringComparison.Ordinal) ? Error.InvalidTopicName : _failError;
          _tcs.TrySetException(new FirebaseException((int)err, ex?.Call<string>("getMessage") ?? "Android FCM task failed."));
        }
      } catch (Exception e) { _tcs.TrySetException(new FirebaseException((int)Error.Unknown, e.Message, e)); } finally { t_onJavaCallbackThread = prev; }
    }
  }
  private sealed class ServiceListenerProxy : UnityEngine.AndroidJavaProxy {
    internal ServiceListenerProxy() : base("com.google.firebase.MessagingUnityService$Listener") { }
    private static string Str(UnityEngine.AndroidJavaObject o, string m) => o?.Call<string>(m) ?? string.Empty;
    [UnityEngine.Scripting.Preserve] public void onNewToken(string token) => MessagingRuntime.NotifyTokenReceived(token);
    [UnityEngine.Scripting.Preserve] public void onRegistered(string fid) => MessagingRuntime.NotifyRegistrationReceived(fid);
    [UnityEngine.Scripting.Preserve] public void onUnregistered(string fid) => MessagingRuntime.NotifyUnregistrationReceived(fid);
    [UnityEngine.Scripting.Preserve] public void onMessageEvent(string id, string type, string err) =>
        MessagingRuntime.NotifyMessageReceived(new FirebaseMessage { MessageId = id ?? string.Empty, MessageType = type ?? string.Empty, Error = err ?? string.Empty });
    [UnityEngine.Scripting.Preserve]
    public void onMessageReceived(UnityEngine.AndroidJavaObject rm) {
      bool prev = t_onJavaCallbackThread; t_onJavaCallbackThread = true;
      try {
        using (rm) {
          if (rm == null) return;
          var data = new Dictionary<string, string>(StringComparer.Ordinal);
          using (UnityEngine.AndroidJavaObject map = rm.Call<UnityEngine.AndroidJavaObject>("getData"))
          using (UnityEngine.AndroidJavaObject es = map?.Call<UnityEngine.AndroidJavaObject>("entrySet"))
          using (UnityEngine.AndroidJavaObject it = es?.Call<UnityEngine.AndroidJavaObject>("iterator"))
            while (it != null && it.Call<bool>("hasNext")) { using UnityEngine.AndroidJavaObject e = it.Call<UnityEngine.AndroidJavaObject>("next"); if (e?.Call<string>("getKey") is string k) data[k] = Str(e, "getValue"); }
          FirebaseNotification notif = null; string link = null;
          using (UnityEngine.AndroidJavaObject n = rm.Call<UnityEngine.AndroidJavaObject>("getNotification")) {
            if (n != null) {
              using UnityEngine.AndroidJavaObject lu = n.Call<UnityEngine.AndroidJavaObject>("getLink"); link = lu?.Call<string>("toString");
              notif = new FirebaseNotification {
                Title = Str(n, "getTitle"), Body = Str(n, "getBody"), Icon = Str(n, "getIcon"), Sound = Str(n, "getSound"), Tag = Str(n, "getTag"), Color = Str(n, "getColor"), ClickAction = Str(n, "getClickAction"),
                BodyLocalizationKey = Str(n, "getBodyLocalizationKey"), BodyLocalizationArgs = n.Call<string[]>("getBodyLocalizationArgs") ?? Array.Empty<string>(),
                TitleLocalizationKey = Str(n, "getTitleLocalizationKey"), TitleLocalizationArgs = n.Call<string[]>("getTitleLocalizationArgs") ?? Array.Empty<string>(),
                Android = new AndroidNotificationParams { ChannelId = Str(n, "getChannelId") },
              };
            }
          }
          int prio = rm.Call<int>("getPriority");
          MessagingRuntime.NotifyMessageReceived(new FirebaseMessage {
            From = Str(rm, "getFrom"), To = Str(rm, "getTo"), MessageId = Str(rm, "getMessageId"), MessageType = Str(rm, "getMessageType"),
            CollapseKey = Str(rm, "getCollapseKey"), Priority = prio == 1 ? "high" : prio == 2 ? "normal" : string.Empty,
            TimeToLive = TimeSpan.FromSeconds(Math.Max(0, rm.Call<int>("getTtl"))), RawData = rm.Call<byte[]>("getRawData") ?? Array.Empty<byte>(),
            Data = data, Notification = notif, Link = FirebaseMessage.ParseLinkUri(link),
          });
        }
      } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { t_onJavaCallbackThread = prev; }
    }
  }
#elif (UNITY_IOS || UNITY_TVOS) && !UNITY_EDITOR
  private const string ObjCLib = "/usr/lib/libobjc.A.dylib", SystemLib = "/usr/lib/libSystem.B.dylib";
  private const int BlockHasSignature = 1 << 30;
  private static readonly IntPtr RtldDefault = new IntPtr(-2);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCTokenCb(IntPtr block, IntPtr tokenObj, IntPtr errObj);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCErrorCb(IntPtr block, IntPtr errObj);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCAuthCb(IntPtr block, [MarshalAs(UnmanagedType.I1)] bool granted, IntPtr errObj);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCNoteCb(IntPtr block, IntPtr noteObj);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCNotifCenterImp(IntPtr self, IntPtr cmd, IntPtr centerOrApp, IntPtr obj, IntPtr compBlock);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCBlockOptInvoke(IntPtr block, nuint options);
#if !UNITY_TVOS
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ObjCBlockVoidInvoke(IntPtr block);
#endif
  [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockDescriptor { public nuint Reserved, Size; public IntPtr Signature; }
  [StructLayout(LayoutKind.Sequential)] private struct ObjCBlockLiteral { public IntPtr Isa; public int Flags, Reserved; public IntPtr Invoke, Descriptor; public int RequestKey, Pad; }
  [DllImport(ObjCLib)] private static extern IntPtr objc_getClass(string name);
  [DllImport(ObjCLib)] private static extern IntPtr objc_getProtocol(string name);
  [DllImport(ObjCLib)] private static extern IntPtr sel_registerName(string name);
  [DllImport(ObjCLib)] private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);
  [DllImport(ObjCLib)] private static extern void objc_registerClassPair(IntPtr cls);
  [DllImport(ObjCLib)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(IntPtr cls, IntPtr name, IntPtr imp, string types);
  [DllImport(ObjCLib)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);
  [DllImport(ObjCLib)] private static extern IntPtr objc_autoreleasePoolPush();
  [DllImport(ObjCLib)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrPtr(IntPtr r, IntPtr s, IntPtr a1);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrNUInt(IntPtr r, IntPtr s, nuint a1);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern nuint SendNUInt(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern nint SendNInt(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern long SendLong(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoid(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtr(IntPtr r, IntPtr s, IntPtr a1);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtrPtr(IntPtr r, IntPtr s, IntPtr a1, IntPtr a2);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidPtrPtrPtr(IntPtr r, IntPtr s, IntPtr a1, IntPtr a2, IntPtr a3);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidNUIntPtr(IntPtr r, IntPtr s, nuint a1, IntPtr a2);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(IntPtr r, IntPtr s);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBoolPtr(IntPtr r, IntPtr s, IntPtr a1);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern void SendVoidBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a1);
  [DllImport(ObjCLib, EntryPoint = "objc_msgSend")] private static extern IntPtr SendObserver(IntPtr r, IntPtr s, IntPtr name, IntPtr obj, IntPtr q, IntPtr block);
  [DllImport(SystemLib)] private static extern IntPtr dlsym(IntPtr handle, string symbol);

  private sealed class PendingCall { internal TaskCompletionSource<string> Tcs; internal GCHandle Pin; internal Error ErrCode; }
  private static readonly ObjCTokenCb s_tokDel = OnTokenDone;
  private static readonly ObjCErrorCb s_errDel = OnErrorDone;
  private static readonly ObjCAuthCb s_authDel = OnAuthDone;
  private static readonly ObjCNoteCb s_tokNoteDel = OnTokenRefreshedNotification, s_unregNoteDel = OnUnregisteredNotification;
  private static readonly ObjCNotifCenterImp s_willPresentDel = OnWillPresentNotification, s_remoteNotifDel = OnDidReceiveRemoteNotification;
#if !UNITY_TVOS
  private static readonly ObjCNotifCenterImp s_didReceiveDel = OnDidReceiveNotificationResponse;
#endif
  private static readonly IntPtr s_tokFn = Marshal.GetFunctionPointerForDelegate(s_tokDel), s_errFn = Marshal.GetFunctionPointerForDelegate(s_errDel), s_authFn = Marshal.GetFunctionPointerForDelegate(s_authDel);
  private static readonly IntPtr s_tokNoteFn = Marshal.GetFunctionPointerForDelegate(s_tokNoteDel), s_unregNoteFn = Marshal.GetFunctionPointerForDelegate(s_unregNoteDel);
  private static readonly IntPtr s_stackBlockIsa = dlsym(RtldDefault, "_NSConcreteStackBlock");
  private static readonly IntPtr s_descTok = MakeDesc("v24@?0@8@16"), s_descErr = MakeDesc("v16@?0@8"), s_descAuth = MakeDesc("v24@?0B8@16");
  private static readonly int s_keyOffset = (int)Marshal.OffsetOf<ObjCBlockLiteral>(nameof(ObjCBlockLiteral.RequestKey));
  private static readonly Dictionary<int, PendingCall> s_pending = new Dictionary<int, PendingCall>();
  private static readonly Queue<string> s_recentMsgKeys = new Queue<string>();
  private static int s_nextKey, s_initialized;
  private static bool s_swizzlerRegistered;
  private static IntPtr s_prevNotifDelegate, s_notifDelegateInstance;
  private static GCHandle s_tokObsPin, s_unregObsPin;

  private static T WithPool<T>(Func<T> fn) { IntPtr p = objc_autoreleasePoolPush(); try { return fn(); } finally { objc_autoreleasePoolPop(p); } }
  private static IntPtr MakeDesc(string sig) {
    var d = new ObjCBlockDescriptor { Size = (nuint)Marshal.SizeOf<ObjCBlockLiteral>(), Signature = Marshal.StringToHGlobalAnsi(sig) };
    IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<ObjCBlockDescriptor>()); Marshal.StructureToPtr(d, p, false); return p;
  }
  private static IntPtr GetFirMessaging() {
    if (!(objc_getClass("FIRApp") is IntPtr ac && ac != IntPtr.Zero && SendPtr(ac, sel_registerName("defaultApp")) is IntPtr app && app != IntPtr.Zero)) return IntPtr.Zero;
    if (SendPtr(app, sel_registerName("options")) is IntPtr opts && opts != IntPtr.Zero && string.IsNullOrEmpty(ReadNSString(SendPtr(opts, sel_registerName("GCMSenderID"))))) return IntPtr.Zero;
    return objc_getClass("FIRMessaging") is IntPtr c && c != IntPtr.Zero ? SendPtr(c, sel_registerName("messaging")) : IntPtr.Zero;
  }
  private static IntPtr RequireFirMessaging() => GetFirMessaging() is IntPtr fm && fm != IntPtr.Zero ? fm : throw new FirebaseException((int)Error.Unknown, "FIRMessaging is not available (ensure FirebaseApp is configured with a non-empty MessageSenderId / GCMSenderID).");
  internal static bool HasTopicRegistration() =>
      MessagingRuntime.HasActiveRegistration || WithPool(() => GetFirMessaging() is IntPtr fm && fm != IntPtr.Zero && !string.IsNullOrEmpty(ReadNSString(SendPtr(fm, sel_registerName("FCMToken")))));

  internal static void EnsureInitialized() {
    if (Interlocked.CompareExchange(ref s_initialized, 1, 0) != 0) return;
    try {
      WithPool(() => {
        if (objc_getClass("NSNotificationCenter") is IntPtr nc && nc != IntPtr.Zero && SendPtr(nc, sel_registerName("defaultCenter")) is IntPtr center && center != IntPtr.Zero) {
          s_tokObsPin = RegisterObserver(center, "FIRMessagingRegistrationTokenRefreshedNotification", "com.firebase.messaging.notif.fcm-token-refreshed", s_tokNoteFn);
          s_unregObsPin = RegisterObserver(center, "FIRMessagingInstallationIdUnregisteredNotification", "com.firebase.messaging.notif.installation-id-unregistered", s_unregNoteFn);
        }
        bool isMain = objc_getClass("NSThread") is IntPtr nt && nt != IntPtr.Zero && SendBool(nt, sel_registerName("isMainThread"));
        if (isMain) InstallNotificationHooks();
        else _ = FirebaseHandler.RunOnMainThreadAsync(() => WithPool(() => { InstallNotificationHooks(); return true; }));
        if (GetFirMessaging() is IntPtr fm && fm != IntPtr.Zero && SendBool(fm, sel_registerName("isAutoInitEnabled"))) DispatchTokenOrRegistration(fm, ReadNSString(SendPtr(fm, sel_registerName("FCMToken"))));
        return true;
      });
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); }
  }
  internal static void OnMainThreadReady() { EnsureInitialized(); try { WithPool(() => { InstallNotificationHooks(); return true; }); } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } }
  internal static void ResetForReload() { }
  private static void InstallNotificationHooks() {
    IntPtr delCls = objc_getClass("FirebaseUnityMessagingNotifDelegate");
    if (delCls == IntPtr.Zero && (delCls = objc_allocateClassPair(objc_getClass("NSObject"), "FirebaseUnityMessagingNotifDelegate", 0)) != IntPtr.Zero) {
      if (objc_getProtocol("UNUserNotificationCenterDelegate") is IntPtr p1 && p1 != IntPtr.Zero) _ = class_addProtocol(delCls, p1);
      if (objc_getProtocol("UIApplicationDelegate") is IntPtr p2 && p2 != IntPtr.Zero) _ = class_addProtocol(delCls, p2);
      _ = class_addMethod(delCls, sel_registerName("userNotificationCenter:willPresentNotification:withCompletionHandler:"), Marshal.GetFunctionPointerForDelegate(s_willPresentDel), "v@:@@@?");
#if !UNITY_TVOS
      _ = class_addMethod(delCls, sel_registerName("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"), Marshal.GetFunctionPointerForDelegate(s_didReceiveDel), "v@:@@@?");
#endif
      _ = class_addMethod(delCls, sel_registerName("application:didReceiveRemoteNotification:fetchCompletionHandler:"), Marshal.GetFunctionPointerForDelegate(s_remoteNotifDel), "v@:@@@?");
      objc_registerClassPair(delCls);
    }
    if (delCls == IntPtr.Zero) return;
    if (s_notifDelegateInstance == IntPtr.Zero) s_notifDelegateInstance = SendPtr(SendPtr(delCls, sel_registerName("alloc")), sel_registerName("init"));
    if (objc_getClass("UNUserNotificationCenter") is IntPtr u && u != IntPtr.Zero && SendPtr(u, sel_registerName("currentNotificationCenter")) is IntPtr unCenter && unCenter != IntPtr.Zero) {
      IntPtr cur = SendPtr(unCenter, sel_registerName("delegate"));
      if (cur != s_notifDelegateInstance) { s_prevNotifDelegate = cur; SendVoidPtr(unCenter, sel_registerName("setDelegate:"), s_notifDelegateInstance); }
    }
    if (!s_swizzlerRegistered && objc_getClass("GULAppDelegateSwizzler") is IntPtr gul && gul != IntPtr.Zero) {
      SendVoid(gul, sel_registerName("proxyOriginalDelegateIncludingAPNSMethods"));
      _ = SendPtrPtr(gul, sel_registerName("registerAppDelegateInterceptor:"), s_notifDelegateInstance);
      s_swizzlerRegistered = true;
    }
  }
  private static GCHandle RegisterObserver(IntPtr center, string symName, string fallback, IntPtr fnPtr) {
    IntPtr sym = dlsym(RtldDefault, symName), nsName = sym != IntPtr.Zero ? Marshal.ReadIntPtr(sym) : IntPtr.Zero;
    if (nsName == IntPtr.Zero) nsName = MakeNSString(fallback);
    GCHandle pin = GCHandle.Alloc(new[] { new ObjCBlockLiteral { Isa = s_stackBlockIsa, Flags = BlockHasSignature, Invoke = fnPtr, Descriptor = s_descErr } }, GCHandleType.Pinned);
    _ = SendObserver(center, sel_registerName("addObserverForName:object:queue:usingBlock:"), nsName, IntPtr.Zero, IntPtr.Zero, pin.AddrOfPinnedObject());
    return pin;
  }
  internal static bool IsAutoInitEnabled() => WithPool(() => GetFirMessaging() is IntPtr fm && (fm == IntPtr.Zero || SendBool(fm, sel_registerName("isAutoInitEnabled"))));
  internal static void SetAutoInitEnabled(bool enabled) => WithPool(() => {
    if (GetFirMessaging() is IntPtr fm && fm != IntPtr.Zero) {
      bool was = SendBool(fm, sel_registerName("isAutoInitEnabled")); SendVoidBool(fm, sel_registerName("setAutoInitEnabled:"), enabled);
      if (!was && enabled) DispatchTokenOrRegistration(fm, ReadNSString(SendPtr(fm, sel_registerName("FCMToken"))));
    }
    return true;
  });
  internal static bool IsDeliveryMetricsExportToBigQueryEnabled() => false;
  internal static void SetDeliveryMetricsExportToBigQueryEnabled(bool enabled) { }
  private static (TaskCompletionSource<string> Tcs, IntPtr Ptr) AllocBlock(IntPtr invoke, IntPtr desc, Error errCode) {
    var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    int key; lock (s_pending) key = ++s_nextKey;
    GCHandle pin = GCHandle.Alloc(new[] { new ObjCBlockLiteral { Isa = s_stackBlockIsa, Flags = BlockHasSignature, Invoke = invoke, Descriptor = desc, RequestKey = key } }, GCHandleType.Pinned);
    lock (s_pending) s_pending[key] = new PendingCall { Tcs = tcs, Pin = pin, ErrCode = errCode };
    return (tcs, pin.AddrOfPinnedObject());
  }
  private static void CompleteCall(IntPtr block, string val, IntPtr errObj) {
    PendingCall c = null;
    try {
      int key = Marshal.ReadInt32(block, s_keyOffset); lock (s_pending) { if (s_pending.TryGetValue(key, out c)) s_pending.Remove(key); }
      if (c == null) return;
      if (c.Pin.IsAllocated) c.Pin.Free();
      if (errObj != IntPtr.Zero) c.Tcs.TrySetException(new FirebaseException((int)c.ErrCode, ReadNSString(SendPtr(errObj, sel_registerName("localizedDescription"))) ?? "iOS FirebaseMessaging error."));
      else c.Tcs.TrySetResult(val ?? string.Empty);
    } catch (Exception ex) { c?.Tcs.TrySetException(new FirebaseException((int)Error.Unknown, ex.Message, ex)); }
  }
  internal static async Task<string> GetTokenAsync() {
    string tok = await WithPool(() => { IntPtr fm = RequireFirMessaging(); var b = AllocBlock(s_tokFn, s_descTok, Error.Unknown); SendVoidPtr(fm, sel_registerName("tokenWithCompletion:"), b.Ptr); return b.Tcs.Task; }).ConfigureAwait(false);
    if (!string.IsNullOrEmpty(tok)) MessagingRuntime.NotifyTokenReceived(tok);
    return tok;
  }
  internal static Task DeleteTokenAsync() => InvokeVoidSelectorAsync("deleteTokenWithCompletion:", Error.Unknown);
  internal static async Task RegisterAsync() { await InvokeVoidSelectorAsync("registerWithCompletion:", Error.Unknown, fallbackToken: true).ConfigureAwait(false); MessagingRuntime.MarkRegistered(); }
  internal static Task UnregisterAsync() => InvokeVoidSelectorAsync("unregisterWithCompletion:", Error.Unknown, fallbackDelete: true);
  internal static Task SubscribeAsync(string topic) => InvokeTopicAsync("subscribeToTopic:completion:", topic);
  internal static Task UnsubscribeAsync(string topic) => InvokeTopicAsync("unsubscribeFromTopic:completion:", topic);
  private static async Task InvokeVoidSelectorAsync(string selName, Error errCode, bool fallbackToken = false, bool fallbackDelete = false) {
    Task<string> task = WithPool(() => {
      IntPtr fm = RequireFirMessaging(), sel = sel_registerName(selName);
      if (!SendBoolPtr(fm, sel_registerName("respondsToSelector:"), sel)) return null;
      var b = AllocBlock(s_errFn, s_descErr, errCode); SendVoidPtr(fm, sel, b.Ptr); return b.Tcs.Task;
    });
    if (task != null) _ = await task.ConfigureAwait(false);
    else if (fallbackToken) MessagingRuntime.NotifyRegistrationReceived(await GetTokenAsync().ConfigureAwait(false));
    else if (fallbackDelete) await DeleteTokenAsync().ConfigureAwait(false);
  }
  private static async Task InvokeTopicAsync(string selName, string topic) =>
      _ = await WithPool(() => { IntPtr fm = RequireFirMessaging(); var b = AllocBlock(s_errFn, s_descErr, Error.InvalidTopicName); SendVoidPtrPtr(fm, sel_registerName(selName), MakeNSString(topic), b.Ptr); return b.Tcs.Task; }).ConfigureAwait(false);
  internal static async Task RequestPermissionAsync() {
    Task<string> task = WithPool(() => {
      IntPtr center = objc_getClass("UNUserNotificationCenter") is IntPtr cls && cls != IntPtr.Zero ? SendPtr(cls, sel_registerName("currentNotificationCenter")) : IntPtr.Zero;
      if (center == IntPtr.Zero) return null;
      var b = AllocBlock(s_authFn, s_descAuth, Error.FailedToRegisterForRemoteNotifications);
      SendVoidNUIntPtr(center, sel_registerName("requestAuthorizationWithOptions:completionHandler:"), 7u, b.Ptr); return b.Tcs.Task;
    });
    if (task == null) return;
    _ = FirebaseHandler.RunOnMainThreadAsync(() => WithPool(() => {
      if (objc_getClass("UIApplication") is IntPtr c && c != IntPtr.Zero && SendPtr(c, sel_registerName("sharedApplication")) is IntPtr app && app != IntPtr.Zero) SendVoid(app, sel_registerName("registerForRemoteNotifications"));
      return true;
    }));
    _ = await task.ConfigureAwait(false);
  }
  private static void DeliverIosNotification(IntPtr notification, bool opened) {
    IntPtr req = notification != IntPtr.Zero ? SendPtr(notification, sel_registerName("request")) : IntPtr.Zero;
    IntPtr content = req != IntPtr.Zero ? SendPtr(req, sel_registerName("content")) : IntPtr.Zero;
    DeliverIosUserInfo(content != IntPtr.Zero ? SendPtr(content, sel_registerName("userInfo")) : IntPtr.Zero, opened);
  }
  private static void DeliverIosUserInfo(IntPtr userInfo, bool opened) {
    if (ReadNSDictionary(userInfo) is IDictionary<string, object> d && MessagingRuntime.ParseApnsPayload(d, opened) is FirebaseMessage msg) {
      bool dup = false;
      if (!string.IsNullOrEmpty(msg.MessageId)) {
        string key = msg.MessageId + (opened ? "|1" : "|0");
        lock (s_recentMsgKeys) { dup = s_recentMsgKeys.Contains(key); if (!dup) { while (s_recentMsgKeys.Count >= 32) _ = s_recentMsgKeys.Dequeue(); s_recentMsgKeys.Enqueue(key); } }
      }
      if (!dup) MessagingRuntime.NotifyMessageReceived(msg);
    }
    if (userInfo != IntPtr.Zero && GetFirMessaging() is IntPtr fm && fm != IntPtr.Zero) SendVoidPtr(fm, sel_registerName("appDidReceiveMessage:"), userInfo);
  }
  private static Dictionary<string, object> ReadNSDictionary(IntPtr dict) {
    if (!IsKindOf(dict, "NSDictionary")) return null;
    IntPtr keys = SendPtr(dict, sel_registerName("allKeys")), idxSel = sel_registerName("objectAtIndex:"), valSel = sel_registerName("objectForKey:");
    nuint n = keys != IntPtr.Zero ? SendNUInt(keys, sel_registerName("count")) : 0;
    var res = new Dictionary<string, object>((int)n, StringComparer.Ordinal);
    for (nuint i = 0; i < n; i++) { IntPtr kObj = SendPtrNUInt(keys, idxSel, i); if (ReadNSString(kObj) is string k) res[k] = ConvertObjCValue(SendPtrPtr(dict, valSel, kObj)); }
    return res;
  }
  private static object ConvertObjCValue(IntPtr o) {
    if (o == IntPtr.Zero) return null;
    if (IsKindOf(o, "NSString")) return ReadNSString(o) ?? string.Empty;
    if (IsKindOf(o, "NSNumber")) { double dv = SendDouble(o, sel_registerName("doubleValue")); long lv = SendLong(o, sel_registerName("longLongValue")); return dv == lv ? (object)lv : dv; }
    if (IsKindOf(o, "NSDictionary")) return ReadNSDictionary(o);
    if (IsKindOf(o, "NSData")) {
      int len = (int)SendNUInt(o, sel_registerName("length")); if (len <= 0) return Array.Empty<byte>();
      var bytes = new byte[len]; if (SendPtr(o, sel_registerName("bytes")) is IntPtr p && p != IntPtr.Zero) Marshal.Copy(p, bytes, 0, len);
      return bytes;
    }
    if (IsKindOf(o, "NSArray")) {
      nuint n = SendNUInt(o, sel_registerName("count")); var list = new List<object>((int)n); IntPtr idxSel = sel_registerName("objectAtIndex:");
      for (nuint i = 0; i < n; i++) list.Add(ConvertObjCValue(SendPtrNUInt(o, idxSel, i)));
      return list;
    }
    return null;
  }
  private static bool IsKindOf(IntPtr o, string clsName) => o != IntPtr.Zero && objc_getClass(clsName) is IntPtr c && c != IntPtr.Zero && SendBoolPtr(o, sel_registerName("isKindOfClass:"), c);
  [AOT.MonoPInvokeCallback(typeof(ObjCNotifCenterImp))]
  private static void OnWillPresentNotification(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr compBlock) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      DeliverIosNotification(notification, opened: false);
      if (s_prevNotifDelegate != IntPtr.Zero && SendBoolPtr(s_prevNotifDelegate, sel_registerName("respondsToSelector:"), cmd)) SendVoidPtrPtrPtr(s_prevNotifDelegate, cmd, center, notification, compBlock);
      else if (compBlock != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<ObjCBlockOptInvoke>(Marshal.ReadIntPtr(compBlock, 16))(compBlock, 0u);
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
#if !UNITY_TVOS
  [AOT.MonoPInvokeCallback(typeof(ObjCNotifCenterImp))]
  private static void OnDidReceiveNotificationResponse(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr compBlock) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      DeliverIosNotification(response != IntPtr.Zero ? SendPtr(response, sel_registerName("notification")) : IntPtr.Zero, opened: true);
      if (s_prevNotifDelegate != IntPtr.Zero && SendBoolPtr(s_prevNotifDelegate, sel_registerName("respondsToSelector:"), cmd)) SendVoidPtrPtrPtr(s_prevNotifDelegate, cmd, center, response, compBlock);
      else if (compBlock != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<ObjCBlockVoidInvoke>(Marshal.ReadIntPtr(compBlock, 16))(compBlock);
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
#endif
  [AOT.MonoPInvokeCallback(typeof(ObjCNotifCenterImp))]
  private static void OnDidReceiveRemoteNotification(IntPtr self, IntPtr cmd, IntPtr app, IntPtr userInfo, IntPtr compBlock) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      DeliverIosUserInfo(userInfo, opened: app != IntPtr.Zero && SendNInt(app, sel_registerName("applicationState")) != 0);
      if (compBlock != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<ObjCBlockOptInvoke>(Marshal.ReadIntPtr(compBlock, 16))(compBlock, 1u);
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  [AOT.MonoPInvokeCallback(typeof(ObjCTokenCb))]
  private static void OnTokenDone(IntPtr block, IntPtr tokObj, IntPtr errObj) {
    IntPtr pool = objc_autoreleasePoolPush(); try { CompleteCall(block, errObj == IntPtr.Zero ? ReadNSString(tokObj) : null, errObj); } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  [AOT.MonoPInvokeCallback(typeof(ObjCErrorCb))]
  private static void OnErrorDone(IntPtr block, IntPtr errObj) {
    IntPtr pool = objc_autoreleasePoolPush(); try { CompleteCall(block, string.Empty, errObj); } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  [AOT.MonoPInvokeCallback(typeof(ObjCAuthCb))]
  private static void OnAuthDone(IntPtr block, bool granted, IntPtr errObj) {
    IntPtr pool = objc_autoreleasePoolPush(); try { CompleteCall(block, granted ? "1" : "0", errObj); } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  [AOT.MonoPInvokeCallback(typeof(ObjCNoteCb))]
  private static void OnTokenRefreshedNotification(IntPtr block, IntPtr noteObj) {
    IntPtr pool = objc_autoreleasePoolPush();
    try {
      string val = noteObj != IntPtr.Zero ? ReadNSString(SendPtr(noteObj, sel_registerName("object"))) : null;
      IntPtr fm = GetFirMessaging(); if (string.IsNullOrEmpty(val) && fm != IntPtr.Zero) val = ReadNSString(SendPtr(fm, sel_registerName("FCMToken")));
      DispatchTokenOrRegistration(fm, val);
    } catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  [AOT.MonoPInvokeCallback(typeof(ObjCNoteCb))]
  private static void OnUnregisteredNotification(IntPtr block, IntPtr noteObj) {
    IntPtr pool = objc_autoreleasePoolPush();
    try { if (noteObj != IntPtr.Zero && ReadNSString(SendPtr(noteObj, sel_registerName("object"))) is string fid && fid.Length > 0) MessagingRuntime.NotifyUnregistrationReceived(fid); }
    catch (Exception ex) { MessagingRuntime.LogDebug(ex); } finally { objc_autoreleasePoolPop(pool); }
  }
  private static void DispatchTokenOrRegistration(IntPtr fm, string val) {
    if (string.IsNullOrEmpty(val)) return;
    IntPtr sel = sel_registerName("isInstallationIdEnabled");
    if (fm != IntPtr.Zero && SendBoolPtr(fm, sel_registerName("respondsToSelector:"), sel) && SendBool(fm, sel)) MessagingRuntime.NotifyRegistrationReceived(val);
    else MessagingRuntime.NotifyTokenReceived(val);
  }
  private static string ReadNSString(IntPtr ns) => IsKindOf(ns, "NSString") && SendPtr(ns, sel_registerName("UTF8String")) is IntPtr p && p != IntPtr.Zero ? Firebase.Internal.FirebaseInterops.PtrToStringUtf8(p) : null;
  private static IntPtr MakeNSString(string s) {
    byte[] utf8 = Encoding.UTF8.GetBytes(s ?? string.Empty); IntPtr buf = Marshal.AllocHGlobal(utf8.Length + 1);
    try { Marshal.Copy(utf8, 0, buf, utf8.Length); Marshal.WriteByte(buf, utf8.Length, 0); return SendPtrPtr(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), buf); }
    finally { Marshal.FreeHGlobal(buf); }
  }
#else
  private static readonly Task<string> s_stubTokenTask = Task.FromResult(MessagingRuntime.StubTokenValue);
  private static volatile bool s_autoInitEnabled = true;
  internal static bool HasTopicRegistration() => MessagingRuntime.HasActiveRegistration;
  internal static void EnsureInitialized() { }
  internal static void OnMainThreadReady() { }
  internal static void ResetForReload() => s_autoInitEnabled = true;
  internal static bool IsAutoInitEnabled() => s_autoInitEnabled;
  internal static void SetAutoInitEnabled(bool enabled) => s_autoInitEnabled = enabled;
  internal static bool IsDeliveryMetricsExportToBigQueryEnabled() => false;
  internal static void SetDeliveryMetricsExportToBigQueryEnabled(bool enabled) { }
  internal static Task<string> GetTokenAsync() { MessagingRuntime.MarkRegistered(); return s_stubTokenTask; }
  internal static Task DeleteTokenAsync() => Task.CompletedTask;
  internal static Task RequestPermissionAsync() => Task.CompletedTask;
  internal static Task SubscribeAsync(string topic) => Task.CompletedTask;
  internal static Task UnsubscribeAsync(string topic) => Task.CompletedTask;
  internal static Task RegisterAsync() { MessagingRuntime.NotifyRegistrationReceived(MessagingRuntime.StubRegistrationIdValue); return Task.CompletedTask; }
  internal static Task UnregisterAsync() { MessagingRuntime.NotifyUnregistrationReceived(MessagingRuntime.StubRegistrationIdValue); return Task.CompletedTask; }
#endif
}

}  // namespace Firebase.Messaging.Internal
