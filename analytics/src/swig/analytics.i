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
//swiglint: disable include-h-allglobals

%module FirebaseAnalyticsInternal

#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%include "std_map.i"

%pragma(csharp) moduleclassmodifiers="internal static class"
%pragma(csharp) modulecode=%{
  // Hold a reference to the default app when methods from this module are
  // referenced.
  static Firebase.FirebaseApp app;
  static FirebaseAnalyticsInternal() { app = Firebase.FirebaseApp.DefaultInstance; }
  /// Get the app used by this module.
  /// @return FirebaseApp instance referenced by this module.
  static Firebase.FirebaseApp App { get { return app; } }
%}
%feature("flatnested");

// Change the default class modifier to internal, so that new classes are not accidentally exposed
%typemap(csclassmodifiers) SWIGTYPE "internal class"

%import "app/src/swig/app.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

// Including cstdint before stdint.i ensures the int64_t typedef is correct,
// otherwise on some platforms it is defined as "long long int" instead of
// "long int".
#include <cstdint>
%include "stdint.i"

// Start of the code added to the C++ module file
%{
#include <vector>
#include "analytics/src/include/firebase/analytics.h"
#include "app/src/log.h"

namespace firebase {
namespace analytics {

// Internal version of LogEvent that takes in two vectors of known types,
// and converts them into C++ Parameters to pass into the public LogEvent instead.
void LogEvent(const char* name, std::vector<std::string> parameter_names,
              std::vector<firebase::Variant> parameter_values) {
  if (parameter_names.size() != parameter_values.size()) {
    firebase::LogError("LogEvent for %s given different list sizes (%d, %d)",
                       name, parameter_names.size(), parameter_values.size());
    return; 
  }

  size_t number_of_parameters = parameter_names.size();
  Parameter* parameters = new Parameter[number_of_parameters];

  for (size_t i = 0; i < number_of_parameters; ++i) {
    parameters[i] = Parameter(parameter_names[i].c_str(), parameter_values[i]);
  }

  LogEvent(name, parameters, number_of_parameters);

  delete[] parameters;
}

// Converts from a generic int, int map to the C++ Consent enums
void SetConsentWithInts(const std::map<int, int>& settings) {
  std::map<ConsentType, ConsentStatus> converted;
  for (const auto& pair : settings) {
    converted[static_cast<ConsentType>(pair.first)] = static_cast<ConsentStatus>(pair.second);
  }
  SetConsent(converted);
}

}  // namespace analytics
}  // namespace firebase

%}  // End of the code added to the C++ module file

// Initialize / Terminate implicitly called when App is created / destroyed.
%ignore firebase::analytics::Initialize;
%ignore firebase::analytics::Terminate;
// Ignore the SendEvent that takes a Parameter array, as we handle it
// with a custom version instead.
%ignore firebase::analytics::LogEvent(const char*, const Parameter*, size_t);
// Ignore SetConsent, in order to convert the types with our own function.
%ignore firebase::analytics::SetConsent;
// Ignore the Parameter class, as we don't want to expose that to C# at all.
%ignore firebase::analytics::Parameter;

// GetSessionId returns Future<long long> in SWIG.
%include "app/src/swig/future.i"
%SWIG_FUTURE(Future_LongLong, long, internal, long long, FirebaseException)

// Ignore the Consent enums, so we can use commented ones in C#
%ignore firebase::analytics::ConsentType;
%ignore firebase::analytics::ConsentStatus;
%template(IntIntMap) std::map<int, int>;

%include "analytics/src/include/firebase/analytics.h"

// Declare the new C++ functions we added in this file that we want
// to expose to C#.
namespace firebase {
namespace analytics {
void LogEvent(const char* name, std::vector<std::string> parameter_names,
              std::vector<firebase::Variant> parameter_values);
void SetConsentWithInts(const std::map<int, int>& settings);
}  // namespace analytics
}  // namespace firebase
