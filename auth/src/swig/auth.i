// Copyright 2016 Google Inc. All Rights Reserved.
//
// This script wraps the C++ Auth API for generating a C# binding which can be
// used inside of Unity. The wrapper is pretty direct. The most heavily modified
// parts are inside the included Future implementation.

%module AuthUtil

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

// SWIG knows when classes are sealed to strip virtual functions and things but
// it doesn't seem to have any magic for static classes, so we have to strip out
// everything it would otherwise generate that we don't actually want.
// For example, we DON'T want constructors, destructors, and cpointer handling,
// nor derived from IDisposable. But we don't just want to hard-code the class
// because we still want a drop-zone for all of the static methods with doxygen
// comments in tact.
%define %STATIC_CLASS(CONSTRUCTOR, CTYPE...)

%typemap(csclassmodifiers) CTYPE "public static class";
%typemap(csbody) CTYPE "";
#if SWIG_VERSION >= 0x040000
%typemap(csdispose) CTYPE "";
%typemap(csdisposing) CTYPE "";
#else
%typemap(csdestruct) CTYPE "";
%typemap(csfinalize) CTYPE "";
#endif
%typemap(csinterfaces) CTYPE "";
%ignore CONSTRUCTOR;

%typemap(cscode) CTYPE %{
  static CONSTRUCTOR {
    LogUtil.InitializeLogging();
  }
%}

%enddef // STATIC_CLASS

%{
#include "app/src/callback.h"
#include "app/src/cleanup_notifier.h"
#include "app/src/log.h"
#include "app/src/mutex.h"
#include "app/memory/shared_ptr.h"
#include "app/src/cpp_instance_manager.h"
#include "auth/src/include/firebase/auth.h"

#include <stdexcept>
#include <algorithm>

#include <assert.h>
%}

%import "app/src/swig/app.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"
%include "stdint.i"

%{
namespace firebase {
namespace auth {
%}

%ignore UserInfoInterface::uid;
%ignore UserInfoInterface::email;
%ignore UserInfoInterface::display_name;
%ignore UserInfoInterface::photo_url;
%ignore UserInfoInterface::provider_id;
%ignore User::uid;
%ignore User::email;
%ignore User::display_name;
%ignore User::photo_url;
%ignore User::provider_id;
%ignore User::GetToken;
%ignore User::GetTokenThreadSafe;
%ignore User::GetTokenLastResult;
%ignore User::provider_data;
%ignore User::is_email_verified;
%ignore User::is_anonymous;
%ignore User::metadata;

%{
// Function pointer type which is equivalent to the
// C# Firebase.Auth.FirebaseAuth.StateChangedDelegate delegate.
typedef void (SWIGSTDCALL* AuthStateChangedDelegateFunc)(App *app);

// Global mutex to protect AuthNotifier::data_
::firebase::Mutex g_auth_notifier_mutex;

// Class which is used by implementations of AuthStateListener and
// AuthTokenListener to forward auth state changes to the C# layer.
class AuthNotifier {
 private:
  // Data passed to the state changed callback.  This is separate from the
  // listener class as the listener can be destroyed by the Auth object while
  // a callback is queued.
  struct CallbackData {
    App *app;
    AuthStateChangedDelegateFunc state_changed_delegate;
    void* callback_reference;
  };

 public:
  // Create a notifier and register it with the specified auth object.
  AuthNotifier(Auth *auth,
               AuthStateChangedDelegateFunc state_changed_delegate) {
    assert(auth);
    assert(state_changed_delegate);
    data_ = ::firebase::MakeShared<CallbackData>();
    data_->app = &auth->app();
    data_->state_changed_delegate = state_changed_delegate;
    data_->callback_reference = nullptr;

    // Remove callback when App is being destroyed.
    CleanupNotifier* notifier = CleanupNotifier::FindByOwner(data_->app);
    assert(notifier);
    notifier->RegisterObject(this, [](void* object) {
      AuthNotifier* notifier = reinterpret_cast<AuthNotifier*>(object);
      notifier->DeleteInternal();
    });
  }

  ~AuthNotifier() {
    DeleteInternal();
  }

  void DeleteInternal() {
    MutexLock lock(g_auth_notifier_mutex);
    if (!data_) return;

    CleanupNotifier* notifier = CleanupNotifier::FindByOwner(data_->app);
    assert(notifier);
    notifier->UnregisterObject(this);

    if (data_->callback_reference) {
      firebase::callback::RemoveCallback(data_->callback_reference);
      data_->callback_reference = nullptr;
    }

    data_.reset();
  }

  // Notify the C# object via the registered delegate of the change.
  void Notify() {
    MutexLock lock(g_auth_notifier_mutex);
    if (!data_) return;

    data_->callback_reference = firebase::callback::AddCallback(
        new firebase::callback::CallbackValue1<::firebase::SharedPtr<CallbackData>>(
            data_, NotifyOnTheMainThread));
  }

 private:
  // Thunk which converts from the current calling convention to SWIGSTDCALL.
  static void NotifyOnTheMainThread(::firebase::SharedPtr<CallbackData> data) {
    {
      ::firebase::MutexLock lock(g_auth_notifier_mutex);
      if (!data->callback_reference) return;
      data->callback_reference = nullptr;
    }
    data->state_changed_delegate(data->app);
  }

 private:
  ::firebase::SharedPtr<CallbackData> data_;
};

// Generate a Auth listener implementation and the methods to instance it.
#define AUTH_LISTENER_IMPL(listener_class_name, listener_base_class,           \
                           notification_method)                                \
  /* Implementation of a Auth listener which is used to forward */             \
  /* auth state / token changes to the C# */                                   \
  class listener_class_name##Impl : public listener_base_class {               \
    public:                                                                    \
      /* Create a listener and register it with the specified auth object. */  \
      listener_class_name##Impl(                                               \
          Auth *auth,                                                          \
          AuthStateChangedDelegateFunc state_changed_delegate) :               \
        notifier_(auth, state_changed_delegate) {                              \
      }                                                                        \
                                                                               \
      /* Remove this listener from the auth object. */                         \
      virtual ~listener_class_name##Impl() {}                                  \
                                                                               \
      /* Override the base class method. */                                    \
      void notification_method(Auth* /* auth */) override {                    \
        notifier_.Notify();                                                    \
      }                                                                        \
                                                                               \
    private:                                                                   \
      AuthNotifier notifier_;                                                  \
  };                                                                           \
                                                                               \
  /* Add a state listener to the specified auth object. */                     \
  /* This returns void* to avoid generating the AuthStateListenerImpl proxy */ \
  /* in C#. */                                                                 \
  static void* Create##listener_class_name(                                    \
      Auth *auth, AuthStateChangedDelegateFunc state_changed_delegate) {       \
    listener_class_name##Impl *listener =                                      \
        new listener_class_name##Impl(auth, state_changed_delegate);           \
    auth->Add##listener_class_name(listener);                                  \
    return listener;                                                           \
  }                                                                            \
                                                                               \
  /* Remove a state listener from it's associated auth object. */              \
  static void Destroy##listener_class_name(Auth *auth, void *listener) {       \
    assert(listener);                                                          \
    listener_class_name##Impl* impl =                                          \
        reinterpret_cast<listener_class_name##Impl*>(listener);                \
    /* Remove listener before deletion just in case vtable being cleared */    \
    /* when the listener is notified. */                                       \
    auth->Remove##listener_class_name(impl);                                   \
    delete impl;                                                               \
  }

