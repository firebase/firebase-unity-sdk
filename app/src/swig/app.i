// Copyright 2016 Google Inc. All Rights Reserved.
//
// Wraps the C++ Firebase App API. This API is generally used by other
// C++ interfaces, and is not meant to be used other than as a simple
// construct. So most of the general interface here is hidden other than
// the basic construction.
//
// swiglint: disable include-h-allglobals

%module AppUtil

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%feature("flatnested");
%include "null_check_this.i"
%include "serial_dispose.i"
/* %include "swig_docs_generated.i" */ // TODO: Generate

%include "stdint.i"

%pragma(csharp) moduleclassmodifiers="internal class"

%{
#if defined(__ANDROID__)
#include <jni.h>
#endif  // defined(__ANDROID__)

// Enable internally visible methods.
#if !defined(INTERNAL_EXPERIMENTAL)
#define INTERNAL_EXPERIMENTAL
#endif  // !defined(INTERNAL_EXPERIMENTAL)

#include <assert.h>
#include <sstream>

#include "app/src/include/firebase/app.h"
#include "app/src/callback.h"
#include "app/src/include/firebase/internal/mutex.h"
#include "app/src/include/firebase/internal/platform.h"
#include "app/src/app_common.h"
#include "app/src/log.h"
#include "app/src/util.h"

#include "app/src/cpp_instance_manager.h"

#if defined(__ANDROID__)
#include "app/src/google_play_services/availability_android.h"
#include "app/src/app_android.h"
#endif // defined(__ANDROID__)

namespace firebase {
#if defined(__ANDROID__)
extern JavaVM *g_jvm;

// Implemented in unity_main.cpp.
extern jobject UnityGetActivity(JNIEnv **env);
#endif  // defined(__ANDROID__)

// Reference count manager for C++ App instance, using pointer as the key for
// searching.
static CppInstanceManager<App> g_app_instances;

// Called from the FirebaseApp.DefaultName property.
static const char *AppGetDefaultName() {
  return kDefaultAppName;
}

static App *AppCreate(const AppOptions *options, const char *name) {
  App* app;
#if defined(__ANDROID__)
  JNIEnv* jni_env;
  jobject activity_local_ref = UnityGetActivity(&jni_env);
  if (name) {
    app = App::Create(*options, name, jni_env, activity_local_ref);
  } else if (options) {
    app = App::Create(*options, jni_env, activity_local_ref);
  } else {
    app = App::Create(jni_env, activity_local_ref);
  }
  jni_env->DeleteLocalRef(activity_local_ref);
#else
  if (name) {
    app = App::Create(*options, name);
  } else if (options) {
    app = App::Create(*options);
  } else {
    app = App::Create();
  }
#endif  // defined(__ANDROID__)
  if (!app) {
    std::stringstream ss;
    ss << kInitResultFailedMissingDependency;
    std::string error(ss.str());
    error += ": Firebase app creation failed.";
    SWIG_CSharpSetPendingException(SWIG_CSharpApplicationException,
                                   error.c_str());
    return nullptr;
  }
  // Handle module initialization failure here and throw
  // an ApplicationException which will be translated into a
  // Firebase.InitializationException in the C# Create() method with a
  // description of the failure (e.g "missing dependency") per module.
  const auto& init_results = app->init_results();
  std::string error;
  for (auto it = init_results.begin(); it != init_results.end(); ++it) {
    if (it->second != kInitResultSuccess) {
      if (!error.length()) {
        // Bake the error code into the start of the message so it can be
        // parsed out at the C# layer.
        std::stringstream ss;
        ss << it->second;
        error = ss.str();
        error += ": Firebase modules failed to initialize: ";
      } else {
        error += ", ";
      }
      error += it->first;
      switch (it->second) {
        case kInitResultSuccess:
          // Handling all cases so that a compile error will occur if new
          // error codes are added.
          break;
        case kInitResultFailedMissingDependency:
          error += " (missing dependency)";
          break;
      }
    }
  }
  if (error.length()) {
    SWIG_CSharpSetPendingException(SWIG_CSharpApplicationException,
                                   error.c_str());
    delete app;
    app = nullptr;
  }
  return app;
}

// Find the instance by name. If not found, create one with given option and
// name.  Also increate reference count by 1.
static App *AppGetOrCreateInstance(const AppOptions *options,
                                   const char *name) {
  // This mutex is to make sure AppGetOrCreateInstance() and AppReleaseReference() happening
  // atomically, since they are the only place to create/delete app from C#.
  MutexLock lock(g_app_instances.mutex());

  // If the name is nullptr, try to find the default instance.
  App *instance = name ? App::GetInstance(name) : App::GetInstance();
  if (!instance) instance = AppCreate(options, name);

  // Increment reference count to this instance.
  g_app_instances.AddReference(instance);

  return instance;
}

// Log a heartbeat only if the current platform is desktop platforms.
static void LogHeartbeatForDesktop(App* app) {
#if FIREBASE_PLATFORM_DESKTOP
    // Only need to call LogHeartbeat on desktop, since native mobile SDKs
    // Will handle heartbeat logging internally.
    app->LogHeartbeat();
#endif  // FIREBASE_PLATFORM_DESKTOP
}

static void RegisterLibrariesHelper(
        const std::map<std::string, std::string>& libraries) {
#if FIREBASE_PLATFORM_ANDROID
    JNIEnv* jni_env;
    jobject activity_local_ref = UnityGetActivity(&jni_env);
    firebase::CallAfterEnsureMethodsCached(
            jni_env, activity_local_ref, [&libraries, &jni_env](){
        for (std::map<std::string, std::string>::const_iterator it =
                libraries.begin(); it != libraries.end(); ++it) {
            const std::string& library = it->first;
            const std::string& version = it->second;
            firebase::App::RegisterLibrary(library.c_str(),
                                           version.c_str(),
                                           jni_env);
        }
    });
    jni_env->DeleteLocalRef(activity_local_ref);
#else
    for (std::map<std::string, std::string>::const_iterator it =
            libraries.begin(); it != libraries.end(); ++it) {
        const std::string& library = it->first;
        const std::string& version = it->second;
        firebase::App::RegisterLibrary(library.c_str(),
                                       version.c_str(),
                                       nullptr);
    }
#endif
}

// Decrease the reference count for the app. When the reference count reaches
// 0, the App will be deleted.
static void AppReleaseReference(App* app) {
  g_app_instances.ReleaseReference(app);
}

// Enable / disable all module initializers.
static void SetEnabledAllAppCallbacks(bool enable) {
  AppCallback::SetEnabledAll(enable);
}

// Enable / disable module initializers.
static void SetEnabledAppCallbackByName(std::string module_name,
                                        bool enable) {
  AppCallback::SetEnabledByName(module_name.c_str(), enable);
}

// Determine whether a module is enabled by name.
static bool GetEnabledAppCallbackByName(std::string module_name) {
  return AppCallback::GetEnabledByName(module_name.c_str());
}

// Pointer to the LogMessage delegate.
typedef void (SWIGSTDCALL* LogMessageDelegateFunc)(LogLevel level,
                                                   const char *message);
static LogMessageDelegateFunc g_log_message_delegate;  // NOLINT
static Mutex g_log_message_mutex;  // NOLINT

// Set the C# delegate to call from AppLogCallback().
static void SetLogFunction(LogMessageDelegateFunc log_message_delegate) {
  MutexLock lock(g_log_message_mutex);
  g_log_message_delegate = log_message_delegate;
}

// Used to chain log callbacks in AppLogCallback().
struct LogCallbackAndData {
  LogCallback callback;
  void *data;
};

// Log callback which logs to the unity log, sets an exception on an assert
// and continues execution.
static void AppLogCallback(LogLevel log_level, const char *log_message,
                           void* callback_data) {
  const LogCallbackAndData* callback_and_data =
      static_cast<LogCallbackAndData*>(callback_data);
  assert(callback_and_data);
  // Log to the default log.
  callback_and_data->callback(
      log_level < kLogLevelAssert ? log_level : kLogLevelError,
      log_message, callback_and_data->data);

  // Postpone the callback to C# to the main thread so that Mono will not attempt to kill the thread
  // which logged a message before during AppDomain unload.
  callback::AddCallbackWithThreadCheck(
      new callback::CallbackValue1String1<LogLevel>(log_level, log_message,
          [](const LogLevel level, const char* message){
    MutexLock lock(g_log_message_mutex);
    // Log via Unity's log if it's configured.
    if (g_log_message_delegate) g_log_message_delegate(level, message);
  }));

  if (log_level == kLogLevelAssert) {
    // TODO(smiles): Need to translate errors into more specific exceptions.
    SWIG_CSharpSetPendingException(SWIG_CSharpApplicationException,
                                   log_message);
  }
}

// Enable / disable log redirection to AppLogCallback().
static void AppEnableLogCallback(bool enable) {
  static LogCallbackAndData callback_and_data;
  LogSetCallback(nullptr, nullptr);
  callback_and_data.callback = LogGetCallback(&callback_and_data.data);
  if (enable) {
    LogSetCallback(AppLogCallback, &callback_and_data);
  }
}

// Expose log level to FirebaseHandler so that it's possible to filter log
// messages from C#.
static LogLevel AppGetLogLevel() { return firebase::GetLogLevel(); }

// Load app options from a JSON file, returns nullptr if it fails to load the
// config.
static firebase::AppOptions* AppOptionsLoadFromJsonConfig(const char* config) {
  return firebase::AppOptions::LoadFromJsonConfig(config);
}

}  // namespace firebase
%}

