// Copyright 2016 Google Inc. All Rights Reserved.
//
// The messaging wrapper is just like the C++ wrapper, except works with c#
// strings. To interface with the map in the C++ wrapper, there are explicit
// calls, as you might expect for interacting with a map like object in C#, such
// as enumerable iteration. It does not convert to C# dictionary types.
//
// Swig Lint doesn't support C#
//swiglint: disable

%module FirebaseMessaging

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="public sealed class"
%feature("flatnested");

%{
#include "app/src/callback.h"
#include "messaging/src/include/firebase/messaging.h"
%}

%import "app/src/swig/app.i"
%include "app/src/swig/future.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"
%include "stdint.i"

// All outputs of StringList should instead be IEnumerable<string>
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<string>") std::vector<std::string>* "StringList";

// All outputs of StringStringMap should instead be IDictionary<string, string>
%typemap(cstype, out="global::System.Collections.Generic.IDictionary<string, string>") std::map<std::string, std::string>* "StringStringMap";
// Because this is used as part of a property, extra typemaps are needed.
%typemap(csin) std::map<std::string, std::string>* "$csclassname.getCPtr(temp$csinput)"
%typemap(csvarin, excode=SWIGEXCODE2) std::map<std::string, std::string>* %{
  set {
    StringStringMap temp$csinput = new StringStringMap();
    foreach (var kvp in $csinput) {
      temp$csinput.Add(kvp);
    }
    $imcall;$excode
  }
%}

