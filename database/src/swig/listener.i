// Copyright 2018 Google Inc. All Rights Reserved.
//
// This file helps implementing listeners for the Firebase Database.
// It contains:
//   1) An implementation of ValueListener that can be registered with a Query.
//      The ValueListenerImpl will call the C# delegate functions
//   2) Helper functions attached to Query, which allow the creation and
//      destruction of ValueListenerImpl instances from C# code.
// The C# code that is called from ValueListenerImpl lives in a different file,
// under firebase/client/unity/src/internal/InternalValueListener.cs

%include "app/src/swig/null_check_this.i"

// To avoid code duplication between OnChildAdded, OnChildChanged and OnChildMoved,
// we use a single callback with a shared enum to distinguish the change type.
%inline %{
namespace firebase {
namespace database {
enum ChildChangeType {
  kAdded = 1,
  kChanged = 2,
  kMoved = 3,
};
} // namespace database
} // namespace firebase
%}

%{
#include "app/src/include/firebase/internal/mutex.h"

namespace firebase {
namespace database {
namespace internal {

typedef void (SWIGSTDCALL *CancelledCallback)(
    int callback_id, Error error, char* msg);
typedef void (SWIGSTDCALL *ValueChangedCallback)(
    int callback_id, void* snapshot);
typedef void (SWIGSTDCALL *ChildChangeCallback)(
    int callback_id, ChildChangeType change_type, void* snapshot, char* previous_sibling_key);
typedef void (SWIGSTDCALL *ChildRemovedCallback)(
    int callback_id, void* snapshot);
typedef TransactionResult (SWIGSTDCALL *TransactionCallback)(
    int callback_id, void* mutable_data);

// This class implements a ValueListener that forwards callbacks to C#.
// It adds and removes itself from listening the query on construction and destruction.
class ValueListenerImpl : public firebase::database::ValueListener {
 public:
  // Creates a ValueListener that starts listening to the given Query.
  ValueListenerImpl(int callback_id, const Query& query)
      : callback_id_(callback_id), query_(query) {
    query_.AddValueListener(this);
  }
  // Stop listening to the query in the destructor.
  virtual ~ValueListenerImpl() {
    query_.RemoveValueListener(this);
  };

  // OnCancelled handler - forwards the callback to the callback queue.
  virtual void OnCancelled(const Error& error, const char* error_message) {
    if (g_cancelled_callback) {
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2String1<int,Error>(
              callback_id_, error, error_message, Cancelled));
    }
  }

  // OnValueChanged handler - forwards the callback to the callback queue.
  virtual void OnValueChanged(const DataSnapshot& snapshot) {
    if (g_value_changed_callback) {
      DataSnapshot* snapshot_copy = new DataSnapshot(snapshot);
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2<int,DataSnapshot*>(
              callback_id_, snapshot_copy, ValueChanged));
    }
  }

  static void RegisterCallbacks(CancelledCallback cancelled_callback,
                                ValueChangedCallback value_changed_callback) {
    MutexLock lock(g_mutex);
    g_cancelled_callback = cancelled_callback;
    g_value_changed_callback = value_changed_callback;
  }

 private:
  // ID to find the destination instance for this listener.
  // This is needed because C++ -> C# callbacks must be static.
  int callback_id_;
  // The query this is listening too.
  // We keep it, so that we can automatically stop listening on destruction.
  Query query_;

  // C# callback functions and a Mutex to protect access to them.
  static Mutex g_mutex;
  static CancelledCallback g_cancelled_callback;
  static ValueChangedCallback g_value_changed_callback;

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void Cancelled(int callback_id, Error error, const char* msg) {
    MutexLock lock(g_mutex);
    if (g_cancelled_callback) {
      g_cancelled_callback(callback_id, error, SWIG_csharp_string_callback(msg));
    }
  }

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void ValueChanged(int callback_id, DataSnapshot* snapshot) {
    MutexLock lock(g_mutex);
    if (g_value_changed_callback) {
      g_value_changed_callback(callback_id, snapshot);
    } else {
      // Delete the copy of the snapshot, as there is no callback made.
      delete snapshot;
    }
  }
};

Mutex ValueListenerImpl::g_mutex;
CancelledCallback ValueListenerImpl::g_cancelled_callback;
ValueChangedCallback ValueListenerImpl::g_value_changed_callback;

// This class implements a ChildListener that forwards callbacks to C#.
// It adds and removes itself from listening the query on construction and destruction.
class ChildListenerImpl : public firebase::database::ChildListener {
 public:
  // Creates a ValueListener that starts listening to the given Query.
  ChildListenerImpl(int callback_id, const Query& query)
      : callback_id_(callback_id), query_(query) {
    query_.AddChildListener(this);
  }
  // Stop listening to the query in the destructor.
  virtual ~ChildListenerImpl() {
    query_.RemoveChildListener(this);
  };

  // OnCancelled handler - forwards the callback to the callback queue.
  virtual void OnCancelled(const Error& error, const char* error_message) {
    if (g_cancelled_callback) {
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2String1<int,Error>(
              callback_id_, error, error_message, Cancelled));
    }
  }

  // Common implementation for OnChildAdded, OnChildChanged & OnChildMoved handlers.
  void OnChildChange(ChildChangeType change_type, const DataSnapshot& snapshot,
      const char *previous_sibling_key) {
    if (g_child_change_callback) {
      DataSnapshot* snapshot_copy = new DataSnapshot(snapshot);
      firebase::callback::AddCallback(
          new firebase::callback::
              CallbackValue3String1<int,ChildChangeType,DataSnapshot*>(
                  callback_id_, change_type, snapshot_copy,
                  previous_sibling_key, ChildChange));
    }
  }

  virtual void OnChildAdded(const DataSnapshot& snapshot, const char *previous_sibling_key) {
    OnChildChange(kAdded, snapshot, previous_sibling_key);
  }

  virtual void OnChildChanged(const DataSnapshot& snapshot, const char *previous_sibling_key) {
    OnChildChange(kChanged, snapshot, previous_sibling_key);
  }

  virtual void OnChildMoved(const DataSnapshot& snapshot, const char *previous_sibling_key) {
    OnChildChange(kMoved, snapshot, previous_sibling_key);
  }

  // OnChildRemoved handler - forwards the callback to the callback queue.
  virtual void OnChildRemoved(const DataSnapshot& snapshot) {
    if (g_child_removed_callback) {
      DataSnapshot* snapshot_copy = new DataSnapshot(snapshot);
      firebase::callback::AddCallback(
          new firebase::callback::CallbackValue2<int,DataSnapshot*>(
              callback_id_, snapshot_copy, ChildRemoved));
    }
  }

  static void RegisterCallbacks(CancelledCallback cancelled_callback,
                                ChildChangeCallback child_change_callback,
                                ChildRemovedCallback child_removed_callback) {
    MutexLock lock(g_mutex);
    g_cancelled_callback = cancelled_callback;
    g_child_change_callback = child_change_callback;
    g_child_removed_callback = child_removed_callback;
  }

 private:
  // ID to find the destination instance for this listener.
  // This is needed because C++ -> C# callbacks must be static.
  int callback_id_;
  // The query this is listening too.
  // We keep it, so that we can automatically stop listening on destruction.
  Query query_;

  // C# callback functions and a Mutex to protect access to them.
  static Mutex g_mutex;
  static CancelledCallback g_cancelled_callback;
  static ChildChangeCallback g_child_change_callback;
  static ChildRemovedCallback g_child_removed_callback;

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void Cancelled(int callback_id, Error error, const char* msg) {
    MutexLock lock(g_mutex);
    if (g_cancelled_callback) {
      g_cancelled_callback(callback_id, error, SWIG_csharp_string_callback(msg));
    }
  }

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void ChildChange(int callback_id, ChildChangeType change_type,
      DataSnapshot* snapshot, const char* previous_sibling_key) {
    MutexLock lock(g_mutex);
    if (g_child_change_callback) {
      g_child_change_callback(callback_id, change_type, snapshot,
          SWIG_csharp_string_callback(previous_sibling_key));
    } else {
      // Delete the copy of the snapshot, as there is no callback made.
      delete snapshot;
    }
  }

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void ChildRemoved(int callback_id, DataSnapshot* snapshot) {
    MutexLock lock(g_mutex);
    if (g_child_removed_callback) {
      g_child_removed_callback(callback_id, snapshot);
    } else {
      // Delete the copy of the snapshot, as there is no callback made.
      delete snapshot;
    }
  }
};

Mutex ChildListenerImpl::g_mutex;
CancelledCallback ChildListenerImpl::g_cancelled_callback;
ChildChangeCallback ChildListenerImpl::g_child_change_callback;
ChildRemovedCallback ChildListenerImpl::g_child_removed_callback;

// This class helps direct transaction callbacks to C#.
// It is similar but not identical to the child & value listeners above.
// Because transactions only require a function pointer, we do not need objects
// and this class is more a collection of helper methods for transactions.
// We do use instances of this class to pass information from and to the
// callback thread.
class TransactionHelper {
 public:
  static void RegisterCallback(TransactionCallback transaction_callback) {
    MutexLock lock(g_mutex);
    g_transaction_callback = transaction_callback;
  }

  static TransactionResult OnTransaction(MutableData *data, void *context) {
    // Instead of defining a Callback3<A,B,C>, we pass a pointer to a helper.
    // Because the callback is blocking, it is safe to pass a pointer to a local variable.
    TransactionHelper helper;
    helper.callback_id = static_cast<int>(reinterpret_cast<size_t>(context));
    // There is no copy constructor for MutableData, so we cannot make a copy.
    // The corresponding C# object will ONLY by valid during the callback.
    helper.data = data;
    // Initialize the result to Abort, in case something goes wrong.
    helper.result = kTransactionResultAbort;
    if (g_transaction_callback) {
      firebase::callback::AddBlockingCallback(
          new firebase::callback::
              CallbackValue1<TransactionHelper*>(&helper, Transaction));
    }
    return helper.result;
  }

 private:
  static Mutex g_mutex;
  static TransactionCallback g_transaction_callback;

  int callback_id;
  MutableData *data;
  // The result of the transaction callback.
  TransactionResult result;

  // UserCallback that is called from the callback queue and actually calls C# code.
  static void Transaction(TransactionHelper* helper) {
    MutexLock lock(g_mutex);
    if (g_transaction_callback) {
      helper->result = g_transaction_callback(helper->callback_id, helper->data);
    } else {
      helper->result = kTransactionResultAbort;
    }
  }
};

Mutex TransactionHelper::g_mutex;
TransactionCallback TransactionHelper::g_transaction_callback;

}  // namespace internal
}  // namespace database
}  // namespace firebase