%rename(SetDatabaseUrlInternal) set_database_url;
%rename(GetDatabaseUrlInternal) database_url;
%csmethodmodifiers firebase::AppOptions::set_database_url(const char *url) "internal";
%csmethodmodifiers firebase::AppOptions::database_url() "internal";

%rename(AppOptionsInternal) firebase::AppOptions;
%typemap(cscode) firebase::AppOptions %{
  /// The database root URL, e.g. @"http://abc-xyz-123.firebaseio.com".
  public System.Uri DatabaseUrl {
    get {
      return Firebase.FirebaseApp.UrlStringToUri(GetDatabaseUrlInternal());
    }
    set {
      SetDatabaseUrlInternal(Firebase.FirebaseApp.UriToUrlString(value));
    }
  }
%}

%include "attribute.i"
%include "std_map.i"
%include "std_vector.i"
%include "std_string.i"

// Globally enable exception checking.
%feature("except:canthrow", "1");
// Globally disable warnings about methods not potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(844);
// Ignore all methods ending in LastResult.
%rename("$ignore", regextarget=1) "LastResult$";
%rename("$ignore", regextarget=1) "LastResult_DEPRECATED$";

%rename(FirebaseApp) firebase::App;
%typemap(csclassmodifiers) firebase::App "public sealed class";

%ignore firebase::kDefaultAppName;

%rename(IsDataCollectionDefaultEnabledInternal)
    IsDataCollectionDefaultEnabled;
%rename(SetDataCollectionDefaultEnabledInternal)
    SetDataCollectionDefaultEnabled;

%csmethodmodifiers firebase::app::IsDataCollectionDefaultEnabled()
    "private"
%csmethodmodifiers firebase::app::SetDataCollectionDefaultEnabled(bool)
    "private"

%typemap(csclassmodifiers) firebase::FutureBase "internal class"
%typemap(csclassmodifiers) firebase::FutureStatus "internal enum"
%include "app/src/swig/future.i"

%typemap(csclassmodifiers) std::map<std::string, std::string> "internal class"
%template(StringStringMap) std::map<std::string, std::string>;
%typemap(csclassmodifiers) std::vector<std::string> "internal class"
%template(StringList) std::vector<std::string>;
// CharVector is now only used by remote_config. Consider moving it there
// and/or removing it completely in favor of Marshal.Copy
%typemap(csclassmodifiers) std::vector<unsigned char> "internal class"
%template(CharVector) std::vector<unsigned char>;

%SWIG_FUTURE(FutureString, string, internal,
             std::string, FirebaseException)  // Future<std::string>
#ifndef USE_FIRESTORE_FUTURE_VOID
%SWIG_FUTURE(FutureVoid, void, internal, void, FirebaseException)
#endif  // USE_FIRESTORE_FUTURE_VOID
%SWIG_FUTURE(FutureBool, bool, internal, bool, FirebaseException) // Future<bool>

// Internal
%ignore firebase::AppOptions::ga_tracking_id() const;
%ignore firebase::AppOptions::set_ga_tracking_id(const char* id);

%typemap(csclassmodifiers) firebase::AppOptions "public sealed class";

%attribute(firebase::AppOptions, const char *, AppId, app_id, set_app_id);
%attribute(firebase::AppOptions, const char *, ApiKey, api_key, set_api_key);
%attribute(firebase::AppOptions, const char *, MessageSenderId,
           messaging_sender_id, set_messaging_sender_id);
%attribute(firebase::AppOptions, const char *, StorageBucket,
           storage_bucket, set_storage_bucket);
%attribute(firebase::AppOptions, const char *, ProjectId,
           project_id, set_project_id);
%attribute(firebase::AppOptions, const char *, PackageName,
           package_name, set_package_name);
%attribute(firebase::App, const char *, NameInternal, name);
// Remove GetInstance() as it's replaced by the DefaultInstance property below.
%ignore firebase::App::GetInstance();
%ignore firebase::App::GetInstance(const char*);
// Remove GetApps() until it is ready to be released.
%ignore firebase::App::GetApps();

%typemap(cscode) firebase::App %{
  // Static constructor to initialize C# logging
  static FirebaseApp() {
    LogUtil.InitializeLogging();
  }

  // Global lock to serialize Dispose() methods.
  static readonly internal System.Object disposeLock = new System.Object();

  // Cached name of the app so that it's available after it has been disposed.
  private string name;

  // Execute a closure rethrowing either DllNotFoundException or
  // TypeInitializationException as a Firebase.InitializationException.
  internal static void TranslateDllNotFoundException(
      System.Action closureToExecute) {
    try {
      closureToExecute();
    } catch (System.Exception exception) {
      if (exception.GetBaseException() is System.DllNotFoundException) {
        throw new Firebase.InitializationException(
            Firebase.InitResult.FailedMissingDependency,
            Firebase.ErrorMessages.DllNotFoundExceptionErrorMessage);
      }
      throw;
    }
  }

  /// @brief Get the default FirebaseApp instance.
  ///
  /// @return Reference to the default app, if it hasn't been created this
  /// method will create it.
  public static FirebaseApp DefaultInstance {
    get {
      FirebaseApp app = GetInstance(DefaultName);
      return app != null ? app : Create();
    }
  }

  /// @brief Get an instance of an app by name.
  ///
  /// @param name Name of the app to retrieve.
  ///
  /// @return Reference to the app if it was previously created, null otherwise.
  public static FirebaseApp GetInstance(string name) {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    FirebaseApp app = null;
    lock (nameToProxy) {
      if (nameToProxy.TryGetValue(name, out app)) {
        if (app == null) nameToProxy.Remove(name);
      }
      return app;
    }
  }

  /// @brief Initializes the default FirebaseApp with default options.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create() {
    return CreateAndTrack(() => { return CreateInternal(); },
                          GetInstance(DefaultName));
  }

  /// @brief Initializes the default FirebaseApp with the given options.
  ///
  /// @param[in] options Options that control the creation of the FirebaseApp.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create(AppOptions options) {
    return CreateAndTrack(() => {
        return CreateInternal(options.ConvertToInternal()); },
      GetInstance(DefaultName));
  }

  /// @brief Initializes a FirebaseApp with the given options that operate
  /// on the named app.
  ///
  /// @param[in] options Options that control the creation of the FirebaseApp.
  /// @param[in] name Name of this FirebaseApp instance.  This is only
  /// required when one application uses multiple FirebaseApp instances.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create(AppOptions options, string name) {
    return CreateAndTrack(() => {
        return CreateInternal(options.ConvertToInternal(), name); },
      GetInstance(name));
  }

  /// Get the name of this App instance.
  ///
  /// @returns The name of this App instance.  If a name wasn't provided via
  /// Create(), this returns @ref DefaultName.
  public string Name {
    get {
      // If the App was created from the C++ object, name will not have been
      // set, so check NameInternal to get it.
      if (name == null) {
        ThrowIfNull();
        name = NameInternal;
      }
      return name;
    }
  }

  /// @brief Gets or sets the minimum log verbosity level for Firebase features.
  ///
  /// - LogLevel.Verbose allows all log messages to be displayed.
  /// - LogLevel.Assert prevents displaying all but fatal errors.
  ///
  /// @note Some Firebase plugins may require you to set their LogLevel separately.
  ///
  /// @return The current LogLevel.
  public static LogLevel LogLevel {
    // NOTE: If a method sets a pending exception then logs an error
    // calling the non-PINVOKE method will incorrectly consume and throw the
    // exception.  To workaround this issue, we're calling the PINVOKE method
    // directly here as we know that these functions can *never* fail.
    get { return (LogLevel)$modulePINVOKE.FirebaseApp_GetLogLevelInternal(); }
    set { $modulePINVOKE.FirebaseApp_SetLogLevelInternal((int)value); }
  }

  /// @brief Event raised when FirebaseApp is disposed.
  ///
  internal event System.EventHandler AppDisposed;

