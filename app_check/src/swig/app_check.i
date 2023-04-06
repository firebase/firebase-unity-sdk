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

// Include required headers for the generated C++ module.
%{
#include "app_check/src/include/firebase/app_check.h"
#include "app_check/src/include/firebase/app_check/app_attest_provider.h"
#include "app_check/src/include/firebase/app_check/debug_provider.h"
#include "app_check/src/include/firebase/app_check/device_check_provider.h"
#include "app_check/src/include/firebase/app_check/play_integrity_provider.h"
%}

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

%include "app_check/src/include/firebase/app_check/app_attest_provider.h"
%include "app_check/src/include/firebase/app_check/debug_provider.h"
%include "app_check/src/include/firebase/app_check/device_check_provider.h"
%include "app_check/src/include/firebase/app_check/play_integrity_provider.h"
%include "app_check/src/include/firebase/app_check.h"
