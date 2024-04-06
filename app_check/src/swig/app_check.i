// Copyright 2023 Google Inc. All Rights Reserved.
//
// C# bindings for the App Check C++ interface.

%module AppCheckUtil

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

%import "app/src/swig/app.i"
%include "app/src/swig/future.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Including cstdint before stdint.i ensures the int64_t typedef is correct,
// otherwise on some platforms it is defined as "long long int" instead of
// "long int".
#include <cstdint>
%include "stdint.i"

// This code block is added to the generated C++ file.
%{
#include <map>

#include "app/src/callback.h"
#include "app_check/src/include/firebase/app_check.h"
#include "app_check/src/include/firebase/app_check/app_attest_provider.h"
#include "app_check/src/include/firebase/app_check/debug_provider.h"
#include "app_check/src/include/firebase/app_check/device_check_provider.h"
#include "app_check/src/include/firebase/app_check/play_integrity_provider.h"

namespace firebase {
namespace app_check {

typedef void (SWIGSTDCALL *GetTokenFromCSharp)(const char* app_name, int key);
typedef void (SWIGSTDCALL *TokenChanged)(const char* app_name, AppCheckToken* token);
typedef void (SWIGSTDCALL *CompleteBuiltInGetToken)(int key, AppCheckToken* token, int error, const char* error_message);

// Should be set to the C# function FirebaseAppCheck.GetTokenFromCSharpMethod
static GetTokenFromCSharp g_get_token_from_csharp = nullptr;
static int g_pending_token_keys = 0;
static std::map<int, std::function<void(AppCheckToken, int, const std::string&)>> g_pending_get_tokens;
static ::firebase::Mutex g_pending_get_tokens_mutex;

// Should be set to the C# function FirebaseAppCheck.TokenChangedMethod
static TokenChanged g_token_changed = nullptr;

// Should be set to the C# function BuiltInProviderWrapper.CompleteBuiltInGetTokenMethod
static CompleteBuiltInGetToken g_complete_built_in_get_token = nullptr;

// Called from C# when a token is fetched, to callback into the C++ SDK.
void FinishGetTokenCallback(int key, const char* token, int64_t expire_ms,
                            int error_code, const char* error_message) {
  // Get the function from the map, and erase it
  std::function<void(AppCheckToken, int, const std::string&)> callback;
  {
    MutexLock lock(g_pending_get_tokens_mutex);
    auto it = g_pending_get_tokens.find(key);
    if (it != g_pending_get_tokens.end()) {
      callback = it->second;
      g_pending_get_tokens.erase(it);
    } else {
      // The callback was missing. This is likely caused by trying to finish the same
      // callback multiple times, so ignore it.
      return;
    }
  }

  AppCheckToken app_check_token;
  app_check_token.token = token;
  app_check_token.expire_time_millis = expire_ms;
  callback(app_check_token, error_code, error_message);
}

// Wrapper that calls g_get_token_from_csharp, to get a new AppCheckToken
// from the C# implementation. Logic to determine the provider is done on
// the C# side.
// Should be used with the callback logic to guarantee it is on the Unity thread.
static void CallGetTokenFromCSharp(int key, const char* name) {
  if (g_get_token_from_csharp) {
    // Should be calling FirebaseAppCheck.GetTokenFromCSharpMethod
    g_get_token_from_csharp(name, key);
  } else {
    // The C# callback has disappeared, so fail the C++ call.
    FinishGetTokenCallback(key, "", 0, kAppCheckErrorInvalidConfiguration,
      "Missing AppCheckProvider C# configuration");
  }
}

// C++ implementation of the AppCheckProvider that calls up to the
// C# library. Note that this isn't meant to wrap the C# providers
// directly, but instead all pass into the C# library itself, which
// then forwards calls to the approprate C# provider.
class SwigAppCheckProvider : public AppCheckProvider {
 public:
  SwigAppCheckProvider(App* app)
    : app_(app) {}

  ~SwigAppCheckProvider() override {}

  void GetToken(std::function<void(AppCheckToken, int, const std::string&)>
                  completion_callback) override {
    if (g_get_token_from_csharp) {
      // Save the callback in the map, and generate a key
      int key;
      {
        MutexLock lock(g_pending_get_tokens_mutex);
        key = g_pending_token_keys++;
        g_pending_get_tokens[key] = completion_callback;
      }
      // Queue a call to the C# function that will generate the token.
      firebase::callback::AddCallback(
        new firebase::callback::CallbackValue1String1<int>(
          key, app_->name(), CallGetTokenFromCSharp));
    } else {
      completion_callback({}, kAppCheckErrorInvalidConfiguration,
        "Missing AppCheckProvider C# configuration");
    }
  }

 private:
  App* app_;
};

// C++ implementation of the AppCheckProviderFactory that can
// forward to the C# implementation. Note that this does not wrap
// the C# factory directly, but instead wraps over the C# library
// itself, which will then forward the call into the set C# factory.
class SwigAppCheckProviderFactory : public AppCheckProviderFactory {
 public:
  SwigAppCheckProviderFactory()
    : provider_map_() {}

  ~SwigAppCheckProviderFactory() override {
    // Clear the map
    for (auto it : provider_map_) {
      delete it.second;
    }
    provider_map_.clear();
  }

  firebase::app_check::AppCheckProvider* CreateProvider(App* app) override {
    // Check the map
    std::map<App*, AppCheckProvider*>::iterator it = provider_map_.find(app);
    if (it != provider_map_.end()) {
      return it->second;
    }
    // Create a new provider and cache it
    SwigAppCheckProvider* provider = new SwigAppCheckProvider(app);
    provider_map_[app] = provider;
    return provider;
  }
 private:
  std::map<App*, AppCheckProvider*> provider_map_;
};

static SwigAppCheckProviderFactory g_swig_factory;

// Called from C# to register the C# to call to get a token.
void SetGetTokenCallback(GetTokenFromCSharp get_token_callback) {
  g_get_token_from_csharp = get_token_callback;

  if (get_token_callback) {
    // If a valid callback, register the Swig Factory as the one to use.
    firebase::app_check::AppCheck::SetAppCheckProviderFactory(&g_swig_factory);
  } else {
    // If given no callback, clear the factory.
    firebase::app_check::AppCheck::SetAppCheckProviderFactory(nullptr);
  }
}

// Wrapper that calls g_token_changed. Should be used with the
// callback logic to guarantee it is on the Unity thread.
static void CallTokenChanged(AppCheckToken token, const char* name) {
  if (g_token_changed) {
    // Should be calling FirebaseAppCheck.TokenChangedMethod
    g_token_changed(name, &token);
  }
}

class SwigAppCheckListener : public AppCheckListener {
 public:
  SwigAppCheckListener(App* app) : app_(app) {}
  ~SwigAppCheckListener() override {}

  void OnAppCheckTokenChanged(const AppCheckToken& token) override {
    if (g_token_changed) {
      AppCheckToken localToken = token;
      // Queue a call to the C# function to pass along the new token.
      firebase::callback::AddCallback(
        new firebase::callback::CallbackValue1String1<AppCheckToken>(
          localToken, app_->name(), CallTokenChanged));
    }
  }
  
 private:
  App* app_;
};

static std::map<App*, SwigAppCheckListener*> g_registered_listeners;

// Called by C# to register an AppCheck instance for token change updates,
// provided via the callback method provided.
void SetTokenChangedCallback(AppCheck* app_check, TokenChanged token_changed_callback) {
  if (token_changed_callback) {
    // Save the callback, and register a new lister for the given app_check
    g_token_changed = token_changed_callback;

    SwigAppCheckListener* listener = new SwigAppCheckListener(app_check->app());
    g_registered_listeners[app_check->app()] = listener;

    app_check->AddAppCheckListener(listener);
  } else {
    // Remove the listener, and cleanup the callback if no more remain.
    SwigAppCheckListener* listener = g_registered_listeners[app_check->app()];
    g_registered_listeners.erase(app_check->app());
    app_check->RemoveAppCheckListener(listener);

    if (g_registered_listeners.empty()) {
      g_token_changed = nullptr;
    }
  }
}

// Called from C# to register the C# function to call to complete pending
// GetToken calls from the built in providers.
void SetCompleteBuiltInGetTokenCallback(CompleteBuiltInGetToken get_token_callback) {
  g_complete_built_in_get_token = get_token_callback;
}

// Calls into C# to complete a call to GetToken on one of the built in provider types.
// Should be used with the callback logic to guarantee it is on the Unity thread.
void CallCompleteBuiltInGetToken(int key, AppCheckToken token, int error, const char* error_message) {
  if (g_complete_built_in_get_token) {
    // Should be calling BuiltInProviderWrapper.CompleteBuiltInGetTokenMethod
    g_complete_built_in_get_token(key, &token, error, error_message);
  }
}

// Called from C# to call the built in provider's GetToken function, and then pass the
// result back to C# when done.
void GetTokenFromBuiltInProvider(AppCheckProvider* provider, int key) {
  auto token_callback{
    [key](firebase::app_check::AppCheckToken token,
          int error_code, const std::string& error_message) {
    // Queue a call to the C# function to pass along the new token.
    firebase::callback::AddCallback(
      new firebase::callback::CallbackValue3String1<int, AppCheckToken, int>(
        key, token, error_code, error_message.c_str(), CallCompleteBuiltInGetToken));
  }};
  provider->GetToken(token_callback);
}

}  // namespace app_check
}  // firebase
%}  // End of C++ code