#if INTERNAL_EXPERIMENTAL
  /// Gets or sets whether automatic data collection is enabled for all
  /// products.
  ///
  /// By default, automatic data collection is enabled. To disable automatic
  /// data collection in your mobile app, add to your Android application's
  /// manifest:
  ///
  /// @if NOT_DOXYGEN
  ///   <meta-data android:name="firebase_data_collection_default_enabled"
  ///   android:value="false" />
  /// @else
  /// @code
  ///   &lt;meta-data android:name="firebase_data_collection_default_enabled"
  ///   android:value="false" /&gt;
  /// @endcode
  /// @endif
  ///
  /// or on iOS or tvOS to your Info.plist:
  ///
  /// @if NOT_DOXYGEN
  ///   <key>FirebaseDataCollectionDefaultEnabled</key>
  ///   <false/>
  /// @else
  /// @code
  ///   &lt;key&gt;FirebaseDataCollectionDefaultEnabled&lt;/key&gt;
  ///   &lt;false/&gt;
  /// @endcode
  /// @endif
  ///
  /// Once your mobile app is set to disable automatic data collection, you can
  /// ask users to consent to data collection, and then enable it after their
  /// approval by calling this method.
  ///
  /// This value is persisted across runs of the app so that it can be set once
  /// when users have consented to collection.
