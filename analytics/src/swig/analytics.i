// Copyright 2016 Google Inc. All Rights Reserved.
//
// This wrapper exposes the analytics c++ interface. It's nearly one to one with
// the c++ interface. The one slight difference is the support for C# arrays of
// Parameters which have known size, so the size argument is not needed when
// using the array of Parameters LogEvent. The other special thing this wrapper
// has to do is copy strings passed around between C# and C++ because the C++
// interface stores references, and the C# strings are marshalled as temporary
// since they have to be converted from unicode strings anyway.
//
// TODO(butterfield): Replace with %ignoreall/%unignoreall
//swiglint: disable include-h-allglobals

%module FirebaseAnalytics

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%include "std_map.i"

%pragma(csharp) moduleclassmodifiers="public sealed class"
%pragma(csharp) modulecode=%{
  // Hold a reference to the default app when methods from this module are
  // referenced.
  static Firebase.FirebaseApp app;
  static FirebaseAnalytics() { app = Firebase.FirebaseApp.DefaultInstance; }
  /// Get the app used by this module.
  /// @return FirebaseApp instance referenced by this module.
  static Firebase.FirebaseApp App { get { return app; } }

  private FirebaseAnalytics() {}
%}
%feature("flatnested");

// These are directly included in the generated code
// it's necessary to put them here so that the generated code knows what we're
// dealing with.
%{
#include "analytics/src/include/firebase/analytics.h"
#include "analytics/src/include/firebase/analytics/event_names.h"
#include "analytics/src/include/firebase/analytics/parameter_names.h"
#include "analytics/src/include/firebase/analytics/user_property_names.h"
%}

%rename(kConsentTypeAdStorage) firebase::analytics::kConsentTypeAdStorage;
%rename(kConsentTypeAnalyticsStorage) firebase::analytics::kConsentTypeAnalyticsStorage;
%rename(kConsentStatusGranted) firebase::analytics::kConsentStatusGranted;
%rename(kConsentStatusDenied) firebase::analytics::kConsentStatusDenied;

// Constant renaming must happen before SWIG_CONSTANT_HEADERS is included.
%rename(kParameterAchievementId) firebase::analytics::kParameterAchievementID;
%rename(kParameterGroupId) firebase::analytics::kParameterGroupID;
%rename(kParameterItemId) firebase::analytics::kParameterItemID;
%rename(kParameterItemLocationId) firebase::analytics::kParameterItemLocationID;
%rename(kParameterTransactionId) firebase::analytics::kParameterTransactionID;

%import "app/src/swig/app.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"
%include "firebase/analytics/event_names.h"
%include "firebase/analytics/parameter_names.h"
%include "firebase/analytics/user_property_names.h"

// Including cstdint before stdint.i ensures the int64_t typedef is correct,
// otherwise on some platforms it is defined as "long long int" instead of
// "long int".
#include <cstdint>
%include "stdint.i"

