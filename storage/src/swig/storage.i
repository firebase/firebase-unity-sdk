// Copyright 2017 Google Inc. All Rights Reserved.
//
// C# bindings for the Storage C++ interface.

%module StorageInternal

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
%include "app/src/swig/init_result.i"
%include "app/src/swig/serial_dispose.i"

// Include required headers and predefined types for the generated C++ module.
%{
#include "app/src/callback.h"
#include "app/src/cpp_instance_manager.h"
#include "storage/src/swig/monitor_controller.h"
#include "storage/src/include/firebase/storage.h"

namespace firebase {
namespace storage {
// Reference count manager for C++ Storage instances, using pointer as the
// key for searching.
static CppInstanceManager<Storage> g_storage_instances;

}  // namespace storage
}  // namespace firebase
%}

%ignore firebase::storage::Storage::GetInstance;

%extend firebase::storage::Storage {
  // Get a C++ instance and increment the reference count to it
  %csmethodmodifiers GetInstanceInternal(App* app, const char* url, InitResult* init_result_out) "internal";
  static ::firebase::storage::Storage* GetInstanceInternal(
      ::firebase::App* app, const char* url, InitResult* init_result_out) {
    // This is to protect from the race condition after GetInstance() is
    // called and before the pointer is added to g_storage_instances.
    ::firebase::MutexLock lock(
        ::firebase::storage::g_storage_instances.mutex());

    ::firebase::storage::Storage* instance = url ?
        ::firebase::storage::Storage::GetInstance(app, url, init_result_out) :
        ::firebase::storage::Storage::GetInstance(app, init_result_out);
    // Increment the reference to this instance.
    ::firebase::storage::g_storage_instances.AddReference(instance);
    return instance;
  }

  // Release and decrement the reference count to a C++ instance
  %csmethodmodifiers ReleaseReferenceInternal(::firebase::storage::Storage* instance) "internal";
  static void ReleaseReferenceInternal(::firebase::storage::Storage* instance) {
    ::firebase::storage::g_storage_instances.ReleaseReference(instance);
  }
}

%rename("FirebaseStorageInternal") firebase::storage::Storage;

// Generate a Task wrapper around storage specific Future<T> types.
%SWIG_FUTURE(Future_StorageMetadata, MetadataInternal, internal,
             firebase::storage::Metadata, FirebaseException)
%SWIG_FUTURE(Future_Size, long, internal, size_t, FirebaseException)


// TODO: Move this into App
// Generates an attribute from get and set methods returning null from the getter
// if the C++ get method returns null.
%define %safeattributestring(Class, AttributeType, AttributeName, GetMethod, SetMethod...)
  %{
static AttributeType& %mangle(Class) ##_## AttributeName ## _get_func(Class* self) {
  auto val = self->GetMethod();
  return *new AttributeType(val ? val : "");
}

#define %mangle(Class) ##_## AttributeName ## _get(self_) \
  %mangle(Class) ##_## AttributeName ## _get_func(self_)
  %}
  #if #SetMethod != ""
    %{
      #define %mangle(Class) ##_## AttributeName ## _set(self_, val_) \
        self_->SetMethod(val_)
    %}
    #if #SetMethod != #AttributeName
      %ignore Class::SetMethod;
    #endif
  #else
    %immutable Class::AttributeName;
  #endif
  %ignore Class::GetMethod();
  %ignore Class::GetMethod() const;
  %newobject Class::AttributeName;
  %typemap(newfree) const AttributeType* AttributeName "delete $1;"
  %extend Class {
    AttributeType AttributeName;
  }
%enddef

// Configure properties for get / set methods on the Storage class.
%attribute(firebase::storage::Storage, firebase::App*, App, app);
%attributestring(firebase::storage::Storage, std::string, Url, url);
%attribute(firebase::storage::Storage, double, MaxDownloadRetryTime,
           max_download_retry_time, set_max_download_retry_time);
%attribute(firebase::storage::Storage, double, MaxUploadRetryTime,
           max_upload_retry_time, set_max_upload_retry_time);
%attribute(firebase::storage::Storage, double, MaxOperationRetryTime,
           max_operation_retry_time, set_max_operation_retry_time);

%typemap(cscode) firebase::storage::Storage %{
  // Generates a key that uniquely identifies this instance.
  internal string InstanceKey { get { return App.Name + Url; } }

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
      firebase::storage::Storage {
    lock (FirebaseApp.disposeLock) {
      ReleaseReferenceInternal(this);
      swigCMemOwn = false;
      swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
          null, global::System.IntPtr.Zero);
    }
}