#endif   // INTERNAL_EXPERIMENTAL
  private bool IsDataCollectionDefaultEnabled {
    get {
      return this.IsDataCollectionDefaultEnabledInternal();
    }
    set {
      this.SetDataCollectionDefaultEnabledInternal(value);
    }
  }

  // Mirrors the map of app names to app instances in the C++ app
  // implementation.
  private static System.Collections.Generic.Dictionary<
    string, FirebaseApp> nameToProxy =
      new System.Collections.Generic.Dictionary<
        string, FirebaseApp>();

  // Dictionary which maps each C++ app to a C# proxy object.  This is used
  // to provide a 1:1 mapping between each C++ app and C# app instance.  By
  // default SWIG will instance a new object under the proxy which doesn't work
  // with firebase::App instances that are shared across the application.
  // NOTE: nameToProxy is used as a mutex / lock for this dictionary.
  private static System.Collections.Generic.Dictionary<
    System.IntPtr, FirebaseApp> cPtrToProxy =
      new System.Collections.Generic.Dictionary<
        System.IntPtr, FirebaseApp>();

  // Dispose all created app.
  internal static void DisposeAllApps() {
    System.Collections.Generic.List<FirebaseApp> appsToDispose =
        new System.Collections.Generic.List<FirebaseApp>();
    lock (nameToProxy) {
      foreach(var keyValue in nameToProxy) {
        appsToDispose.Add(keyValue.Value);
      }
    }
    foreach(FirebaseApp app in appsToDispose) {
      app.Dispose();
    }
  }

  // Add / replace a firebase::App / proxy reference.
  private void AddReference() {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    lock (nameToProxy) {
      swigCMemOwn = true;
      nameToProxy[Name] = this;
      cPtrToProxy[swigCPtr.Handle] = this;
    }
  }

  // Destroy the C++ firebase::App and remove references from cPtrToProxy and
  // nameToProxy.
  private void RemoveReference() {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    lock (nameToProxy) {
      System.IntPtr cPtr = swigCPtr.Handle;
      if (cPtr != System.IntPtr.Zero) {
        System.GC.SuppressFinalize(this);
        if (swigCMemOwn) {
          int previousAppCount = nameToProxy.Count;

          // Remove the app from the tracking objects.
          cPtrToProxy.Remove(cPtr);
          nameToProxy.Remove(Name);
          // Instead of directly deleting the C++ instance, dereference it.
          ReleaseReferenceInternal(this);

          // If the amount of active Apps falls to zero, inform the callback
          // that all Apps have been destroyed.
          // Note: We don't just check if currently zero because DestroyAll
          // clears the list before deleting each one, which would cause the
          // callback to happen multiple times.
          if (previousAppCount > 0 && nameToProxy.Count == 0) {
            OnAllAppsDestroyed();
          }
        }
      }
      swigCMemOwn = false;
      swigCPtr = new System.Runtime.InteropServices.HandleRef(
          null, System.IntPtr.Zero);
    }
  }

  // Throw a NullReferenceException if the proxy references a null pointer.
  private void ThrowIfNull() {
    if (swigCPtr.Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException("App has been disposed");
    }
  }

  // Tracks if the AppUtil callbacks have been initialized.
  private static bool AppUtilCallbacksInitialized = false;

  // A lock object to use when initializing/terminating app callbacks.
  private static object AppUtilCallbacksLock = new object();

  // Configuration to enable a C++ module.
  public class EnableModuleParams {
    // Name of the C++ module to enable.
    public string CppModuleName { get; set; }
    // Name of the C# class that is required to interact with the module.
    public string CSharpClassName { get; set; }
    // Whether to always enable the module regardless of the C# class being present.
    public bool AlwaysEnable { get; set; }

    // Construct an instance.
    public EnableModuleParams(string csharp, string cpp, bool always = false) {
      CSharpClassName = csharp;
      CppModuleName = cpp;
      AlwaysEnable = always;
    }
  }

  // Initialize the callbacks that go through AppUtil, such as logging.
  private static void InitializeAppUtilCallbacks() {
    lock (AppUtilCallbacksLock) {
      if (AppUtilCallbacksInitialized) return;

      // On non-Android platforms, enable auto-initialization of all modules
      // with accompanying C# code.
      // On Android, modules are initially enabled based upon the corresponding
      // Java component being included.  If the C# component of a module is
      // missing on Android, this disables the module.
      EnableModuleParams[] modules = {
        new EnableModuleParams(
          "Firebase.Analytics.FirebaseAnalytics, Firebase.Analytics",
          "analytics"
        ),
        new EnableModuleParams(
          "Firebase.AppCheck.FirebaseAppCheck, Firebase.AppCheck",
          "app_check"
        ),
        new EnableModuleParams(
          "Firebase.Auth.FirebaseAuth, Firebase.Auth",
          "auth"
        ),
        new EnableModuleParams(
          "Firebase.Crashlytics.Crashlytics, Firebase.Crashlytics",
          "crashlytics"
        ),
        new EnableModuleParams(
          "Firebase.Database.FirebaseDatabase, Firebase.Database",
          "database"
        ),
        new EnableModuleParams(
          "Firebase.DynamicLinks.DynamicLinks, Firebase.DynamicLinks",
          "dynamic_links"
        ),
        new EnableModuleParams(
          "Firebase.Functions.FirebaseFunctions, Firebase.Functions",
          "functions"
        ),
        new EnableModuleParams(
          "Firebase.Installations.FirebaseInstallations, Firebase.Installations",
          "installations"
        ),
        new EnableModuleParams(
          "Firebase.Invites.FirebaseInvites, Firebase.Invites",
          "invites"
        ),
        new EnableModuleParams(
          "Firebase.Messaging.FirebaseMessaging, Firebase.Messaging",
          "messaging"
        ),
        new EnableModuleParams(
          "Firebase.Performance.FirebasePerformance, Firebase.Performance",
          "performance"
        ),
        new EnableModuleParams(
          "Firebase.RemoteConfig.FirebaseRemoteConfig, Firebase.RemoteConfig",
          "remote_config"
        ),
        new EnableModuleParams(
          "Firebase.Storage.FirebaseStorage, Firebase.Storage",
          "storage"
        ),
        new EnableModuleParams(
          "Firebase.TestLab.GameLoop, Firebase.TestLab",
          "test_lab",
          true // Test Lab doesn't have a Java component
        ),
      };
      bool isAndroid = Firebase.Platform.PlatformInformation.IsAndroid;
      bool enableAll = false;
      if (!isAndroid) {
        // This is required as the C++ shared library can stay resident in
        // memory while the C# component is reloaded.  This handles C#
        // components being dynamically removed in a project in the Unity
        // editor.
        AppUtil.SetEnabledAllAppCallbacks(false);
        enableAll = true;
      }
      foreach (var module in modules) {
        bool csharpClassPresent = false;
        try {
          csharpClassPresent = System.Type.GetType(module.CSharpClassName) != null;
        } catch (System.Exception) {
          // Ignore any exceptions caused by type loading.
        }
        bool moduleCurrentlyEnabled =
            AppUtil.GetEnabledAppCallbackByName(module.CppModuleName);
        bool enableModule = csharpClassPresent &&
                            (enableAll || moduleCurrentlyEnabled || module.AlwaysEnable);
        if (enableModule != moduleCurrentlyEnabled) {
          LogUtil.LogMessage(Firebase.LogLevel.Debug,
                             System.String.Format("{0} module '{1}' for '{2}'",
                                          enableModule ? "Enable" : "Disable",
                                          module.CppModuleName,
                                          module.CSharpClassName));
        }
        AppUtil.SetEnabledAppCallbackByName(module.CppModuleName, enableModule);
      }

      AppUtilCallbacksInitialized = true;
    }
  }

  // Normally, when all the apps are destroyed, OnAllAppsDestroyed is called,
  // which performs some clean up logic.
  // However, there are some cases, such as when resetting the default App
  // after updating Google Play Services, that we plan to immediately recreate
  // an app.
  // If this is set to true, the clean up logic will not occur, preventing the
  // need to immediately recreate everything.
  private static bool PreventOnAllAppsDestroyed = false;

  // Called when all FirebaseApp objects are destroyed, to preform cleanup
  // on systems that are set up when constructing FirebaseApps, such as
  // the Log function.
  private static void OnAllAppsDestroyed() {
    if (PreventOnAllAppsDestroyed || nameToProxy.Count > 0) return;

    lock (AppUtilCallbacksLock) {
      if (AppUtilCallbacksInitialized) {
        if (!Firebase.Platform.PlatformInformation.IsAndroid) {
          AppUtil.SetEnabledAllAppCallbacks(false);
        }

        AppUtilCallbacksInitialized = false;
      }
    }
  }

  // Convert a URL string to System.Uri returning null if the string is empty / an invalid Uri.
  internal static System.Uri UrlStringToUri(string urlString) {
      if (System.String.IsNullOrEmpty(urlString)) return null;
      try {
        return new System.Uri(urlString);
      } catch (System.UriFormatException) {
        return null;
      }
  }

  // Convert a System.Uri to a URL string.  If the uri is null this returns an empty string.
  internal static string UriToUrlString(System.Uri uri) {
    return uri != null ? uri.OriginalString : "";
  }

  // Get the target of a WeakReference returning null if the handle is invalid.
  // This will also return null if the specified reference is null.
  internal static object WeakReferenceGetTarget(System.WeakReference weakReference) {
    if (weakReference != null) {
      try {
        return weakReference.Target;
      } catch (System.InvalidOperationException) {
        // Target will throw if the object is finalized while using the accessor.
        return null;
      }
    }
    return null;
  }

  // Initialize Crashlytics via reflection.
  // Returns true if Crashlytics was successfully initialized and false
  // if Crashlytics was not installed.
  private static bool InitializeCrashlyticsIfPresent() {
    try {
      System.Reflection.Assembly crashAssembly =
          System.Reflection.Assembly.Load("Firebase.Crashlytics");
      System.Type crashlytics =
          crashAssembly.GetType("Firebase.Crashlytics.Crashlytics");
      if (crashlytics == null) {
        throw new Firebase.InitializationException(
          Firebase.InitResult.FailedMissingDependency,
          "Crashlytics initialization failed. " +
            "Could not find Crashlytics class.");
      }
      System.Reflection.MethodInfo initMethod =
          crashlytics.GetMethod("Initialize",
                                System.Reflection.BindingFlags.NonPublic |
                                System.Reflection.BindingFlags.Static);
      if (initMethod == null) {
        throw new Firebase.InitializationException(
          Firebase.InitResult.FailedMissingDependency,
          "Crashlytics initialization failed. " +
            "Could not find Crashlytics initializer.");
      }
      initMethod.Invoke(null, null);
    } catch (System.IO.FileNotFoundException) {
      // Crashlytics is not installed, carry on.
      return false;
    } catch (System.Exception ex) {
      throw new Firebase.InitializationException(
          Firebase.InitResult.FailedMissingDependency,
          "Crashlytics initialization failed with an unexpected error.",
          ex);
    }
    return true;
  }

  // Delegate that creates an app.
  private delegate FirebaseApp CreateDelegate();

  // Whether Crashlytics initialization has been attempted.
  private static bool crashlyticsInitializationAttempted = false;

  private static bool userAgentRegistered = false;

  // Instantiates a C++ App and returns a reference to the C# proxy.
  // existingProxy is required here to prevent the finalizer being executed
  // while we're creating a new FirebaseApp proxy.
  private static FirebaseApp CreateAndTrack(CreateDelegate createDelegate,
                                            FirebaseApp existingProxy) {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    bool initializeCrashlytics = false;
    Firebase.Platform.FirebaseHandler.Create(
        Firebase.Platform.FirebaseAppUtils.Instance);
    FirebaseApp newProxy;

    lock (nameToProxy) {
      // If this is the first App, register library information.
      if (!userAgentRegistered) {
        userAgentRegistered = true;
        var userAgentMap = new StringStringMap();

        // fire-(unity|mono)/<sdk_version>
        var libraryPrefix = "fire-" +
            Firebase.Platform.PlatformInformation.RuntimeName;
        userAgentMap[libraryPrefix] =
            Firebase.VersionInfo.SdkVersion;
        // fire-(unity|mono)-ver/<unity|mono_version>
        userAgentMap[libraryPrefix + "-ver"] =
            Firebase.Platform.PlatformInformation.RuntimeVersion;
        // fire-(unity|mono)/<github-action-built|custom_built>
        userAgentMap[libraryPrefix + "-buildsrc"] =
            Firebase.VersionInfo.BuildSource;

        RegisterLibrariesInternal(userAgentMap);
      }

      InitializeAppUtilCallbacks();
      var cPtrHandleRef = new System.Runtime.InteropServices.HandleRef(
          null, System.IntPtr.Zero);
      try {
        AppSetDefaultConfigPath(
            Firebase.Platform.PlatformInformation.DefaultConfigLocation);
        newProxy = createDelegate();
        // SWIG's attempts to determine whether a method can throw exceptions
        // is very flakey.  In particular when generating the Create() method
        // (no arguments), no exception check is inserted.  Therefore, force the
        // exception check here to be sure.
        if ($modulePINVOKE.SWIGPendingException.Pending) {
          throw $modulePINVOKE.SWIGPendingException.Retrieve();
        }
        // Proxy creation shouldn't fail without setting a pending exception, but if
        // it does throw an initialization exception to force cleanup.
        if (newProxy == null) {
          throw new Firebase.InitializationException(
              Firebase.InitResult.FailedMissingDependency,
              "App creation failed with an unknown error.");
        }
        cPtrHandleRef = FirebaseApp.getCPtr(newProxy);
      } catch (System.ApplicationException exception) {
        // Stop the mono behavior or update thread and tear down logging.
        OnAllAppsDestroyed();
        // Convert an initialization exception to a Firebase exception.
        string errorMessage = exception.Message;
        Firebase.InitResult initResult =
            Firebase.InitResult.FailedMissingDependency;
        // Parse the InitResult value from the exception message.
        int errorCodeOffset = errorMessage.IndexOf(":");
        if (errorCodeOffset >= 0) {
          initResult = (Firebase.InitResult)System.Int32.Parse(
              errorMessage.Substring(0, errorCodeOffset));
          errorMessage = errorMessage.Substring(errorCodeOffset + 1);
        }
        // TODO(smiles): Improve error code passing from C++ to C# so that
        // we don't need to parse magic strings from the error message.

        if (errorMessage.IndexOf("Please verify the AAR") >= 0) {
          errorMessage += "\n" + Firebase.ErrorMessages.DependencyNotFoundErrorMessage;
        }

        //         util_android.cc)
        throw new Firebase.InitializationException(initResult, errorMessage);
      } catch (System.Exception exception) {
        // Stop the mono behavior or update thread and tear down logging.
        OnAllAppsDestroyed();
        throw exception;
      }
      // On failure return null.
      if (cPtrHandleRef.Handle == System.IntPtr.Zero) {
        return null;
      }
      // If the app proxy already exists, just return it.
      FirebaseApp existingProxyForNewApp = null;
      if (cPtrToProxy.TryGetValue(cPtrHandleRef.Handle,
                                  out existingProxyForNewApp)) {
        if (existingProxyForNewApp != null) {
          // This *should* never occur.
          if (existingProxy != existingProxyForNewApp) {
            LogUtil.LogMessage(Firebase.LogLevel.Warning,
                               System.String.Format(
                               "Detected multiple FirebaseApp proxies for {0}",
                               existingProxy.Name));
            existingProxy.Dispose();
          }
          return existingProxyForNewApp;
        }
      }

      // Log a heartbeat after all Unity user agents have been registered.
      LogHeartbeatInternal(newProxy);

      // Cache the name so that it can be accessed after the app is disposed.
      newProxy.name = newProxy.NameInternal;
      // By default the newly created proxy doesn't own the C++ app, take
      // ownership here.
      newProxy.AddReference();
      if (!crashlyticsInitializationAttempted && !IsCheckDependenciesRunning()) {
        initializeCrashlytics = true;
        crashlyticsInitializationAttempted = true;
      }
    }

    // We initialize Crashlytics outside the lock because Crashlytics gets a
    // reference to FirebaseApp DefaultInstance
    if (initializeCrashlytics) {
      Firebase.Platform.FirebaseHandler.RunOnMainThread(() => {
        // Crashlytics initialization must be run on the Unity main thread.
        return InitializeCrashlyticsIfPresent();
      });
    }

    return newProxy;
  }

  // Used to indicate that no thread is currently running CheckDependencies.
  private const int CheckDependenciesNoThread = -1;
  // Used to indicate that a call to CheckDependencies is occurring, but the
  // thread has not yet been created to perform it.
  private const int CheckDependenciesPendingThread = -2;
  // Used to determine which thread is running CheckDependencies.
  private static int CheckDependenciesThread = CheckDependenciesNoThread;
  // Lock for the CheckDependenciesThread, as it could be incorrectly called
  // multiple times.
  private static object CheckDependenciesThreadLock = new object();

  // Set the thread id to indicate if something is running CheckDependencies.
  // Throws an exception if trying to set the value from an invalid context.
  private static void SetCheckDependenciesThread(int threadId) {
    lock (CheckDependenciesThreadLock) {
      if (CheckDependenciesThread == CheckDependenciesNoThread ||
          CheckDependenciesThread == CheckDependenciesPendingThread ||
          CheckDependenciesThread ==
              System.Threading.Thread.CurrentThread.ManagedThreadId) {
        CheckDependenciesThread = threadId;
      } else {
        throw new System.InvalidOperationException(
            "Don't call other Firebase functions while CheckDependencies is running.");
      }
    }
  }

  // While CheckDependencies is running, no other Firebase calls should
  // be occurring. Primarily, if the Unity main thread tries to use Firebase
  // features, it can result in locking the main thread, which CheckDependencies
  // relies on, but along with that CheckDependencies tears down the default
  // FirebaseApp, which other Firebase calls may try to use.
  private static void ThrowIfCheckDependenciesRunning() {
    lock (CheckDependenciesThreadLock) {
      if (CheckDependenciesThread != CheckDependenciesNoThread &&
          CheckDependenciesThread !=
              System.Threading.Thread.CurrentThread.ManagedThreadId) {
        throw new System.InvalidOperationException(
            "Don't call Firebase functions before CheckDependencies has finished");
      }
    }
  }

  // Checks if CheckDependencies is running on any thread
  //
  // @return a boolean indicating whether check dependencies is running on any
  // thread
  private static bool IsCheckDependenciesRunning() {
    lock (CheckDependenciesThreadLock) {
      return CheckDependenciesThread != CheckDependenciesNoThread;
    }
  }

  /// @brief Asynchronously checks if all of the necessary dependencies for
  /// Firebase are present on the system, and in the necessary state.
  ///
  /// @note In some cases, this operation can take a long time. It's recommended
  /// to perform other application specific tasks in parallel while checking
  /// dependencies for Firebase.
  ///
  /// @return A Task that on completion will contain the DependencyStatus enum
  /// value, indicating the state of the required dependencies.
  public static System.Threading.Tasks.Task<DependencyStatus>
      CheckDependenciesAsync() {
    SetCheckDependenciesThread(CheckDependenciesPendingThread);
    Firebase.Platform.FirebaseHandler.CreatePartialOnMainThread(
        Firebase.Platform.FirebaseAppUtils.Instance);
    InitializeAppUtilCallbacks();
    return System.Threading.Tasks.Task.Run<DependencyStatus>(() => {
      // Set the thread id, so that this thread can access and create a
      // FirebaseApp.
      SetCheckDependenciesThread(
          System.Threading.Thread.CurrentThread.ManagedThreadId);
      DependencyStatus result = CheckDependencies();
      // Finished running CheckDependencies, so clear the thread for others.
      SetCheckDependenciesThread(CheckDependenciesNoThread);
      return result;
    });
  }

  /// @brief Asynchronously checks if all of the necessary dependencies for
  /// Firebase are present on the system, and in the necessary state and
  /// attempts to fix them if they are not.
  ///
  /// @note In some cases, this operation can take a long time and in some cases
  /// may prompt the user to update other services on the device. It's recommended
  /// to perform other application specific tasks in parallel while checking and
  /// potentially fixing dependencies for Firebase.
  ///
  /// If it's appropriate for your app to handle checking and fixing dependencies
  /// separately, you can. Here's effectively what CheckAndFixDependenciesAsync
  /// does:
  ///
  /// @code{.cs}
  ///   using System.Threading.Tasks;  // Needed for the Unwrap extension method.
  ///
  ///   // ...
  ///
  ///   Firebase.FirebaseApp.CheckDependenciesAsync().ContinueWith(checkTask => {
  ///     // Peek at the status and see if we need to try to fix dependencies.
  ///     Firebase.DependencyStatus status = checkTask.Result;
  ///     if (status != Firebase.DependencyStatus.Available) {
  ///       return Firebase.FirebaseApp.FixDependenciesAsync().ContinueWith(t => {
  ///         return Firebase.FirebaseApp.CheckDependenciesAsync();
  ///       }).Unwrap();
  ///     } else {
  ///       return checkTask;
  ///     }
  ///   }).Unwrap().ContinueWith(task => {
  ///     dependencyStatus = task.Result;
  ///     if (dependencyStatus == Firebase.DependencyStatus.Available) {
  ///       // TODO: Continue with Firebase initialization.
  ///     } else {
  ///       Debug.LogError(
  ///         "Could not resolve all Firebase dependencies: " + dependencyStatus);
  ///     }
  ///   });
  /// @endcode
  ///
  /// @return A Task that on completion will contain the DependencyStatus enum
  /// value, indicating the state of the required dependencies.
  public static System.Threading.Tasks.Task<DependencyStatus>
      CheckAndFixDependenciesAsync() {
    return System.Threading.Tasks.TaskExtensions.Unwrap(
        Firebase.FirebaseApp.CheckDependenciesAsync().ContinueWith(checkTask => {
          // Peek at the status and see if we need to try to fix dependencies.
          Firebase.DependencyStatus status = checkTask.Result;
          if (status != Firebase.DependencyStatus.Available) {
            return System.Threading.Tasks.TaskExtensions.Unwrap(
                Firebase.FirebaseApp.FixDependenciesAsync().ContinueWith(t => {
                  return Firebase.FirebaseApp.CheckDependenciesAsync();
            }));
          } else {
            return checkTask;
          }
        }));
  }

  /// @brief Checks if all of the necessary dependencies for Firebase
  /// are present on the system, and in the necessary state.
  ///
  /// @note This function should only be called before using any Firebase APIs.
  ///
  /// @return DependencyStatus enum value, indicating the state of
  /// the required dependencies.
  private static DependencyStatus CheckDependencies() {
    DependencyStatus status = DependencyStatus.Available;
    TranslateDllNotFoundException(() => {
        status = CheckDependenciesInternal();
      });
    return status;
  }

  private static DependencyStatus CheckDependenciesInternal() {
    // If a FirebaseApp instance was successfully created, all modules have been
    // successfully initialized.
    if (Firebase.Platform.PlatformInformation.IsAndroid &&
        FirebaseApp.GetInstance(FirebaseApp.DefaultName) == null) {
      // As Google Play Services isn't required for all APIs, firstly attempt
      // to create the app and if any APIs fail to initialize we can fallback
      // to seeing whether the failure was due to an out of date gmscore.
      InitResult initResult = InitResult.Success;
      FirebaseApp app = null;
      try {
        app = DefaultInstance;
        // If we created the app successfully and therefore initialized all
        // modules successfully, we know Google Play Services is up to date.
        return DependencyStatus.Available;
      } catch (Firebase.InitializationException exception) {
        initResult = exception.InitResult;
        if (initResult != InitResult.FailedMissingDependency) {
          throw exception;
        }
      } finally {
        if (app != null) app.Dispose();
      }
      GooglePlayServicesAvailability result =
          Firebase.AppUtil.CheckAndroidDependencies();
      switch (result) {
        case GooglePlayServicesAvailability.AvailabilityAvailable:
          return DependencyStatus.Available;
        case GooglePlayServicesAvailability.AvailabilityUnavailableDisabled:
          return DependencyStatus.UnavailableDisabled;
        case GooglePlayServicesAvailability.AvailabilityUnavailableInvalid:
          return DependencyStatus.UnavailableInvalid;
        case GooglePlayServicesAvailability.AvailabilityUnavailableMissing:
          return DependencyStatus.UnavilableMissing;
        case GooglePlayServicesAvailability.AvailabilityUnavailablePermissions:
          return DependencyStatus.UnavailablePermission;
        case GooglePlayServicesAvailability.AvailabilityUnavailableUpdateRequired:
          return DependencyStatus.UnavailableUpdaterequired;
        case GooglePlayServicesAvailability.AvailabilityUnavailableUpdating:
          return DependencyStatus.UnavailableUpdating;
        case GooglePlayServicesAvailability.AvailabilityUnavailableOther:
          return DependencyStatus.UnavailableOther;
      }
      return initResult == InitResult.Success ? DependencyStatus.Available :
          DependencyStatus.UnavailableOther;
    } else {
      return DependencyStatus.Available;
    }
  }

  /// @brief Attempts to fix any missing dependencies that would prevent Firebase
  /// from working on the system.
  ///
  /// Since this function is asynchronous, the returned Task must be monitored
  /// in order to tell when it has completed.  Also note, that depending on the
  /// fixes necessary, the user may be prompted for additional input.
  ///
  /// @return System.Threading.Tasks.Task A task that tracks the progress of
  /// the fix.
  public static System.Threading.Tasks.Task FixDependenciesAsync() {
    System.Threading.Tasks.Task task = null;
    TranslateDllNotFoundException(() => {
        task = Firebase.AppUtil.FixAndroidDependenciesAsync().ContinueWith(
          t => {
            if (t.Exception != null) throw t.Exception;
            // After attempting to fix the Android dependencies we recreated
            // the default app so that all modules are re-initialized since
            // it's likely some of them failed prior to a Google Play services
            // update.
            ResetDefaultAppCPtr();
          });
      });
    return task;
  }

  // Disposes of, and then recreates, the default App instance.
  private static void ResetDefaultAppCPtr() {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    // We lock nameToProxy, as other lookups do.
    lock (nameToProxy) {
      // Prior to destroying the app, we initialize Google Play
      // Services module, to make sure it stays valid throughout this process.
      // This is required to make sure the futures owned by the module are
      // not torn down if all app instances are destroyed.  If FixDependencies
      // is currently in progress a future owned by the module is currently
      // owned by a C# proxy object.
      AppUtil.InitializePlayServicesInternal();
      // Similar to the above logic, we set this flag to prevent the callback
      // on all the Apps being destroyed, as we plan to immediately recreate
      // the app.
      PreventOnAllAppsDestroyed = true;

      // Recreate the C++ application under the default instance so that all
      // references to the C# proxy remain valid.
      FirebaseApp app = DefaultInstance;
      app.RemoveReference();
      app.swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
          app, AppUtilPINVOKE.FirebaseApp_CreateInternal__SWIG_0());
      app.AddReference();

      // Unblock the all Apps being destroyed callback.
      PreventOnAllAppsDestroyed = false;
      // Undo the extra Initialize we did at the beginning of this.
      AppUtil.TerminatePlayServicesInternal();
    }
  }

  /// @brief Get the AppOptions the FirebaseApp was created with.
  /// @returns AppOptions used to create the FirebaseApp.
  public AppOptions Options {
    get {
      ThrowIfNull();
      // Return a copy based on the internal AppOptions.
      return new AppOptions(options());
    }
  }

  // Get the synchronization context for the current thread.
  // We do not use SynchronizationContext.Current as it's possible
  // The thread is not managed by Firebase in which case we could be
  // scheduling operations in a way that doesn't conform to our requirements.
  // For example, Unity 2017 installs a SynchronizationContext that stops
  // excecuting tasks when Time.timeScale is 0.
  internal static System.Threading.SynchronizationContext
      ThreadSynchronizationContext {
    get {
      return Platform.FirebaseHandler.DefaultInstance != null &&
          Platform.FirebaseHandler.DefaultInstance.IsMainThread() ?
            Firebase.Platform.PlatformInformation.SynchronizationContext :
            null;
    }
  }

  private Firebase.Platform.FirebaseAppPlatform appPlatform = null;
  internal Firebase.Platform.FirebaseAppPlatform AppPlatform {
    get {
      lock (typeof(Firebase.Platform.FirebaseAppPlatform)) {
        if (appPlatform == null) {
          appPlatform = new Firebase.Platform.FirebaseAppPlatform(this);
        }
        return appPlatform;
      }
    }
  }
