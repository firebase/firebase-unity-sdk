/*
 * Copyright 2021 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

// This file is used by SWIG to generate wrappers in target languages (so far
// just C#) around the C++ interface. A few .i files are defined to help
// organizing the public classes.

// Make sure to check in the new generated code when changing the SWIG
// interface (by running
// `firebase/firestore/client/unity/generated/update-src.sh`).
// Alternatively, add `NO_IFTTT=<Reason>` to the CL description.
//


#ifdef USE_EXPORT_FIX
// Generate a function that we can reference to force linker
// to include the generated swig library symbols correctly
// see export_fix.cc for more details
%include "app/src/export_fix.h"
%{#include "app/src/export_fix.h"%}
#endif

%module FirestoreCpp
%pragma(csharp) moduleclassmodifiers="internal sealed class"

// By default SWIG generates classes as public, but we want to hide them since
// we wrap them in our own hand-written classes giving us complete control over
// our public API, so we set csclassmodifiers to "internal class".
// Note that any class that has an `Internal` suffix in its name will
// additionally be made "internal" due to the post-processing step here:
// http://google3/firebase/app/client/unity/swig_post_process.py?l=438&rcl=267221433
%typemap(csclassmodifiers) SWIGTYPE "internal class";

// Make sure proxy classes generated for pointers are also internal (when
// searching for the suitable typemap, SWIG goes from most to least specialized,
// not unlike C++ template argument deduction, so `SWIGTYPE *` "overrides"
// `SWIGTYPE`).
%typemap(csclassmodifiers) SWIGTYPE * "internal class";

// Work around a corner case in SWIG postprocessing by an FPL team's script (
// http://google3/firebase/app/client/unity/swig_post_process.py):
// - by default, SWIG makes all generated methods public, and there doesn't
//   appear to be a way to modify that default for all methods;
// - the script changes method visibility of all classes suffixed by `Internal`
//   to `internal`. This doesn't apply to Firestore classes because they use
//   the `Proxy` suffix.
// - the script also changes arguments of all `public` methods to use camelCase,
//   regardless of the accessibility level of the enclosing class.
// - taken together, this leads to compilation errors where `init_result_out`
//   argument of `Firestore::GetInstance` would be renamed in the function
//   signature but not in its implementation
//
// As a workaround, manually mark this method as `internal` so that the
// processing meant for `public` methods doesn't get applied.
%csmethodmodifiers firebase::firestore::Firestore::GetInstance "internal";

%import "app/src/swig/app.i"
%import "firestore/src/swig/proxy_helpers.i"
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"
%include "firestore/src/swig/types.i"

// Generate std types.
%include "stdint.i"
%include "stl.i"

// `firestore.h` includes all other Firestore public headers, so including just
// it is sufficient. Note that the code in `%{ ... %}` braces is not processed
// by SWIG; it is copied as-is into the SWIG-generated C++ file, and its only
// purpose is to make sure the resulting file compiles.
%{
#include "firestore/src/include/firebase/firestore.h"
%}

// Exception handling.
%{
#include <stdexcept>

#include "firestore/src/common/firestore_exceptions_common.h"
%}

// The boilerplate necessary to propagate Firestore exceptions from C++ to C#.
// See http://www.swig.org/Doc3.0/CSharp.html#CSharp_custom_application_exception
//
// IMPORTANT: the definition of the exported function has to satisfy the regex
// defined here:
// http://google3/firebase/app/client/unity/swig_extract_symbols.bzl;l=10-12;rcl=372332490
// In particular, you cannot declare the function itself as `extern "C"`, it has
// to be inside an `extern "C"` block, and everything up to and including the
// function name has to be defined on the same line.
%insert(runtime) %{
using CSharpExceptionCallback = void (SWIGSTDCALL*)(const char*);

CSharpExceptionCallback firestore_exception_callback = nullptr;

extern "C" {

SWIGEXPORT void SWIGSTDCALL FirestoreExceptionRegisterCallback(CSharpExceptionCallback callback) {
  firestore_exception_callback = callback;
}

}

static void SWIG_CSharpSetPendingExceptionFirestore(const char* msg) {
  firestore_exception_callback(msg);
}
%}

%pragma(csharp) imclasscode=%{
  class FirestoreExceptionHelper {
    // The C# delegate for the C++ `firestore_exception_callback`.
    public delegate void FirestoreExceptionDelegate(string message);
    static FirestoreExceptionDelegate firestoreDelegate =
      new FirestoreExceptionDelegate(SetPendingFirestoreException);

    // IMPORTANT: the next line has to be a single line. Otherwise, it won't be
    // correctly handled by `swig_post_process.py` which relies on a regex to
    // replace the name of the DLL import.
    [global::System.Runtime.InteropServices.DllImport("$dllimport", EntryPoint="FirestoreExceptionRegisterCallback")]
      public static extern
        void FirestoreExceptionRegisterCallback(FirestoreExceptionDelegate firestoreCallback);

    static void SetPendingFirestoreException(string message) {
      SWIGPendingException.Set(new FirestoreException(FirestoreError.Internal, message));
    }

    static FirestoreExceptionHelper() {
      FirestoreExceptionRegisterCallback(firestoreDelegate);
    }
  }

// Suppress the warning about an unused variable.
#pragma warning disable 414
  static FirestoreExceptionHelper exceptionHelper = new FirestoreExceptionHelper();
#pragma warning restore 414
%}

// TODO(b/186588556): for the invalid argument case, pass whether this is a null
// argument, an out of range argument, or something else (the first two cases
// have dedicated exception classes in C#).
//
// TODO(b/186588556): for the invalid argument case, pass the parameter name as
// well (needs to be attached to the C++ exception).
//
// Important: `invalid_argument` is a subclass of `logic_error` -- make sure
// they are checked in order from most to least derived.
//
// The catch-all clause is to work around iOS issues where some exception types
// aren't getting caught correctly.
%exception {
  try {
    $action
  } catch (const ::firebase::firestore::FirestoreInternalError& e) {
    SWIG_CSharpSetPendingExceptionFirestore(e.what());
    return $null;
  } catch (const std::invalid_argument& e) {
    SWIG_CSharpSetPendingExceptionArgument(
        SWIG_CSharpExceptionArgumentCodes::SWIG_CSharpArgumentException,
        e.what(), /*paramName=*/"");
    return $null;
  } catch (const std::logic_error& e) {
    SWIG_CSharpSetPendingException(SWIG_CSharpInvalidOperationException,
        e.what());
    return $null;
  } catch (const std::exception& e) {
    SWIG_CSharpSetPendingException(SWIG_CSharpInvalidOperationException,
        e.what());
    return $null;
  } catch (...) {
    SWIG_CSharpSetPendingException(SWIG_CSharpInvalidOperationException,
        "Unknown error has occurred.");
    return $null;
  }
}

