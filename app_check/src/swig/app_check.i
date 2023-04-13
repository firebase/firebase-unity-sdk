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

#include "app_check/src/include/firebase/app_check.h"
#include "app_check/src/include/firebase/app_check/app_attest_provider.h"
#include "app_check/src/include/firebase/app_check/debug_provider.h"
#include "app_check/src/include/firebase/app_check/device_check_provider.h"
#include "app_check/src/include/firebase/app_check/play_integrity_provider.h"

namespace firebase {
namespace app_check {

typedef void (SWIGSTDCALL *GetTokenFromCSharp)(const char* app_name, int key);

static GetTokenFromCSharp g_get_token_from_csharp = nullptr;
static int g_pending_token_keys = 0;
static std::map<int, std::function<void(AppCheckToken, int, const std::string&)>> g_pending_get_tokens;

// C++ implementation of the AppCheckProvider that calls up to the
// C# library.
class SwigAppCheckProvider : public AppCheckProvider {
 public:
  SwigAppCheckProvider(App* app)
    : app_(app) {}

  ~SwigAppCheckProvider() override {}

  void GetToken(std::function<void(AppCheckToken, int, const std::string&)>
                  completion_callback) override {
    if (g_get_token_from_csharp) {
      // Save the callback in the map, and generate a key
      int key = g_pending_token_keys++;
      g_pending_get_tokens[key] = completion_callback;
      // Call the C# function that will generate the token, passing the key.
      g_get_token_from_csharp(app_->name(), key);
    } else {
      completion_callback({}, kAppCheckErrorInvalidConfiguration,
        "Missing AppCheckProvider C# configuration");
    }
  }

 private:
  App* app_;
};

// C++ implementation of the AppCheckProviderFactory that can
// forward to the C# implementation.
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
void SetAppCheckCallbacks(GetTokenFromCSharp get_token_callback) {
  g_get_token_from_csharp = get_token_callback;

  if (get_token_callback) {
    // If a valid callback, register the Swig Factory as the one to use.
    firebase::app_check::AppCheck::SetAppCheckProviderFactory(&g_swig_factory);
  } else {
    // If given no callback, clear the factory.
    firebase::app_check::AppCheck::SetAppCheckProviderFactory(nullptr);
  }
}

// Called from C# when a token is fetched, to callback into the C++ SDK.
void FinishGetTokenCallback(int key, const char* token, int64_t expire_ms,
                            int error_code, const char* error_message) {
  // Get the function from the map, and erase it
  auto callback = g_pending_get_tokens[key];
  g_pending_get_tokens.erase(key);

  AppCheckToken app_check_token;
  app_check_token.token = token;
  app_check_token.expire_time_millis = expire_ms;
  callback(app_check_token, error_code, error_message);
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

%pragma(csharp) modulecode=%{
  internal delegate void GetTokenFromCSharpDelegate(string appName, int key);
%}

// Map callback function types to delegates.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
  firebase::app_check::GetTokenFromCSharp,
  Firebase.AppCheck.AppCheckUtil.GetTokenFromCSharpDelegate)

%include "app_check/src/include/firebase/app_check/app_attest_provider.h"
%include "app_check/src/include/firebase/app_check/debug_provider.h"
%include "app_check/src/include/firebase/app_check/device_check_provider.h"
%include "app_check/src/include/firebase/app_check/play_integrity_provider.h"
%include "app_check/src/include/firebase/app_check.h"

// Declare the new C++ functions we added in this file that we want
// to expose to C#.
namespace firebase {
namespace app_check {
void SetAppCheckCallbacks(firebase::app_check::GetTokenFromCSharp get_token_callback);
void FinishGetTokenCallback(int key, const char* token, int64_t expire_ms,
                            int error_code, const char* error_message);
}  // app_check
}  // firebase