%}

// Replace the default Dispose() method to remove references to this instance
// from the map of FirebaseApp instances and notify listeners that the app has
// been disposed.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::App {
    // If the name was not cached beforehand, do so now.
    if (name == null) {
      name = NameInternal;
    }

    // Raise AppDisposed event once when FirebaseApp is disposed.
    if (AppDisposed != null) {
      AppDisposed(this, System.EventArgs.Empty);
      AppDisposed = null;
    }

    RemoveReference();
  }

// Delete the underlying C++ object when all references to this object have been
// destroyed. This is automatically done with newer versions of swig.
#if SWIG_VERSION < 0x040000
%typemap(csfinalize) firebase::App %{
  ~$csclassname() {
    Dispose();
  }
%}
#endif

%{
namespace firebase {
namespace callback {
  char *SwigStringConvert(const char *s) {
    return SWIG_csharp_string_callback(s);
  }
}  // callback
}  // firebase
%}

// Replace Create() methods to provide the same signature across all platforms.
%extend firebase::App {
  %csmethodmodifiers CreateInternal() "internal";
  static App *CreateInternal() {
    return ::firebase::AppGetOrCreateInstance(nullptr, nullptr);
  }

  %csmethodmodifiers CreateInternal(const AppOptions &options) "internal";
  static App *CreateInternal(const AppOptions &options) {
    return ::firebase::AppGetOrCreateInstance(&options, nullptr);
  }

  %csmethodmodifiers CreateInternal(const AppOptions &options, const char *name) "internal";
  static App *CreateInternal(const AppOptions &options, const char *name) {
    // If a name isn't specified, fall back to the default app.
    if (name && strlen(name) == 0) name = nullptr;
    return ::firebase::AppGetOrCreateInstance(&options, name);
  }

  %csmethodmodifiers ReleaseReferenceInternal(App* app) "internal";
  static void ReleaseReferenceInternal(App* app) {
    return ::firebase::AppReleaseReference(app);
  }

  %csmethodmodifiers SetLogLevelInternal(firebase::LogLevel level) "internal";
  static void SetLogLevelInternal(firebase::LogLevel level) {
    firebase::SetLogLevel(level);
  }

  %csmethodmodifiers GetLogLevelInternal() "internal";
  static firebase::LogLevel GetLogLevelInternal() {
    return firebase::GetLogLevel();
  }

  %csmethodmodifiers RegisterLibrariesInternal() "internal";
  static void RegisterLibrariesInternal(std::map<std::string, std::string> libraries) {
    firebase::RegisterLibrariesHelper(libraries);
  }

 %csmethodmodifiers LogHeartbeatInternal(App* app) "internal";
  static void LogHeartbeatInternal(App* app) {
    firebase::LogHeartbeatForDesktop(app);
  }

  %csmethodmodifiers AppSetDefaultConfigPath(const char* path) "internal";
  static void AppSetDefaultConfigPath(const char* path) {
    firebase::App::SetDefaultConfigPath(path);
  }
}

