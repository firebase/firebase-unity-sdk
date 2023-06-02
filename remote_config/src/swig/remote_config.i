// Copyright 2016 Google Inc. All Rights Reserved.
//
// This file is used by swig to generate wrappers in target languages (so far
// just C#), around the C++ interface. All of the functions exposed in remote_config.h
// are put into a class called RemoteConfig.
//

%module RemoteConfigUtil

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
%include "stdint.i"

%import "app/src/swig/app.i"
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"
%include "app/src/swig/future.i"

// Start of code added to the C++ module file
%{
#include <algorithm>
#include <map>
#include <vector>
#include "app/src/callback.h"
#include "app/src/cpp_instance_manager.h"
#include "remote_config/src/include/firebase/remote_config.h"

namespace firebase {
namespace remote_config {

// Declare the C++ callback delegate equivalent to C# RemoteConfigUtil.ConfigUpdateDelegate
typedef void (SWIGSTDCALL *ConfigUpdateListener)(const char* app_name, ConfigUpdate* config_update, int error);

// Reference count manager for C++ RemoteConfig instance, using pointer as the
// key for searching.
static CppInstanceManager<RemoteConfig> g_rc_instances;

// App -> C++ ConfigUpdateListener map.
static std::map<App*, firebase::remote_config::ConfigUpdateListenerRegistration> g_registered_listeners;

// Should be set to the C# function FirebaseRemoteConfig.ConfigUpdateMethod
static ConfigUpdateListener g_config_updated = nullptr;

// Internal struct used to pass the Remote Config stored data from C++ to C#.
struct ConfigValueInternal {
  // The raw data.
  std::vector<unsigned char> data;
  // The source of the data.
  ValueSource source;
};

// Wrapper that calls g_config_updated. Should be used with the
// callback logic to guarantee it is on the Unity thread.
static void CallConfigUpdate(ConfigUpdate cu, RemoteConfigError error, const char* name) {
  if (g_config_updated) {
    // Should be calling FirebaseRemoteConfig.ConfigUpdateMethod
    g_config_updated(name, &cu, static_cast<int>(error));
  }
}
// Called by C# to register a RemoteConfig instance for config updates,
// provided via the callback method provided.
void SetConfigUpdateCallback(RemoteConfig* rc, firebase::remote_config::ConfigUpdateListener on_config_updated) {
  // If given a method, save it, and add a new listener for Config Updates.
  if (on_config_updated) {
    if (!g_config_updated) {
      g_config_updated = on_config_updated;
    }
    std::string app_name = rc->app()->name();
    ConfigUpdateListenerRegistration registration = rc->AddOnConfigUpdateListener([app_name](ConfigUpdate&& cu, RemoteConfigError error) {
      // Queue up a call to g_config_updated
      ConfigUpdate cuLocal = std::move(cu);
      firebase::callback::AddCallback(
        new firebase::callback::CallbackValue2String1<ConfigUpdate, RemoteConfigError>(
          cuLocal, error, app_name.c_str(), CallConfigUpdate));
    });
    
    // Save the registration in a map so that it can be removed later if needed.
    g_registered_listeners[rc->app()] = registration;
  } else {
    // Remove the listener, and cleanup the callback if no more remain.
    ConfigUpdateListenerRegistration registration = g_registered_listeners[rc->app()];
    g_registered_listeners.erase(rc->app());
    registration.Remove();

    if (g_registered_listeners.empty()) {
      g_config_updated = nullptr;
    }
  }
}

}  // remote_config
}  // firebase

%} // End of code added to the C++ module file

// All outputs of CharVector should instead be IEnumerable<byte>
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<byte>") std::vector<unsigned char> "CharVector";
// All outputs of StringList should instead be IEnumerable<string>
%typemap(cstype, out="global::System.Collections.Generic.IEnumerable<string>") std::vector<std::string> "StringList";

// Ignore unused structs and enums
%ignore firebase::remote_config::ValueInfo;
%ignore firebase::remote_config::ConfigKeyValue;
%ignore firebase::remote_config::ConfigSetting;
%ignore firebase::remote_config::RemoteConfigError;
%ignore firebase::remote_config::ConfigUpdateListenerRegistration;