%{
#include <queue>
#include <string>

#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/log.h"

namespace firebase {
namespace messaging {


// C++ implementation of the Listener interface that forwards the messages to
// the cached C# callbacks.
class ListenerImpl : public Listener {
public:
  // Function which is used to reference a C# delegate which is called from
  // OnMessage() callback in this class.  SWIGSTDCALL is used here as C#
  // delegates *must* be called using the stdcall calling convention rather
  // than whatever the compiler defines.
  typedef int (SWIGSTDCALL *MessageReceivedCallback)(void* message);
  // Function which is used to reference a C# delegate which is called from
  // OnTokenReceived() callback in this class.  SWIGSTDCALL is used here as C#
  // delegates *must* be called using the stdcall calling convention rather
  // than whatever the compiler defines.
  typedef void (SWIGSTDCALL *TokenReceivedCallback)(char* token);

  ListenerImpl() {}
  virtual ~ListenerImpl() {}

  // Receives a message from the messaging module and forwards it to the
  // callback queue which will eventually result in a call to
  // MessageReceived on the main thread.
  virtual void OnMessage(const firebase::messaging::Message& message) {
    QueueItem(&messages_, message);
    firebase::LogDebug("queued message %s", message.message_id.c_str());
    SendPendingEvents();
  }

  // Receives a token from the messaging module and forwards it to the
  // callback queue which will eventually result in a call to
  // TokenReceived on the main thread.
  virtual void OnTokenReceived(const char* token) {
    QueueItem(&tokens_, std::string(token));
    firebase::LogDebug("queued token %s", token);
    SendPendingEvents();
  }

  // Send any pending events on the currently allocated listener.
  static void SendPendingEvents() {
    MutexLock lock(g_mutex);
    if (g_listener) g_listener->SendQueuedEventsIfEnabled();
  }

  // Configures methods that call C# delegates when OnMessageReceived() and
  // OnTokenReceived() methods of the listener are called.
  // When callbacks are specified a listener is created, if both arguments
  // are null the listener is destroyed.
  static void SetCallbacks(MessageReceivedCallback messageCallback,
                           TokenReceivedCallback tokenCallback) {
    MutexLock lock(g_mutex);
    ListenerImpl *new_listener = (messageCallback && tokenCallback) ?
        new ListenerImpl : nullptr;
    Listener *previous_listener = SetListener(new_listener);
    g_message_received_callback = messageCallback;
    g_token_received_callback = tokenCallback;
    if (previous_listener) delete previous_listener;
    g_listener = new_listener;
  }

  // Enables / disables listener callbacks.  When a callback is disabled,
  // events are queued in the ListenerImpl.
  static void SetCallbacksEnabled(bool message_callback_enabled,
                                  bool token_callback_enabled) {
    g_message_callback_enabled = message_callback_enabled;
    g_token_callback_enabled = token_callback_enabled;
  }

 private:
  // Send any queued events (messages, tokens) if the queues are enabled.
  void SendQueuedEventsIfEnabled() {
    MutexLock lock(g_mutex);
    if (g_message_callback_enabled) {
      while (messages_.size()) {
        const Message &message = messages_.front();
        firebase::LogDebug("sending message %s", message.message_id.c_str());
        firebase::callback::AddCallback(
            new firebase::callback::Callback1<firebase::messaging::Message>(
                message, MessageReceived));
        messages_.pop();
      }
    }
    if (g_token_callback_enabled) {
      while (tokens_.size()) {
        const std::string &token = tokens_.front();
        firebase::LogDebug("sending token %s", token.c_str());
        firebase::callback::AddCallback(
            new firebase::callback::CallbackString(
                token.c_str(), TokenReceived));
        tokens_.pop();
      }
    }
  }

  // Push an item into the queue and discard the oldest item if the queue
  // has reached kMaxQueueSize.
  template<typename T>
  static void QueueItem(std::queue<T> *queue, const T& item) {
    MutexLock lock(g_mutex);
    while (queue->size() > kMaxQueueSize) {
      queue->pop();
    }
    queue->push(item);
  }

  // Cache of messages that are sent to the C# proxy class when a message
  // event handler is set.
  std::queue<Message> messages_;
  // Cache of tokens that are sent to the C# proxy class when a token
  // event handler is set.
  std::queue<std::string> tokens_;
  // Maximum number of items to queue in the messages and tokens queues.
  static const size_t kMaxQueueSize = 32;

  // Wrapper that calls g_message_received_callback converting from the compiler
  // defined calling convention (e.g cdecl) to SWIGSTCALL.
  static void MessageReceived(void* message) {
    if (g_message_received_callback) {
      // Copy the message so that it can be owned and cleaned up by the C#
      // proxy object.
      Message *copy = new Message();
      *copy = *reinterpret_cast<Message*>(message);
      // If the callback didn't take ownership of the message, delete it.
      if (!g_message_received_callback(copy)) {
        delete copy;
      }
    }
  }

  // Wrapper that calls g_message_token_callback converting from the compiler
  // defined calling convention (e.g cdecl) to SWIGSTCALL.
  static void TokenReceived(const char* token) {
    if (g_token_received_callback) {
      g_token_received_callback(SWIG_csharp_string_callback(token));
    }
  }

  // Mutex which controls access to queues and callback pointers in this class.
  static Mutex g_mutex;
  // Currently allocated listener.
  static ListenerImpl *g_listener;
  // Functions initialized
  static MessageReceivedCallback g_message_received_callback;
  static TokenReceivedCallback g_token_received_callback;
  // Set to false if messages / tokens should be queued.
  static bool g_message_callback_enabled;
  static bool g_token_callback_enabled;
};

Mutex ListenerImpl::g_mutex;
ListenerImpl *ListenerImpl::g_listener = nullptr;
ListenerImpl::MessageReceivedCallback
    ListenerImpl::g_message_received_callback = nullptr;
ListenerImpl::TokenReceivedCallback
    ListenerImpl::g_token_received_callback = nullptr;
bool ListenerImpl::g_message_callback_enabled = false;
bool ListenerImpl::g_token_callback_enabled = false;

// Sets callbacks on ListenerImpl via ListenerImpl::SetCallbacks.
// This is in the firebase::messaging to simplify calling this method from C#.
void SetListenerCallbacks(
    ListenerImpl::MessageReceivedCallback message_callback,
    ListenerImpl::TokenReceivedCallback token_callback) {
  ListenerImpl::SetCallbacks(message_callback, token_callback);
}

// Enables / disables listener callbacks on ListenerImpl.  When a callback
// is disabled, events are queued in the ListenerImpl.
void SetListenerCallbacksEnabled(bool message_callback_enabled,
                                 bool token_callback_enabled) {
  ListenerImpl::SetCallbacksEnabled(message_callback_enabled,
                                    token_callback_enabled);
}

// Send any events queued in the listener.
void SendPendingEvents() {
  ListenerImpl::SendPendingEvents();
}

// Copy the notification out of a message.
void* MessageCopyNotification(void* message) {
  firebase::messaging::Notification *notification =
      static_cast<firebase::messaging::Message*>(message)->notification;
  firebase::messaging::Notification *notification_copy = nullptr;
  if (notification) {
    notification_copy = new firebase::messaging::Notification;
    *notification_copy = *notification;
  }
  return notification_copy;
}

// Copy the notification out of a message.
void* NotificationCopyAndroidNotificationParams(void* notification) {
  firebase::messaging::AndroidNotificationParams *android =
      static_cast<firebase::messaging::Notification*>(notification)->android;
  firebase::messaging::AndroidNotificationParams *android_copy = nullptr;
  if (android) {
    android_copy = new firebase::messaging::AndroidNotificationParams;
    *android_copy = *android;
  }
  return android_copy;
}

}  // messaging
}  // firebase
%}