%ignore firebase::App::Create();
%ignore firebase::App::Create(const AppOptions &options);
%ignore firebase::App::Create(const AppOptions &options, const char *name);

// Disable warnings about C++ identifiers named the same as C# keywords.
// This is caused by internal in the firebase::internal namespace.
%warnfilter(314);
%include "app/src/include/firebase/app.h"
%warnfilter(+314);

// Generate a DefaultName property that calls firebase::AppGetDefaultName
%csmethodmodifiers firebase::App::DefaultName
 "/// Gets the default name for FirebaseApp objects.
  public";
%attribute(firebase::App, static const char *, DefaultName, DefaultName);

// The %attribute macro can create instance properties but fails to generate
// the correct C/C++ _get #defines for static properties.  The following
// overrides each generated #define for a static property (requiring self) and
// replaces it with a #define that requires no arguments.
%{
#undef firebase_App_DefaultName_get
#define firebase_App_DefaultName_get() firebase::AppGetDefaultName()
%}

// Tell SWIG that the C function pointer cfunc is the same as the C# delegate
// type csdelegate.
%define SWIG_MAP_CFUNC_TO_CSDELEGATE(cfunc, csdelegate) \
%typemap(cstype) cfunc #csdelegate ;
%typemap(imtype) cfunc #csdelegate ;
%typemap(csin) cfunc "$csinput";
%typemap(in, canthrow=1) cfunc %{
  $1 = ($1_ltype)$input;
%}
%enddef