AUTH_LISTENER_IMPL(AuthStateListener, AuthStateListener, OnAuthStateChanged);
AUTH_LISTENER_IMPL(IdTokenListener, IdTokenListener, OnIdTokenChanged);

// Reference count manager for C++ Auth instance, using pointer as the key for
// searching.
static CppInstanceManager<Auth> g_auth_instances;

}  // namespace auth
}  // namespace firebase
%}

%rename(FirebaseAuth) firebase::auth::Auth;
// Seal this class.
%typemap(csclassmodifiers) FirebaseAuth "public sealed class";

// All outputs of StringStringMap should instead be IDictionary<string, string>
%typemap(cstype, out="global::System.Collections.Generic.IDictionary<string, string>")
    std::map<std::string, std::string>* "StringStringMap";
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

%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<string>") std::vector<std::string>* "StringList";
%typemap(csin) std::vector<std::string>* "$csclassname.getCPtr(temp$csinput)"
%typemap(csvarin, excode=SWIGEXCODE2) std::vector<std::string>* %{
  set {
    StringList temp$csinput = new StringList();
    foreach (var val in $csinput) {
      temp$csinput.Add(val);
    }
    $imcall;$excode
   }
%}

%csmethodmodifiers firebase::auth::Auth::FederatedAuthProviderData::provider_id "
  public";
%rename(ProviderId) provider_id;

%csmethodmodifiers firebase::auth::Auth::FederatedOAuthProviderData::scopes "
  public";
%rename(Scopes) scopes;
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<string>")
    std::vector< std::string > * "StringList";

%csmethodmodifiers
  firebase::auth::Auth::FederatedOAuthProviderData::custom_parameters "public";
%rename(CustomParameters) custom_parameters;

// This is here, instead of the src because of b/35780150
%csmethodmodifiers firebase::auth::Auth::FetchProvidersResult::providers "
  /// The IDPs (identity providers) that can be used for `email`.
  public";
%immutable firebase::auth::Auth::FetchProvidersResult::providers;
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<string>")
    std::vector< std::string > * "StringList";

// Don't expose UserInfoInterfaceList externally.
%typemap(csclassmodifiers) std::vector<firebase::auth::UserInfoInterface*> "internal class"
%template(UserInfoInterfaceList) std::vector<firebase::auth::UserInfoInterface*>;
// All outputs of UserInfoInterfaceList should be IEnumerable<IUserInfo>
%typemap(cstype,
         out="global::System.Collections.Generic.IEnumerable<IUserInfo>")
  std::vector<firebase::auth::UserInfoInterface*>&
  "UserInfoInterfaceList";
%typemap(csvarout) std::vector<firebase::auth::UserInfoInterface*>& %{
  get {
    // Convert the UserInfoInterfaceList into a List<IUserInfo>, as we don't
    // expose UserInfoInterface, which inherites from the public IUserInfo.
    UserInfoInterfaceList oldList = new UserInfoInterfaceList($imcall, false);
    System.Collections.Generic.List<IUserInfo> newList =
      new System.Collections.Generic.List<IUserInfo>();
    foreach (IUserInfo info in oldList) {
      newList.Add(info);
    }
    return newList;
  }
%}

%SWIG_FUTURE(Future_User, FirebaseUser, internal, firebase::auth::User *,
             FirebaseException)
%SWIG_FUTURE(Future_FetchProvidersResult, FetchProvidersResult, internal,
             firebase::auth::Auth::FetchProvidersResult, FirebaseException)
%SWIG_FUTURE(Future_Credential, Credential, internal,
             firebase::auth::Credential, FirebaseException)

// Custom SignInResult handler for the Future_SignInResult implementation.
// Maps auth specific error codes to Auth specific firebase exceptions.
%define %SWIG_FUTURE_SIGINRESULT_GET_TASK(CSNAME...)
  // Helper for csout typemap to convert futures into tasks.
  // This would be internal, but we need to share it across assemblies.
  static public
    System.Threading.Tasks.Task<SignInResult> GetTask(CSNAME fu)
    {
       System.Threading.Tasks.TaskCompletionSource<SignInResult> tcs =
          new System.Threading.Tasks.TaskCompletionSource<SignInResult>();
      if (fu.status() == FutureStatus.Invalid) {
        tcs.SetException(
          new FirebaseException(0, "Asynchronous operation was not started."));
        return tcs.Task;
      }
      fu.SetOnCompletionCallback(() => {
        try {
          if (fu.status() == FutureStatus.Invalid) {
            /// No result is pending.
            /// FutureBase::Release() or move operator was called.
            tcs.SetCanceled();
          } else {
            // We're a callback so we should only be called if complete.
            System.Diagnostics.Debug.Assert(
                fu.status() != FutureStatus.Complete,
                "Callback triggered but the task is not invalid or complete.");
            int error = fu.error();
            if (error != 0) {
              // check for FirebaseAccountLinkException
              if(error == (int)AuthError.CredentialAlreadyInUse) {
                tcs.SetException(
                  new FirebaseAccountLinkException(error,
                                                   fu.error_message(),
                                                   fu.GetResult()));
              } else {
                // Pass the API specific error code and error message to an
                // exception.
                tcs.SetException(new FirebaseException(error,
                                                       fu.error_message()));
              }
            } else {
              // Success!
              tcs.SetResult(fu.GetResult());
            }
          }
        } catch (System.Exception e) {
          Firebase.LogUtil.LogMessage(
              Firebase.LogLevel.Error,
              System.String.Format(
                  "Internal error while completing task {0}", e));
        }
        fu.Dispose();  // As we no longer need the future, deallocate it.
      });
      return tcs.Task;
    }
%enddef // SWIG_FUTURE_SIGINRESULT_GET_TASK

// Assembles the SignInResult Future handler from the stock Future Handler
// macros defined in future.i and a custom GetTask implementation defined
// above.
%define %SWIG_FUTURE_AUTH_SIGNINRESULT(CSACCESS...)
  %SWIG_FUTURE_HEADER(Future_SignInResult, SignInResult, CSACCESS,
                      firebase::auth::SignInResult)
  %SWIG_FUTURE_SIGINRESULT_GET_TASK(Future_SignInResult)
  %SWIG_FUTURE_FOOTER(Future_SignInResult, SignInResult,
                      firebase::auth::SignInResult)
%enddef

%SWIG_FUTURE_AUTH_SIGNINRESULT(internal)

%csmethodmodifiers FetchProvidersForEmail(const char *email) "internal";
%rename(FetchProvidersForEmailInternal) FetchProvidersForEmail;

%csmethodmodifiers language_code() "internal";
%rename(LanguageCodeInternal) language_code;

%csmethodmodifiers set_language_code(const char *lagnuage_code) "internal";
%rename(SetLanguageCodeInternal) set_language_code;

