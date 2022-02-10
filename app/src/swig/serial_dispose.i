// Copyright 2019 Google Inc. All Rights Reserved.

// Serialize the destruction of all objects.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public") SWIGTYPE {
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public") SWIGTYPE {
#endif
    lock (FirebaseApp.disposeLock) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
            null, global::System.IntPtr.Zero);
      }
      global::System.GC.SuppressFinalize(this);
    }
  }

// Serialize the destruction of all derived objects.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing_derived, methodname="Dispose", parameters="bool disposing",
#else
%typemap(csdestruct_derived, methodname="Dispose",
#endif
         methodmodifiers="public") SWIGTYPE {
    lock (FirebaseApp.disposeLock) {
      if (swigCPtr.Handle != global::System.IntPtr.Zero) {
        if (swigCMemOwn) {
          swigCMemOwn = false;
          $imcall;
        }
        swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
            null, global::System.IntPtr.Zero);
      }
      global::System.GC.SuppressFinalize(this);
#if SWIG_VERSION >= 0x040000
      base.Dispose(disposing);
#else
      base.Dispose();
#endif
    }
   }
