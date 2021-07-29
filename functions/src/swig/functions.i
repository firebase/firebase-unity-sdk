// Copyright 2018 Google Inc. All Rights Reserved.
//
// C# bindings for the Functions C++ interface.

%module FunctionsInternal

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
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Include required headers and predefined types for the generated C++ module.
%{
#include "app/src/callback.h"
#include "app/src/cpp_instance_manager.h"
#include "functions/src/include/firebase/functions.h"
%}

%rename("FirebaseFunctionsInternal") firebase::functions::Functions;

// Generate a Task wrapper around functions specific Future<T> types.
%SWIG_FUTURE(Future_HttpsCallableResult, HttpsCallableResultInternal, internal,
             firebase::functions::HttpsCallableResult, FirebaseException)

// Configure properties for get / set methods on the Functions class.
%attribute(firebase::functions::Functions, firebase::App*, App, app);
// TODO(klimt): Add this back when we add region support.
// %attributestring(firebase::functions::Functions, std::string, Region, region);

%typemap(cscode) firebase::functions::Functions %{
  // Take / release ownership of the C++ object.
  internal void SetSwigCMemOwn(bool ownership) {
    swigCMemOwn = ownership;
  }
%}

// Delete the C++ object if hasn't already been deleted.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::functions::Functions {
    lock (FirebaseApp.disposeLock) {
      ReleaseReferenceInternal(this);
      swigCMemOwn = false;
      swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
          null, global::System.IntPtr.Zero);
    }
}

%rename("HttpsCallableReferenceInternal") firebase::functions::HttpsCallableReference;
// Configure properties for get / set methods on the HttpsCallableReference class.
%attribute(firebase::functions::HttpsCallableReference, firebase::functions::Functions*,
           Functions, functions);
%attributestring(firebase::functions::HttpsCallableReference, bool,
                 IsValid, is_valid);
// Remove the copy operator as the proxy uses the copy constructor.
%ignore firebase::functions::HttpsCallableReference::operator=(const HttpsCallableReference&);

%extend firebase::functions::HttpsCallableReference {
  // There's nothing to add for now.
}

%rename("HttpsCallableResultInternal") firebase::functions::HttpsCallableResult;
// Configure properties for get / set methods on the HttpsCallableResult class.
// TODO(klimt): Implement getting the Data from the result.
// %attributeref(firebase::functions::HttpsCallableResult, firebase::Variant, Data, data);
// Remove the copy operator as the proxy uses the copy constructor.
%ignore firebase::functions::HttpsCallableResult::operator=(const HttpsCallableResult&);

%rename("FunctionsErrorCode") firebase::functions::Error;
%rename("%(regex:/Error(.*)/\\1/)s", %$isenumitem) "";

// SWIG doesn't follow #include statements so forward declare all classes here
// so that proxies can be created for classes that are referenced before they
// definition is parsed.
namespace firebase {
namespace functions {
class HttpsCallableReference;
}  // namespace functions
}  // namespace firebase

%{
namespace firebase {
namespace functions {
// Reference count manager for C++ Functions instance, using pointer as the key for
// searching.
static CppInstanceManager<Functions> g_functions_instances;
}  // namespace functions
}  // namespace firebase
%}

%ignore firebase::functions::Functions::GetInstance;
%extend firebase::functions::Functions {
  // Get a C++ instance and increment the reference count to it
  %csmethodmodifiers GetInstanceInternal(App* app, const char* region, InitResult* init_result_out) "internal";
  static Functions* GetInstanceInternal(App* app, const char* region, InitResult* init_result_out) {
    // This is to protect from the race condition after GetInstance() is
    // called and before the pointer is added to g_functions_instances.
    ::firebase::MutexLock lock(
        ::firebase::functions::g_functions_instances.mutex());

    firebase::functions::Functions* instance = firebase::functions::Functions::GetInstance(
        app, region, init_result_out);
    ::firebase::functions::g_functions_instances.AddReference(instance);
    return instance;
  }

  // Release and decrement the reference count to a C++ instance
  %csmethodmodifiers ReleaseReferenceInternal(firebase::functions::Functions* instance) "internal";
  static void ReleaseReferenceInternal(firebase::functions::Functions* instance) {
    ::firebase::functions::g_functions_instances.ReleaseReference(instance);
  }
}

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namepsace.
// 516: Ignore warnings about overloaded methods being ignored.
// In all cases, the overload does the right thing e.g
// Functions::GetHttpsCallable(const char*) used instead of
// Functions::GetHttpsCallable(std::string&).
// 814: Globally disable warnings about methods not potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,516,844);

%include "functions/src/include/firebase/functions/common.h"
%include "functions/src/include/firebase/functions/callable_reference.h"
%include "functions/src/include/firebase/functions/callable_result.h"
%include "functions/src/include/firebase/functions.h"