// Wrap the Futures used by App Check
%SWIG_FUTURE(Future_AppCheckToken, AppCheckTokenInternal, internal,
  firebase::app_check::AppCheckToken, FirebaseException);

// Rename all the generated classes to *Internal, which will
// not be exposed in the public interface, but can be referenced
// from the hand written C# code.
%rename("AppCheckInternal")
  firebase::app_check::AppCheck;
%rename("AppCheckTokenInternal")
  firebase::app_check::AppCheckToken;
%rename("AppCheckListenerInternal")
  firebase::app_check::AppCheckListener;
%rename("AppCheckProviderInternal")
  firebase::app_check::AppCheckProvider;
%rename("AppCheckProviderFactoryInternal")
  firebase::app_check::AppCheckProviderFactory;
%rename("AppCheckProviderInternal")
  firebase::app_check::AppCheckProvider;
// The default Providers
%rename("AppAttestProviderFactoryInternal")
  firebase::app_check::AppAttestProviderFactory;
%rename("DebugAppCheckProviderFactoryInternal")
  firebase::app_check::DebugAppCheckProviderFactory;
%rename("DeviceCheckProviderFactoryInternal")
  firebase::app_check::DeviceCheckProviderFactory;
%rename("PlayIntegrityProviderFactoryInternal")
  firebase::app_check::PlayIntegrityProviderFactory;