%pragma(csharp) modulecode=%{
  // Forwards message and token received events from ListenerImpl to
  // Listener.MessageReceivedDelegateMethod and
  // Listener.TokenReceivedDelegateMethod respectively.
  internal class Listener : System.IDisposable {
    // Delegate called from ListenerImpl::MessageReceivedCallback().
    internal delegate int MessageReceivedDelegate(System.IntPtr message);
    // Delegate called from ListenerImpl::TokenReceivedCallback().
    internal delegate void TokenReceivedDelegate(string token);

    // Create delegate instances that are connected to a C/C++ compatible
    // methods.
    private MessageReceivedDelegate messageReceivedDelegate =
        new MessageReceivedDelegate(MessageReceivedDelegateMethod);
    private TokenReceivedDelegate tokenReceivedDelegate =
        new TokenReceivedDelegate(TokenReceivedDelegateMethod);
    // Hold a reference to the default app until this object is finalized.
    private FirebaseApp app = Firebase.FirebaseApp.DefaultInstance;

    // Reference to the listener instance.
    private static Listener listener;

    // Create the listener instance if it doesn't exist.
    internal static Listener Create() {
      lock (typeof(Listener)) {
        if (listener != null) return listener;
        listener = new Listener();
        return listener;
      }
    }

    // Destroy the listener if it exists.
    internal static void Destroy() {
      lock (typeof(Listener)) {
        if (listener == null) return;
        listener.Dispose();
      }
    }

    // Get the app associated with the listener.
    internal static FirebaseApp App {
      get {
        lock (typeof(Listener)) {
          return (listener != null) ? listener.app : null;
        }
      }
    }

    // Setup callbacks from ListenerImpl C++ class to this object.
    private Listener() {
      FirebaseMessaging.SetListenerCallbacks(messageReceivedDelegate,
                                             tokenReceivedDelegate);
    }

    ~Listener() { Dispose(); }

    // Disconnect callbacks from ListenerImpl from this object.
    public void Dispose() {
      lock (typeof(Listener)) {
        if (listener == this) {
          System.Diagnostics.Debug.Assert(app != null);
          FirebaseMessaging.SetListenerCallbacks(null, null);
          listener = null;
          app = null;
        }
      }
    }

    // Called from ListenerImpl::MessageReceived() via the
    // messageReceivedDelegate.
    [MonoPInvokeCallback(typeof(MessageReceivedDelegate))]
    private static int MessageReceivedDelegateMethod(System.IntPtr message) {
      return ExceptionAggregator.Wrap(() => {
          // Use a local copy so another thread cannot unset this before we use it.
          var handler = FirebaseMessaging.MessageReceivedInternal;
          if (handler != null) {
            handler(null, new Firebase.Messaging.MessageReceivedEventArgs(
                new FirebaseMessage(message, true)));
            return 1;
          }
          return 0;
        }, 0);
    }

    // Called from ListenerImpl::TokenReceived() via the
    // tokenReceivedDelegate.
    [MonoPInvokeCallback(typeof(TokenReceivedDelegate))]
    private static void TokenReceivedDelegateMethod(string token) {
      ExceptionAggregator.Wrap(() => {
          // Use a local copy so another thread cannot unset this before we use it.
          var handler = FirebaseMessaging.TokenReceivedInternal;
          if (handler != null) {
            handler(null, new Firebase.Messaging.TokenReceivedEventArgs(token));
          }
        });
    }
  }

  // Backing store for messaging events.
  internal static event System.EventHandler<MessageReceivedEventArgs>
    MessageReceivedInternal;
  internal static event System.EventHandler<TokenReceivedEventArgs>
    TokenReceivedInternal;

  // Create the listener if messaging events are set and pump the message queue
  // of an existing listener, otherwise destroy it.
  internal static void CreateOrDestroyListener() {
    lock (typeof(Listener)) {
      bool messageReceivedSet = MessageReceivedInternal != null;
      bool tokenReceivedSet = TokenReceivedInternal != null;
      if (messageReceivedSet || tokenReceivedSet) {
        Listener.Create();
      } else {
        Listener.Destroy();
      }
      FirebaseMessaging.SetListenerCallbacksEnabled(
          messageReceivedSet, tokenReceivedSet);
      if (messageReceivedSet || tokenReceivedSet) {
        FirebaseMessaging.SendPendingEvents();
      }
    }
  }

  // Reference to the listener instance.
  private static Listener listener;

  // Create the listener and hold a reference.
  static FirebaseMessaging() {
    listener = Listener.Create();
  }

  /// Get the app used by this module.
  /// @return FirebaseApp instance referenced by this module.
  static Firebase.FirebaseApp App { get { return Listener.App; } }

  private FirebaseMessaging() { }

  /// Enable or disable token registration during initialization of Firebase
  /// Cloud Messaging.
  ///
  /// This token is what identifies the user to Firebase, so disabling this
  /// avoids creating any new identity and automatically sending it to Firebase,
  /// unless consent has been granted.
  ///
  /// If this setting is enabled, it triggers the token registration refresh
  /// immediately. This setting is persisted across app restarts and overrides
  /// the setting "firebase_messaging_auto_init_enabled" specified in your
  /// Android manifest (on Android) or Info.plist (on iOS and tvOS).
  ///
  /// <p>By default, token registration during initialization is enabled.
  ///
  /// The registration happens before you can programmatically disable it, so
  /// if you need to change the default, (for example, because you want to
  /// prompt the user before FCM generates/refreshes a registration token on app
  /// startup), add to your application’s manifest:
  ///
  /// @if NOT_DOXYGEN
  ///   <meta-data android:name="firebase_messaging_auto_init_enabled"
  ///   android:value="false" />
  /// @else
  /// @code
  ///   &lt;meta-data android:name="firebase_messaging_auto_init_enabled"
  ///   android:value="false" /&gt;
  /// @endcode
  /// @endif
  ///
  /// or on iOS or tvOS to your Info.plist:
  ///
  /// @if NOT_DOXYGEN
  ///   <key>FirebaseMessagingAutoInitEnabled</key>
  ///   <false/>
  /// @else
  /// @code
  ///   &lt;key&gt;FirebaseMessagingAutoInitEnabled&lt;/key&gt;
  ///   &lt;false/&gt;
  /// @endcode
  /// @endif
  public static bool TokenRegistrationOnInitEnabled {
    get {
      return FirebaseMessaging.IsTokenRegistrationOnInitEnabledInternal();
    }
    set {
      FirebaseMessaging.SetTokenRegistrationOnInitEnabledInternal(value);
    }
  }

  /// Enables or disables Firebase Cloud Messaging message delivery metrics
  /// export to BigQuery.
  ///
  /// By default, message delivery metrics are not exported to BigQuery. Use
  /// this method to enable or disable the export at runtime. In addition, you
  /// can enable the export by adding to your manifest. Note that the run-time
  /// method call will override the manifest value.
  ///
  /// @code
  /// <meta-data android:name= "delivery_metrics_exported_to_big_query_enabled"
  ///            android:value="true"/>
  /// @endcode
  ///
  /// @note This function is currently only implemented on Android, and has no
  /// behavior on other platforms.
  public static bool DeliveryMetricsExportedToBigQueryEnabled {
    get {
      return FirebaseMessaging.DeliveryMetricsExportToBigQueryEnabledInternal();
    }
    set {
      FirebaseMessaging.SetDeliveryMetricsExportToBigQueryInternal(value);
    }
  }

  /// @brief This creates a Firebase Installations ID, if one does not exist, and
  /// sends information about the application and the device where it's running to
  /// the Firebase backend.
  ///
  /// @return A task with the token.
  public static System.Threading.Tasks.Task<string> GetTokenAsync() {
    return FirebaseMessaging.GetTokenInternalAsync();
  }

  /// @brief Deletes the default token for this Firebase project.
  ///
  /// Note that this does not delete the Firebase Installations ID that may have
  /// been created when generating the token. See Installations.Delete() for
  /// deleting that.
  ///
  /// @return A task that completes when the token is deleted.
  public static System.Threading.Tasks.Task DeleteTokenAsync() {
    return FirebaseMessaging.DeleteTokenInternalAsync();
  }

  // NOTE: MessageReceivedEventArgs is defined in
  // firebase/messaging/client/unity/src/MessagingEventArgs.cs.
#if DOXYGEN
  /// Called on the client when a message arrives.
  public static event System.EventHandler<MessageReceivedEventArgs>
      MessageReceived;
#else
  public static event System.EventHandler<MessageReceivedEventArgs>
      MessageReceived {
    add {
      lock (typeof(Listener)) {
        MessageReceivedInternal += value;
        CreateOrDestroyListener();
      }
    }
    remove {
      lock (typeof(Listener)) {
        MessageReceivedInternal -= value;
        CreateOrDestroyListener();
      }
    }
  }
#endif  // DOXYGEN

  // NOTE: TokenReceivedEventArgs is defined in
  // firebase/messaging/client/unity/src/MessagingEventArgs.cs.
#if DOXYGEN
  /// Called on the client when a registration token message arrives.
  public static event System.EventHandler<TokenReceivedEventArgs>
      TokenReceived;
#else
  public static event System.EventHandler<TokenReceivedEventArgs>
      TokenReceived {
    add {
      lock (typeof(Listener)) {
        TokenReceivedInternal += value;
        CreateOrDestroyListener();
      }
    }
    remove {
      lock (typeof(Listener)) {
        TokenReceivedInternal -= value;
        CreateOrDestroyListener();
      }
    }
  }
#endif  // DOXYGEN

%}