%}

%extend firebase::database::Query {
  // Creates a new value listener with the given callback_id to handle the call.
  // Returns that listener, so that the caller can manage that memory.
  void* CreateValueListener(int callback_id) {
    ::firebase::database::internal::ValueListenerImpl* listener =
        new ::firebase::database::internal::ValueListenerImpl(callback_id, *$self);
    return listener;
  }

  // Destroys a ValueListener that was created by CreateValueListener.
  // Note: this needs to be static because it might be called from a C# finalizer
  // and those can run in any weird order.
  static void DestroyValueListener(void* listener) {
    ::firebase::database::internal::ValueListenerImpl* value_listener =
        reinterpret_cast<::firebase::database::internal::ValueListenerImpl*>(listener);
    delete value_listener;
  }

  // Register C# callbacks
  static void RegisterValueListenerCallbacks(
      ::firebase::database::internal::CancelledCallback cancelled_callback,
      ::firebase::database::internal::ValueChangedCallback value_changed_callback) {
    ::firebase::database::internal::ValueListenerImpl::RegisterCallbacks(
        cancelled_callback, value_changed_callback);
  }

  // Creates a new child listener with the given callback_id to handle the call.
  // Returns that listener, so that the caller can manage that memory.
  void* CreateChildListener(int callback_id) {
    ::firebase::database::internal::ChildListenerImpl* listener =
        new ::firebase::database::internal::ChildListenerImpl(callback_id, *$self);
    return listener;
  }

  // Destroys a ChildListener that was created by CreateChildListener.
  // Note: this needs to be static because it might be called from a C# finalizer
  // and those can run in any weird order.
  static void DestroyChildListener(void* listener) {
    ::firebase::database::internal::ChildListenerImpl* child_listener =
        reinterpret_cast<::firebase::database::internal::ChildListenerImpl*>(listener);
    delete child_listener;
  }

  // Register C# callbacks
  static void RegisterChildListenerCallbacks(
      ::firebase::database::internal::CancelledCallback cancelled_callback,
      ::firebase::database::internal::ChildChangeCallback child_change_callback,
      ::firebase::database::internal::ChildRemovedCallback child_removed_callback) {
    ::firebase::database::internal::ChildListenerImpl::RegisterCallbacks(
        cancelled_callback, child_change_callback, child_removed_callback);
  }
}