namespace firebase {
namespace analytics {

// ctype is equivalent to java's "jni"
// This is specific to C#

// This is kind of hacky, but we can sneak in an extra argument while
// we're still on the C++ side
// The sneaky input is named with the $1 to allow this hack to hopefully work
// with multiple array inputs pairs if ever it were needed
%typemap(ctype)   (const Parameter* parameters, size_t number_of_parameters)
    "firebase::analytics::Parameter** $1_ptr_array, size_t"

// to match the function prototype, we need to do the hack in the
// intermediary code as well.
%typemap(imtype)  (const Parameter* parameters, size_t number_of_parameters)
    "System.IntPtr arg, int"

// In C# though, we're just Parameter[], as C# arrays have known .Length
%typemap(cstype)  (const Parameter* parameters, size_t number_of_parameters)
    "params Parameter[]"

// We need to add code in C# to modify the Proxy array into an array of C ptrs
// before we pass it in. We can do that with the csin, pre attribute.
%typemap(csin, pre=
" // First copy the array of proxy classes containing C pointers
  // to an array of c pointers
  System.IntPtr[] swig_unwrap_$csinput  = new System.IntPtr[$csinput.Length];
  for (int i = 0; i < $csinput.Length; ++i) {
    swig_unwrap_$csinput[i] = (System.IntPtr)Parameter.getCPtr($csinput[i]);
  }
    fixed ( System.IntPtr* swig_ptrTo_$csinput = swig_unwrap_$csinput )
") (const Parameter* parameters, size_t number_of_parameters)
    "(System.IntPtr)swig_ptrTo_$csinput, $csinput.Length"

%typemap(in) (const Parameter* parameters, size_t number_of_parameters) %{
  // The csin typemap above extracted the pointers to the C++ classes from the
  // C# Parameter proxy classes.  The array of pointers is provided here via:
  // $1_ptr_array: Array of pointers to the C class
  // $input: re-assigned to the size of the array of pointers

  // Copy into an array of Parameter structs prior to calling LogEvent.
  // This array is deleted using the
  // %typemap(freearg)(parameter, number_of_paramters) typemap below.
  firebase::analytics::Parameter* $1_array =
      new firebase::analytics::Parameter[$input];
  for (size_t i = 0; i < $input; ++i) {
    ParameterCopy::AsParameterCopy($1_ptr_array[i])->CopyToParameter(
        &$1_array[i]);
  }

  $1 = $1_array;
  $2 = $input;
%}

// The 'in' typemap is really just code up to the point when the C++ function is
// invoked, so we actually need this to follow up and free the temporary memory.
// The short-lived nature of this is ok, because we know java's jni copies it by
// the time we get back from the C invocation
%typemap(freearg) (const Parameter* parameters, size_t number_of_parameters) %{
  if ($1_array) delete [] $1_array;
%}

%{
// Parameter which maintains a copy of strings provided on construction.
// This requires a copy of any strings construction as strings passed to the
// constructor are temporarily allocated by the SWIG binding code and
// deallocated after construction.
class ParameterCopy : private firebase::analytics::Parameter {
 public:
  ParameterCopy(const char *parameter_name, const char *parameter_value) :
      Parameter(nullptr, 0) {
    Initialize(parameter_name, parameter_value);
  }

  ParameterCopy(const char *parameter_name, int64_t parameter_value) :
      Parameter(nullptr, 0) {
    Initialize(parameter_name, parameter_value);
  }

  ParameterCopy(const char *parameter_name, double parameter_value) :
      Parameter(nullptr, 0) {
    Initialize(parameter_name, parameter_value);
  }

  ~ParameterCopy() {}

  // Initialize this parameter with a new name and value.
  void Initialize(const char *parameter_name,
                  firebase::Variant parameter_value) {
    SetString(parameter_name, &name_copy, &name);
    if (parameter_value.is_string()) {
      const char* string_value = parameter_value.string_value();
      // Make `value` store its own bytes.
      value = firebase::Variant::MutableStringFromStaticString(
          string_value ? string_value : "");
    } else {
      value = parameter_value;
    }
  }

  // Copy to a Parameter.
  // IMPORTANT: Since the supplied parameter simply references pointers within
  // this object, the lifetime of the parameter must exceed this instance.
  void CopyToParameter(firebase::analytics::Parameter *parameter) const {
    *parameter = *AsParameter();
  }

  // This is only non-const so the Parameter can be cast to ParameterCopy in
  // order to delete the object.
  // IMPORTANT: Do *not* mutate the returned Parameter use accessors on this
  // object instead.
  firebase::analytics::Parameter* AsParameter() { return this; }
  const firebase::analytics::Parameter* AsParameter() const { return this; }

  // Convert a Parameter* (assuming it was allocated as a copy there is no
  // checking here so be careful) to a ParameterCopy pointer.
  static ParameterCopy* AsParameterCopy(
      firebase::analytics::Parameter* parameter) {
    return static_cast<ParameterCopy*>(parameter);
  }

 private:
  // Copy the specified string value into string_storage with the C pointer
  // to the string stored in output.
  template<typename T>
  static void SetString(const char *value, std::string * const string_storage,
                        T *output) {
    if (value) {
      *string_storage = value;
    } else {
      string_storage->clear();
    }
    *output = string_storage->c_str();
  }

  std::string name_copy;
};
%}

%extend Parameter {
  Parameter(const char *parameter_name, const char *parameter_value) {
    return (new ParameterCopy(parameter_name, parameter_value))->AsParameter();
  }

  Parameter(const char* parameter_name, int64_t parameter_value) {
    return (new ParameterCopy(parameter_name, parameter_value))->AsParameter();
  }

  Parameter(const char* parameter_name, double parameter_value) {
    return (new ParameterCopy(parameter_name, parameter_value))->AsParameter();
  }

  ~Parameter() {
    delete ParameterCopy::AsParameterCopy($self);
  }
}
// Overridden in the class extension methods above.
%ignore Parameter::Parameter(const char* parameter_name,
                             const char* parameter_value);
%ignore Parameter::Parameter(const char* parameter_name,
                             int parameter_value);
%ignore Parameter::Parameter(const char* parameter_name,
                             int64_t parameter_value);
%ignore Parameter::Parameter(const char* parameter_name,
                             double parameter_value);
// Initialize / Terminate implicitly called when App is created / destroyed.
%ignore Initialize;
%ignore Terminate;
// SetConsent handled via SetConsentInternal below.
%ignore SetConsent;

} // namespace analytics
} // namespace firebase

%typemap(csclassmodifiers) firebase::analytics::Parameter "public sealed class";

// This is a hack that overrides this specific log function, which is currently
// the only `unsafe` function that gets generated.
%csmethodmodifiers
    firebase::analytics::LogEvent(const char *name,
                                  const Parameter *parameters,
                                  size_t number_of_parameters)
 "/// @brief Log an event with associated parameters.
  ///
  /// An Event is an important occurrence in your app that you want to measure.
  /// You can report up to 500 different types of events per app and you can
  /// associate up to 25 unique parameters with each Event type.
  ///
  /// Some common events are in the reference guide via the
  /// FirebaseAnalytics.Event* constants, but you may also choose to specify
  /// custom event types that are associated with your specific app.
  ///
  /// @param[in] name Name of the event to log. Should contain 1 to 32
  ///     alphanumeric characters or underscores. The name must start with an
  ///     alphabetic character. Some event names are reserved. See
  ///     `Analytics Events` for the list of reserved event
  ///     names. The \"firebase_\" prefix is reserved and should not be used.
  ///     Note that event names are case-sensitive and that logging two events
  ///     whose names differ only in case will result in two distinct events.
  /// @param[in] parameters A parameter array of `Parameter` instances.
  public unsafe";

%rename(SetUserId) firebase::analytics::SetUserID;
%rename(SetSessionTimeoutDurationInternal) SetSessionTimeoutDuration;
%csmethodmodifiers firebase::analytics::SetSessionTimeoutDuration(int64_t milliseconds) "internal";

%pragma(csharp) modulecode=%{
  /// @brief Sets the duration of inactivity that terminates the current session.
  ///
  /// @note The default value is 30 minutes.
  ///
  /// @param timeSpan The duration of inactivity that terminates the current
  /// session.
  public static void SetSessionTimeoutDuration(System.TimeSpan timeSpan) {
    SetSessionTimeoutDurationInternal((long)timeSpan.TotalMilliseconds);
  }
%}

// GetSessionId returns Future<long long> in SWIG.
%include "app/src/swig/future.i"
%SWIG_FUTURE(Future_LongLong, long, internal, long long, FirebaseException)

%include "analytics/src/include/firebase/analytics.h"

%rename(ConsentType) firebase::analytics::ConsentType;
%rename(ConsentStatus) firebase::analytics::ConsentStatus;
// Add a swig C++ function to call into the Analytics C++ implementation.
%{
namespace firebase {
namespace analytics {

  void SetConsentInternal(std::map<firebase::analytics::ConsentType, firebase::analytics::ConsentStatus> *ptr) {
    firebase::analytics::SetConsent(*ptr);
  }

} // namespace analytics
} // namespace firebase
%}
// The definition on the C++ side, so that swig is aware of the function's existence.
void SetConsentInternal(std::map<firebase::analytics::ConsentType, firebase::analytics::ConsentStatus> *ptr);

%typemap(csclassmodifiers) firebase::analytics::ConsentType "enum";
%typemap(csclassmodifiers) firebase::analytics::ConsentStatus "enum";

%typemap(csclassmodifiers) std::map<firebase::analytics::ConsentType, firebase::analytics::ConsentStatus> "internal class"
%template(ConsentMap) std::map<firebase::analytics::ConsentType, firebase::analytics::ConsentStatus>;

namespace firebase {
namespace analytics {
    
%pragma(csharp) modulecode=%{
  /// @brief Sets the applicable end user consent state (e.g., for device
  /// identifiers) for this app on this device.
  ///
  /// Use the consent map to specify individual consent type values. Settings are
  /// persisted across app sessions. By default consent types are set to
  /// "granted".
  public static void SetConsent(System.Collections.Generic.IDictionary<ConsentType, ConsentStatus> consentSettings) {
    ConsentMap consentSettingsMap = new ConsentMap();
    foreach(var kv in consentSettings) {
      consentSettingsMap[kv.Key] = kv.Value;
    }
    SetConsentInternal(consentSettingsMap);
  }
%}

}  // namespace analytics
}  // namespace firebase