// Ignore the Error enum, since it will just be done by hand.
%ignore firebase::app_check::AppCheckError;

// Ignore GetToken, which is going to have to be handled differently
%ignore GetToken;

// This code block is added directly to the C# module class (AppCheckUtil)
%pragma(csharp) modulecode=%{
  internal delegate void GetTokenFromCSharpDelegate(string appName, int key);

  internal delegate void TokenChangedDelegate(string appName, System.IntPtr tokenCPtr);

  internal delegate void CompleteBuiltInGetTokenDelegate(int key, System.IntPtr tokenCPtr, int error, string errorMessage);
%}

// Map callback function types to delegates.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
  firebase::app_check::GetTokenFromCSharp,
  Firebase.AppCheck.AppCheckUtil.GetTokenFromCSharpDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
  firebase::app_check::TokenChanged,
  Firebase.AppCheck.AppCheckUtil.TokenChangedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
  firebase::app_check::CompleteBuiltInGetToken,
  Firebase.AppCheck.AppCheckUtil.CompleteBuiltInGetTokenDelegate)

%include "app_check/src/include/firebase/app_check/app_attest_provider.h"
%include "app_check/src/include/firebase/app_check/debug_provider.h"
%include "app_check/src/include/firebase/app_check/device_check_provider.h"
%include "app_check/src/include/firebase/app_check/play_integrity_provider.h"
%include "app_check/src/include/firebase/app_check.h"

// Declare the new C++ functions we added in this file that we want
// to expose to C#.
namespace firebase {
namespace app_check {
void SetGetTokenCallback(firebase::app_check::GetTokenFromCSharp get_token_callback);
void FinishGetTokenCallback(int key, const char* token, int64_t expire_ms,
                            int error_code, const char* error_message);

void SetTokenChangedCallback(firebase::app_check::AppCheck* app_check,
                             firebase::app_check::TokenChanged token_changed_callback);

void SetCompleteBuiltInGetTokenCallback(firebase::app_check::CompleteBuiltInGetToken);
void GetTokenFromBuiltInProvider(AppCheckProvider* provider, int key);
}  // app_check
}  // firebase