%include "app/src/swig/init_result.i"

// Implemented inline below.
%ignore firebase::auth::Auth::app;
%ignore firebase::auth::Auth::GetApp;

%ignore firebase::auth::Auth::GetAuth;
%newobject firebase::auth::Auth::GetAuthInternal;

// The following methods need to be overridden to return a customized proxy to
// each C++ object.
%rename(SignInWithCustomTokenInternal)
  firebase::auth::Auth::SignInWithCustomToken;
%rename(SignInWithCredentialInternal)
  firebase::auth::Auth::SignInWithCredential;
%rename(SignInAndRetrieveDataWithCredentialInternal)
  firebase::auth::Auth::SignInAndRetrieveDataWithCredential;
%rename(SignInAnonymouslyInternal)
  firebase::auth::Auth::SignInAnonymously;
%rename(SignInWithEmailAndPasswordInternal)
  firebase::auth::Auth::SignInWithEmailAndPassword;
%rename(CreateUserWithEmailAndPasswordInternal)
  firebase::auth::Auth::CreateUserWithEmailAndPassword;

%rename(SignInWithProviderInternal)
  firebase::auth::Auth::SignInWithProvider;

%extend firebase::auth::Auth {
  // Get a C++ instance and increment the reference count to it
  %csmethodmodifiers GetAuthInternal(App* app, InitResult* init_result_out) "internal";
  static Auth* GetAuthInternal(App* app, InitResult* init_result_out) {
    // This is to protect from the race condition after GetAuth() is
    // called and before the pointer is added to g_auth_instances.
    ::firebase::MutexLock lock(
        ::firebase::auth::g_auth_instances.mutex());

    firebase::auth::Auth* instance = firebase::auth::Auth::GetAuth(
        app, init_result_out);
    ::firebase::auth::g_auth_instances.AddReference(instance);
    return instance;
  }

  // Release and decrement the reference count to a C++ instance
  %csmethodmodifiers ReleaseReferenceInternal(firebase::auth::Auth* instance) "internal";
  static void ReleaseReferenceInternal(firebase::auth::Auth* instance) {
    ::firebase::auth::g_auth_instances.ReleaseReference(instance);
  }
}