%typemap(cscode) firebase::messaging::Message %{
  /// The Time To Live (TTL) for the message.
  ///
  /// This field is only used for downstream messages received through
  /// FirebaseMessaging.MessageReceived().
  public System.TimeSpan TimeToLive {
    get {
      return System.TimeSpan.FromSeconds(TimeToLiveInternal);
    }
  }

  private static System.DateTime UnixEpochUtc =
    new System.DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);

  /// UTC time when the message was sent.
  internal System.DateTime SentTime {
    get {
      return UnixEpochUtc.AddMilliseconds(SentTimeInternal);
    }
  }

  /// Optional notification to show. This only set if a notification was
  /// received with this message, otherwise it is null.
  ///
  /// This field is only used for downstream messages received through
  /// FirebaseMessaging.MessageReceived.
  public FirebaseNotification Notification {
    get {
      System.IntPtr cPtr =
          FirebaseMessaging.MessageCopyNotification(swigCPtr.Handle);
      if (cPtr != System.IntPtr.Zero) {
        return new FirebaseNotification(cPtr, true);
      }
      return null;
    }
  }

  /// The link into the app from the message.
  ///
  /// This field is only used for downstream messages.
  public System.Uri Link {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(LinkInternal);
    }
  }

  /// Gets the binary payload. For webpush and non-json messages, this is the
  /// body of the request entity.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public byte[] RawData {
    get {
      byte[] array = new byte[RawDataInternal.Count];
      RawDataInternal.CopyTo(array);
      return array;
    }
  }