%rename("StorageReferenceInternal") firebase::storage::StorageReference;
// Configure properties for get / set methods on the StorageReference
// class.
%attribute(firebase::storage::StorageReference, firebase::storage::Storage*,
           Storage, storage);
%attributestring(firebase::storage::StorageReference, std::string,
                 Bucket, bucket);
%attributestring(firebase::storage::StorageReference, std::string,
                 FullPath, full_path);
%attributestring(firebase::storage::StorageReference, std::string,
                     Name, name);
%attribute(firebase::storage::StorageReference, bool, IsValid, is_valid);
// All read and write methods need to be customized as methods on the generated
// C# proxy for Listener are not from the underlying C++ object.
%ignore firebase::storage::StorageReference::GetFile;
%ignore firebase::storage::StorageReference::GetBytes;
%ignore firebase::storage::StorageReference::PutBytes;
%ignore firebase::storage::StorageReference::PutFile;
// Remove the copy operator as the proxy uses the copy constructor.
%ignore firebase::storage::StorageReference::operator=(const StorageReference&);

%extend firebase::storage::StorageReference {
  // Fetch buffer_size bytes into buffer using monitor_controller to monitor
  // progress and optionally control the stream.
  firebase::Future<size_t> GetBytesUsingMonitorController(
      void* buffer, size_t buffer_size,
      firebase::storage::MonitorController *monitor_controller) {
    return self->GetBytes(buffer, buffer_size, monitor_controller,
                          firebase::storage::MonitorController::GetController(
                              monitor_controller));
  }

  // Read the storage reference into a local file at path using
  // monitor_controller to monitor progress and optionally control the stream.
  firebase::Future<size_t> GetFileUsingMonitorController(
      const char* path,
      firebase::storage::MonitorController *monitor_controller) {
    return self->GetFile(path, monitor_controller,
                         firebase::storage::MonitorController::GetController(
                             monitor_controller));
  }

  // Write buffer_size bytes from buffer to the storage reference using
  // monitor_controller to monitor progress and optionally control the stream.
  firebase::Future<firebase::storage::Metadata> PutBytesUsingMonitorController(
      const void* buffer, size_t buffer_size,
      const firebase::storage::Metadata* metadata,
      firebase::storage::MonitorController* monitor_controller) {
    firebase::storage::Controller* controller =
        firebase::storage::MonitorController::GetController(monitor_controller);
    return metadata == nullptr ?
        self->PutBytes(buffer, buffer_size, monitor_controller, controller) :
        self->PutBytes(buffer, buffer_size, *metadata, monitor_controller,
                       controller);
  }

  // Write the local file at path to the storage reference using
  // monitor_controller to monitor progress and optionally control the stream.
  firebase::Future<firebase::storage::Metadata> PutFileUsingMonitorController(
      const char* path,
      const firebase::storage::Metadata* metadata,
      firebase::storage::MonitorController* monitor_controller) {
    firebase::storage::Controller* controller =
        firebase::storage::MonitorController::GetController(monitor_controller);
    return metadata == nullptr ?
        self->PutFile(path, monitor_controller, controller) :
        self->PutFile(path, *metadata, monitor_controller, controller);
  }
}

%rename("MetadataInternal") firebase::storage::Metadata;
// Configure properties for get / set methods on the Metadata class.
%safeattributestring(firebase::storage::Metadata, std::string, Bucket, bucket);
%safeattributestring(firebase::storage::Metadata, std::string, CacheControl,
                     cache_control, set_cache_control);
%safeattributestring(firebase::storage::Metadata, std::string, ContentDisposition,
                     content_disposition, set_content_disposition);
%safeattributestring(firebase::storage::Metadata, std::string, ContentEncoding,
                     content_encoding, set_content_encoding);
%safeattributestring(firebase::storage::Metadata, std::string, ContentLanguage,
                     content_language, set_content_language);
%safeattributestring(firebase::storage::Metadata, std::string, ContentType,
                     content_type, set_content_type);
%attribute(firebase::storage::Metadata, int64_t, CreationTime, creation_time);
// The map returned by firebase::storage::Metadata::custom_metadata is mutable
// so we define a setter (set_custom_metadata) so the a map can be
// explicitly set by the user of the class.  By default SWIG will return a
// StringStringMap (very nice) which is unfortunately a copy of the data
// structure.
%csmethodmodifiers firebase::storage::Metadata::custom_metadata "private";
%rename("GetCustomMetadata") firebase::storage::Metadata::custom_metadata;
%csmethodmodifiers firebase::storage::Metadata::SetCustomMetadata "private";
%attribute(firebase::storage::Metadata, int64_t, Generation, generation);
%attribute(firebase::storage::Metadata, int64_t, MetadataGeneration,
           metadata_generation);
