// Copyright 2016 Google Inc. All Rights Reserved.
//
// This file contains a macro which should be used for wrapping the Future<T>
// class. To use it, first
// %include "app/src/swig/future.i"
// Then, before including your headers, use the macro %SWIG_FUTURE
// For example:
// %SWIG_FUTURE(Future_String, string, std::string)
//
// The arguments are defined below, where the macro is defined.

namespace firebase {
// Do not generate C# interfaces for internal APIs.
%ignore detail::FutureApiInterface;
%ignore FutureBase::FutureBase(detail::FutureApiInterface*, const FutureHandle&);
%ignore Future::Future(detail::FutureApiInterface*, const FutureHandle&);
%ignore FutureHandle;

// These functions are deprecated in C++, and so are ignored in C#.
%ignore FutureBase::Error;
%ignore FutureBase::ErrorMessage;
%ignore FutureBase::ResultVoid;
%ignore FutureBase::Status;
%ignore Future::Result;

// These functions return pointers to values we want in C#, but we cannot
// dereference the pointers in C#, so instead we ignore these and create a
// custom wrapper that returns by value (see const CTYPE result() in the %extend
// section below.
%ignore FutureBase::result_void;
%ignore Future::result;

// The OnCompletion callbacks use the default calling convention, which is
// usually __cdecl in C++, except it can vary by compiler flags or
// implementation, and in C# it must explicitly use __stdcall. So, we have to
// wrap these and use a callback with the explicit calling convention that can
// cross the mananged/unmanaged boundary. SWIG provides a macro we use for
// declaring the correct calling convention in a cross platform way.
%ignore FutureBase::OnCompletion;
%ignore Future::OnCompletion;

// We don't have a need for the equals operator, as C# Futures/Tasks don't
// support copying.
%ignore FutureBase::operator=;

}  // namespace firebase

// Ignore FIREBASE_DEPRECATED tags on methods.
#undef FIREBASE_DEPRECATED
#define FIREBASE_DEPRECATED

%include "app/src/include/firebase/future.h"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// The Future implementation is assembled in a series of three macros,
// The HEADER, the GET_TASK implementation, and the FOOTER. This componentized
// approach allows for custom GET_TASK implementations for various SDKs
// in the case that they need to map SDK-specific error codes to SDK-specific
// exceptions. Please see the definiton of %SWIG_FUTURE below for more
// information about the parameters.
%define %SWIG_FUTURE_HEADER(CSNAME, CSTYPE, CSACCESS, CTYPE)

%{
#include "app/src/callback.h"
#include "app/src/include/firebase/future.h"
%}


// # Callback flow between C++ and C#:
//
// ## Callback Registration
//
// 1) A C# user-friendly delegate "Action" gets passed to a C#
//    user-friendly Register function "SetOnCompletionCallback()".
// 2) That user-friendly register function will create a callback wrapper
//    "SWIG_CompletionDispatcher()" that works with marshalable types, and
//    registers it with a PInvoke externed function (generated C# function for
//    "SWIG_OnCompletion()"), taking the wrapper delegate
//    "SWIG_CompletionDelegate".
//
// ### Cross the C# -> C boundary:
//
// 3) The C library Reigister function "SWIG_OnCompletion()" uses another
//    callback wrapper "CSNAME##_CB()" that has the correct calling convention
//    "(SWIGSTDCALL* CSNAME##_CB_Type)()" when invoking the C# callback. This
//    wrapper is registered with the C++ library's Register function
//    "OnCompletion()".
// 4) The C++ library's register function stores the callback.
//
// ## Callback Invocation
//
// 1) The C++ library invokes it's callback. This callback doesn't have the C#
//    calling convention, but it's ok because it's a wrapper in the C library,
//    recall that "CSNAME##_CB()" was registered above.
// 2) That wrapper unpacks the C# callback from the user data, and using the
//    correct calling convention invokes the C# callback "SWIG_OnCompletion()"
//    which has the matching signature and marshallable types.
//
// ### Cross the C -> C# boundary:
//
// 3) The C# wrapper callback finally converts the result and handle refs to
//    meaningful C# data, and invokes the original user provided callback.
// 4) The user's callback executes.