// Configure the ConfigInfo class
%csmethodmodifiers fetch_time "internal";
%rename(FetchTimeInternal) fetch_time;
%csmethodmodifiers throttled_end_time "internal";
%rename(ThrottledEndTimeInternal) throttled_end_time;

%typemap(cscode) firebase::remote_config::ConfigInfo %{
  private System.DateTime UnixEpochUtc =
      new System.DateTime(1970, 1, 1, 0, 0, 0, 0, System.DateTimeKind.Utc);

  /// @brief The time when the last fetch operation completed.
  public System.DateTime FetchTime {
    get {
      return UnixEpochUtc.AddMilliseconds(FetchTimeInternal);
    }
  }

  /// @brief The time when Remote Config data refreshes will no longer
  /// be throttled.
  public System.DateTime ThrottledEndTime {
    get {
      return UnixEpochUtc.AddMilliseconds(ThrottledEndTimeInternal);
    }
  }
%}

%immutable firebase::remote_config::ConfigInfo::last_fetch_status;
%immutable firebase::remote_config::ConfigInfo::last_fetch_failure_reason;

// These are here instead of the header due to b/35780150
%csmethodmodifiers firebase::remote_config::ConfigInfo::last_fetch_status "
  /// @brief The status of the last fetch request.
  public";
%csmethodmodifiers firebase::remote_config::ConfigInfo::last_fetch_failure_reason "
  /// @brief The reason the most recent fetch failed.
  public";

// Make snake_case properties into CamelCase.
// ConfigInfo
%rename(LastFetchFailureReason) last_fetch_failure_reason;
%rename(LastFetchStatus) last_fetch_status;

%typemap(csclassmodifiers) firebase::remote_config::ConfigInfo
  "public sealed class";

%SWIG_FUTURE(Future_ConfigInfo, ConfigInfo, internal, firebase::remote_config::ConfigInfo, FirebaseException) // Future<ConfigInfo>

%rename (FirebaseRemoteConfigInternal) firebase::remote_config::RemoteConfig;

%rename (ConfigSettingsInternal) firebase::remote_config::ConfigSettings;

%rename (ConfigUpdateInternal) firebase::remote_config::ConfigUpdate;

// Configure properties for get / set methods on the FirebaseRemoteConfigInternal class.
%attribute(firebase::remote_config::RemoteConfig, firebase::App*, App, app);

// Hide GetInstance() as this is wrapped in GetInstanceInternal() in
// order to properly handle reference count.
%ignore firebase::remote_config::RemoteConfig::GetInstance;

%ignore firebase::remote_config::RemoteConfig::SetDefaults;
// Ignore the various Get<Types>, as GetValue is used instead.
%ignore firebase::remote_config::RemoteConfig::GetBoolean;
%ignore firebase::remote_config::RemoteConfig::GetLong;
%ignore firebase::remote_config::RemoteConfig::GetDouble;
%ignore firebase::remote_config::RemoteConfig::GetString;
%ignore firebase::remote_config::RemoteConfig::GetData;
// Ignore GetAll, as we do not rely on the C++ function for it.
%ignore firebase::remote_config::RemoteConfig::GetAll;
%ignore firebase::remote_config::RemoteConfig::AddOnConfigUpdateListener;

%typemap(cscode) firebase::remote_config::RemoteConfig %{
  // Generates a key that uniquely identifies this instance.
  internal string InstanceKey { get { return App.Name; } }

  // Take / release ownership of the C++ object.
  internal void SetSwigCMemOwn(bool ownership) {
    swigCMemOwn = ownership;
  }
%}

// Replace the default Dispose() method to remove references to this instance
// from the map of FirebaseRemoteConfig instances.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
    firebase::remote_config::RemoteConfig {
  lock (FirebaseApp.disposeLock) {
    ReleaseReferenceInternal(this);
    swigCMemOwn = false;
    swigCPtr = new System.Runtime.InteropServices.HandleRef(
            null, System.IntPtr.Zero);
  }
  System.GC.SuppressFinalize(this);
}

