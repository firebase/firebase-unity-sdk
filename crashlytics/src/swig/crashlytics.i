// Copyright 2018 Google Inc. All Rights Reserved.
//
// C# bindings for the Crashlytics C++ interface.

%module CrashlyticsInternal

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

%include "std_vector.i"

%import "app/src/swig/app.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/init_result.i"
%include "app/src/swig/serial_dispose.i"

// Include required headers and predefined types for the generated C++ module.
%{
#include "app/src/callback.h"
#include "crashlytics/src/cpp/include/firebase/crashlytics.h"
%}

%rename("FirebaseCrashlyticsInternal") firebase::crashlytics::Crashlytics;
%rename("FirebaseCrashlyticsFrame") firebase::crashlytics::Frame;

%typemap(csclassmodifiers) std::vector<firebase::crashlytics::Frame> "internal class"
%template(StackFrames) std::vector<firebase::crashlytics::Frame>;
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<FirebaseCrashlyticsFrame>") std::vector<firebase::crashlytics::Frame> "StackFrames";

// Delete the C++ object if hasn't already been deleted.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::crashlytics::Crashlytics {
    lock (FirebaseApp.disposeLock) {
      // TODO(@samedson) Do I need to call ReleaseReferenceInternal
      swigCMemOwn = false;
      swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
          null, global::System.IntPtr.Zero);
      // Suppress finalization.
      global::System.GC.SuppressFinalize(this);
    }
}

%typemap(cscode) firebase::crashlytics::Crashlytics %{
  // Determine whether this object has been disposed.
  // This should be called while holding FirebaseApp.disposeLock.
  internal bool IsDisposed {
    get { return swigCPtr.Handle == System.IntPtr.Zero; }
  }
%}

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namepsace.
// 451: Ignore warnings about const char* arguments that may leak memory.
// Methods that pass const char* do not allocate memory in the generated
// C code so will not leak memory.
// 516: Ignore warnings about overloaded methods being ignored.
// In all cases, the overload does the right thing e.g
// Functions::GetHttpsCallable(const char*) used instead of
// Functions::GetHttpsCallable(std::string&).
// 814: Globally disable warnings about methods not potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,451,516,844);

%include "crashlytics/src/cpp/include/firebase/crashlytics.h"