// void is a special type since it isn't a real returnable type.
#if "CTYPE"=="void"

%typemap(cstype, out="System.Threading.Tasks.Task")
  firebase::Future<CTYPE> "CSNAME";

%typemap(csout) firebase::Future<CTYPE> {
    System.Threading.Tasks.Task ret = CSNAME.GetTask(
        new CSNAME($imcall, true));
    $excode
    return ret;
  }

#else

%typemap(cstype, out="System.Threading.Tasks.Task<CSTYPE>")
  firebase::Future<CTYPE> "CSNAME";

%typemap(csout, excode=SWIGEXCODE2) firebase::Future<CTYPE> {
    var future = $imcall;
    $excode
    return CSNAME.GetTask(new CSNAME(future, true));
  }

#endif  //  "CTYPE"=="void"

// Replace the default Dispose() method to delete the callback data if
// allocated.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing_derived, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct_derived, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::Future<CTYPE> {
    lock (FirebaseApp.disposeLock) {
      if (swigCPtr.Handle != System.IntPtr.Zero) {
        SetCompletionData(System.IntPtr.Zero);
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new System.Runtime.InteropServices.HandleRef(
            null, System.IntPtr.Zero);
      }
      System.GC.SuppressFinalize(this);
#if SWIG_VERSION >= 0x040000
      base.Dispose(disposing);
#else
      base.Dispose();
#endif
    }
 }

// The Future C# class is marked as internal, as the user should be using Task
%typemap(csclassmodifiers) firebase::Future<CTYPE> "CSACCESS class"