%}

%typemap(cscode) firebase::messaging::Notification %{
  /// Android-specific data to show.
  public AndroidNotificationParams Android {
    get {
      System.IntPtr cPtr =
          FirebaseMessaging.NotificationCopyAndroidNotificationParams(
              swigCPtr.Handle);
      if (cPtr != System.IntPtr.Zero) {
        return new AndroidNotificationParams(cPtr, true);
      }
      return null;
    }
  }
%}

%typemap(csclassmodifiers) firebase::messaging::Message
   "public sealed class";
%rename(FirebaseMessage) firebase::messaging::Message;
%typemap(csclassmodifiers) firebase::messaging::Notification
   "public sealed class";
%rename(FirebaseNotification) firebase::messaging::Notification;

%rename(IsTokenRegistrationOnInitEnabledInternal)
    IsTokenRegistrationOnInitEnabled;
%rename(SetTokenRegistrationOnInitEnabledInternal)
    SetTokenRegistrationOnInitEnabled;

%rename(DeliveryMetricsExportToBigQueryEnabledInternal)
    DeliveryMetricsExportToBigQueryEnabled;
%rename(SetDeliveryMetricsExportToBigQueryInternal)
    SetDeliveryMetricsExportToBigQuery;

%rename(GetTokenInternalAsync)
    GetToken;
