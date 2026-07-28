// Copyright 2016 Google Inc. All Rights Reserved.
//
// The messaging wrapper is just like the C++ wrapper, except works with c#
// strings. To interface with the map in the C++ wrapper, there are explicit
// calls, as you might expect for interacting with a map like object in C#, such
// as enumerable iteration. It does not convert to C# dictionary types.
//
// Swig Lint doesn't support C#
//swiglint: disable

%module FirebaseMessagingInternal

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

// Change the default class modifier to internal, so that new classes are not accidentally exposed
%typemap(csclassmodifiers) SWIGTYPE "internal class"

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

// Start of the code added to the C++ module file
%{
#include <queue>
#include <string>

#include "app/src/callback.h"
#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/log.h"
#include "messaging/src/include/firebase/messaging.h"

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
  typedef bool (SWIGSTDCALL *MessageReceivedCallback)(void* message);
  // Function which is used to reference a C# delegate which is called from
  // OnTokenReceived() callback in this class.  SWIGSTDCALL is used here as C#
  // delegates *must* be called using the stdcall calling convention rather
  // than whatever the compiler defines.
  typedef void (SWIGSTDCALL *TokenReceivedCallback)(char* token);
  // Function which is used to reference a C# delegate which is called from
  // OnRegistrationReceived() callback in this class.
  typedef void (SWIGSTDCALL *RegistrationReceivedCallback)(char* installation_id);
  // Function which is used to reference a C# delegate which is called from
  // OnUnregistrationReceived() callback in this class.
  typedef void (SWIGSTDCALL *UnregistrationReceivedCallback)(char* installation_id);

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

  // Receives registration from the messaging module and forwards it to the
  // callback queue which will eventually result in a call to
  // RegistrationReceived on the main thread.
  virtual void OnRegistrationReceived(const char* installation_id) {
    QueueItem(&registrations_, std::string(installation_id));
    firebase::LogDebug("queued registration %s", installation_id);
    SendPendingEvents();
  }

  // Receives unregistration from the messaging module and forwards it to the
  // callback queue which will eventually result in a call to
  // UnregistrationReceived on the main thread.
  virtual void OnUnregistrationReceived(const char* installation_id) {
    QueueItem(&unregistrations_, std::string(installation_id));
    firebase::LogDebug("queued unregistration %s", installation_id);
    SendPendingEvents();
  }

  // Send any pending events on the currently allocated listener.
  static void SendPendingEvents() {
    MutexLock lock(g_mutex);
    if (g_listener) g_listener->SendQueuedEventsIfEnabled();
  }

  // Configures methods that call C# delegates when OnMessageReceived(),
  // OnTokenReceived(), OnRegistrationReceived(), and OnUnregistrationReceived()
  // methods of the listener are called.
  // When callbacks are specified a listener is created, if all arguments
  // are null the listener is destroyed.
  static void SetCallbacks(MessageReceivedCallback messageCallback,
                           TokenReceivedCallback tokenCallback,
                           RegistrationReceivedCallback registrationCallback,
                           UnregistrationReceivedCallback unregistrationCallback) {
    MutexLock lock(g_mutex);
    ListenerImpl *new_listener = (messageCallback || tokenCallback ||
                                  registrationCallback || unregistrationCallback) ?
        new ListenerImpl : nullptr;
    Listener *previous_listener = SetListener(new_listener);
    g_message_received_callback = messageCallback;
    g_token_received_callback = tokenCallback;
    g_registration_received_callback = registrationCallback;
    g_unregistration_received_callback = unregistrationCallback;
    if (previous_listener) delete previous_listener;
    g_listener = new_listener;
  }

  // Enables / disables listener callbacks.  When a callback is disabled,
  // events are queued in the ListenerImpl.
  static void SetCallbacksEnabled(bool message_callback_enabled,
                                  bool token_callback_enabled,
                                  bool registration_callback_enabled,
                                  bool unregistration_callback_enabled) {
    g_message_callback_enabled = message_callback_enabled;
    g_token_callback_enabled = token_callback_enabled;
    g_registration_callback_enabled = registration_callback_enabled;
    g_unregistration_callback_enabled = unregistration_callback_enabled;
  }

 private:
  // Send any queued events (messages, tokens, registrations, unregistrations)
  // if the queues are enabled.
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
    if (g_registration_callback_enabled) {
      while (registrations_.size()) {
        const std::string &installation_id = registrations_.front();
        firebase::LogDebug("sending registration %s", installation_id.c_str());
        firebase::callback::AddCallback(
            new firebase::callback::CallbackString(
                installation_id.c_str(), RegistrationReceived));
        registrations_.pop();
      }
    }
    if (g_unregistration_callback_enabled) {
      while (unregistrations_.size()) {
        const std::string &installation_id = unregistrations_.front();
        firebase::LogDebug("sending unregistration %s", installation_id.c_str());
        firebase::callback::AddCallback(
            new firebase::callback::CallbackString(
                installation_id.c_str(), UnregistrationReceived));
        unregistrations_.pop();
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
  // Cache of registration IDs that are sent to the C# proxy class when a
  // registration event handler is set.
  std::queue<std::string> registrations_;
  // Cache of unregistration IDs that are sent to the C# proxy class when an
  // unregistration event handler is set.
  std::queue<std::string> unregistrations_;
  // Maximum number of items to queue in the event queues.
  static const size_t kMaxQueueSize = 32;

  // Wrapper that calls g_message_received_callback converting from the compiler
  // defined calling convention (e.g cdecl) to SWIGSTCALL.
  static void MessageReceived(void* message) {
    if (g_message_received_callback) {
      // Copy the message so that it can be owned and cleaned up by the C#
      // proxy object.
      Message* original = reinterpret_cast<Message*>(message);
      Message* copy = new Message();
      *copy = *original;
      // If there is a notification, create a copy of that too
      if (original->notification) {
        copy->notification = new Notification();
        *copy->notification = *original->notification;

        // Then, if there is an AndroidNotificationParams, we need to copy that too
        if (original->notification->android) {
          copy->notification->android = new AndroidNotificationParams();
          *copy->notification->android = *original->notification->android;
        }
      }
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

  // Wrapper that calls g_message_token_callback converting from the compiler
  // defined calling convention (e.g cdecl) to SWIGSTCALL.
  static void RegistrationReceived(const char* installation_id) {
    if (g_registration_received_callback) {
      g_registration_received_callback(SWIG_csharp_string_callback(installation_id));
    }
  }

  static void UnregistrationReceived(const char* installation_id) {
    if (g_unregistration_received_callback) {
      g_unregistration_received_callback(SWIG_csharp_string_callback(installation_id));
    }
  }

  // Mutex which controls access to queues and callback pointers in this class.
  static Mutex g_mutex;
  // Currently allocated listener.
  static ListenerImpl *g_listener;
  // Functions initialized
  static MessageReceivedCallback g_message_received_callback;
  static TokenReceivedCallback g_token_received_callback;
  static RegistrationReceivedCallback g_registration_received_callback;
  static UnregistrationReceivedCallback g_unregistration_received_callback;
  // Set to false if events should be queued.
  static bool g_message_callback_enabled;
  static bool g_token_callback_enabled;
  static bool g_registration_callback_enabled;
  static bool g_unregistration_callback_enabled;
};

Mutex ListenerImpl::g_mutex;
ListenerImpl *ListenerImpl::g_listener = nullptr;
ListenerImpl::MessageReceivedCallback
    ListenerImpl::g_message_received_callback = nullptr;
ListenerImpl::TokenReceivedCallback
    ListenerImpl::g_token_received_callback = nullptr;
ListenerImpl::RegistrationReceivedCallback
    ListenerImpl::g_registration_received_callback = nullptr;
ListenerImpl::UnregistrationReceivedCallback
    ListenerImpl::g_unregistration_received_callback = nullptr;
bool ListenerImpl::g_message_callback_enabled = false;
bool ListenerImpl::g_token_callback_enabled = false;
bool ListenerImpl::g_registration_callback_enabled = false;
bool ListenerImpl::g_unregistration_callback_enabled = false;

// Sets callbacks on ListenerImpl via ListenerImpl::SetCallbacks.
// This is in the firebase::messaging to simplify calling this method from C#.
void SetListenerCallbacks(
    ListenerImpl::MessageReceivedCallback message_callback,
    ListenerImpl::TokenReceivedCallback token_callback,
    ListenerImpl::RegistrationReceivedCallback registration_callback,
    ListenerImpl::UnregistrationReceivedCallback unregistration_callback) {
  ListenerImpl::SetCallbacks(message_callback, token_callback,
                             registration_callback, unregistration_callback);
}

// Enables / disables listener callbacks on ListenerImpl.  When a callback
// is disabled, events are queued in the ListenerImpl.
void SetListenerCallbacksEnabled(bool message_callback_enabled,
                                 bool token_callback_enabled,
                                 bool registration_callback_enabled,
                                 bool unregistration_callback_enabled) {
  ListenerImpl::SetCallbacksEnabled(message_callback_enabled,
                                    token_callback_enabled,
                                    registration_callback_enabled,
                                    unregistration_callback_enabled);
}

// Send any events queued in the listener.
void SendPendingEvents() {
  ListenerImpl::SendPendingEvents();
}

}  // messaging
}  // firebase

%}  // End of code added to the C++ module file

// Start of the code added to the C# module file
%pragma(csharp) modulecode=%{
  // Forwards message and token received events from ListenerImpl to
  // Listener.MessageReceivedDelegateMethod and
  // Listener.TokenReceivedDelegateMethod respectively.
  internal class Listener : System.IDisposable {
    // Delegate called from ListenerImpl::MessageReceivedCallback().
    internal delegate bool MessageReceivedDelegate(System.IntPtr message);
    // Delegate called from ListenerImpl::TokenReceivedCallback().
    internal delegate void TokenReceivedDelegate(string token);
    internal delegate void RegistrationReceivedDelegate(string installationId);
    internal delegate void UnregistrationReceivedDelegate(string installationId);

    // Create delegate instances that are connected to a C/C++ compatible
    // methods.
    private MessageReceivedDelegate messageReceivedDelegate =
        new MessageReceivedDelegate(MessageReceivedDelegateMethod);
    private TokenReceivedDelegate tokenReceivedDelegate =
        new TokenReceivedDelegate(TokenReceivedDelegateMethod);
    private RegistrationReceivedDelegate registrationReceivedDelegate =
        new RegistrationReceivedDelegate(RegistrationReceivedDelegateMethod);
    private UnregistrationReceivedDelegate unregistrationReceivedDelegate =
        new UnregistrationReceivedDelegate(UnregistrationReceivedDelegateMethod);
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
      FirebaseMessagingInternal.SetListenerCallbacks(messageReceivedDelegate,
                                                     tokenReceivedDelegate,
                                                     registrationReceivedDelegate,
                                                     unregistrationReceivedDelegate);
    }

    ~Listener() { Dispose(); }

    // Disconnect callbacks from ListenerImpl from this object.
    public void Dispose() {
      lock (typeof(Listener)) {
        if (listener == this) {
          System.Diagnostics.Debug.Assert(app != null);
          FirebaseMessagingInternal.SetListenerCallbacks(null, null, null, null);
          listener = null;
          app = null;
        }
      }
    }

    // Called from ListenerImpl::MessageReceived() via the
    // messageReceivedDelegate.
    [MonoPInvokeCallback(typeof(MessageReceivedDelegate))]
    private static bool MessageReceivedDelegateMethod(System.IntPtr message) {
      bool tookOwnership = false;
      return ExceptionAggregator.Wrap(() => {
          // Use a local copy so another thread cannot unset this before we use it.
          var handler = FirebaseMessagingInternal.MessageReceivedInternal;
          if (handler != null) {
            // Take ownership, and track it so that the caller of this knows, even
            // if an exception is thrown, since the C# object will still delete it.
            FirebaseMessageInternal messageInternal = new FirebaseMessageInternal(message, true);
            tookOwnership = true;
            handler(null, new Firebase.Messaging.MessageReceivedEventArgs(
                FirebaseMessage.FromInternal(messageInternal)));
            messageInternal.Dispose();
            return true;
          }
          return false;
        }, tookOwnership);
    }

    // Called from ListenerImpl::TokenReceived() via the
    // tokenReceivedDelegate.
    [MonoPInvokeCallback(typeof(TokenReceivedDelegate))]
    private static void TokenReceivedDelegateMethod(string token) {
      ExceptionAggregator.Wrap(() => {
          // Use a local copy so another thread cannot unset this before we use it.
          var handler = FirebaseMessagingInternal.TokenReceivedInternal;
          if (handler != null) {
            handler(null, new Firebase.Messaging.TokenReceivedEventArgs(token));
          }
        });
    }

    [MonoPInvokeCallback(typeof(RegistrationReceivedDelegate))]
    private static void RegistrationReceivedDelegateMethod(string installationId) {
      ExceptionAggregator.Wrap(() => {
          var handler = FirebaseMessagingInternal.RegistrationReceivedInternal;
          if (handler != null) {
            handler(null, new Firebase.Messaging.RegistrationReceivedEventArgs(installationId));
          }
        });
    }

    [MonoPInvokeCallback(typeof(UnregistrationReceivedDelegate))]
    private static void UnregistrationReceivedDelegateMethod(string installationId) {
      ExceptionAggregator.Wrap(() => {
          var handler = FirebaseMessagingInternal.UnregistrationReceivedInternal;
          if (handler != null) {
            handler(null, new Firebase.Messaging.UnregistrationReceivedEventArgs(installationId));
          }
        });
    }
  }

  // Backing store for messaging events.
  internal static event System.EventHandler<MessageReceivedEventArgs>
    MessageReceivedInternal;
  internal static event System.EventHandler<TokenReceivedEventArgs>
    TokenReceivedInternal;
  internal static event System.EventHandler<RegistrationReceivedEventArgs>
    RegistrationReceivedInternal;
  internal static event System.EventHandler<UnregistrationReceivedEventArgs>
    UnregistrationReceivedInternal;

  // Create the listener if messaging events are set and pump the message queue
  // of an existing listener, otherwise destroy it.
  internal static void CreateOrDestroyListener() {
    lock (typeof(Listener)) {
      bool messageReceivedSet = MessageReceivedInternal != null;
      bool tokenReceivedSet = TokenReceivedInternal != null;
      bool registrationReceivedSet = RegistrationReceivedInternal != null;
      bool unregistrationReceivedSet = UnregistrationReceivedInternal != null;
      bool anySet = messageReceivedSet || tokenReceivedSet || registrationReceivedSet || unregistrationReceivedSet;
      if (anySet) {
        Listener.Create();
      } else {
        Listener.Destroy();
      }
      FirebaseMessagingInternal.SetListenerCallbacksEnabled(
          messageReceivedSet, tokenReceivedSet, registrationReceivedSet, unregistrationReceivedSet);
      if (anySet) {
        FirebaseMessagingInternal.SendPendingEvents();
      }
    }
  }

  // Reference to the listener instance.
  private static Listener listener;

  // Create the listener and hold a reference.
  static FirebaseMessagingInternal() {
    listener = Listener.Create();
  }

  /// Get the app used by this module.
  /// @return FirebaseApp instance referenced by this module.
  static Firebase.FirebaseApp App { get { return Listener.App; } }

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

  // NOTE: RegistrationReceivedEventArgs is defined in
  // firebase/messaging/client/unity/src/MessagingEventArgs.cs.
#if DOXYGEN
  /// Called on the client when a registration installation ID arrives or changes.
  public static event System.EventHandler<RegistrationReceivedEventArgs>
      RegistrationReceived;
#else
  public static event System.EventHandler<RegistrationReceivedEventArgs>
      RegistrationReceived {
    add {
      lock (typeof(Listener)) {
        RegistrationReceivedInternal += value;
        CreateOrDestroyListener();
      }
    }
    remove {
      lock (typeof(Listener)) {
        RegistrationReceivedInternal -= value;
        CreateOrDestroyListener();
      }
    }
  }
#endif  // DOXYGEN

  // NOTE: UnregistrationReceivedEventArgs is defined in
  // firebase/messaging/client/unity/src/MessagingEventArgs.cs.
#if DOXYGEN
  /// Called on the client when an unregistration confirmation arrives.
  public static event System.EventHandler<UnregistrationReceivedEventArgs>
      UnregistrationReceived;
#else
  public static event System.EventHandler<UnregistrationReceivedEventArgs>
      UnregistrationReceived {
    add {
      lock (typeof(Listener)) {
        UnregistrationReceivedInternal += value;
        CreateOrDestroyListener();
      }
    }
    remove {
      lock (typeof(Listener)) {
        UnregistrationReceivedInternal -= value;
        CreateOrDestroyListener();
      }
    }
  }
#endif  // DOXYGEN

%}  // End of code added to the C# module file