// Generate callback function types.
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::firestore::csharp::DocumentEventListenerCallback,
                             Firebase.Firestore.DocumentReference.ListenerDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::firestore::csharp::QueryEventListenerCallback,
                             Firebase.Firestore.Query.ListenerDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::firestore::csharp::SnapshotsInSyncCallback,
                             Firebase.Firestore.FirebaseFirestore.SnapshotsInSyncDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::firestore::csharp::TransactionCallbackFn,
                             Firebase.Firestore.TransactionManager.TransactionCallbackDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(::firebase::firestore::csharp::LoadBundleTaskProgressCallback,
                             Firebase.Firestore.FirebaseFirestore.LoadBundleTaskProgressDelegate)

// Generate Future instantiations, must be before other wrappers.
%include "app/src/swig/future.i"
%SWIG_FUTURE(Future_QuerySnapshot, QuerySnapshotProxy, internal,
             firebase::firestore::QuerySnapshot, FirestoreException)
%SWIG_FUTURE(Future_DocumentSnapshot, DocumentSnapshotProxy, internal,
             firebase::firestore::DocumentSnapshot, FirestoreException)
%SWIG_FUTURE(Future_DocumentReference, DocumentReferenceProxy, internal,
             firebase::firestore::DocumentReference, FirestoreException)
// Override the default FutureVoid with a version that throws FirestoreException.
%SWIG_FUTURE(Future_FirestoreVoid, void, internal, void, FirestoreException)
%SWIG_FUTURE(Future_LoadBundleTaskProgress, LoadBundleTaskProgressProxy,
             internal, firebase::firestore::LoadBundleTaskProgress,
             FirestoreException)
%SWIG_FUTURE(Future_Query, QueryProxy,
             internal, firebase::firestore::Query,
             FirestoreException)

