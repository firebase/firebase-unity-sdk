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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Internal;
using Firebase.Platform;

[assembly: InternalsVisibleTo("Firebase.TestHarness"), InternalsVisibleTo("Firebase.Tests"), InternalsVisibleTo("Firebase.Benchmarks")]
[assembly: InternalsVisibleTo("Assembly-CSharp"), InternalsVisibleTo("Assembly-CSharp-Editor"), InternalsVisibleTo("Tests")]

namespace Firebase.Messaging.Internal {

#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
internal static class MessagingRuntime {
  internal const int MaxQueueSize = 32;
  internal const string StubTokenValue = "StubToken", StubRegistrationIdValue = "StubRegistrationId";

  private sealed class EventSlot<TItem, TArgs> where TArgs : EventArgs {
    private readonly Queue<TItem> _queue = new Queue<TItem>();
    private readonly Func<TItem, TArgs> _wrap;
    internal EventHandler<TArgs> Handlers;
    internal EventSlot(Func<TItem, TArgs> wrap) => _wrap = wrap;
    internal TArgs CreateArgs(TItem item) => _wrap(item);
    internal void UnsubscribeLocked(EventHandler<TArgs> h) => Handlers -= h;
    internal void ClearLocked() { Handlers = null; _queue.Clear(); }
    internal TItem[] SubscribeLocked(EventHandler<TArgs> h) {
      Handlers += h; if (_queue.Count == 0) return null;
      TItem[] items = _queue.ToArray(); _queue.Clear(); return items;
    }
    internal bool EnqueueIfNoSubscriberLocked(TItem item) {
      if (Handlers != null) return false;
      while (_queue.Count >= MaxQueueSize) _queue.Dequeue();
      _queue.Enqueue(item); return true;
    }
    internal void RemoveQueuedLocked(Predicate<TItem> match) {
      for (int i = 0, n = _queue.Count; i < n; i++) { TItem cur = _queue.Dequeue(); if (!match(cur)) _queue.Enqueue(cur); }
    }
  }

  private static readonly object s_lock = new object();
  private static readonly EventSlot<FirebaseMessage, MessageReceivedEventArgs> s_messages = new(m => new MessageReceivedEventArgs(m));
  private static readonly EventSlot<string, TokenReceivedEventArgs> s_tokens = new(t => new TokenReceivedEventArgs(t));
  private static readonly EventSlot<string, RegistrationReceivedEventArgs> s_registrations = new(id => new RegistrationReceivedEventArgs(id));
  private static readonly EventSlot<string, UnregistrationReceivedEventArgs> s_unregistrations = new(id => new UnregistrationReceivedEventArgs(id));
  private static string s_lastToken;
  private static bool s_hasActiveRegistration;
  private static long s_epoch;
  private static volatile bool s_mainReady;
  private static volatile int s_mainThreadId;
  private static volatile SynchronizationContext s_mainSyncContext;
  internal static Action<Action> CustomMainThreadDispatcher;

  static MessagingRuntime() => FirebaseInstanceCache.RegisterResetHook(ResetForReload);
#if UNITY_2019_3_OR_NEWER
  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void CaptureMainThread() => CaptureCurrentThreadAsMainForTesting();
  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
  private static void OnAfterSceneLoad() { EnsureMainThreadDispatcher(); MobileMessagingBridge.OnMainThreadReady(); }
#endif
#if UNITY_EDITOR
  [UnityEditor.InitializeOnLoadMethod]
  private static void OnEditorInitialize() { CaptureCurrentThreadAsMainForTesting(); EnsureMainThreadDispatcher(); }
#endif

  internal static void LogDebug(Exception ex) => System.Diagnostics.Debug.WriteLine(ex?.Message);
#if UNITY_2019_3_OR_NEWER
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void LogUnityError(Exception ex) => UnityEngine.Debug.LogError(ex);
#endif
  internal static void LogSubscriberError(Exception ex) {
    try {
#if UNITY_2019_3_OR_NEWER
      if (IsOnUnityMainThread) { LogUnityError(ex); return; }
#endif
      System.Diagnostics.Trace.TraceError(ex?.ToString());
    } catch (Exception fallbackEx) { System.Diagnostics.Trace.TraceError(fallbackEx.Message); }
  }