// Rename the generated classes to *Internal
%rename(FirebaseMessageInternal) firebase::messaging::Message;
%rename(FirebaseNotificationInternal) firebase::messaging::Notification;
%rename(AndroidNotificationParamsInternal) firebase::messaging::AndroidNotificationParams;
%rename(MessagingOptionsInternal) firebase::messaging::MessagingOptions;

// Messaging has a lot of read-only properties, so make all immutable
// and call out the mutable ones.
%immutable;
%feature("immutable","0") firebase::messaging::MessagingOptions::suppress_notification_permission_prompt;

// Ignore functions and classes that we don't need to expose to C#
%ignore firebase::messaging::PollableListener;
%ignore firebase::messaging::Listener;
%ignore firebase::messaging::Initialize;
%ignore firebase::messaging::Terminate;
%ignore firebase::messaging::SetListener;

%include "messaging/src/include/firebase/messaging.h"

// Map callback function types to delegates.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::MessageReceivedCallback,
    Firebase.Messaging.FirebaseMessagingInternal.Listener.MessageReceivedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::TokenReceivedCallback,
    Firebase.Messaging.FirebaseMessagingInternal.Listener.TokenReceivedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::RegistrationReceivedCallback,
    Firebase.Messaging.FirebaseMessagingInternal.Listener.RegistrationReceivedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::messaging::ListenerImpl::UnregistrationReceivedCallback,
    Firebase.Messaging.FirebaseMessagingInternal.Listener.UnregistrationReceivedDelegate)

// Declare the functions we added to the C++ module that we want to generate C# for
namespace firebase {
namespace messaging {
%csmethodmodifiers SetListenerCallbacks "private";
void SetListenerCallbacks(
    firebase::messaging::ListenerImpl::MessageReceivedCallback messageCallback,
    firebase::messaging::ListenerImpl::TokenReceivedCallback tokenCallback,
    firebase::messaging::ListenerImpl::RegistrationReceivedCallback registrationCallback,
    firebase::messaging::ListenerImpl::UnregistrationReceivedCallback unregistrationCallback);
%csmethodmodifiers SetListenerCallbacksEnabled "private";
void SetListenerCallbacksEnabled(bool message_callback_enabled,
                                 bool token_callback_enabled,
                                 bool registration_callback_enabled,
                                 bool unregistration_callback_enabled);
%csmethodmodifiers SendPendingEvents "private";
void SendPendingEvents();

}  // messaging
}  // firebase