%typemap(cscode) firebase::auth::Auth %{
  /// The user-facing language code for auth operations that can be internationalized, such as
  /// FirebaseUser.sendEmailVerification(). This language code should follow the conventions defined
  /// by the IETF in BCP 47.
  public System.String LanguageCode {
    get {
      return LanguageCodeInternal();
    }
    set {
      SetLanguageCodeInternal(value);
    }
  }

  /// Sign-in a user authenticated via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS and Android platforms. On
  /// non-mobile platforms this method will return a Future with a preset error
  /// code: kAuthErrorUnimplemented.
  public System.Threading.Tasks.Task<SignInResult> SignInWithProviderAsync(
      FederatedAuthProvider provider) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<SignInResult>();
    SignInWithProviderInternalAsync(provider).ContinueWith(task => {
        CompleteSignInResultTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }

  // Holds a reference to the FirebaseApp proxy object so that it isn't
  // garbage collected while the application holds a reference to this object.
  private FirebaseApp appProxy;
  // C++ pointer to the firebase::App object, used to lookup this instance
  // in the appCPtrToAuth dictionary on Dispose().
  private System.IntPtr appCPtr;

  // Called by AuthStateListenerImpl::OnAuthStateChanged when the
  // state of this object changes.
  internal delegate void StateChangedDelegate(System.IntPtr authCPtr);

  // Pointer to the AuthStateListenerImpl.
  private System.IntPtr authStateListener;
  // Pointer to IdTokenListenerImpl.
  private System.IntPtr idTokenListener;
  // Proxy for the current user object.
  private FirebaseUser currentUser;

  // Retrieve a reference to the auth object associated with the specified app.
  private static FirebaseAuth ProxyFromAppCPtr(System.IntPtr appCPtr) {
    lock (appCPtrToAuth) {
      Firebase.Auth.FirebaseAuth auth;
      if (appCPtrToAuth.TryGetValue(appCPtr, out auth)) {
        return auth;
      }
    }
    return null;
  }

  // Throw a NullReferenceException if this proxy references a deleted object.
  private void ThrowIfNull() {
    if (swigCPtr.Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /// @brief Returns the FirebaseAuth object for an App. Creates the
  /// FirebaseAuth if required.
  ///
  /// @param[in] app The FirebaseApp to use for the FirebaseAuth object.
  /// @throw InitializationException Thrown with the invalid InitResult if
  /// initialization failed.
  public static FirebaseAuth GetAuth(FirebaseApp app) {
    // Lookup a pre-existing instance for the specified app.
    FirebaseAuth auth;
    lock (appCPtrToAuth) {
      System.IntPtr appCPtr = FirebaseApp.getCPtr(app).Handle;
      auth = ProxyFromAppCPtr(appCPtr);
      if (auth != null) return auth;
      InitResult init_result;
      FirebaseApp.TranslateDllNotFoundException(() => {
          auth = GetAuthInternal(app, out init_result);
          if (init_result != InitResult.Success) {
            throw new Firebase.InitializationException(init_result);
          }
        });
      auth.authStateListener = AuthUtil.CreateAuthStateListener(
          auth, StateChangedFunction);
      auth.idTokenListener = AuthUtil.CreateIdTokenListener(
          auth, IdTokenChangedFunction);
      auth.appProxy = app;
      auth.appCPtr = appCPtr;
      app.AppDisposed += auth.OnAppDisposed;
      appCPtrToAuth[appCPtr] = auth;
    }
    return auth;
  }

  private void OnAppDisposed(object sender, System.EventArgs eventArgs) {
    Dispose();
  }

  // Dispose the auth object.
  private void DisposeInternal() {
    lock (appCPtrToAuth) {
      lock (FirebaseApp.disposeLock) {
        System.GC.SuppressFinalize(this);
        // NOTE: swigCPtr is generated by SWIG in the proxy class and points to
        // the underlying C++ object.
        if (swigCPtr.Handle != System.IntPtr.Zero) {
          // Remove the app from dictionaries tracking the object.
          appCPtrToAuth.Remove(appCPtr);
          appProxy.AppDisposed -= OnAppDisposed;
          appProxy = null;
          appCPtr = System.IntPtr.Zero;

          // Detatch the user proxy from the C++ object as it will no longer
          // be valid.  FirebaseUser never owns the C++ object and so will never
          // delete it.
          // NOTE: This uses the cached currentUser as the auth object may be
          // destroyed at this point.
          if (currentUser != null) {
            currentUser.Dispose();
            currentUser = null;
          }

          // Destroy token and auth state listeners.
          if (authStateListener != System.IntPtr.Zero) {
            AuthUtil.DestroyAuthStateListener(this, authStateListener);
            authStateListener = System.IntPtr.Zero;
          }
          if (idTokenListener != System.IntPtr.Zero) {
            AuthUtil.DestroyIdTokenListener(this, idTokenListener);
            idTokenListener = System.IntPtr.Zero;
          }

          ReleaseReferenceInternal(this);
          swigCMemOwn = false;
          swigCPtr = new System.Runtime.InteropServices.HandleRef(
              null, System.IntPtr.Zero);
        }
      }
    }
  }

  // Lookup an auth object from a C++ pointer and forward the auth object to a closure.
  private static void ForwardStateChange(System.IntPtr appCPtr,
                                         System.Action<FirebaseAuth> stateChangeClosure) {
    lock (appCPtrToAuth) {
      FirebaseAuth auth = ProxyFromAppCPtr(appCPtr);
      if (auth != null) ExceptionAggregator.Wrap(() => { stateChangeClosure(auth); });
    }
  }

  // Method which forwards auth state changes to the StateChanged event.
  [MonoPInvokeCallback(typeof(StateChangedDelegate))]
  internal static void StateChangedFunction(System.IntPtr appCPtr) {
    ForwardStateChange(appCPtr, (auth) => {
        // If any state changed events are registered, signal them.
        if (auth.stateChangedImpl != null) {
          lock (appCPtrToAuth) {
            auth.UpdateCurrentUser(auth.CurrentUserInternal);
          }
          auth.stateChangedImpl(auth, System.EventArgs.Empty);
        }

        // Auth should have loaded persistent cache if exists when the listener event is triggered
        // for the first time.
        auth.persistentLoaded = true;
      });
  }

  // Method which forwards ID token changes to the IdTokenChanged event.
  [MonoPInvokeCallback(typeof(StateChangedDelegate))]
  internal static void IdTokenChangedFunction(System.IntPtr appCPtr) {
    ForwardStateChange(appCPtr, (auth) => {
        // If any state changed events are registered, signal them.
        if (auth.idTokenChangedImpl != null) {
          auth.idTokenChangedImpl(auth, System.EventArgs.Empty);
        }
      });
  }

  /// @brief Returns the FirebaseAuth associated with
  /// FirebaseApp.DefaultApp.  FirebaseAuth will be created if required.
  ///
  /// @throw InitializationException Thrown with the invalid InitResult if
  /// initialization failed.
  public static FirebaseAuth DefaultInstance {
    get { return GetAuth(FirebaseApp.DefaultInstance); }
  }


  /// @brief Get the FirebaseApp associated with this object.
  ///
  /// @return FirebaseApp associated with this object.
  public FirebaseApp App {
    get { return appProxy; }
  }

  /// @brief Event raised on changes in the authentication state.
  ///
  /// Authentication state changes are:
  ///   - Right after the listener has been registered
  ///   - When a user signs in
  ///   - When the current user signs out
  ///   - When the current user changes
  ///
  /// It is a recommended practice to always listen to sign-out events, as you
  /// may want to prompt the user to sign in again and maybe restrict the
  /// information or actions they have access to.
  ///
  public event System.EventHandler StateChanged {
    add {
      stateChangedImpl += value;
      if (persistentLoaded) {
        value(this, System.EventArgs.Empty);
      }
    }
    remove { stateChangedImpl -= value; }
  }

  // Actual event handler for StateChanged.
  private event System.EventHandler stateChangedImpl;

  /// @brief Event raised on ID token changes.
  ///
  /// Authentication ID token changes are:
  ///   - Right after the listener has been registered
  ///   - When a user signs in
  ///   - When the current user signs out
  ///   - When the current user changes
  ///   - When there is a change in the current user's token
  ///
  public event System.EventHandler IdTokenChanged {
    add {
      idTokenChangedImpl += value;
      if (persistentLoaded) {
        value(this, System.EventArgs.Empty);
      }
    }
    remove { idTokenChangedImpl -= value; }
  }

  // Actual event handler for IdTokenChanged.
  private event System.EventHandler idTokenChangedImpl;

  // Tracks if the persistent cache has been loaded.
  private bool persistentLoaded = false;

  // Maps C++ app pointers to created FirebaseAuth objects.
  private static
    System.Collections.Generic.Dictionary<System.IntPtr, Firebase.Auth.FirebaseAuth>
      appCPtrToAuth =
        new System.Collections.Generic.Dictionary<System.IntPtr,
                                                  Firebase.Auth.FirebaseAuth>();

  /// Asynchronously requests the IDPs (identity providers) that can be used
  /// for the given email address.
  ///
  /// Useful for an "identifier-first" login flow.
  ///
  /// @code{.cs}
  ///  // Print out all available providers for a given email.
  ///  void DisplayIdentityProviders(Firebase.Auth.FirebaseAuth auth,
  ///                                String email) {
  ///    auth.FetchProvidersForEmailAsync().ContinueWith((authTask) => {
  ///      if (authTask.IsCanceled) {
  ///        DebugLog("Provider fetch canceled.");
  ///      } else if (authTask.IsFaulted) {
  ///        DebugLog("Provider fetch encountered an error.");
  ///        DebugLog(authTask.Exception.ToString());
  ///      } else if (authTask.IsCompleted) {
  ///        DebugLog("Email Providers:");
  ///        foreach (string provider in authTask.result) {
  ///          DebugLog(provider);
  ///        }
  ///      }
  ///    });
  ///  }
  /// @endcode
  public System.Threading.Tasks.Task<System.Collections.Generic.IEnumerable<string>>
      FetchProvidersForEmailAsync(string email) {
    ThrowIfNull();
    System.Threading.Tasks.TaskCompletionSource<System.Collections.Generic.IEnumerable<string>>
        taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<
          System.Collections.Generic.IEnumerable<string>>();
    System.Threading.Tasks.Task<Firebase.Auth.FetchProvidersResult> task =
        FetchProvidersForEmailInternalAsync(email);
    task.ContinueWith(t => {
      System.Collections.Generic.List<string> stringListCopy =
          new System.Collections.Generic.List<string>();
      if (t.Result != null && t.Result.Providers != null) {
        foreach(string s in t.Result.Providers) {
          stringListCopy.Add(s);
        }
      }
      taskCompletionSource.SetResult(stringListCopy);
    });
    return taskCompletionSource.Task;
  }

  // Update the cached user proxy for this object.
  private FirebaseUser UpdateCurrentUser(FirebaseUser proxy) {
    lock (appCPtrToAuth) {
      if (proxy == null) {
        // If there is no current user, remove the cached proxy.
        currentUser = null;
      } else if (currentUser == null) {
        // If no proxy is cached, cache the current proxy.
        currentUser = proxy;
      } else {
        // If the user changed, update the cached proxy.
        if (FirebaseUser.getCPtr(currentUser).Handle !=
            FirebaseUser.getCPtr(proxy).Handle) {
          currentUser.Dispose();
          currentUser = proxy;
        }
      }
      if (currentUser != null) currentUser.authProxy = this;
    }
    return currentUser;
  }

  /// @brief Synchronously gets the cached current user, or null if there is none.
  /// @note Accessing this property may block and wait until the FirebaseAuth instance finishes
  /// loading the saved user's state. This should only happen for a short
  /// period of time after the FirebaseAuth instance is created.
  public FirebaseUser CurrentUser {
    get {
      var user = swigCPtr.Handle != System.IntPtr.Zero ? CurrentUserInternal : null;
      return UpdateCurrentUser(user);
    }
  }

  /// Asynchronously logs into Firebase with the given Auth token.
  ///
  /// An error is returned, if the token is invalid, expired or otherwise
  /// not accepted by the server.
  public System.Threading.Tasks.Task<FirebaseUser> SignInWithCustomTokenAsync(
      string token) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<FirebaseUser>();
    SignInWithCustomTokenInternalAsync(token).ContinueWith(task => {
        CompleteFirebaseUserTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }

  /// @brief Asynchronously logs into Firebase with the given `Auth` token.
  ///
  /// An error is returned, if the token is invalid, expired or otherwise not
  /// accepted by the server.
  public System.Threading.Tasks.Task<FirebaseUser> SignInWithCredentialAsync(
      Credential credential) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<FirebaseUser>();
    SignInWithCredentialInternalAsync(credential).ContinueWith(task => {
        CompleteFirebaseUserTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }

  /// Asynchronously logs into Firebase with the given credentials.
  ///
  /// For example, the credential could wrap a Facebook login access token,
  /// a Twitter token/token-secret pair).
  ///
  /// The SignInResult contains both a reference to the User (which can be null
  /// if the sign in failed), and AdditionalUserInfo, which holds details
  /// specific to the Identity Provider used to sign in.
  ///
  /// An error is returned if the token is invalid, expired, or otherwise not
  /// accepted by the server.
  public System.Threading.Tasks.Task<SignInResult>
      SignInAndRetrieveDataWithCredentialAsync(Credential credential) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<SignInResult>();
    SignInAndRetrieveDataWithCredentialInternalAsync(credential).ContinueWith(
        task => { CompleteSignInResultTask(task, taskCompletionSource); });
    return taskCompletionSource.Task;
  }

  /// Asynchronously creates and becomes an anonymous user.
  /// If there is already an anonymous user signed in, that user will be
  /// returned instead.
  /// If there is any other existing user, that user will be signed out.
  ///
  /// <SWIG>
  /// @if swig_examples
  /// @code{.cs}
  ///  bool SignIn(Firebase.Auth.FirebaseAuth auth) {
  ///    auth.SignInAnonymouslyAsync().ContinueWith((authTask) => {
  ///      if (authTask.IsCanceled) {
  ///        DebugLog("Anonymous sign in canceled.");
  ///      } else if (authTask.IsFaulted) {
  ///        DebugLog("Anonymous sign in encountered an error.");
  ///        DebugLog(authTask.Exception.ToString());
  ///      } else if (authTask.IsCompleted) {
  ///        DebugLog("Anonymous sign in successful!");
  ///      }
  ///    });
  ///  }
  /// @endcode
  public System.Threading.Tasks.Task<FirebaseUser> SignInAnonymouslyAsync() {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<FirebaseUser>();
    SignInAnonymouslyInternalAsync().ContinueWith(task => {
        CompleteFirebaseUserTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }

  /// Signs in using provided email address and password.
  /// An error is returned if the password is wrong or otherwise not accepted
  /// by the server.
  public System.Threading.Tasks.Task<FirebaseUser> SignInWithEmailAndPasswordAsync(
      string email, string password) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<FirebaseUser>();
    SignInWithEmailAndPasswordInternalAsync(email, password).ContinueWith(
        task => {
          CompleteFirebaseUserTask(task, taskCompletionSource);
        });
    return taskCompletionSource.Task;
  }

  /// Creates, and on success, logs in a user with the given email address
  /// and password.
  ///
  /// An error is returned when account creation is unsuccessful
  /// (due to another existing account, invalid password, etc.).
  public System.Threading.Tasks.Task<FirebaseUser>
      CreateUserWithEmailAndPasswordAsync(string email, string password) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<FirebaseUser>();
    CreateUserWithEmailAndPasswordInternalAsync(email, password).ContinueWith(
        task => {
          CompleteFirebaseUserTask(task, taskCompletionSource);
        });
    return taskCompletionSource.Task;
  }

  // Complete a task that returns a FirebaseUser.
  private void CompleteFirebaseUserTask(
      System.Threading.Tasks.Task<FirebaseUser> task,
      System.Threading.Tasks.TaskCompletionSource<FirebaseUser>
        taskCompletionSource) {
    if (task.IsCanceled) {
      taskCompletionSource.SetCanceled();
    } else if (task.IsFaulted) {
      Firebase.Internal.TaskCompletionSourceCompat<FirebaseUser>.SetException(
          taskCompletionSource, task.Exception);
    } else {
      taskCompletionSource.SetResult(UpdateCurrentUser(task.Result));
    }
  }

  // Complete a task that returns a SignInResult.
  private void CompleteSignInResultTask(
      System.Threading.Tasks.Task<SignInResult> task,
      System.Threading.Tasks.TaskCompletionSource<SignInResult>
        taskCompletionSource) {
    if (task.IsCanceled) {
      taskCompletionSource.SetCanceled();
    } else if (task.IsFaulted) {
      Firebase.Internal.TaskCompletionSourceCompat<SignInResult>.SetException(
          taskCompletionSource, task.Exception);
    } else {
      SignInResult result = task.Result;
      result.authProxy = this;
      taskCompletionSource.SetResult(result);
    }
  }
%}

// Replace the default Dispose() method to remove references to this instance
// from the map of FirebaseAuth instances.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::auth::Auth {
  DisposeInternal();
}

%typemap(csclassmodifiers) firebase::auth::Auth "public sealed class";
%typemap(csclassmodifiers) firebase::auth::User "public sealed class";
%rename(FirebaseUser) firebase::auth::User;

// TODO(butterfield): Aren't these all redundant? -- ignored via the C scope / name
%ignore FirebaseUser::uid;
%ignore FirebaseUser::email;
%ignore FirebaseUser::display_name;
%ignore FirebaseUser::photo_url;
%ignore FirebaseUser::provider_id;
%ignore FirebaseUser::GetToken;
%ignore FirebaseUser::GetTokenThreadSafe;
%ignore FirebaseUser::GetTokenLastResult;
%ignore FirebaseUser::provider_data;
%ignore FirebaseUser::is_email_verified;
%ignore FirebaseUser::is_anonymous;
%ignore FirebaseUser::metadata;

%typemap(csclassmodifiers) firebase::auth::User::UserProfile
  "public sealed class";
%typemap(csclassmodifiers) firebase::auth::Credential "public sealed class";

%typemap(csclassmodifiers) firebase::auth::FederatedAuthProvider "public class";

%typemap(csclassmodifiers) firebase::auth::FederatedOAuthProvider
  "public sealed class";

// Provider classes declared in third_party/firebase/cpp/auth/src/included/credential.h
// Rename the kProviderId string constant for all of our AuthProviders.
%rename(ProviderId) firebase::auth::EmailAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::FacebookAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::GameCenterAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::GitHubAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::GoogleAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::MicrosoftAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::PhoneAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::PlayGamesAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::TwitterAuthProvider::kProviderId;
%rename(ProviderId) firebase::auth::YahooAuthProvider::kProviderId;

%STATIC_CLASS(EmailAuthProvider(), firebase::auth::EmailAuthProvider)
%STATIC_CLASS(FacebookAuthProvider(), firebase::auth::FacebookAuthProvider)
%STATIC_CLASS(GoogleAuthProvider(), firebase::auth::GoogleAuthProvider)
%STATIC_CLASS(GitHubAuthProvider(), firebase::auth::GitHubAuthProvider)
%STATIC_CLASS(GameCenterAuthProvider(), firebase::auth::GameCenterAuthProvider)
%STATIC_CLASS(TwitterAuthProvider(), firebase::auth::TwitterAuthProvider)
%STATIC_CLASS(OAuthProvider(), firebase::auth::OAuthProvider)
%STATIC_CLASS(PlayGamesAuthProvider(), firebase::auth::PlayGamesAuthProvider)

// Instead of using the Result class, we will just use IEnumerable<string>.
%typemap(csclassmodifiers) firebase::auth::Auth::FetchProvidersResult
  "internal sealed class";


// Make snake_case properties CamelCase.
// FetchProvidersResult
%rename(Providers) firebase::auth::Auth::FetchProvidersResult::providers;
// User::UserProfile
%rename(DisplayName) firebase::auth::User::UserProfile::display_name;
%rename(PhotoUrlInternal) firebase::auth::User::UserProfile::photo_url;
// AdditionalUserInfo
%rename(ProviderId) firebase::auth::AdditionalUserInfo::provider_id;
%rename(UserName) firebase::auth::AdditionalUserInfo::user_name;
%rename(UpdatedCredential) firebase::auth::AdditionalUserInfo::updated_credential;
// SignInResult
%rename(UserInternal) firebase::auth::SignInResult::user;
%rename(Info) firebase::auth::SignInResult::info;
// UserMetadata
%rename(CreationTimestamp) firebase::auth::UserMetadata::creation_timestamp;
%rename(LastSignInTimestamp) firebase::auth::UserMetadata::last_sign_in_timestamp;
%rename(Meta) firebase::auth::SignInResult::meta;

%typemap(cscode) firebase::auth::SignInResult %{
  // Holds a reference to the FirebaseAuth proxy object so that it isn't
  // garbage collected while the application holds a reference to this object.
  internal FirebaseAuth authProxy;

  /// The currently signed-in FirebaseUser, or null if there isn't any (i.e.
  /// the user is signed out).
  public FirebaseUser User { get { return authProxy.CurrentUser; } }
%}

// This is here, instead of the src because of b/35780150
// doxy2swig can't convert property doxy strings on nested classes.
%csmethodmodifiers firebase::auth::User::UserProfile::display_name "
  /// Gets or sets the display name associated with the user.
  public";

%typemap(cscode) firebase::auth::User::UserProfile %{
  /// User photo URI.
  /// The photo url associated with the user, if any.
  public System.Uri PhotoUrl {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(PhotoUrlInternal);
    }
    set {
      PhotoUrlInternal = Firebase.FirebaseApp.UriToUrlString(value);
    }
  }
%}

%typemap(cscode) firebase::auth::User %{
  // Holds a reference to the FirebaseAuth proxy object so that it isn't
  // garbage collected while the application holds a reference to this object.
  // This is set by FirebaseAuth when a user is fetched from the object.
  internal FirebaseAuth authProxy;

  /// The photo url associated with the user, if any.
  public System.Uri PhotoUrl {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(PhotoUrlInternal);
    }
  }
  // Complete a task that returns a SignInResult.
  private void CompleteSignInResultTask(
      System.Threading.Tasks.Task<SignInResult> task,
      System.Threading.Tasks.TaskCompletionSource<SignInResult>
        taskCompletionSource) {
    if (task.IsCanceled) {
      taskCompletionSource.SetCanceled();
    } else if (task.IsFaulted) {
      Firebase.Internal.TaskCompletionSourceCompat<SignInResult>.SetException(
          taskCompletionSource, task.Exception);
    } else {
      SignInResult result = task.Result;
      result.authProxy = authProxy;
      taskCompletionSource.SetResult(result);
    }
  }

  /// Reauthenticate a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS and Android platforms. On
  /// non-mobile platforms this method will return a Future with a preset error
  /// code: kAuthErrorUnimplemented.
  public System.Threading.Tasks.Task<SignInResult>
      ReauthenticateWithProviderAsync(FederatedAuthProvider provider) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<SignInResult>();
    ReauthenticateWithProviderInternalAsync(provider).ContinueWith(task => {
        CompleteSignInResultTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }

  // Throw a NullReferenceException if this proxy references a deleted object.
  private void ThrowIfNull() {
    if (swigCPtr.Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /// Link a user via a federated auth provider.
  ///
  /// @note: This operation is supported only on iOS and Android platforms. On
  /// non-mobile platforms this method will return a Future with a preset error
  /// code: kAuthErrorUnimplemented.
  public System.Threading.Tasks.Task<SignInResult> LinkWithProviderAsync(
      FederatedAuthProvider provider) {
    ThrowIfNull();
    var taskCompletionSource =
        new System.Threading.Tasks.TaskCompletionSource<SignInResult>();
    LinkWithProviderInternalAsync(provider).ContinueWith(task => {
        CompleteSignInResultTask(task, taskCompletionSource);
      });
    return taskCompletionSource.Task;
  }
%}

%typemap(cscode) firebase::auth::UserInfoInterface %{
  public System.Uri PhotoUrl {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(PhotoUrlInternal);
    }
  }
%}



// Disable warning about potentially leaking memory in UserProfile as we
// workaround the potential leaks of string by overriding all string property
// setters to always pass a string and then clean up on destruction.
%warnfilter(451) firebase::auth::User::UserProfile;
// Clean up SWIG allocated strings on destruction.
%extend firebase::auth::User::UserProfile {
  // String pointers were allocated by SWIG when set so free them.
  ~UserProfile() {
    if ($self->display_name) delete[] $self->display_name;
    if ($self->photo_url) delete[] $self->photo_url;
  }
}

// SWIG's generated C++ will leak null strings, so never allow a char* to be
// set to null.
%typemap(csvarin, excode=SWIGEXCODE2) char*, char %{
    set { value = value ?? ""; $imcall ; $excode }
  %}

// Rename User Federated Auth methods
%rename(ReauthenticateWithProviderInternal)
  firebase::auth::User::ReauthenticateWithProvider;
%rename(LinkWithProviderInternal)
  firebase::auth::User::LinkWithProvider;

// Rename token retrieval method.
// NOTE: This is not a property as it is an asynchronous operation.
%rename(Token) firebase::auth::User::GetTokenThreadSafe;
%ignore firebase::auth::User::GetToken;

// Ignore deprecated fields of User that are not replaced by properties.
%ignore firebase::auth::User::UID;
%ignore firebase::auth::User::PhotoUrl;
%ignore firebase::auth::User::Token;
%ignore firebase::auth::User::EmailVerified;
%ignore firebase::auth::User::Anonymous;
%ignore firebase::auth::User::RefreshToken;
// NOTE: It's not necesaary to ignore the following methods
// as they're replaced by the attributes below:
// * firebase::auth::User::Email
// * firebase::auth::User::DisplayName
// * firebase::auth::User::ProviderId
// * firebase::auth::User::ProviderData

// Ignore deprecated fields of UserInfoInterface that are not replaced by
// properties.
%ignore firebase::auth::UserInfoInterface::UID;
%ignore firebase::auth::UserInfoInterface::PhotoUrl;
// NOTE: It's not necesaary to ignore the following methods
// as they're replaced by the attributes below:
// * firebase::auth::UserInfoInterface::Email
// * firebase::auth::UserInfoInterface::DisplayName
// * firebase::auth::UserInfoInterface::ProviderId

// Deprecated method that conflicts with the CurrentUser property.
%ignore firebase::auth::Auth::CurrentUser;
// Make basic getters use C# Properties instead.
%attribute(firebase::auth::Auth, firebase::auth::User *,
           CurrentUserInternal, current_user);

%attributestring(firebase::auth::Credential, std::string, Provider, provider);

// Firebase.Auth.FirebaseUser (inherits IUserInfo)
%attributestring(firebase::auth::User, std::string, DisplayName, display_name);
%attributestring(firebase::auth::User, std::string, Email, email);
%attribute(firebase::auth::User, bool, IsAnonymous, is_anonymous);
%attribute(firebase::auth::User, bool, IsEmailVerified, is_email_verified);
%attributeval(firebase::auth::User, firebase::auth::UserMetadata, Metadata, metadata);
%attributestring(firebase::auth::User, std::string, PhoneNumber, phone_number);
%attributestring(firebase::auth::User, std::string, PhotoUrlInternal, photo_url);
%attribute(firebase::auth::User,
  std::vector<firebase::auth::UserInfoInterface*>&, ProviderData, provider_data);
%attributestring(firebase::auth::User, std::string, ProviderId, provider_id);
%attributestring(firebase::auth::User, std::string, UserId, uid);

// Change the fields on UserInfoInterface and inheritors to use Properties.
%attributestring(firebase::auth::UserInfoInterface, std::string, UserId, uid);
%attributestring(firebase::auth::UserInfoInterface, std::string, Email, email);
%attributestring(firebase::auth::UserInfoInterface, std::string, DisplayName, display_name);
%attributestring(firebase::auth::UserInfoInterface, std::string, PhotoUrlInternal, photo_url);
%attributestring(firebase::auth::UserInfoInterface, std::string, ProviderId, provider_id);


%typemap(csinterfaces) firebase::auth::UserInfoInterface
    "IUserInfo, global::System.IDisposable";
// We modify auth::User via command line, because we want to change the
// inheritance, but swig does not seem to support removing a parent class.
// See firebase/auth/client/unity/BUILD:swig_interface postcmd for the rule.

// Remove equals and copy constructor - The user should not be creating new
// Credential objects.
%ignore firebase::auth::Credential::operator=;
%ignore firebase::auth::Credential::Credential(const Credential& rhs);

%rename(IsValid) firebase::auth::Credential::is_valid;

SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::auth::AuthStateChangedDelegateFunc,
                             Firebase.Auth.FirebaseAuth.StateChangedDelegate)

// The classes should be sealed.
%typemap(csclassmodifiers) firebase::auth::AdditionalUserInfo
  "public sealed class";
%typemap(csclassmodifiers) firebase::auth::SignInResult "public sealed class";
// The classes are not meant to be publicly constructable.
%ignore firebase::auth::AdditionalUserInfo::AdditionalUserInfo;
%ignore firebase::auth::SignInResult::SignInResult;
// The fields in the classes are meant to be readonly.
%immutable firebase::auth::AdditionalUserInfo::provider_id;
%immutable firebase::auth::AdditionalUserInfo::user_name;
%immutable firebase::auth::AdditionalUserInfo::profile;
%immutable firebase::auth::SignInResult::user;
%immutable firebase::auth::SignInResult::info;
%immutable firebase::auth::UserMetadata::creation_timestamp;
%immutable firebase::auth::UserMetadata::last_sign_in_timestamp;

// Provide comments for the fields, because of b/35780150
%csmethodmodifiers firebase::auth::AdditionalUserInfo::provider_id "
  /// The provider identifier.
  public";
%csmethodmodifiers firebase::auth::AdditionalUserInfo::user_name "
  /// The name of the user.
  public";
%csmethodmodifiers firebase::auth::SignInResult::user "
  /// The currently signed-in FirebaseUser, or null if there isn't any (i.e.
  /// the user is signed out).
  public";
%csmethodmodifiers firebase::auth::SignInResult::info "
  /// Identity-provider specific information for the user, if the provider is
  /// one of Facebook, Github, Google, or Twitter.
  public";

// Convert Profile to a C# object dictionary.
%rename(ProfileInternal) firebase::auth::AdditionalUserInfo::profile;
%typemap(cscode) firebase::auth::AdditionalUserInfo %{
  /// Additional identity-provider specific information.
  /// Most likely a hierarchical key-value mapping, like a parsed JSON file.
  public global::System.Collections.Generic.IDictionary<string, object>
      Profile {
    get {
      return ProfileInternal.ToStringVariantMap();
    }
  }
%}

%typemap(csclassmodifiers)
  firebase::auth::PhoneAuthProvider::ForceResendingToken "public sealed class";
// Remove equals and the constructor, as we don't want to expose that to C#.
%ignore firebase::auth::PhoneAuthProvider::ForceResendingToken::operator=;
%ignore firebase::auth::PhoneAuthProvider::ForceResendingToken::ForceResendingToken;

// Make the PhoneAuthProvider class internal, as we use a handwritten version
// publicly, which wraps this internal one.
%rename (PhoneAuthProviderInternal) firebase::auth::PhoneAuthProvider;

// We need a C++ implementation of the Phone Auth Listener to use.
%ignore firebase::auth::PhoneAuthProvider::Listener;
%ignore firebase::auth::PhoneAuthProvider::VerifyPhoneNumber;
%{
namespace firebase {
namespace auth {

// The callbacks that are used by the Phone Auth Listener, that need to reach
// back to C# callbacks.
typedef void (SWIGSTDCALL *VerificationCompletedCallback)(
    int callback_id, void* credential);
typedef void (SWIGSTDCALL *VerificationFailedCallback)(
    int callback_id, char* error);
typedef void (SWIGSTDCALL *CodeSentCallback)(
    int callback_id, char* verification_id,
    PhoneAuthProvider::ForceResendingToken* token);
typedef void (SWIGSTDCALL *TimeOutCallback)(
    int callback_id, char* verification_id);

// Provider a C++ implementation of the Phone Auth Listener that can forward
// the calls back to the C# delegates.
class PhoneAuthListenerImpl
  : public firebase::auth::PhoneAuthProvider::Listener {
 public:
  PhoneAuthListenerImpl(int callback_id) : callback_id_(callback_id) {}
  virtual ~PhoneAuthListenerImpl() {}

  virtual void OnVerificationCompleted(Credential credential) {
    if (g_verification_completed_callback) {
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2<int, Credential>(
              callback_id_, credential, VerificationCompleted));
    }
  }

  virtual void OnVerificationFailed(const std::string& error) {
    if (g_verification_failed_callback) {
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue1String1<int>(
              callback_id_, error.c_str(), VerificationFailed));
    }
  }

  virtual void OnCodeSent(const std::string& verification_id,
                          const PhoneAuthProvider::ForceResendingToken& token) {
    if (g_code_sent_callback) {
      // Use a copy of the token to keep it in memory. The callback is in
      // charge of cleaning it up.
      PhoneAuthProvider::ForceResendingToken* copy =
          new PhoneAuthProvider::ForceResendingToken(token);
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2String1<
            int, PhoneAuthProvider::ForceResendingToken*>(
              callback_id_, copy, verification_id.c_str(), CodeSent));
    }
  }

  virtual void OnCodeAutoRetrievalTimeOut(const std::string& verification_id) {
    if (g_time_out_callback) {
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue1String1<int>(
              callback_id_, verification_id.c_str(), TimeOut));
    }
  }

  // Called from C# to pass along the C# functions to be called.
  static void SetCallbacks(VerificationCompletedCallback completed_callback,
                           VerificationFailedCallback failed_callback,
                           CodeSentCallback sent_callback,
                           TimeOutCallback time_callback) {
    MutexLock lock(g_mutex);
    g_verification_completed_callback = completed_callback;
    g_verification_failed_callback = failed_callback;
    g_code_sent_callback = sent_callback;
    g_time_out_callback = time_callback;
  }
 private:
  int callback_id_;

  static Mutex g_mutex;
  static VerificationCompletedCallback g_verification_completed_callback;
  static VerificationFailedCallback g_verification_failed_callback;
  static CodeSentCallback g_code_sent_callback;
  static TimeOutCallback g_time_out_callback;

  static void VerificationCompleted(int callback_id, Credential credential) {
    MutexLock lock(g_mutex);
    if (g_verification_completed_callback) {
      // Copy the credential so it can be owned by the C# proxy object.
      Credential* copy = new Credential(credential);
      g_verification_completed_callback(callback_id, copy);
    }
  }

  static void VerificationFailed(int callback_id, const char* error) {
    MutexLock lock(g_mutex);
    if (g_verification_failed_callback) {
      g_verification_failed_callback(callback_id,
                                     SWIG_csharp_string_callback(error));
    }
  }

  static void CodeSent(int callback_id,
                       PhoneAuthProvider::ForceResendingToken* token,
                       const char* id) {
    MutexLock lock(g_mutex);
    if (g_code_sent_callback) {
      g_code_sent_callback(callback_id, SWIG_csharp_string_callback(id), token);
    } else {
      // We made a copy of the token for the callback, so delete it since
      // it won't be saved by the C# object.
      delete token;
    }
  }

  static void TimeOut(int callback_id, const char* verification_id) {
    MutexLock lock(g_mutex);
    if (g_time_out_callback) {
      g_time_out_callback(callback_id,
                          SWIG_csharp_string_callback(verification_id));
    }
  }
};

Mutex PhoneAuthListenerImpl::g_mutex;
VerificationCompletedCallback PhoneAuthListenerImpl::g_verification_completed_callback = nullptr;
VerificationFailedCallback PhoneAuthListenerImpl::g_verification_failed_callback = nullptr;
CodeSentCallback PhoneAuthListenerImpl::g_code_sent_callback = nullptr;
TimeOutCallback PhoneAuthListenerImpl::g_time_out_callback = nullptr;

}  // namespace auth
}  // namespace firebase
%}