%extend firebase::database::DatabaseReference {
  static void RegisterTransactionCallback(
      ::firebase::database::internal::TransactionCallback transaction_callback) {
    firebase::database::internal::TransactionHelper::RegisterCallback(transaction_callback);
  }

  Future<DataSnapshot> RunTransaction(int callback_id, bool trigger_local_events) {
    // We do not actually need a pointer as context, only an int, so we
    // simply cast the callback_id to void* and store it in the address.
    void* context = reinterpret_cast<void*>(callback_id);
    return $self->RunTransaction(
        firebase::database::internal::TransactionHelper::OnTransaction,
        context, trigger_local_events);
  }
}

// Some of these maps are redundant (e.g. CancelledCallback), but that does not
// seem to cause problems with SWIG.
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::CancelledCallback,
    Firebase.Database.Internal.InternalValueListener.OnCancelledDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::ValueChangedCallback,
    Firebase.Database.Internal.InternalValueListener.OnValueChangedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::CancelledCallback,
    Firebase.Database.Internal.InternalChildListener.OnCancelledDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::ChildChangeCallback,
    Firebase.Database.Internal.InternalChildListener.OnChildChangeDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::ChildRemovedCallback,
    Firebase.Database.Internal.InternalChildListener.OnChildRemovedDelegate)
SWIG_MAP_CFUNC_TO_CSDELEGATE(
    ::firebase::database::internal::TransactionCallback,
    Firebase.Database.Internal.InternalTransactionHandler.TransactionDelegate)
