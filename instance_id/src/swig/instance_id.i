// Copyright 2017 Google Inc. All Rights Reserved.

%module FirebaseInstanceIdUtil

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
#include "instance_id/src/include/firebase/instance_id.h"
%}

%import "app/src/swig/app.i"
%include "app/src/swig/future.i"
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Give the error a name that is less likely to clash with end user's code
// when the module is imported.
%rename(InstanceIdError) firebase::instance_id::Error;
// Give the class a name that is less likely to clash with end user's code
// when the module is imported.
%rename(FirebaseInstanceId) firebase::instance_id::InstanceId;
// Hide GetInstanceId() as this is wrapped in GetInstanceIdInternal() in
// order to properly handle reference count.
%ignore firebase::instance_id::InstanceId::GetInstanceId;
// The C# proxy maintains a reference to the C# app proxy instead.
%ignore firebase::instance_id::InstanceId::app;
// Rename all methods that need null checks.
%rename(GetIdInternal) firebase::instance_id::InstanceId::GetId;
%rename(DeleteIdInternal) firebase::instance_id::InstanceId::DeleteId;
%rename(GetTokenInternal) firebase::instance_id::InstanceId::GetToken;
%rename(DeleteTokenInternal) firebase::instance_id::InstanceId::DeleteToken;

%{
namespace firebase {
namespace instance_id {
// Reference count manager for C++ Instance Id instance, using pointer as the
// key for searching.
static CppInstanceManager<InstanceId> g_instance_id_instances;
}  // namespace instance_id
}  // namespace firebase
%}

%extend firebase::instance_id::InstanceId {
  %csmethodmodifiers GetInstanceIdInternal(App* app, InitResult* init_result_out) "internal";
  static InstanceId* GetInstanceIdInternal(
      App* app, InitResult* init_result_out) {
    // This is to protect from the race condition after
    // InstanceId::GetInstanceId() is called and before the pointer is added to
    // g_instance_id_instances.
    ::firebase::MutexLock lock(
        ::firebase::instance_id::g_instance_id_instances.mutex());
    firebase::instance_id::InstanceId* instance =
        firebase::instance_id::InstanceId::GetInstanceId(app, init_result_out);
    ::firebase::instance_id::g_instance_id_instances.AddReference(instance);
    return instance;
  }

  %csmethodmodifiers ReleaseReferenceInternal(firebase::instance_id::InstanceId* instance) "internal";
  static void ReleaseReferenceInternal(
      firebase::instance_id::InstanceId* instance) {
    ::firebase::instance_id::g_instance_id_instances.ReleaseReference(
        instance);
  }
}

%typemap(cscode) firebase::instance_id::InstanceId %{
  // Holds a reference to the FirebaseApp proxy object so that it isn't
  // deallocated when Dispose() or Finalize() is called on the FirebaseApp
  // instance referenced by this object.
  private FirebaseApp appProxy;

  // Enables Instance ID objects to be found based upon the C++ App pointer.
  private static System.Collections.Generic.Dictionary<System.IntPtr,
                                                       FirebaseInstanceId>
      instanceIdByAppCPtr = new System.Collections.Generic.Dictionary<
        System.IntPtr, FirebaseInstanceId>();

  // Retrieve a reference to an instance ID object from a C pointer.
  private static FirebaseInstanceId ProxyFromAppCPtr(System.IntPtr appCPtr) {
    lock (instanceIdByAppCPtr) {
      FirebaseInstanceId iid = null;
      if (instanceIdByAppCPtr.TryGetValue(appCPtr, out iid)) {
        return iid;
      }
      return null;
    }
  }

  /// @brief App object associated with this instance ID.
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
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. Use Installations GetIdAsync() instead.")]
  public System.Threading.Tasks.Task<string> GetIdAsync() {
    ThrowIfNull();
    return GetIdInternalAsync();
  }

  /// @brief Delete the ID associated with the app, revoke all tokens and
  /// allocate a new ID.
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. Use Installations DeleteAsync() instead.")]
  public System.Threading.Tasks.Task DeleteIdAsync() {
    ThrowIfNull();
    return DeleteIdInternalAsync();
  }

  /// @brief Returns a token that authorizes an Entity to perform an action on
  /// behalf of the application identified by Instance ID.
  ///
  /// This is similar to an OAuth2 token except, it applies to the
  /// application instance instead of a user.
  ///
  /// For example, to get a token that can be used to send messages to an
  /// application via Firebase Messaging, set entity to the
  /// sender ID, and set scope to "FCM".
  ///
  /// @returns A token that can identify and authorize the instance of the
  /// application on the device.
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. For Firebase Messaging, use Messaging GetToken(), otherwise use Installations GetToken() instead.")]
  public System.Threading.Tasks.Task<string> GetTokenAsync() {
    ThrowIfNull();
    return GetTokenInternalAsync();
  }

  /// @brief Revokes access to a scope (action)
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. For Firebase Messaging, use Messaging DeleteTokenAsync(), otherwise use Installations DeleteAsync() instead.")]
  public System.Threading.Tasks.Task DeleteTokenAsync() {
    ThrowIfNull();
    return DeleteTokenInternalAsync();
  }

  /// @brief Returns the `InstanceId` object for an `App` creating the
  /// `InstanceId` if required.
  ///
  /// @param[in] app The `App` to create an `InstanceId` object from. On
  /// **iOS** this must be the default Firebase `App`.
  ///
  /// @returns InstanceId object if successful, null otherwise.
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. Use Installations GetInstance() instead.")]
  public static FirebaseInstanceId GetInstanceId(FirebaseApp app) {
    FirebaseInstanceId instanceId = null;
    lock (instanceIdByAppCPtr) {
      System.IntPtr appCPtr = FirebaseApp.getCPtr(app).Handle;
      instanceId = ProxyFromAppCPtr(appCPtr);
      if (instanceId != null) return instanceId;
      FirebaseApp.TranslateDllNotFoundException(() => {
          InitResult initResult;
          instanceId = GetInstanceIdInternal(app, out initResult);
          if (initResult != InitResult.Success) {
            throw new Firebase.InitializationException(initResult);
          }
        });
      if (instanceId != null) {
        instanceId.appProxy = app;
        app.AppDisposed += instanceId.OnAppDisposed;
        instanceIdByAppCPtr[appCPtr] = instanceId;
      }
    }
    return instanceId;
  }

  /// @brief InstanceId associated with the default Firebase App.
  ///
  /// @returns An InstanceId object associated with the default Firebase App.
  [System.Obsolete("InstanceId is deprecated and will be removed in future release. Use Installations DefaultInstance instead.")]
  public static FirebaseInstanceId DefaultInstance {
    get {
      var app = FirebaseApp.DefaultInstance;
      return app != null ? GetInstanceId(app) : null;
    }
  }

  private void OnAppDisposed(object sender, System.EventArgs eventArgs) {
    Dispose();
  }
%}

// Replace the default Dispose() method to remove references to this instance
// from the map of FirebaseInstanceId instances.
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
    firebase::instance_id::InstanceId {
  lock (instanceIdByAppCPtr) {
    if (appProxy != null) {
      instanceIdByAppCPtr.Remove(FirebaseApp.getCPtr(appProxy).Handle);
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

%include "instance_id/src/include/firebase/instance_id.h"