// Code to inject directly into the proxy class in C#.
%typemap(cscode, noblock=1) firebase::Future<CTYPE> {

%enddef // SWIG_FUTURE_HEADER

// Generic implementation to plumb the results of C++ Futures to those of
// their C# counterparts. Throws newly constructed exceptions around
// non-successful results. Please see the definiton of %SWIG_FUTURE below for
// more information about the parameters.
%define %SWIG_FUTURE_GET_TASK(CSNAME, CSTYPE, CTYPE, EXTYPE)
  // Helper for csout typemap to convert futures into tasks.
  // This would be internal, but we need to share it accross assemblies.
#if "CTYPE"=="void"
  static public System.Threading.Tasks.Task GetTask(CSNAME fu) {
    System.Threading.Tasks.TaskCompletionSource<int> tcs =
        new System.Threading.Tasks.TaskCompletionSource<int>();
#else
  static public System.Threading.Tasks.Task<CSTYPE> GetTask(CSNAME fu) {
    System.Threading.Tasks.TaskCompletionSource<CSTYPE> tcs =
        new System.Threading.Tasks.TaskCompletionSource<CSTYPE>();
#endif  // "CTYPE"=="void"

    // Check if an exception has occurred previously and propagate it if it has.
    // This has to be done before accessing the future because the future object
    // might be invalid.
    if ($imclassname.SWIGPendingException.Pending) {
      tcs.SetException($imclassname.SWIGPendingException.Retrieve());
      return tcs.Task;
    }

    if (fu.status() == FutureStatus.Invalid) {
      tcs.SetException(
        new FirebaseException(0, "Asynchronous operation was not started."));
      return tcs.Task;
    }
    fu.SetOnCompletionCallback(() => {
      try {
        if (fu.status() == FutureStatus.Invalid) {
          // No result is pending.
          // FutureBase::Release() or move operator was called.
          tcs.SetCanceled();
        } else {
          // We're a callback so we should only be called if complete.
          System.Diagnostics.Debug.Assert(
              fu.status() != FutureStatus.Complete,
              "Callback triggered but the task is not invalid or complete.");

          int error = fu.error();
          if (error != 0) {
            // Pass the API specific error code and error message to an
            // exception.
            tcs.SetException(new EXTYPE(error, fu.error_message()));
          } else {
            // Success!
#if "CTYPE"=="void"
            tcs.SetResult(0);
#else
            tcs.SetResult(fu.GetResult());
#endif  // "CTYPE"=="void"
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
%enddef // SWIG_FUTURE_GET_TASK


// Completes the Future class definition after the FUTURE_HEADER and
// FUTURE_GET_TASK implementations above.
// Please see the definiton of %SWIG_FUTURE below for more information about the
// parameters.
%define %SWIG_FUTURE_FOOTER(CSNAME, CSTYPE, CTYPE)
  public delegate void Action();

  // On iOS and tvOS, in order to marshal a delegate from C#, it needs both a
  // MonoPInvokeCallback attribute, and be static.
  // Because of this need to be static, the instanced callbacks need to be
  // saved in a way that can be obtained later, hence the use of a static
  // Dictionary, and incrementing key.
  // Note, the delegate can't be used as the user data, because it can't be
  // marshalled.
  private static System.Collections.Generic.Dictionary<int, Action> Callbacks;
  private static int CallbackIndex = 0;
  private static object CallbackLock = new System.Object();

  // Handle to data allocated in SWIG_OnCompletion().
  private System.IntPtr callbackData = System.IntPtr.Zero;

  // Throw a ArgumentNullException if the object has been disposed.
  private void ThrowIfDisposed() {
    if (swigCPtr.Handle == System.IntPtr.Zero) {
      throw new System.ArgumentNullException("Object is disposed");
    }
  }

  // Registers a callback which will be triggered when the result of this future
  // is complete.
  public void SetOnCompletionCallback(Action userCompletionCallback) {
    ThrowIfDisposed();
    if (SWIG_CompletionCB == null) {
      SWIG_CompletionCB =
        new SWIG_CompletionDelegate(SWIG_CompletionDispatcher);
    }

    // Cache the callback, and pass along the key to it.
    int key;
    lock (CallbackLock) {
      if (Callbacks == null) {
        Callbacks = new System.Collections.Generic.Dictionary<int, Action>();
      }
      key = ++CallbackIndex;
      Callbacks[key] = userCompletionCallback;
    }
    SetCompletionData(SWIG_OnCompletion(SWIG_CompletionCB, key));
  }

  // Free data structure allocated in SetOnCompletionCallback() and save
  // a reference to the current data structure if specified.
  private void SetCompletionData(System.IntPtr data) {
    lock (FirebaseApp.disposeLock) {
      // The callback that was made could theoretically be triggered before this point,
      // which would Dispose this object. In that case, we want to free the data we
      // were given, since otherwise it would be leaked.
      if (swigCPtr.Handle == System.IntPtr.Zero) {
        SWIG_FreeCompletionData(data);
      } else {
        // Free the old data, before saving the new data for deletion
        SWIG_FreeCompletionData(callbackData);
        callbackData = data;
      }
    }
  }

  // Handles the C++ callback, and calls the cached C# callback.
  [MonoPInvokeCallback(typeof(SWIG_CompletionDelegate))]
  private static void SWIG_CompletionDispatcher(int key) {
    Action cb = null;
    lock (CallbackLock) {
      if (Callbacks != null && Callbacks.TryGetValue(key, out cb)) {
        Callbacks.Remove(key);
      }
    }
    if (cb != null) cb();
  }

  internal delegate void SWIG_CompletionDelegate(int index);
  private SWIG_CompletionDelegate SWIG_CompletionCB = null;
}

%typemap(cstype) CSNAME##_CB_Type "SWIG_CompletionDelegate";
%typemap(imtype) CSNAME##_CB_Type "CSNAME.SWIG_CompletionDelegate";
%typemap(csin) CSNAME##_CB_Type "$csinput";

// Redefine the typemap for the callback type used in the wrapper code that
// registers the pointer from C#. This is required due to SWIG treating the
// callback type as a value rather than a pointer.
%typemap(in, canthrow=1) CSNAME##_CB_Type %{ $1 = ($1_ltype)$input; %}

%{
  // This adds the STDCALL thunk, necessary for C/C# interoperability.
  // The function pointer type declared in the C++ library should have the
  // exact same signature, however it lacks this explicit calling convention
  // declaration. We declare it here with explicit calling convention and use
  // a thin wrapper (just below) so that we can be certain we match the same
  // calling convention in C# when we trigger the callback.
  typedef void (SWIGSTDCALL* CSNAME##_CB_Type)(int index);

  // Associates callback data with each Future<CTYPE> instance.
  struct CSNAME##CallbackData {
    // C# delegate method that should be called on the main thread.
    CSNAME##_CB_Type cs_callback;
    // Key of the callback in the C# CSTYPE.Callbacks dictionary.
    int cs_key;
  };

  // Use a static function to make callback so don't need to worry about
  // whether using stdcall or cdecl for the cs_callback function.
  static void CallbackImpl(CSNAME##CallbackData data) {
    data.cs_callback(data.cs_key);
  }

  // This is a C++ wrapper callback that queues the registered C# callback.
  // We unpack the real C# callback from the user data, where we packed it
  // when the callback was registered.
  void CSNAME##_CB(const firebase::Future<CTYPE>& /*result_data*/,
                   void *user_data) {
    auto cbdata = reinterpret_cast<CSNAME##CallbackData*>(user_data);
    firebase::callback::AddCallback(
        new firebase::callback::CallbackValue1<CSNAME##CallbackData>(
            *cbdata, CallbackImpl));
  }
%}

// Expand the template for SWIG to generate a wrapper.
%template( CSNAME ) firebase::Future<CTYPE>;

namespace firebase {

// C++ Code
%extend Future<CTYPE> {
  // This handles converting void * to IntPtr on the C# side, instead of making
  // a SWIG proxy wrapper for the void * type.
  // http://www.swig.org/Doc3.0/SWIGDocumentation.html#CSharp_void_pointers
  %apply void *VOID_INT_PTR { void * }

  // This function is wrapped in C#, so we change it to not be exposed as a
  // public interface (default).
  %csmethodmodifiers SWIG_OnCompletion "internal";

  // This function provides the API (when wrapped and exposed in C#) for
  // registering a callback that can cross the C/C# boundary.
  // It will pack the function pointer and original index in an std::pair,
  // and register our calling-convention-correcting callback wrapper.
  void* SWIG_OnCompletion(CSNAME##_CB_Type cs_callback, int cs_key) {
    CSNAME##CallbackData* cbdata = new CSNAME##CallbackData;
    cbdata->cs_callback = cs_callback;
    cbdata->cs_key = cs_key;
    $self->OnCompletion(CSNAME##_CB, cbdata);
    return cbdata;
  }

  // Deallocate data allocated for the completion callback.
  static void SWIG_FreeCompletionData(void *data) {
    delete reinterpret_cast<CSNAME##CallbackData*>(data);
  }

// Only generate the return logic for non-void types.
#if "CTYPE"!="void"

  // This method copies the value return by Future::result() so that it's
  // possible to marshal the return value to C#.
  // We can not return pointer return values of Future::result() as pointers
  // to C++ objects cannot be dereferenced in C#.
  const CTYPE GetResult() const {
    // The Future internally always stores it's value with CTYPE *, so 'result'
    // can always be dereferenced except when the type in void.
    return *$self->result();
  }

#endif // "CTYPE"!="void"
}

} // namespace firebase

%enddef // SWIG_FUTURE_FOOTER

// This macro is used to generate the SWIG wrapper for each Future<T> type.
// The CTYPE is the T in Future<T>, and the CSTYPE is what the CTYPE maps to in
// C#. For example: Future<int>, Future<std::string>, Future<void>, etc. Each
// of these would need a macro call before being processed by SWIG. The CSNAME
// is what the specialized Future proxy class will be called in C#.
// For example if you have:
//   Future<std::string>
// appearing anywhere your code, you should call this macro:
//   %SWIG_FUTURE(MyStringFuture, string, public, std::string,
//                FirebaseException)
// before %including that file in your SWIG wrapper.
// EXTYPE is the type of exception to throw when an error is reported by the
// future. If unsure, use FirebaseException.
%define %SWIG_FUTURE(CSNAME, CSTYPE, CSACCESS, CTYPE, EXTYPE)
  %SWIG_FUTURE_HEADER(CSNAME, CSTYPE, CSACCESS, CTYPE)
  %SWIG_FUTURE_GET_TASK(CSNAME, CSTYPE, CTYPE, EXTYPE)
  %SWIG_FUTURE_FOOTER(CSNAME, CSTYPE, CTYPE)
%enddef
