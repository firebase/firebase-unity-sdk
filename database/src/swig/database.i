// Copyright 2017 Google Inc. All Rights Reserved.
//
// C# bindings for the Database C++ interface.
// Swig Lint doesn't support C#
//swiglint: disable

%module DatabaseInternal

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
%include "app/src/swig/init_result.i"
%include "app/src/swig/null_check_this.i"
%include "app/src/swig/serial_dispose.i"

%ignore firebase::database::internal;

%typemap(csclassmodifiers) SWIGTYPE "internal class";
%typemap(csclassmodifiers) enum SWIGTYPE "internal enum";

// Include require headers for the generated C++ module.
%{
#include "app/src/callback.h"
#include "app/src/cpp_instance_manager.h"
#include "database/src/include/firebase/database.h"

#include <stdexcept>
#include <algorithm>
#include <utility>

namespace firebase {
namespace database {
// Reference count manager for C++ Database instances, using pointer as the
// key for searching.
static CppInstanceManager<Database> g_database_instances;

} // namespace database
} // namespace firebase
%}

%extend firebase::database::Database {
  // Get a C++ instance and increment the reference count to it
  %csmethodmodifiers GetInstanceInternal(App* app, const char* url, InitResult* init_result_out) "internal";
  static ::firebase::database::Database* GetInstanceInternal(
      ::firebase::App* app, const char* url, InitResult* init_result_out) {
    // This is to protect from the race condition after GetInstance() is
    // called and before the pointer is added to g_database_instances.
    ::firebase::MutexLock lock(
        ::firebase::database::g_database_instances.mutex());

    ::firebase::database::Database* instance = url ?
        ::firebase::database::Database::GetInstance(app, url, init_result_out) :
        ::firebase::database::Database::GetInstance(app, init_result_out);

    // Increment the reference to this instance.
    ::firebase::database::g_database_instances.AddReference(instance);
    return instance;
  }

  // Release and decrement the reference count to a C++ instance
  %csmethodmodifiers ReleaseReferenceInternal(::firebase::database::Database* instance) "internal";
  static void ReleaseReferenceInternal(::firebase::database::Database* instance) {
    ::firebase::database::g_database_instances.ReleaseReference(instance);
  }
}

// Avoid filename conflicts with the public API
%rename (InternalDataSnapshot) DataSnapshot;
%rename (InternalDatabaseReference) DatabaseReference;
%rename (InternalFirebaseDatabase) Database;
%rename (InternalMutableData) MutableData;
%rename (InternalQuery) Query;
%rename (InternalTransactionResult) TransactionResult;

%SWIG_FUTURE(Future_InternalDataSnapshot, InternalDataSnapshot, internal,
             firebase::database::DataSnapshot, FirebaseException)

// Delete the C++ object if hasn't already been deleted.
#if SWIG_VERSION >= 0x040000
%typemap(csdisposing, methodname="Dispose",
         parameters="bool disposing", methodmodifiers="public")
#else
%typemap(csdestruct, methodname="Dispose", methodmodifiers="public")
#endif
      firebase::database::Database {
    lock (FirebaseApp.disposeLock) {
      ReleaseReferenceInternal(this);
      swigCMemOwn = false;
      swigCPtr = new global::System.Runtime.InteropServices.HandleRef(
          null, global::System.IntPtr.Zero);

      // Suppress finalization.
      global::System.GC.SuppressFinalize(this);
    }
}

%include "database/src/swig/listener.i"

%typemap(csclassmodifiers) std::vector<firebase::database::DataSnapshot> "internal class"
%template(InternalDataSnapshotList) std::vector<firebase::database::DataSnapshot>;

// 314: Ignore warnings about the internal namespace being renamed to
// _internal as we flatten everything in the target namepsace.
// 516: Ignore warnings about overloaded methods being ignored.
// In all cases, the overload does the right thing e.g
// Storage::GetReference(const char*) used instead of
// Storage::GetReference(std::string&).
// 814: Globally disable warnings about methods not potentially throwing
// exceptions.  We know that *all* methods can potentially throw exceptions.
%warnfilter(314,516,844);

%rename("$ignore", regextarget=1, fullname=1) "firebase::database::MutableData::.*";
%rename("%s") firebase::database::MutableData::~MutableData;
%rename("%s") firebase::database::MutableData::children_count;
%rename("%s") firebase::database::MutableData::key;
%rename("%s") firebase::database::MutableData::value;
%rename("%s") firebase::database::MutableData::set_value;
%rename("%s") firebase::database::MutableData::priority;
%rename("%s") firebase::database::MutableData::set_priority;
%rename("%s") firebase::database::MutableData::HasChild;
%rename("%s") firebase::database::MutableData::ChildrenEnumerator;

// These methods are unused and trigger the creation of additional classes
// if they aren't ignored.
%rename("$ignore") firebase::database::DatabaseReference::RunTransaction;
%rename("$ignore") firebase::database::DatabaseReference::
  UpdateChildren(const std::map<std::string, Variant>& values);
%rename("$ignore") firebase::database::DisconnectionHandler::
  UpdateChildren(const std::map<std::string, Variant>& values);

// It's overkill to expose the full vector interface if we only need an enumerator
%ignore firebase::database::MutableDataChildrenEnumerator::MutableDataChildrenEnumerator;
%inline %{
namespace firebase {
namespace database {
class MutableDataChildrenEnumerator {
 public:
  MutableDataChildrenEnumerator(std::vector<firebase::database::MutableData>&& children)
      : children_(std::move(children)), position_(-1) {}
  firebase::database::MutableData* Current() {
    if (position_ < 0 || position_ >= children_.size()) {
      return nullptr;
    } else {
      return &children_[position_];
    }
  }
  bool MoveNext() { return ++position_ < children_.size(); }
  void Reset() { position_ = -1; }
 private:
  std::vector<firebase::database::MutableData> children_;
  int position_;
};
}  // namespace database
}  // namespace firebase
%}

%include "database/src/include/firebase/database/common.h"
%csmethodmodifiers firebase::database::Database::GetInstance(::firebase::App*, InitResult*) "internal";
%csmethodmodifiers firebase::database::Database::GetInstance(::firebase::App*, const char*, InitResult*) "internal";
%newobject firebase::database::Database::GetInstance;
%ignore firebase::database::Database::url;
%include "database/src/include/firebase/database.h"
%include "database/src/include/firebase/database/disconnection.h"
%include "database/src/include/firebase/database/transaction.h"
%include "database/src/include/firebase/database/data_snapshot.h"
%include "database/src/include/firebase/database/listener.h"
%include "database/src/include/firebase/database/query.h"
%include "database/src/include/firebase/database/database_reference.h"
%include "database/src/include/firebase/database/mutable_data.h"

%newobject firebase::database::MutableData::ChildrenEnumerator;
%rename("%s") firebase::database::MutableData::GetChild;
%extend firebase::database::MutableData {
  firebase::database::MutableDataChildrenEnumerator* ChildrenEnumerator() {
    return new firebase::database::MutableDataChildrenEnumerator($self->children());
  }

  // Return a pointer to a move constructed Child, since swig tries to use
  // a copy constructor otherwise.
  firebase::database::MutableData* GetChild(const char* path) {
    return new firebase::database::MutableData($self->Child(path));
  }
}