%extend firebase::auth::PhoneAuthProvider {
  // Creates a new Listener with the given callback_id to handle the call.
  // Returns that Listener, so the caller can manage that memory.
  void* VerifyPhoneNumberInternal(
      const char* phone_number, uint32_t auto_verify_time_out_ms,
      const firebase::auth::PhoneAuthProvider::ForceResendingToken* token,
      int callback_id) {
    firebase::auth::PhoneAuthListenerImpl* listener =
        new firebase::auth::PhoneAuthListenerImpl(callback_id);
    self->VerifyPhoneNumber(phone_number, auto_verify_time_out_ms,
                            token, listener);
    return listener;
  }

  // Destroys the provided Listener implementation.
  void DestroyListenerImplInternal(void* listener) {
    assert(listener);
    delete reinterpret_cast<firebase::auth::PhoneAuthListenerImpl*>(listener);
  }

  // Save the C# callbacks so they can be called later.
  static void SetCallbacks(
      firebase::auth::VerificationCompletedCallback completed_callback,
      firebase::auth::VerificationFailedCallback failed_callback,
      firebase::auth::CodeSentCallback sent_callback,
      firebase::auth::TimeOutCallback time_out_callback) {
    firebase::auth::PhoneAuthListenerImpl::SetCallbacks(
        completed_callback, failed_callback, sent_callback, time_out_callback);
  }
}