SWIG_MAP_CFUNC_TO_CSDELEGATE(
    firebase::remote_config::ConfigUpdateListener,
    Firebase.RemoteConfig.RemoteConfigUtil.ConfigUpdateDelegate)

%include "remote_config/src/include/firebase/remote_config.h"

// Start of C++ definitions that Swig needs to know of, to generate C# for
namespace firebase {
namespace remote_config {

struct ConfigValueInternal {
  std::vector<unsigned char> data;
  ValueSource source;
};

void SetConfigUpdateCallback(firebase::remote_config::RemoteConfig* rc,
                             firebase::remote_config::ConfigUpdateListener config_listener);

}
} // End of C++ definitions


%pragma(csharp) modulecode=%{

  internal delegate void ConfigUpdateDelegate(string appName, System.IntPtr configUpdatePtr, int error);
  
  /// The C++ mapping requires a StringStringMap, so convert the more generic
  /// dictionary into that map.
  internal static StringStringMap ConvertDictionaryToMap(
      System.Collections.Generic.IDictionary<string, object> oldMap) {
    StringStringMap newMap = new StringStringMap();

    foreach (System.Collections.Generic.KeyValuePair<string, object>
             pair in oldMap) {
      if (pair.Value is string) {
        newMap[pair.Key] = pair.Value as string;
      } else if (pair.Value is System.Collections.Generic.IEnumerable<byte>) {
        // For lists of bytes, match the UTF8 conversion used by the base
        // implementation.
        var list =
            new System.Collections.Generic.List<byte>(
                pair.Value as System.Collections.Generic.IEnumerable<byte>);
        newMap[pair.Key] = System.Text.Encoding.UTF8.GetString(list.ToArray());
      } else if (pair.Value is System.Collections.IEnumerable) {
        // For other collections, just try to convert the inner values.
        var list = pair.Value as System.Collections.IEnumerable;
        var stringBuilder = new System.Text.StringBuilder();
        foreach (object obj in list) {
          stringBuilder.Append(System.Convert.ToString(obj, System.Globalization.CultureInfo.InvariantCulture));
        }
        newMap[pair.Key] = stringBuilder.ToString();
      } else {
        // For everything else, go straight to a string.
        newMap[pair.Key] = System.Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture);
      }
    }

    return newMap;
  }
%}

%extend firebase::remote_config::RemoteConfig {
  static RemoteConfig* GetInstanceInternal(firebase::App* app) {
    // This is to protect from the race condition after
    // RemoteConfig::GetInstance() is called and before the pointer is added to
    // g_rc_instances.
    ::firebase::MutexLock lock(
        ::firebase::remote_config::g_rc_instances.mutex());
    firebase::remote_config::RemoteConfig* rc =
        firebase::remote_config::RemoteConfig::GetInstance(app);

    ::firebase::remote_config::g_rc_instances.AddReference(rc);
    return rc;
  }

  static void ReleaseReferenceInternal(
      firebase::remote_config::RemoteConfig* rc) {
    ::firebase::remote_config::g_rc_instances.ReleaseReference(rc);
  }

  firebase::remote_config::ConfigValueInternal GetValueInternal(const char* key) {
    firebase::remote_config::ConfigValueInternal config_value;
    firebase::remote_config::ValueInfo value_info;
    config_value.data = self->GetData(key, &value_info);
    config_value.source = value_info.source;
    return config_value;
  }

  Future<void> SetDefaultsInternal(
      std::map<std::string, std::string> default_dict) {
    size_t default_count = default_dict.size();

    firebase::remote_config::ConfigKeyValue* default_array =
        new firebase::remote_config::ConfigKeyValue[default_count];

    int index = 0;
    for (auto itr = default_dict.begin(); itr != default_dict.end(); ++itr) {
      default_array[index].key = itr->first.c_str();
      default_array[index].value = itr->second.c_str();
      index++;
    }

    firebase::Future<void> future_result =
        self->SetDefaults(default_array, default_count);
    delete[] default_array;
    return future_result;
  }
}

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namespace.
// 844: Globally disable warnings about methods potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,844);