%rename(DeleteTokenInternalAsync)
    DeleteToken;

%typemap(csclassmodifiers) firebase::messaging::MessagingOptions
   "public sealed class";
%rename(SuppressNotificationPermissionPrompt)
    suppress_notification_permission_prompt;

%csmethodmodifiers firebase::messaging::IsTokenRegistrationOnInitEnabled()
    "internal"
%csmethodmodifiers firebase::messaging::SetTokenRegistrationOnInitEnabled(bool)
    "internal"

%csmethodmodifiers firebase::messaging::DeliveryMetricsExportToBigQueryEnabled()
    "internal"
%csmethodmodifiers firebase::messaging::SetDeliveryMetricsExportToBigQuery(bool)
    "internal"

%csmethodmodifiers firebase::messaging::GetToken()
    "internal"
%csmethodmodifiers firebase::messaging::DeleteToken()
    "internal"

// Messaging has a lot of read-only properties, so make all immutable
// and call out the mutable ones.
%immutable;
%feature("immutable","0") firebase::messaging::Message::data;
%feature("immutable","0") firebase::messaging::Message::message_id;
%feature("immutable","0") firebase::messaging::Message::to;
%feature("immutable","0") firebase::messaging::MessagingOptions::suppress_notification_permission_prompt;

// The following docs are all here instead of the header due to b/35780150.

%csmethodmodifiers firebase::messaging::Message::from "
  /// Gets the authenticated ID of the sender. This is a project number in most cases.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::to "
  /// Gets or sets recipient of a message.
  ///
  /// For example it can be a registration token, a topic name, a IID or project
  /// ID.
  ///
  /// This field is used for both upstream messages sent with
  /// firebase::messaging:Send() and downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event. For upstream messages,
  /// PROJECT_ID@gcm.googleapis.com or the more general IID format are accepted.
  public"