%typemap(cscode) firebase::auth::PhoneAuthProvider %{
public delegate void VerificationCompletedDelegate(int callbackId, System.IntPtr credential);
public delegate void VerificationFailedDelegate(int callbackId, string error);
public delegate void CodeSentDelegate(int callbackId, string verificationId, System.IntPtr token);
public delegate void TimeOutDelegate(int callbackId, string verificationId);
%}

// Map callback function types delegates.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::auth::VerificationCompletedCallback,
    Firebase.Auth.PhoneAuthProviderInternal.VerificationCompletedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::auth::VerificationFailedCallback,
    Firebase.Auth.PhoneAuthProviderInternal.VerificationFailedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::auth::CodeSentCallback,
    Firebase.Auth.PhoneAuthProviderInternal.CodeSentDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::auth::TimeOutCallback,
    Firebase.Auth.PhoneAuthProviderInternal.TimeOutDelegate)

%include "auth/src/include/firebase/auth/credential.h"
%include "auth/src/include/firebase/auth/types.h"
%include "auth/src/include/firebase/auth/user.h"
%include "auth/src/include/firebase/auth.h"

namespace firebase {
namespace auth {
// Add a listener to the specified auth object.
%csmethodmodifiers CreateAuthStateListener "internal";
static void* CreateAuthStateListener(
    firebase::auth::Auth *auth,
    firebase::auth::AuthStateChangedDelegateFunc state_changed_delegate);
// Remove a listener from the specified auth object.
%csmethodmodifiers DestroyAuthStateListener "internal";
static void DestroyAuthStateListener(firebase::auth::Auth *auth, void *listener);

// Add a listener to the specified auth object.
%csmethodmodifiers CreateIdTokenListener "internal";
static void* CreateIdTokenListener(
    firebase::auth::Auth *auth,
    firebase::auth::AuthStateChangedDelegateFunc state_changed_delegate);
// Remove a listener from the specified auth object.
%csmethodmodifiers DestroyIdTokenListener "internal";
static void DestroyIdTokenListener(firebase::auth::Auth *auth, void *listener);
}  // namespace auth
}  // namespace firebase