  internal static int MainThreadId => s_mainThreadId;
  internal static bool IsOnUnityMainThread => s_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == s_mainThreadId;
  internal static bool HasActiveRegistration { get { lock (s_lock) return s_hasActiveRegistration; } }
  internal static void MarkRegistered() { lock (s_lock) s_hasActiveRegistration = true; }
  internal static void CaptureCurrentThreadAsMainForTesting() { s_mainThreadId = Thread.CurrentThread.ManagedThreadId; s_mainSyncContext = SynchronizationContext.Current; }
  private static void InitOnMainThread() {
    if (s_mainReady) return;
    s_mainReady = true; EnsureMainThreadDispatcher(); try { _ = FirebaseApp.DefaultInstance; } catch (Exception ex) { LogDebug(ex); }
  }
  private static void EnsureInitialized() {
    if (!s_mainReady) {
      if (IsOnUnityMainThread) InitOnMainThread();
      else if (FirebaseHandler.DefaultInstance != null) _ = FirebaseHandler.RunOnMainThreadAsync(() => { InitOnMainThread(); return true; });
    }
    MobileMessagingBridge.EnsureInitialized();
  }
  internal static void EnsureMainThreadDispatcher() {
    try { if (FirebaseHandler.DefaultInstance == null && FirebaseHandler.AppUtils != null) FirebaseHandler.CreatePartialOnMainThread(FirebaseHandler.AppUtils); } catch (Exception ex) { LogDebug(ex); }
  }
  internal static void ResetForReload() {
    lock (s_lock) { s_epoch++; s_messages.ClearLocked(); s_tokens.ClearLocked(); s_registrations.ClearLocked(); s_unregistrations.ClearLocked(); s_lastToken = null; s_hasActiveRegistration = false; }
    s_mainReady = false; CustomMainThreadDispatcher = null; MobileMessagingBridge.ResetForReload();
  }
  internal static void ClearRegistrationState() { lock (s_lock) { s_lastToken = null; s_hasActiveRegistration = false; } }
  internal static bool RegistrationOnInitEnabled {
    get { EnsureInitialized(); return MobileMessagingBridge.IsAutoInitEnabled(); } set { EnsureInitialized(); MobileMessagingBridge.SetAutoInitEnabled(value); }
  }
  internal static bool DeliveryMetricsExportedToBigQueryEnabled {
    get { EnsureInitialized(); return MobileMessagingBridge.IsDeliveryMetricsExportToBigQueryEnabled(); } set { EnsureInitialized(); MobileMessagingBridge.SetDeliveryMetricsExportToBigQueryEnabled(value); }
  }
  internal static Task<string> GetTokenAsync() { EnsureInitialized(); return MobileMessagingBridge.GetTokenAsync(); }
  internal static Task DeleteTokenAsync() { EnsureInitialized(); ClearRegistrationState(); return MobileMessagingBridge.DeleteTokenAsync(); }
  internal static Task RegisterAsync() { EnsureInitialized(); return MobileMessagingBridge.RegisterAsync(); }
  internal static Task UnregisterAsync() { EnsureInitialized(); ClearRegistrationState(); return MobileMessagingBridge.UnregisterAsync(); }
  internal static Task RequestPermissionAsync() { EnsureInitialized(); return MobileMessagingBridge.RequestPermissionAsync(); }
  internal static Task SubscribeAsync(string topic) { EnsureInitialized(); return CheckTopicPrerequisites(topic) is FirebaseException ex ? Task.FromException(ex) : MobileMessagingBridge.SubscribeAsync(topic); }
  internal static Task UnsubscribeAsync(string topic) { EnsureInitialized(); return CheckTopicPrerequisites(topic) is FirebaseException ex ? Task.FromException(ex) : MobileMessagingBridge.UnsubscribeAsync(topic); }
  private static FirebaseException CheckTopicPrerequisites(string topic) =>
      !MobileMessagingBridge.HasTopicRegistration() && !MobileMessagingBridge.IsAutoInitEnabled()
          ? new FirebaseException((int)Error.NoRegistrationToken, "Cannot update subscription when RegistrationOnInitEnabled is false and no token is registered.") : ValidateTopic(topic);
  internal static FirebaseException ValidateTopic(string topic) {
    if (string.IsNullOrEmpty(topic)) return new FirebaseException((int)Error.InvalidTopicName, "Topic name must not be null or empty.");
    string clean = topic.StartsWith("/topics/", StringComparison.Ordinal) ? topic.Substring(8) : topic;
    if (clean.Length == 0 || clean.Length > 900) return new FirebaseException((int)Error.InvalidTopicName, $"Cannot parse topic name {topic}.");
    for (int i = 0; i < clean.Length; i++) {
      char c = clean[i];
      if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '~' || c == '%'))
        return new FirebaseException((int)Error.InvalidTopicName, $"Cannot parse topic name {topic}.");
    }
    return null;
  }
  internal static void AddMessageReceived(EventHandler<MessageReceivedEventArgs> h) => AddHandler(s_messages, h);
  internal static void RemoveMessageReceived(EventHandler<MessageReceivedEventArgs> h) => RemoveHandler(s_messages, h);
  internal static void AddTokenReceived(EventHandler<TokenReceivedEventArgs> h) => AddHandler(s_tokens, h);
  internal static void RemoveTokenReceived(EventHandler<TokenReceivedEventArgs> h) => RemoveHandler(s_tokens, h);
  internal static void AddRegistrationReceived(EventHandler<RegistrationReceivedEventArgs> h) => AddHandler(s_registrations, h);
  internal static void RemoveRegistrationReceived(EventHandler<RegistrationReceivedEventArgs> h) => RemoveHandler(s_registrations, h);
  internal static void AddUnregistrationReceived(EventHandler<UnregistrationReceivedEventArgs> h) => AddHandler(s_unregistrations, h);
  internal static void RemoveUnregistrationReceived(EventHandler<UnregistrationReceivedEventArgs> h) => RemoveHandler(s_unregistrations, h);

  private static void AddHandler<TItem, TArgs>(EventSlot<TItem, TArgs> slot, EventHandler<TArgs> h) where TArgs : EventArgs {
    if (h == null) return;
    EnsureInitialized();
    TItem[] drained; long epoch; lock (s_lock) { epoch = s_epoch; drained = slot.SubscribeLocked(h); }
    if (drained != null) for (int i = 0; i < drained.Length; i++) DispatchSlot(slot, drained[i], epoch);
  }
  private static void RemoveHandler<TItem, TArgs>(EventSlot<TItem, TArgs> slot, EventHandler<TArgs> h) where TArgs : EventArgs { if (h != null) lock (s_lock) slot.UnsubscribeLocked(h); }
  private static void NotifySlot<TItem, TArgs>(EventSlot<TItem, TArgs> slot, TItem item) where TArgs : EventArgs {
    long epoch; lock (s_lock) { if (slot.EnqueueIfNoSubscriberLocked(item)) return; epoch = s_epoch; } DispatchSlot(slot, item, epoch);
  }
  internal static void NotifyMessageReceived(FirebaseMessage message) { if (message != null) NotifySlot(s_messages, message); }
  internal static void NotifyRegistrationReceived(string installationId) { if (!string.IsNullOrEmpty(installationId)) { MarkRegistered(); NotifySlot(s_registrations, installationId); } }
  internal static void NotifyTokenReceived(string token) {
    if (string.IsNullOrEmpty(token)) return;
    long epoch;
    lock (s_lock) {
      s_hasActiveRegistration = true; if (string.Equals(s_lastToken, token, StringComparison.Ordinal)) return;
      s_lastToken = token; if (s_tokens.EnqueueIfNoSubscriberLocked(token)) return; epoch = s_epoch;
    }
    DispatchSlot(s_tokens, token, epoch);
  }
  internal static void NotifyUnregistrationReceived(string installationId) {
    if (string.IsNullOrEmpty(installationId)) return;
    long epoch;
    lock (s_lock) {
      s_hasActiveRegistration = false; s_registrations.RemoveQueuedLocked(id => string.Equals(id, installationId, StringComparison.Ordinal));
      if (s_unregistrations.EnqueueIfNoSubscriberLocked(installationId)) return; epoch = s_epoch;
    }
    DispatchSlot(s_unregistrations, installationId, epoch);
  }

  private static void DispatchSlot<TItem, TArgs>(EventSlot<TItem, TArgs> slot, TItem item, long epoch) where TArgs : EventArgs {
    TArgs args = slot.CreateArgs(item);
    DispatchToMainThread(() => {
      Delegate[] list; lock (s_lock) { if (s_epoch != epoch || slot.Handlers == null) return; list = slot.Handlers.GetInvocationList(); }
      for (int i = 0; i < list.Length; i++) {
        var single = (EventHandler<TArgs>)list[i]; bool active; lock (s_lock) active = s_epoch == epoch && IsSubscribed(slot.Handlers, single);
        if (active) { try { single(null, args); } catch (Exception ex) { LogSubscriberError(ex); } }
      }
    });
  }
  private static bool IsSubscribed<TArgs>(EventHandler<TArgs> multicast, EventHandler<TArgs> target) where TArgs : EventArgs {
    if (multicast == null || target == null) return false;
    if (ReferenceEquals(multicast, target) || multicast.Equals(target)) return true;
    Delegate[] list = multicast.GetInvocationList();
    for (int i = 0; i < list.Length; i++) if (list[i].Equals(target)) return true;
    return false;
  }
  internal static void DispatchToMainThread(Action callback) {
    if (callback == null) return;
    if (CustomMainThreadDispatcher is Action<Action> custom) { custom(callback); return; }
    if (FirebaseHandler.DefaultInstance == null && s_mainSyncContext != null && SynchronizationContext.Current != s_mainSyncContext) {
      s_mainSyncContext.Post(_ => callback(), null); return;
    }
    _ = FirebaseHandler.RunOnMainThreadAsync(() => { callback(); return true; });
  }

  internal static bool IsUnreservedDataKey(string k) =>
      !string.IsNullOrEmpty(k) && !k.StartsWith("google.", StringComparison.Ordinal) && !k.StartsWith("gcm.", StringComparison.Ordinal) &&
      k != "from" && k != "to" && k != "collapse_key" && k != "message_id" && k != "message_type" && k != "priority" && k != "time_to_live" && k != "error" && k != "error_description" && k != "rawData" && k != "aps";
  internal static FirebaseMessage ParseAndroidExtras(IDictionary<string, string> extras, string intentDataUri, bool notificationOpened) {
    if (extras == null || extras.Count == 0) return null;
    string msgId = GetStr(extras, "google.message_id"); if (msgId.Length == 0) msgId = GetStr(extras, "message_id");
    string from = GetStr(extras, "from"); if (msgId.Length == 0 || from.Length == 0) return null;
    var data = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (KeyValuePair<string, string> kv in extras) if (IsUnreservedDataKey(kv.Key) && kv.Value != null) data[kv.Key] = kv.Value;
    string linkStr = !string.IsNullOrEmpty(intentDataUri) ? intentDataUri : (GetStr(extras, "gcm.n.link_android") is string la && la.Length > 0 ? la : GetStr(extras, "gcm.n.link"));
    int ttlSec = int.TryParse(GetStr(extras, "google.ttl"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) && t > 0 ? t : 0;
    FirebaseNotification notif = null;
    string title = GetStr(extras, "gcm.n.title"), body = GetStr(extras, "gcm.n.body");
    if (title.Length > 0 || body.Length > 0 || GetStr(extras, "gcm.n.e") == "1") {
      notif = new FirebaseNotification {
        Title = title, Body = body, Icon = GetStr(extras, "gcm.n.icon"), Tag = GetStr(extras, "gcm.n.tag"),
        Badge = GetStr(extras, "gcm.n.notification_count"), Color = GetStr(extras, "gcm.n.color"), ClickAction = GetStr(extras, "gcm.n.click_action"),
        Sound = GetStr(extras, "gcm.n.sound2") is string s2 && s2.Length > 0 ? s2 : GetStr(extras, "gcm.n.sound"),
        BodyLocalizationKey = GetStr(extras, "gcm.n.body_loc_key"), BodyLocalizationArgs = ParseJsonStrings(GetStr(extras, "gcm.n.body_loc_args")),
        TitleLocalizationKey = GetStr(extras, "gcm.n.title_loc_key"), TitleLocalizationArgs = ParseJsonStrings(GetStr(extras, "gcm.n.title_loc_args")),
        Android = new AndroidNotificationParams { ChannelId = GetStr(extras, "gcm.n.android_channel_id") },
      };
    }
    return new FirebaseMessage {
      MessageId = msgId, From = from, To = GetStr(extras, "google.to"), MessageType = GetStr(extras, "message_type"),
      CollapseKey = GetStr(extras, "collapse_key"), Priority = MapPriority(GetStr(extras, "google.delivered_priority")),
      TimeToLive = TimeSpan.FromSeconds(ttlSec), Error = GetStr(extras, "error"), ErrorDescription = GetStr(extras, "error_description"),
      Data = data, Notification = notif, NotificationOpened = notificationOpened, Link = FirebaseMessage.ParseLinkUri(linkStr),
    };
  }
  internal static FirebaseMessage ParseApnsPayload(IDictionary<string, object> userInfo, bool notificationOpened) {
    if (userInfo == null) return null;
    var data = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (KeyValuePair<string, object> kv in userInfo) if (IsUnreservedDataKey(kv.Key) && kv.Value is string sv) data[kv.Key] = sv;
    FirebaseNotification notif = null;
    if (userInfo.TryGetValue("aps", out object apsObj) && apsObj is IDictionary<string, object> aps) {
      notif = new FirebaseNotification {
        Sound = GetObjStr(aps, "sound"), ClickAction = GetObjStr(aps, "category"),
        Badge = aps.TryGetValue("badge", out object b) && b != null ? Convert.ToString(b, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty,
      };
      if (aps.TryGetValue("alert", out object alertObj)) {
        if (alertObj is string alertBody) notif.Body = alertBody;
        else if (alertObj is IDictionary<string, object> ad) {
          notif.Title = GetObjStr(ad, "title"); notif.Body = GetObjStr(ad, "body");
          notif.BodyLocalizationKey = GetObjStr(ad, "loc-key"); notif.BodyLocalizationArgs = ToStrings(ad, "loc-args");
          notif.TitleLocalizationKey = GetObjStr(ad, "title-loc-key"); notif.TitleLocalizationArgs = ToStrings(ad, "title-loc-args");
        }
      }
    }
    int ttlSec = userInfo.TryGetValue("time_to_live", out object ttlObj) && ttlObj != null && int.TryParse(Convert.ToString(ttlObj, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pt) && pt > 0 ? pt : 0;
    byte[] raw = userInfo.TryGetValue("rawData", out object ro) && ro is byte[] rb ? rb : Array.Empty<byte>();
    string msgId = GetObjStr(userInfo, "gcm.message_id"); if (msgId.Length == 0) msgId = GetObjStr(userInfo, "message_id");
    return new FirebaseMessage {
      From = GetObjStr(userInfo, "from"), To = GetObjStr(userInfo, "to"), CollapseKey = GetObjStr(userInfo, "collapse_key"),
      MessageId = msgId, MessageType = GetObjStr(userInfo, "message_type"), Priority = GetObjStr(userInfo, "priority"),
      TimeToLive = TimeSpan.FromSeconds(ttlSec), Error = GetObjStr(userInfo, "error"), ErrorDescription = GetObjStr(userInfo, "error_description"),
      Data = data, RawData = raw, Notification = notif, NotificationOpened = notificationOpened, Link = FirebaseMessage.ParseLinkUri(GetObjStr(userInfo, "gcm.n.link")),
    };
  }
  private static string GetStr(IDictionary<string, string> d, string k) => d != null && d.TryGetValue(k, out string v) && v != null ? v : string.Empty;
  private static string GetObjStr(IDictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out object v) && v is string s ? s : string.Empty;
  private static string MapPriority(string raw) => raw == "1" || string.Equals(raw, "high", StringComparison.OrdinalIgnoreCase) ? "high" : raw == "2" || string.Equals(raw, "normal", StringComparison.OrdinalIgnoreCase) ? "normal" : string.Empty;
  private static IEnumerable<string> ParseJsonStrings(string json) {
    if (!string.IsNullOrEmpty(json)) { try { if (Google.MiniJSON.Json.Deserialize(json) is IList list) return CopyList(list); } catch (Exception ex) { LogDebug(ex); } }
    return Array.Empty<string>();
  }
  private static IEnumerable<string> ToStrings(IDictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out object v) && v is IList list ? CopyList(list) : Array.Empty<string>();
  private static IEnumerable<string> CopyList(IList list) {
    if (list == null || list.Count == 0) return Array.Empty<string>();
    var res = new List<string>(list.Count); for (int i = 0; i < list.Count; i++) res.Add(list[i]?.ToString() ?? string.Empty); return res;
  }
}

}  // namespace Firebase.Messaging.Internal