// By default, SWIG initializes C++ objects that will be used by C# proxies in
// an inefficient way. Let's say that in the C++ code being wrapped, a return
// value of type `Foo` is produced by a function `GetFoo`. For classes, SWIG
// always deals with pointers (see
// http://www.swig.org/Doc3.0/SWIGDocumentation.html#SWIG_nn18), and thus it
// needs to turn the returned value into a pointer to a dynamically allocated
// object. The default involves an extraneous intermediate variable and forces
// use of the copy constructor through casting:
//
//   Foo result;
//   result = GetFoo();
//   jresult = new Foo((const Foo&)result);
//   return jresult;
//
// This default works around some corner cases that aren't applicable to
// Firestore. It can be turned off by using `optimal="1"` attribute and
// providing a custom typemap, resulting in:
//
//   jresult = new Foo(GetFoo());
//   return jresult;
//
// See http://www.swig.org/Doc3.0/SWIGDocumentation.html#Typemaps_optimal for
// details.
%typemap(out, optimal="1") SWIGTYPE "$result = new $1_ltype($1);"

// Generate a C# wrapper for ListenerRegistration. Defined before DocumentReference and Query.
SWIG_CREATE_PROXY(firebase::firestore::ListenerRegistration)
%rename("%s") firebase::firestore::ListenerRegistration::Remove;
%include "firestore/src/include/firebase/firestore/listener_registration.h"

// Generate a C# wrapper for Source. Must be before DocumentReference and Query.
SWIG_CREATE_PROXY(firebase::firestore::Source)
%include "firestore/src/include/firebase/firestore/source.h"

// Generate a C# wrapper for FieldPath. Must be above DocumentSnapshot and SetOptions.
SWIG_CREATE_PROXY(firebase::firestore::FieldPath)
%rename("%s") firebase::firestore::FieldPath::FieldPath(const std::vector<std::string>&);
%include "firestore/src/include/firebase/firestore/field_path.h"

// Generate a C# wrapper for SetOptions and ignore SetOptions::Type. Must be before
// DocumentReference.
SWIG_CREATE_PROXY(firebase::firestore::SetOptions)
%rename("%s") firebase::firestore::SetOptions::SetOptions();
%rename("%s") firebase::firestore::SetOptions::Merge;
%rename("%s") firebase::firestore::SetOptions::MergeFields;
%include "firestore/src/include/firebase/firestore/set_options.h"

// Generate a C# wrapper for DocumentReference.
SWIG_CREATE_PROXY(firebase::firestore::DocumentReference)
%rename("%s") firebase::firestore::DocumentReference::Get;
%rename("%s") firebase::firestore::DocumentReference::Delete;
%rename("%s") firebase::firestore::DocumentReference::id;
%rename("%s") firebase::firestore::DocumentReference::path;
%rename("%s") firebase::firestore::DocumentReference::Parent;
%rename("%s") firebase::firestore::DocumentReference::Collection;
%rename("%s") firebase::firestore::DocumentReference::is_valid;
%include "firestore/src/include/firebase/firestore/document_reference.h"

// Generate a C# wrapper for SnapshotMetadata. Must be above QuerySnapshot and DocumentSnapshot.
%rename("%sProxy") firebase::firestore::SnapshotMetadata;
%include "firestore/src/include/firebase/firestore/snapshot_metadata.h"

// Generate a C# wrapper for DocumentSnapshot and DocumentSnapshot::ServerTimestampBehavior.
// Must be above DocumentChange.
SWIG_CREATE_PROXY(firebase::firestore::DocumentSnapshot)
%rename("%s") firebase::firestore::DocumentSnapshot::id;
%rename("%s") firebase::firestore::DocumentSnapshot::reference;
%rename("%s") firebase::firestore::DocumentSnapshot::exists;
%rename("%s") firebase::firestore::DocumentSnapshot::metadata;
%rename("%s") firebase::firestore::DocumentSnapshot::Get;
%rename("%s") firebase::firestore::DocumentSnapshot::ServerTimestampBehavior;
%include "firestore/src/include/firebase/firestore/document_snapshot.h"

// Generate a C# wrapper for DocumentChange and DocumentChange::Type.
// Must be after DocumentSnapshot.
SWIG_CREATE_PROXY(firebase::firestore::DocumentChange)
%rename("%s") firebase::firestore::DocumentChange::type;
%rename("%s") firebase::firestore::DocumentChange::document;
%rename("%s") firebase::firestore::DocumentChange::old_index;
%rename("%s") firebase::firestore::DocumentChange::new_index;
%rename("%s") firebase::firestore::DocumentChange::npos;
%rename("%s") firebase::firestore::DocumentChange::Type;
%include "firestore/src/include/firebase/firestore/document_change.h"