%safeattributestring(firebase::storage::Metadata, std::string, Name, name);
%safeattributestring(firebase::storage::Metadata, std::string, Path, path);
%attribute(firebase::storage::Metadata, int64_t, SizeBytes, size_bytes);
%attribute(firebase::storage::Metadata, int64_t, UpdatedTime, updated_time);
%safeattributestring(firebase::storage::Metadata, std::string, Md5Hash, md5_hash);
%attribute(firebase::storage::Metadata, bool, IsValid, is_valid);

%extend firebase::storage::Metadata {
  // Implementation of custom_metadata that allows the user to set
  // values in the map.
  void SetCustomMetadata(
      std::map<std::string, std::string>* user_metadata) {
    auto* mutable_metadata = self->custom_metadata();
    if (mutable_metadata) *mutable_metadata = *user_metadata;
  }

  // Create a copy of metadata.
  // The wrapper code SWIG generates for this method will copy the object
  // and take ownership of the object it allocates on the heap.
  firebase::storage::Metadata CopyCppObject() {
    return *self;
  }
}

// Hook up accessors for to the CustomMetadata property.
%typemap(cscode) firebase::storage::Metadata %{
  // This *must* be set to prevent MetadataInternal from being deallocated
  // when all references to FirebaseStorage are removed.
  internal FirebaseStorage storage;

  // Users of this class *must* use this constructor to prevent the C++
  // object from being deallocated.
  public MetadataInternal(FirebaseStorage storage) : this() {
    this.storage = storage;
  }

  public StringStringMap CustomMetadata {
    get { return GetCustomMetadata(); }
    set { SetCustomMetadata(value); }
  }

  // Copy this object.
  public MetadataInternal Copy() {
    var newMetadata = CopyCppObject();
    newMetadata.storage = storage;
    return newMetadata;
  }
%}

// Remove the copy operator as the proxy uses the copy constructor.
%ignore firebase::storage::Metadata::operator=(const Metadata&);

// SWIG will generate a wrapper for listener which will simply result in
// callbacks landing in the C++ class rather than the C# interface.  Therefore,
// disable generation of this C# class and use a custom binding.
%ignore firebase::storage::Listener;

%rename("ControllerInternal") firebase::storage::Controller;
// Configure properties for get / set methods on the Controller class.
%attribute(firebase::storage::Controller, bool, IsPaused, is_paused);
%attribute(firebase::storage::Controller, int64_t, BytesTransferred,
           bytes_transferred);
%attribute(firebase::storage::Controller, int64_t, TotalByteCount,
           total_byte_count);
%attribute(firebase::storage::Controller, bool, IsValid, is_valid);
// Remove the copy operator as the proxy uses the copy constructor.
%ignore firebase::storage::Controller::operator=(const Controller&);

// TODO(smiles): All exceptions on tasks need to be propagated via
// StorageException rather than returning this error code.
%rename("ErrorInternal") firebase::storage::Error;

%rename("MonitorControllerInternal") firebase::storage::MonitorController;
%attribute(firebase::storage::MonitorController, firebase::storage::Controller*,
           Controller, controller);
%csmethodmodifiers
    firebase::storage::MonitorController::SetPausedEvent "private";
%csmethodmodifiers
    firebase::storage::MonitorController::SetProgressEvent "private";
%attribute(firebase::storage::MonitorController, int64_t, BytesTransferred,
           bytes_transferred);
%attribute(firebase::storage::MonitorController, int64_t, TotalByteCount,
           total_byte_count);

// Map MonitorController::Event to a C# delegate.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::storage::MonitorController::Event,
    Firebase.Storage.MonitorControllerInternal.MonitorControllerEventDelegate)