%{
#include "app/src/include/firebase/log.h"
%}

%include "app/src/include/firebase/log.h"

namespace firebase {

// PollCallbacks, from src/callback.h, is the only function that needs to be
// exposed, so just declare it directly, instead of ignoring everything.
namespace callback {
%csmethodmodifiers PollCallbacks() "internal";
void PollCallbacks();
}  // namespace callback

%csmethodmodifiers AppEnableLogCallback(bool) "internal";
void AppEnableLogCallback(bool);

%csmethodmodifiers AppGetLogLevel() "internal";
firebase::LogLevel AppGetLogLevel();

// Used to enable all modules on platforms other than Android.
%csmethodmodifiers SetEnabledAllAppCallbacks(bool) "internal";
void SetEnabledAllAppCallbacks(bool);

%csmethodmodifiers SetEnabledAppCallbackByName(std::string, bool) "internal";
void SetEnabledAppCallbackByName(std::string, bool);

%csmethodmodifiers GetEnabledAppCallbackByName(std::string) "internal";
bool GetEnabledAppCallbackByName(std::string);

// Used to pass a log callback to the C++ layer.
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::LogMessageDelegateFunc,
                             LogUtil.LogMessageDelegate)
%csmethodmodifiers SetLogFunction(::firebase::LogMessageDelegateFunc) "internal";
void SetLogFunction(::firebase::LogMessageDelegateFunc);

%newobject AppOptionsLoadFromJsonConfig(const char*);
%csmethodmodifiers AppOptionsLoadFromJsonConfig(const char*) "internal";
firebase::AppOptions* AppOptionsLoadFromJsonConfig(const char* config);

}  // namespace firebase

// Code and structures for checking that the necessary dependencies are in
// place.

// Trick availability.h into existing, even for builds that don't target
// android, so we can get at its Availability enum:
%rename(GooglePlayServicesAvailability) google_play_services::Availability;
/// Hide GooglePlayServicesAvailability
%typemap(csclassmodifiers) google_play_services::Availability
   "internal enum";

#if !defined(__ANDROID__)
  #define __ANDROID__
  #define SWIG_BUILD
  typedef void JNIEnv;
  typedef void* jobject;
  // This is a bit of a hack.  We need to undefine the header guard for
  // availability.h, because for non-android targets it has already been
  // included, and done nothing.  (Because __Android__ was not defined.)
  #undef FIREBASE_APP_SRC_INCLUDE_GOOGLE_PLAY_SERVICES_AVAILABILITY_H_
#endif  // !defined(__ANDROID__)

%ignore google_play_services::CheckAvailability;
%ignore google_play_services::MakeAvailable;
%ignore google_play_services::MakeAvailableLastResult;

%include "app/src/include/google_play_services/availability.h"

#if defined(SWIG_BUILD)
  #undef __ANDROID__
  #undef SWIG_BUILD
#endif  // defined(SWIG_BUILD)

%{
#if !defined(__ANDROID__)
  #define __ANDROID__
  #define SWIG_BUILD
  typedef void JNIEnv;
  typedef void* jobject;
  // This is a bit of a hack.  We need to undefine the header guard for
  // availability.h, because for non-android targets it has already been
  // included, and done nothing.  (Because __Android__ was not defined.)
  #undef FIREBASE_APP_SRC_INCLUDE_GOOGLE_PLAY_SERVICES_AVAILABILITY_H_
#endif  // !defined(__ANDROID__)

#include "google_play_services/availability.h"

#if defined(SWIG_BUILD)
  #undef __ANDROID__
  #undef SWIG_BUILD
#endif  // defined(SWIG_BUILD)

%}