// Generate a C# wrapper for FieldValue.
SWIG_CREATE_PROXY(firebase::firestore::FieldValue)
%rename("%s") firebase::firestore::FieldValue::Null;
%rename("%s") firebase::firestore::FieldValue::Delete;
%rename("%s") firebase::firestore::FieldValue::Integer;
%rename("%s") firebase::firestore::FieldValue::Double;
%rename("%s") firebase::firestore::FieldValue::Boolean;
%rename("%s") firebase::firestore::FieldValue::String;
%rename("%s") firebase::firestore::FieldValue::Blob;
%rename("%s") firebase::firestore::FieldValue::Timestamp;
%rename("%s") firebase::firestore::FieldValue::GeoPoint;
%rename("%s") firebase::firestore::FieldValue::Reference;
%rename("%s") firebase::firestore::FieldValue::ServerTimestamp;
%rename("%s") firebase::firestore::FieldValue::Increment(T);
%rename("IntegerIncrement") firebase::firestore::FieldValue::Increment<int64_t, 0>;
%rename("DoubleIncrement") firebase::firestore::FieldValue::Increment<double, 0>;
%rename("%s") firebase::firestore::FieldValue::boolean_value;
%rename("%s") firebase::firestore::FieldValue::double_value;
%rename("%s") firebase::firestore::FieldValue::integer_value;
%rename("%s") firebase::firestore::FieldValue::string_value;
%rename("%s") firebase::firestore::FieldValue::reference_value;
%rename("%s") firebase::firestore::FieldValue::timestamp_value;
%rename("%s") firebase::firestore::FieldValue::geo_point_value;
%rename("%s") firebase::firestore::FieldValue::blob_size;
%rename("%s") firebase::firestore::FieldValue::blob_value;
%rename("%s") firebase::firestore::FieldValue::type;
%rename("%s") firebase::firestore::FieldValue::is_null;
%rename("%s") firebase::firestore::FieldValue::is_map;
%rename("%s") firebase::firestore::FieldValue::is_array;
%rename("%s") firebase::firestore::FieldValue::is_valid;
%rename("%s") firebase::firestore::FieldValue::Type;
%include "firestore/src/include/firebase/firestore/field_value.h"
// Without an explicit instantiation, SWIG would end up ignoring the template
// functions. Specifying the defaulted template argument is necessary.
%template(IntegerIncrement) firebase::firestore::FieldValue::Increment<int64_t, 0>;
%template(DoubleIncrement) firebase::firestore::FieldValue::Increment<double, 0>;

// Generate a C# wrapper for WriteBatch. Most member functions are ignored in
// favor of wrappers defined in `csharp/map.h` and `csharp/vector.h`.
SWIG_CREATE_PROXY(firebase::firestore::WriteBatch);
%rename("%s") firebase::firestore::WriteBatch::Delete;
%rename("%s") firebase::firestore::WriteBatch::Commit;
%include "firestore/src/include/firebase/firestore/write_batch.h"

// Generate a C# wrapper for MetadataChanges.
SWIG_CREATE_PROXY(firebase::firestore::MetadataChanges)
%include "firestore/src/include/firebase/firestore/metadata_changes.h"

// Generate a C# wrapper for Query and Query::Direction. Must be above CollectionReference.
SWIG_CREATE_PROXY(firebase::firestore::Query);
%rename("%s") firebase::firestore::Query::WhereEqualTo;
%rename("%s") firebase::firestore::Query::WhereNotEqualTo;
%rename("%s") firebase::firestore::Query::WhereLessThan;
%rename("%s") firebase::firestore::Query::WhereLessThanOrEqualTo;
%rename("%s") firebase::firestore::Query::WhereGreaterThan;
%rename("%s") firebase::firestore::Query::WhereGreaterThanOrEqualTo;
%rename("%s") firebase::firestore::Query::WhereArrayContains;
%rename("%s") firebase::firestore::Query::OrderBy;
%rename("%s") firebase::firestore::Query::Limit;
%rename("%s") firebase::firestore::Query::LimitToLast;
// Note: evidently SWIG matches function parameter types using simple text
// comparison, thus explicitly qualifying the namespace on `DocumentSnapshot`
// would cause this `%rename` directive to not match anything (because the
// actual header doesn't qualify the `DocumentSnapshot` argument).
%rename("%s") firebase::firestore::Query::StartAt(const DocumentSnapshot&) const;
%rename("%s") firebase::firestore::Query::StartAfter(const DocumentSnapshot&) const;
%rename("%s") firebase::firestore::Query::EndBefore(const DocumentSnapshot&) const;
%rename("%s") firebase::firestore::Query::EndAt(const DocumentSnapshot&) const;
%rename("%s") firebase::firestore::Query::Get;
%rename("%s") firebase::firestore::Query::Direction;
%include "firestore/src/include/firebase/firestore/query.h"

