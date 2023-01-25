// Copyright 2020 Google Inc. All Rights Reserved.

%module InstallationsUtil

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%pragma(csharp) moduleclassmodifiers="internal sealed class"
%feature("flatnested");

%{
#include "app/src/cpp_instance_manager.h"
#include "installations/src/include/firebase/installations.h"
%}

%import "app/src/swig/app.i"
%include "app/src/swig/future.i"
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Give the class a name that is less likely to clash with end user's code
// when the module is imported.
%rename(FirebaseInstallations) firebase::installations::Installations;
// Hide GetInstance() as this is wrapped in GetInstallationsInternal() in
// order to properly handle reference count.
%ignore firebase::installations::Installations::GetInstance;
// The C# proxy maintains a reference to the C# app proxy instead.
%ignore firebase::installations::Installations::app;
// Rename all methods that need null checks.
%rename(GetIdInternal) firebase::installations::Installations::GetId;
%rename(DeleteInternal) firebase::installations::Installations::Delete;
%rename(GetTokenInternal) firebase::installations::Installations::GetToken;

%{
namespace firebase {
namespace installations {
// Reference count manager for C++ Installations instance, using pointer as the
// key for searching.
static CppInstanceManager<Installations> g_installations_instances;
}  // namespace installations
}  // namespace firebase
%}

%extend firebase::installations::Installations {
  %csmethodmodifiers GetInstallationsInternal(App* app) "internal";
  static Installations* GetInstallationsInternal(App* app) {
    // This is to protect from the race condition after
    // Installations::GetInstance() is called and before the pointer is added to
    // g_installations_instances.
    ::firebase::MutexLock lock(
        ::firebase::installations::g_installations_instances.mutex());
    firebase::installations::Installations* instance =
        firebase::installations::Installations::GetInstance(app);
    ::firebase::installations::g_installations_instances.AddReference(instance);
    return instance;
  }

  %csmethodmodifiers LogHeartbeatInternal(App* app) "internal";
  static void LogHeartbeatInternal(App* app) {
    // Call the internal getter in order to trigger usage logging.
    ::firebase::MutexLock lock(
        ::firebase::installations::g_installations_instances.mutex());
    firebase::installations::Installations* instance =
        firebase::installations::Installations::GetInstance(app);
    // Future-proof against the possibility of the instance having no other
    // references by incrementing and decrementing the reference counter so that
    // memory can be freed if the reference count reaches zero.
    ::firebase::installations::g_installations_instances.AddReference(instance);
    ::firebase::installations::g_installations_instances.ReleaseReference(instance);
  }

  %csmethodmodifiers ReleaseReferenceInternal(firebase::installations::Installations* instance) "internal";
  static void ReleaseReferenceInternal(
      firebase::installations::Installations* instance) {
    ::firebase::installations::g_installations_instances.ReleaseReference(
        instance);
  }
}

%typemap(cscode) firebase::installations::Installations %{
  // Holds a reference to the FirebaseApp proxy object so that it isn't
  // deallocated when Dispose() or Finalize() is called on the FirebaseApp
  // instance referenced by this object.
  private FirebaseApp appProxy;

  // Enables Installations objects to be found based upon the C++ App pointer.
  private static System.Collections.Generic.Dictionary<System.IntPtr,
                                                       FirebaseInstallations>
      installationsByAppCPtr = new System.Collections.Generic.Dictionary<
        System.IntPtr, FirebaseInstallations>();

  // Retrieve a reference to an Installations object from a C pointer.
  private static FirebaseInstallations ProxyFromAppCPtr(System.IntPtr appCPtr) {
    lock (installationsByAppCPtr) {
      FirebaseInstallations installations = null;
      installationsByAppCPtr.TryGetValue(appCPtr, out installations);
      return installations;
    }
  }

  /// @brief App object associated with this Installations.
  public FirebaseApp App { get { return appProxy; } }

  // Throw a NullReferenceException if this proxy references a deleted object.
  private void ThrowIfNull() {
    if (swigCPtr.Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /// @brief Returns a stable identifier that uniquely identifies the app
  /// instance.
  ///
  /// @returns Unique identifier for the app instance.
  public System.Threading.Tasks.Task<string> GetIdAsync() {
    ThrowIfNull();
    return GetIdInternalAsync();
  }

  /// @brief Delete the ID associated with the app, revoke all tokens, and
  /// allocate a new ID.
  public System.Threading.Tasks.Task DeleteAsync() {
    ThrowIfNull();
    return DeleteInternalAsync();
  }

  /// @brief Returns a token that authorizes an Entity to perform an action on
  /// behalf of the application identified by Installations.
  ///
  /// This is similar to an OAuth2 token except it applies to the
  /// application instance instead of a user.
  ///
  /// For example, to get a token that can be used to send messages to an
  /// application via Firebase Cloud Messaging, set entity to the
  /// sender ID, and set scope to "FCM".
  ///
  /// @returns A token that can identify and authorize the instance of the
  /// application on the device.
  public System.Threading.Tasks.Task<string> GetTokenAsync(bool forceRefresh) {
    ThrowIfNull();
    return GetTokenInternalAsync(forceRefresh);
  }

  /// @brief Returns the `Installations` object for an `App` creating the
  /// `Installations` if required.
  ///
  /// @param[in] app The `App` to create an `Installations` object from. On
  /// **iOS and tvOS** this must be the default Firebase `App`.
  ///
  /// @returns Installations object if successful, null otherwise.
  public static FirebaseInstallations GetInstance(FirebaseApp app) {
    FirebaseInstallations installations = null;
    lock (installationsByAppCPtr) {
      System.IntPtr appCPtr = FirebaseApp.getCPtr(app).Handle;
      installations = ProxyFromAppCPtr(appCPtr);
      if (installations != null) {
        LogHeartbeatInternal(app);
        return installations;
      }
      FirebaseApp.TranslateDllNotFoundException(() => {
          installations = GetInstallationsInternal(app);
        });
      if (installations != null) {
        installations.appProxy = app;
        app.AppDisposed += installations.OnAppDisposed;
        installationsByAppCPtr[appCPtr] = installations;
      }
    }
    return installations;
  }

  /// @brief Installations associated with the default Firebase App.
  ///
  /// @returns An Installations object associated with the default Firebase App.
  public static FirebaseInstallations DefaultInstance {
    get {
      var app = FirebaseApp.DefaultInstance;
      return app != null ? GetInstance(app) : null;
    }
  }

  private void OnAppDisposed(object sender, System.EventArgs eventArgs) {
    Dispose();
  }
%}

// Replace the default Dispose() method to remove references to this instance
// from the map of FirebaseInstallations instances.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
    firebase::installations::Installations {
  lock (installationsByAppCPtr) {
    if (appProxy != null) {
      installationsByAppCPtr.Remove(FirebaseApp.getCPtr(appProxy).Handle);
      appProxy.AppDisposed -= OnAppDisposed;
      appProxy = null;
    }
    ReleaseReferenceInternal(this);
    swigCMemOwn = false;
    swigCPtr = new System.Runtime.InteropServices.HandleRef(
            null, System.IntPtr.Zero);
  }
  System.GC.SuppressFinalize(this);
}

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namespace.
// 844: Globally disable warnings about methods potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,844);

%include "installations/src/include/firebase/installations.h"