%csmethodmodifiers firebase::messaging::Message::collapse_key "
  /// Gets the collapse key used for collapsible messages.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::data "
  /// Gets or sets the metadata, including all original key/value pairs.
  /// Includes some of the HTTP headers used when sending the message. `gcm`,
  /// `google` and `goog` prefixes are reserved for internal use.
  ///
  /// This field is used for both upstream messages sent with
  /// firebase::messaging::Send() and downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::message_id "
  /// Gets or sets the message ID. This can be specified by sender. Internally a
  /// hash of the message ID and other elements will be used for storage. The ID
  /// must be unique for each topic subscription - using the same ID may result
  /// in overriding the original message or duplicate delivery.
  ///
  /// This field is used for both upstream messages sent with
  /// firebase::messaging::Send() and downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::message_type "
  /// Gets the message type, equivalent with a content-type.
  /// CCS uses \"ack\", \"nack\" for flow control and error handling.
  /// \"control\" is used by CCS for connection control.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::priority "
  /// Gets the priority level. Defined values are \"normal\" and \"high\".
  /// By default messages are sent with normal priority.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::time_to_live "
  /// Gets the time to live, in seconds.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::error "
  /// Gets the error code. Used in \"nack\" messages for CCS, and in responses
  /// from the server.
  /// See the CCS specification for the externally-supported list.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::error_description "
  /// Gets the human readable details about the error.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::notification "
  /// Gets the optional notification to show. This only set if a notification
  /// was received with this message, otherwise it is null.
  ///
  /// The notification is only guaranteed to be valid during the call to the
  /// @ref FirebaseMessaging.MessageReceived event. If you need to keep it
  /// around longer you will need to make a copy of either the FirebaseMessage
  /// or FirebaseNotification. Copying the FirebaseMessage object implicitly
  /// makes a deep copy of the FirebaseNotification which is owned by the
  /// FirebaseMessage.Message.
  ///
  /// This field is only used for downstream messages received through the
  /// @ref FirebaseMessaging.MessageReceived event.
  public"

%csmethodmodifiers firebase::messaging::Message::notification_opened "
  /// Gets a flag indicating whether this message was opened by tapping a
  /// notification in the OS system tray. If the message was received this way
  /// this flag is set to true.
  public"

// Make snake_case properties CamelCase.
// Message
%rename(CollapseKey) collapse_key;
%rename(Data) data;
%rename(Error) error;
%rename(ErrorDescription) error_description;
%rename(From) from;
%rename(MessageId) message_id;
%rename(MessageType) message_type;
%rename(Priority) priority;
%rename(RawDataInternal) raw_data;
%rename(NotificationOpened) notification_opened;
%csmethodmodifiers firebase::messaging::Message::time_to_live "internal";
%rename(TimeToLiveInternal) time_to_live;
%rename(SentTimeInternal) sent_time;
// Hide the string link field as we expose it as a URI in the C# interface.
%rename(LinkInternal) link;
%csmethodmodifiers firebase::messaging::Message::link "internal";
// Notification is manually copied out of the Message so the proxy doesn't
// reference memory in firebase::messaging::Message.
%ignore firebase::messaging::Message::notification;
// AndroidNotificationParams is manually copied out of the Notification so the
// proxy doesn't reference memory in firebase::messaging::Notification.
%ignore firebase::messaging::Notification::android;
%rename(To) to;
// No reason to export the PollableListener to C#
%ignore firebase::messaging::PollableListener;

// The following docs are all here instead of the header due to b/35780150.

%csmethodmodifiers firebase::messaging::Notification::title "
  /// Indicates notification title. This field is not visible on tvOS, iOS
  /// phones and tablets.
  public"

%csmethodmodifiers firebase::messaging::Notification::body "
  /// Indicates notification body text.
  public"

%csmethodmodifiers firebase::messaging::Notification::icon "
  /// Indicates notification icon. Sets value to myicon for drawable resource
  /// myicon.
  public"

%csmethodmodifiers firebase::messaging::Notification::sound "
  /// Indicates a sound to play when the device receives the notification.
  /// Supports default, or the filename of a sound resource bundled in the
  /// app.
  ///
  /// Android sound files must reside in /res/raw/, while tvOS and iOS sound
  /// files can be in the main bundle of the client app or in the
  /// Library/Sounds folder of the app’s data container.
  public"

%csmethodmodifiers firebase::messaging::Notification::badge "
  /// Indicates the badge on the client app home icon. iOS and tvOS only.
  public"

%csmethodmodifiers firebase::messaging::Notification::tag "
  /// Indicates whether each notification results in a new entry in the
  /// notification drawer on Android. If not set, each request creates a new
  /// notification. If set, and a notification with the same tag is already
  /// being shown, the new notification replaces the existing one in the
  /// notification drawer.
  public"

%csmethodmodifiers firebase::messaging::Notification::color "
  /// Indicates color of the icon, expressed in \#rrggbb format. Android only.
  public"