// Extend the Firebase.Storage.StorageInternal module class.
%typemap(cscode) firebase::storage::MonitorController %{
  // Called when a paused event occurs.
  public event System.EventHandler<System.EventArgs> Paused;
  // Called when a progress event occurs.
  public event System.EventHandler<System.EventArgs> Progress;

  // Delegate called from firebase::storage::MonitorController::Event.
  internal delegate void MonitorControllerEventDelegate(
      System.IntPtr controllerPtr);

  // Delegate that forwards to the Paused event.
  private System.Action forwardToPausedEvent;
  // Delegate that forwards to the Progress event.
  private System.Action forwardToProgressEvent;
  // Hold a reference to the storage reference that owns the data backing this
  // object.
  private StorageReferenceInternal storageReference;

  // Map of MonitorControllerInternal instances to proxies used to look
  // up the proxy from a C++ pointer in an event callback.
  // It is only possible to pass static methods to C/C++ methods when using
  // Mono AOT / IL2CPP.  When attempting to use delegate instances
  // (e.g closures) the IL2CPP runtime stops execution.
  private static readonly System.Collections.Generic.Dictionary<
    System.IntPtr, System.WeakReference> cPtrsToProxies =
      new System.Collections.Generic.Dictionary<System.IntPtr,
                                                System.WeakReference>();

  // Create a MonitorControllerInternal instance, attaching delegates
  // to redirect C++ pause and progress events to the Paused and Progress
  // members of the instance.
  public static MonitorControllerInternal Create(
      StorageReferenceInternal storageReference) {
    var proxy = new MonitorControllerInternal(storageReference);
    proxy.storageReference = storageReference;
    var cPtr = MonitorControllerInternal.getCPtr(proxy).Handle;
    lock (MonitorControllerInternal.cPtrsToProxies) {
      MonitorControllerInternal.cPtrsToProxies[cPtr] =
          new System.WeakReference(proxy, false);
    }
    // NOTE: References must be held in this instance to the forwardTo*Event
    // closures to prevent them from being garbage collected as Set*Event()
    // are unmanaged methods that do not hold references to the objects.
    proxy.forwardToPausedEvent = () => {
      if (proxy.Paused != null) {
        proxy.Paused(proxy, System.EventArgs.Empty);
      }
    };
    proxy.SetPausedEvent(OnPaused, cPtr);
    proxy.forwardToProgressEvent = () => {
      if (proxy.Progress != null) {
        proxy.Progress(proxy, System.EventArgs.Empty);
      }
    };
    proxy.SetProgressEvent(OnProgress, cPtr);
    return proxy;
  }

  // Forwards a paused event to subscribes of the Paused event.
  // Called from firebase::storage::MonitorController::OnPaused().
  [MonoPInvokeCallback(typeof(MonitorControllerInternal))]
  private static void OnPaused(System.IntPtr controllerPtr) {
    ExceptionAggregator.Wrap(() => {
        var proxy = ProxyFromCPtr(controllerPtr);
        if (proxy != null) proxy.forwardToPausedEvent();
      });
  }

  // Forwards a progress event to subscribes of the Progress event.
  // Called from firebase::storage::MonitorController::OnProgress().
  [MonoPInvokeCallback(typeof(MonitorControllerInternal))]
  private static void OnProgress(System.IntPtr controllerPtr) {
    ExceptionAggregator.Wrap(() => {
        var proxy = ProxyFromCPtr(controllerPtr);
        if (proxy != null) proxy.forwardToProgressEvent();
      });
  }

  // Lookup a proxy instance from a C/C++ pointer.
  private static MonitorControllerInternal ProxyFromCPtr(
      System.IntPtr controllerPtr) {
    System.WeakReference weakReference;
    lock (MonitorControllerInternal.cPtrsToProxies) {
      if (MonitorControllerInternal.cPtrsToProxies.TryGetValue(
              controllerPtr, out weakReference)) {
        return Firebase.FirebaseApp.WeakReferenceGetTarget(weakReference) as
          MonitorControllerInternal;
      }
    }
    return null;
  }

  // Register a cancellation token.
  public void RegisterCancellationToken(
      System.Threading.CancellationToken cancellationToken) {
    cancellationToken.Register(() => { Controller.Cancel(); });
  }
%}

// Remove the weak reference to this object when it's disposed.
// The boilerplate dispose logic is copied from the default template.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
    firebase::storage::MonitorController {
  lock (FirebaseApp.disposeLock) {
    if (swigCPtr.Handle != global::System.IntPtr.Zero) {
      lock (MonitorControllerInternal.cPtrsToProxies) {
        MonitorControllerInternal.cPtrsToProxies.Remove(swigCPtr.Handle);
      }
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


// SWIG doesn't follow #include statements so forward declare all classes here
// so that proxies can be created for classes that are referenced before they
// definition is parsed.
namespace firebase {
namespace storage {
class StorageReference;
class MonitorController;
}  // namespace storage
}  // namespace firebase

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namepsace.
// 516: Ignore warnings about overloaded methods being ignored.
// In all cases, the overload does the right thing e.g
// Storage::GetReference(const char*) used instead of
// Storage::GetReference(std::string&).
// 814: Globally disable warnings about methods not potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,516,844);

%include "storage/src/include/firebase/storage/common.h"
%include "storage/src/include/firebase/storage/controller.h"
%include "storage/src/include/firebase/storage/listener.h"
%include "storage/src/include/firebase/storage/metadata.h"
%include "storage/src/include/firebase/storage/storage_reference.h"
%include "storage/src/swig/monitor_controller.h"
%include "storage/src/include/firebase/storage.h"