namespace firebase {

static google_play_services::Availability CheckAndroidDependencies();
static ::firebase::Future<void> FixAndroidDependencies();

static void InitializePlayServicesInternal();
static void TerminatePlayServicesInternal();
}

%{
namespace firebase {

static google_play_services::Availability CheckAndroidDependencies() {
#if defined(__ANDROID__)
  JNIEnv* jni_env;
  jobject activity_local_ref = UnityGetActivity(&jni_env);
  google_play_services::Availability availability =
      google_play_services::CheckAvailability(jni_env, activity_local_ref);
  jni_env->DeleteLocalRef(activity_local_ref);
  return availability;
#else
  return google_play_services::kAvailabilityUnavailableOther;
#endif  // defined(__ANDROID__)
}

static Future<void> FixAndroidDependencies() {
#if defined(__ANDROID__)
  JNIEnv* jni_env;
  jobject activity_local_ref = UnityGetActivity(&jni_env);
  Future<void> future = google_play_services::MakeAvailable(
      jni_env, activity_local_ref);
  jni_env->DeleteLocalRef(activity_local_ref);
  return future;
#else
  return Future<void>();
#endif  // defined(__ANDROID__)
}

static void InitializePlayServicesInternal() {
#if defined(__ANDROID__)
  JNIEnv* jni_env;
  jobject activity_local_ref = UnityGetActivity(&jni_env);
  google_play_services::Initialize(jni_env, activity_local_ref);
  jni_env->DeleteLocalRef(activity_local_ref);
#endif  // defined(__ANDROID__)
}

static void TerminatePlayServicesInternal() {
#if defined(__ANDROID__)
  JNIEnv* jni_env;
  jobject activity_local_ref = UnityGetActivity(&jni_env);
  google_play_services::Terminate(jni_env);
  jni_env->DeleteLocalRef(activity_local_ref);
#endif  // defined(__ANDROID__)
}

}  // namespace firebase

%}

// Generate a internal C# wrapper for Variant. Note that it is intended to be
// readonly.
%{
#include "app/src/include/firebase/variant.h"
%}

%typemap(csclassmodifiers) firebase::Variant "internal class"

// We only want some functions from Variant, so ignore everything, then
// unignore the functions we care about.
%rename("$ignore", regextarget=1, fullname=1) "firebase::Variant::.*";
%rename("%s") firebase::Variant::~Variant;
%rename("%s") firebase::Variant::Type;
%rename("%s", regextarget=1) ".*kType.*";
%rename("%s") firebase::Variant::type;
%rename("%s") firebase::Variant::is_fundamental_type;
%rename("%s") firebase::Variant::is_string;
%rename("%s") firebase::Variant::AsString;
%rename("%s") firebase::Variant::vector;
%rename("%s") firebase::Variant::map;
%rename("%s") firebase::Variant::int64_value;
%rename("%s") firebase::Variant::double_value;
%rename("%s") firebase::Variant::bool_value;
%rename("%s") firebase::Variant::string_value;

// Static methods that allow to create Variants
%rename("%s") firebase::Variant::FromInt64;
%rename("%s") firebase::Variant::FromDouble;
%rename("%s") firebase::Variant::FromBool;
%rename("FromString") firebase::Variant::FromMutableString;
%rename("%s") firebase::Variant::Null;

// To create map & list Variants, we first create an empty instance of that
// type and then use map() and vector() to fill them.
%rename("%s") firebase::Variant::EmptyMap;
%rename("%s") firebase::Variant::EmptyVector;

// Blob related methods that allow to create a blob and access it's data.
%rename("%s") firebase::Variant::EmptyMutableBlob;
%rename("%s") firebase::Variant::blob_size;
%rename("%s") firebase::Variant::untyped_mutable_blob_data;
// These should only be called in FromBlob and blob_as_bytes below:
%csmethodmodifiers firebase::Variant::EmptyMutableBlob(uint) "private static";
%csmethodmodifiers firebase::Variant::untyped_mutable_blob_data() "private";
%csmethodmodifiers firebase::Variant::blob_size() "private";

%typemap(csclassmodifiers) std::map<firebase::Variant, firebase::Variant> "internal class"
%template(VariantVariantMap) std::map<firebase::Variant, firebase::Variant>;
%typemap(csclassmodifiers) std::vector<firebase::Variant> "internal class"
%template(VariantList) std::vector<firebase::Variant>;

%extend firebase::Variant {
  // Return the mutable_blob_data as void*.
  // This is done so that SWIG automatically uses IntPtr as the return type.
  // (Instead of generating a SWIGTYPE_p_unsigned_char type.)
  void* untyped_mutable_blob_data() const {
    return $self->mutable_blob_data();
  }
}

%typemap(cscode) firebase::Variant %{
  // Create a new "MutableBlob" Variant from a C# byte[].
  // This copies the byte[] content into memory owned by the Variant.
  public static Variant FromBlob(byte[] blob) {
    Variant v = EmptyMutableBlob((uint)blob.Length);
    System.Runtime.InteropServices.Marshal.Copy(
        blob, 0, v.untyped_mutable_blob_data(), blob.Length);
    return v;
  }

  // Returns a copy of the binary data contained in a "MutableBlob" Variant.
  // If called multiple times it creates a new copy every time.
  public byte[] blob_as_bytes() {
    byte[] blob = new byte[blob_size()];
    System.Runtime.InteropServices.Marshal.Copy(
        untyped_mutable_blob_data(), blob, 0, blob.Length);
    return blob;
  }

  // Creates a Variant from an object.
  // The allowed types are:
  //   - bool
  //   - string
  //   - long
  //   - double
  //   - IDictionary
  //   - IList
  //   - byte[]
  //   - null
  // For convenience, ints will automatically be converted to longs.
  // (Other integer types will NOT be converted and throw an ArgumentException.)
  // This restriction is transitive, e.g. the objects in a list must also be
  // of the above types.
  public static Variant FromObject(object o) {
    if (o == null) {
      return Variant.Null();
    } else if (o is string) {
      return Variant.FromString((string)o);
    } else if (o is long) {
      return Variant.FromInt64((long)o);
    } else if (o is double) {
      return Variant.FromDouble((double)o);
    } else if (o is bool) {
      return Variant.FromBool((bool)o);
    } else if (o is byte[]) {
      // Note: byte[] is an IList, so this check needs to remain first.
      return Variant.FromBlob((byte[])o);
    } else if (o is System.Collections.IList) {
      var list = (System.Collections.IList)o;
      Variant v = Variant.EmptyVector();
      VariantList vList = v.vector();
      foreach (var item in list) {
        vList.Add(FromObject(item));
      }
      return v;
    } else if (o is System.Collections.IDictionary) {
      var map = (System.Collections.IDictionary)o;
      Variant v = Variant.EmptyMap();
      VariantVariantMap vMap = v.map();
      foreach (System.Collections.DictionaryEntry entry in map) {
        Variant copiedKey = FromObject(entry.Key);
        Variant copiedValue = FromObject(entry.Value);
        vMap[copiedKey] = copiedValue;
      }
      return v;
    } else if (o is System.Byte  || o is System.Int16  || o is System.Int32 ||
               o is System.SByte || o is System.UInt16 || o is System.UInt32) {
      // For convenience, convert smaller integer types to long.
      // Int64 (long) is handled above and UInt64 is intentionally skipped
      // as it might not fit into an Int64.
      return Variant.FromInt64(System.Convert.ToInt64(o));
    } else if (o is float) {
      // For convenience, convert single precision floats to double.
      // Decimal values are ignored as they would lose precision if converted.
      return Variant.FromDouble(System.Convert.ToDouble(o));
    }
    throw new System.ArgumentException(
        "Invalid type " + o.GetType() + " for conversion to Variant");
  }

%}

%include "app/src/include/firebase/variant.h"