%csmethodmodifiers firebase::messaging::Notification::click_action "
  /// The action associated with a user click on the notification.
  ///
  /// On Android, if this is set, an activity with a matching intent filter is
  /// launched when user clicks the notification.
  ///
  /// If set on iOS or tvOS, corresponds to category in APNS payload.
  public"

%csmethodmodifiers firebase::messaging::Notification::body_loc_key "
  /// Indicates the key to the body string for localization.
  ///
  /// On iOS and tvOS, this corresponds to \"loc-key\" in APNS payload.
  ///
  /// On Android, use the key in the app's string resources when populating this
  /// value.
  public"

%csmethodmodifiers firebase::messaging::Notification::body_loc_args "
  /// Indicates the string value to replace format specifiers in body string
  /// for localization.
  ///
  /// On iOS and tvOS, this corresponds to \"loc-args\" in APNS payload.
  ///
  /// On Android, these are the format arguments for the string resource. For
  /// more information, see [Formatting strings][1].
  ///
  /// [1]:
  /// https://developer.android.com/guide/topics/resources/string-resource.html#FormattingAndStyling
  public"

%csmethodmodifiers firebase::messaging::Notification::title_loc_key "
  /// Indicates the key to the title string for localization.
  ///
  /// On iOS and tvOS, this corresponds to \"title-loc-key\" in APNS payload.
  ///
  /// On Android, use the key in the app's string resources when populating this
  /// value.
  public"

%csmethodmodifiers firebase::messaging::Notification::title_loc_args "
  /// Indicates the string value to replace format specifiers in title string
  /// for localization.
  ///
  /// On iOS and tvOS, this corresponds to \"title-loc-args\" in APNS payload.
  ///
  /// On Android, these are the format arguments for the string resource. For
  /// more information, see [Formatting strings][1].
  ///
  /// [1]:
  /// https://developer.android.com/guide/topics/resources/string-resource.html#FormattingAndStyling
  public"


// Notification
%rename(Badge) firebase::messaging::Notification::badge;
%rename(Body) firebase::messaging::Notification::body;
%rename(BodyLocalizationArgs) firebase::messaging::Notification::body_loc_args;
%rename(BodyLocalizationKey) firebase::messaging::Notification::body_loc_key;
%rename(ClickAction) firebase::messaging::Notification::click_action;
%rename(Color) firebase::messaging::Notification::color;
%rename(Icon) firebase::messaging::Notification::icon;
%rename(Sound) firebase::messaging::Notification::sound;
%rename(Tag) firebase::messaging::Notification::tag;
%rename(Title) firebase::messaging::Notification::title;
%rename(TitleLocalizationArgs) firebase::messaging::Notification::title_loc_args;
%rename(TitleLocalizationKey) firebase::messaging::Notification::title_loc_key;

// AndroidNotificationParams
%rename(ChannelId) firebase::messaging::AndroidNotificationParams::channel_id;

%ignore firebase::messaging::Listener;
%ignore firebase::messaging::Initialize;
%ignore firebase::messaging::Terminate;
%ignore firebase::messaging::SetListener;

%include "messaging/src/include/firebase/messaging.h"

// Map callback function types to delegates.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::MessageReceivedCallback,
    Firebase.Messaging.FirebaseMessaging.Listener.MessageReceivedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::TokenReceivedCallback,
    Firebase.Messaging.FirebaseMessaging.Listener.TokenReceivedDelegate)

namespace firebase {
namespace messaging {
%csmethodmodifiers SetListenerCallbacks "private";
void SetListenerCallbacks(
    firebase::messaging::ListenerImpl::MessageReceivedCallback messageCallback,
    firebase::messaging::ListenerImpl::TokenReceivedCallback tokenCallback);
%csmethodmodifiers SetListenerCallbacksEnabled "private";
void SetListenerCallbacksEnabled(bool message_callback_enabled,
                                 bool token_callback_enabled);
%csmethodmodifiers SendPendingEvents "private";
void SendPendingEvents();
%csmethodmodifiers MessageCopyNotification "internal";
void* MessageCopyNotification(void* message);
%csmethodmodifiers NotificationCopyAndroidNotificationParams "internal";
void* NotificationCopyAndroidNotificationParams(void* notification);

}  // messaging
}  // firebase