// Generate a C# wrapper for CollectionReference. Must be after Query.
SWIG_CREATE_PROXY(firebase::firestore::CollectionReference);
%rename("%s") firebase::firestore::CollectionReference::id;
%rename("%s") firebase::firestore::CollectionReference::path;
%rename("%s") firebase::firestore::CollectionReference::Document;
%rename("%s") firebase::firestore::CollectionReference::Parent;
%include "firestore/src/include/firebase/firestore/collection_reference.h"

// Generate a C# wrapper for QuerySnapshot. Must be after SnapshotMetadata.
SWIG_CREATE_PROXY(firebase::firestore::QuerySnapshot);
%rename("%s") firebase::firestore::QuerySnapshot::query;
%rename("%s") firebase::firestore::QuerySnapshot::metadata;
%rename("%s") firebase::firestore::QuerySnapshot::size;
%include "firestore/src/include/firebase/firestore/query_snapshot.h"

// Generate a C# wrapper for Settings.
SWIG_CREATE_PROXY(firebase::firestore::Settings);
%rename("%s") firebase::firestore::Settings::kCacheSizeUnlimited;
%rename("%s") firebase::firestore::Settings::Settings;
%rename("%s") firebase::firestore::Settings::host;
%rename("%s") firebase::firestore::Settings::is_ssl_enabled;
%rename("%s") firebase::firestore::Settings::is_persistence_enabled;
%rename("%s") firebase::firestore::Settings::cache_size_bytes;
%rename("%s") firebase::firestore::Settings::set_host;
%rename("%s") firebase::firestore::Settings::set_ssl_enabled;
%rename("%s") firebase::firestore::Settings::set_persistence_enabled;
%rename("%s") firebase::firestore::Settings::set_cache_size_bytes;
%include "firestore/src/include/firebase/firestore/settings.h"

// Generate a C# wrapper for LoadBundleTaskProgress.
SWIG_CREATE_PROXY(firebase::firestore::LoadBundleTaskProgress);
%rename("%s") firebase::firestore::LoadBundleTaskProgress::State;
%rename("%s") firebase::firestore::LoadBundleTaskProgress::documents_loaded;
%rename("%s") firebase::firestore::LoadBundleTaskProgress::total_documents;
%rename("%s") firebase::firestore::LoadBundleTaskProgress::bytes_loaded;
%rename("%s") firebase::firestore::LoadBundleTaskProgress::total_bytes;
%rename("%s") firebase::firestore::LoadBundleTaskProgress::state;
%include "firestore/src/include/firebase/firestore/load_bundle_task_progress.h"

// Generate a C# wrapper for TransactionOptions.
SWIG_CREATE_PROXY(firebase::firestore::TransactionOptions);
%rename("%s") firebase::firestore::TransactionOptions::TransactionOptions;
%rename("%s") firebase::firestore::TransactionOptions::max_attempts;
%rename("%s") firebase::firestore::TransactionOptions::set_max_attempts;
%include "firestore/src/include/firebase/firestore/transaction_options.h"

// Generate a C# wrapper for Firestore. Comes last because it refers to multiple
// other classes (e.g. `CollectionReference`).
SWIG_CREATE_PROXY(firebase::firestore::Firestore);
%rename("%s") firebase::firestore::Firestore::app;
%newobject firebase::firestore::Firestore::GetInstance;
%rename("%s") firebase::firestore::Firestore::GetInstance;
%rename("%s") firebase::firestore::Firestore::Collection;
%rename("%s") firebase::firestore::Firestore::Document;
%rename("%s") firebase::firestore::Firestore::CollectionGroup;
%rename("%s") firebase::firestore::Firestore::settings;
%rename("%s") firebase::firestore::Firestore::set_settings;
%rename("%s") firebase::firestore::Firestore::batch;
%rename("%s") firebase::firestore::Firestore::DisableNetwork;
%rename("%s") firebase::firestore::Firestore::EnableNetwork;
%rename("%s") firebase::firestore::Firestore::WaitForPendingWrites;
%rename("%s") firebase::firestore::Firestore::Terminate;
%rename("%s") firebase::firestore::Firestore::ClearPersistence;
%rename("%s") firebase::firestore::Firestore::NamedQuery;
%rename("%s") firebase::firestore::Firestore::set_log_level;

