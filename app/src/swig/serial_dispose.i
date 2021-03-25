// Copyright 2019 Google Inc. All Rights Reserved.

// Serialize the destruction of all objects.
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public") SWIGTYPE {
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
%typemap(csdestruct_derived, methodname="Dispose",
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
      base.Dispose();
    }
   }