%include "firestore/src/include/firebase/firestore.h"

// Interop helpers.
%{
#include "firestore/src/swig/document_event_listener.h"
#include "firestore/src/swig/equality_compare.h"
#include "firestore/src/swig/hash.h"
#include "firestore/src/swig/query_event_listener.h"
#include "firestore/src/swig/snapshots_in_sync_listener.h"
#include "firestore/src/swig/load_bundle_task_progress_callback.h"
%}
%include "firestore/src/swig/document_event_listener.h"
%include "firestore/src/swig/equality_compare.h"
%include "firestore/src/swig/hash.h"
%include "firestore/src/swig/query_event_listener.h"
%include "firestore/src/swig/snapshots_in_sync_listener.h"
%include "firestore/src/swig/load_bundle_task_progress_callback.h"

%{
#include "firestore/src/swig/api_headers.h"
%}
%include "firestore/src/swig/api_headers.h"

// `std::unordered_map<K, V>` wrapper.
%{
#include "firestore/src/swig/map.h"
%}
%include "firestore/src/swig/map.h"

%rename("$ignore", regextarget=1, fullname=1) "firebase::firestore::csharp::Map.*::Wrap";
%rename("$ignore", regextarget=1, fullname=1) "firebase::firestore::csharp::Map.*::Unwrap";

// Transaction support classes and functions.
SWIG_CREATE_PROXY(firebase::firestore::csharp::TransactionManager)
%rename("%s") firebase::firestore::csharp::TransactionManager::TransactionManager;
%rename("CppDispose") firebase::firestore::csharp::TransactionManager::Dispose;
%rename("%s") firebase::firestore::csharp::TransactionManager::RunTransaction;

SWIG_CREATE_PROXY(firebase::firestore::csharp::TransactionCallback)
%rename("%s") firebase::firestore::csharp::TransactionCallback::callback_id;
%rename("%s") firebase::firestore::csharp::TransactionCallback::Delete;
%rename("%s") firebase::firestore::csharp::TransactionCallback::Get;
%rename("%s") firebase::firestore::csharp::TransactionCallback::OnCompletion;
%rename("%s") firebase::firestore::csharp::TransactionCallback::Set;
%rename("%s") firebase::firestore::csharp::TransactionCallback::Update;

SWIG_CREATE_PROXY(firebase::firestore::csharp::TransactionResultOfGet)
%rename("%s") firebase::firestore::csharp::TransactionResultOfGet::error_code;
%rename("%s") firebase::firestore::csharp::TransactionResultOfGet::error_message;
%rename("%s") firebase::firestore::csharp::TransactionResultOfGet::is_valid;
%rename("%s") firebase::firestore::csharp::TransactionResultOfGet::TakeSnapshot;

%{
#include "firestore/src/swig/transaction_manager.h"
%}
%include "firestore/src/swig/transaction_manager.h"

namespace firebase {
namespace firestore {

%rename(FieldToValueMapIterator) csharp::Map<std::string, FieldValue>::MapIterator;
%template(FieldToValueMap) csharp::Map<std::string, FieldValue>;
%rename(FieldPathToValueMapIterator) csharp::Map<FieldPath, FieldValue>::MapIterator;
%template(FieldPathToValueMap) csharp::Map<FieldPath, FieldValue>;

} // namespace firestore
} // namespace firebase

// `std::vector<T>` wrapper.
%{
#include "firestore/src/swig/vector.h"
%}
%include "firestore/src/swig/vector.h"

%rename("$ignore", regextarget=1, fullname=1) "firebase::firestore::csharp::Vector.*::Wrap";
%rename("$ignore", regextarget=1, fullname=1) "firebase::firestore::csharp::Vector.*::Unwrap";

namespace firebase {
namespace firestore {

%template(DocumentChangeVector) csharp::Vector<DocumentChange>;
%template(DocumentSnapshotVector) csharp::Vector<DocumentSnapshot>;
%template(FieldPathVector) csharp::Vector<FieldPath>;
%template(FieldValueVector) csharp::Vector<FieldValue>;

} // namespace firestore
} // namespace firebase

// # LINT.ThenChange(//depot/google3/firebase/firestore/client/unity/generated/src/last-updated.txt)
