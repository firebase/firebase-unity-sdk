/*
 * Copyright 2016 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Database.Internal;
using Google.MiniJSON;

namespace Firebase.Database {
  /// <summary>
  ///   A Firebase reference represents a particular location in your
  /// <see cref="FirebaseDatabase" />
  ///   and can be used for reading or writing data to that
  /// <see cref="FirebaseDatabase" /> location.
  /// </summary>
  /// <remarks>
  ///   A Firebase reference represents a particular location in your
  /// <see cref="FirebaseDatabase" />
  ///   and can be used for reading or writing data to that
  /// <see cref="FirebaseDatabase" /> location.
  ///   This class is the starting point for all Database operations. After you've
  ///   initialized it with a URL, you can use it to read data, write data,
  /// and to create mores instances of <see cref="DatabaseReference" />.
  /// </remarks>
  public sealed class DatabaseReference : Query {
    private readonly InternalDatabaseReference internalReference;

    /// <param name="repo">The repo for this ref</param>
    /// <param name="path">The path to reference</param>
    internal DatabaseReference(
        InternalDatabaseReference internalRef, FirebaseDatabase database)
        : base(internalRef, database) {
      internalReference = internalRef;
      Database = database;
    }

    // Getters and other auxiliary methods
    /// <summary>Gets the Database instance associated with this reference.</summary>
    /// <returns>The Database object for this reference.</returns>
    public FirebaseDatabase Database { get; private set; }

    /// A DatabaseReference to the parent location, or null if this instance references the root
    /// location.
    public DatabaseReference Parent {
      get {
        // In C++, root.GetParent returns the root again.
        // But for backward's compatibility, we want to return null in C#.
        return internalReference.is_root() ?
            null : new DatabaseReference(internalReference.GetParent(), Database);
      }
    }

    /// A reference to the root location of this Firebase Database.
    public DatabaseReference Root {
      get { return new DatabaseReference(internalReference.GetRoot(), Database); }
    }

    /// <summary>Get a reference to location relative to this one</summary>
    /// <param name="pathString">
    ///   The relative path from this reference to the new one that should be created
    /// </param>
    /// <returns>A new DatabaseReference to the given path</returns>
    public DatabaseReference Child(string pathString) {
      return new DatabaseReference(internalReference.Child(pathString), Database);
    }

    /// <summary>Create a reference to an auto-generated child location.</summary>
    /// <remarks>
    ///   Create a reference to an auto-generated child location. The child key is generated
    ///   client-side and incorporates an estimate of the server's time for sorting purposes.
    ///   Locations generated on a single client will be sorted in the order that they are created,
    ///   and will be sorted approximately in order across all clients.
    /// </remarks>
    /// <returns>A DatabaseReference pointing to the new location</returns>
    public DatabaseReference Push() {
      return new DatabaseReference(internalReference.PushChild(), Database);
    }

    /// <summary>Set the data at this location to the given value.</summary>
    /// <remarks>
    ///   Set the data at this location to the given value. Passing null to setValue() will delete
    ///   the data at the specified location.
    ///   The allowed types are:
    ///   - bool
    ///   - string
    ///   - long
    ///   - double
    ///   - IDictionary{string, object}
    ///   - List{object}
    ///   <br /><br />
    /// </remarks>
    /// <param name="value">The value to set at this location</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetValueAsync(object value) {
      return WrapInternalDefaultTask(internalReference.SetValueAsync(Utilities.MakeVariant(value)));
    }

    /// <summary>Set the data at this location to the given string json represenation.</summary>
    /// <remarks />
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetRawJsonValueAsync(string jsonValue) {
      return SetValueAsync(Json.Deserialize(jsonValue));
    }

    /// <summary>Set the data and priority to the given values.</summary>
    /// <remarks>
    ///   Set the data and priority to the given values. Passing null to setValue() will delete the
    ///   data at the specified location.
    ///   The allowed types are:
    ///   - bool
    ///   - string
    ///   - long
    ///   - double
    ///   - IDictionary{string, object}
    ///   - List{object}
    ///   <br /><br />
    /// </remarks>
    /// <param name="value">The value to set at this location</param>
    /// <param name="priority">The priority to set at this location</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetValueAsync(object value, object priority) {
      return WrapInternalDefaultTask(internalReference.SetValueAndPriorityAsync(
          Utilities.MakeVariant(value), Utilities.MakePriorityVariant(priority)));
    }

    /// <summary>Set the data and priority to the given values.</summary>
    /// <remarks />
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetRawJsonValueAsync(string jsonValue, object priority) {
      return SetValueAsync(Json.Deserialize(jsonValue), priority);
    }

    // Set priority
    /// <summary>Set a priority for the data at this Database location.</summary>
    /// <remarks>
    ///   Set a priority for the data at this Database location.
    ///   Priorities can be used to provide a custom ordering for the children at a location
    ///   (if no priorities are specified, the children are ordered by key).
    ///   <br /><br />
    ///   You cannot set a priority on an empty location. For this reason
    ///   setValue(data, priority) should be used when setting initial data with a specific priority
    ///   and setPriority should be used when updating the priority of existing data.
    ///   <br /><br />
    ///   Children are sorted based on this priority using the following rules:
    ///   <ul>
    ///     <li>Children with no priority come first.</li>
    ///     <li>
    ///       Children with a number as their priority come next. They are sorted numerically by
    ///       priority (small to large).
    ///     </li>
    ///     <li>Children with a string as their priority come last. They are sorted
    ///       lexicographically by priority.
    ///     </li>
    ///     <li>
    ///       Whenever two children have the same priority (including no priority),
    ///       they are sorted by key.  Numeric keys come first (sorted numerically),
    ///       followed by the remaining keys (sorted lexicographically).
    ///     </li>
    ///   </ul>
    ///   Note that numerical priorities are parsed and ordered as IEEE 754 double-precision
    ///   floating-point numbers.
    ///   Keys are always stored as strings and are treated as numeric only when they can be parsed
    ///   as a 32-bit integer.
    /// </remarks>
    /// <param name="priority">The priority to set at the specified location.</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetPriorityAsync(object priority) {
      return WrapInternalDefaultTask(internalReference.SetPriorityAsync(Utilities.MakePriorityVariant(priority)));
    }

    // Update
    /// <summary>Update the specific child keys to the specified values.</summary>
    /// <remarks>
    ///   Update the specific child keys to the specified values.  Passing null in a map to
    ///   updateChildren() will Remove the value at the specified location.
    /// </remarks>
    /// <param name="update">The paths to update and their new values</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task UpdateChildrenAsync(IDictionary<string, object> update) {
      return internalReference.UpdateChildrenAsync(Utilities.MakeVariant(update));
    }

    // Remove
    /// <summary>Set the value at this location to 'null'</summary>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task RemoveValueAsync() {
      return internalReference.RemoveValueAsync();
    }

    // Access to disconnect operations
    /// <summary>Provides access to disconnect operations at this location</summary>
    /// <returns>An object for managing disconnect operations at this location</returns>
    public OnDisconnect OnDisconnect() {
      return new OnDisconnect(internalReference.OnDisconnect());
    }

    // Transactions
    /// <summary>Run a transaction on the data at this location.</summary>
    /// <remarks>
    ///   A transaction is a data transformation function that is continually
    /// attempted until the DatabaseReference location remains unchanged during
    /// the operation.
    /// </remarks>
    /// <param name="transaction">A function to perform the transaction and return a result</param>
    public Task<DataSnapshot> RunTransaction(Func<MutableData, TransactionResult> transaction) {
      return RunTransaction(transaction, true);
    }

    /// <summary>Run a transaction on the data at this location.</summary>
    /// <remarks>
    ///   A transaction is a data transformation function that is continually
    /// attempted until the DatabaseReference location remains unchanged during
    /// the operation.
    /// </remarks>
    /// <param name="transaction">A function to perform the transaction and return a result</param>
    /// <param name="fireLocalEvents">
    ///   Defaults to true. If set to false, events will only be fired for
    ///   the final result state of the transaction, and not for any
    ///   intermediate states
    /// </param>
    public Task<DataSnapshot> RunTransaction(Func<MutableData, TransactionResult> transaction,
        bool fireLocalEvents) {
      InternalTransactionHandler handler =
          new InternalTransactionHandler(transaction, Database);
      Task<InternalDataSnapshot> t =
          internalReference.RunTransactionAsync(handler.CallbackId, fireLocalEvents);
      t.ContinueWith((task) => {
          // We dispose the handler once the transaction completes.
          // No matter HOW the transaction completed, we're done with the handler.
          // Note: WrapInternalDataSnapshotTask will register a second ContinueWith on this task.
          // This is perfectly valid in C#. It is also safe, as we don't care about the
          // order in which these continuations are called.
          handler.Dispose();
      });
      return WrapInternalDataSnapshotTask(t);
    }

    // Manual Connection Management
    /// <summary>
    ///   Manually disconnect the Firebase Database client from the server and disable
    ///   automatic reconnection.
    /// </summary>
    /// <remarks>
    ///   Manually disconnect the Firebase Database client from the server and disable
    ///   automatic reconnection.
    ///   Note: Invoking this method will impact all Firebase Database connections.
    /// </remarks>
    public static void GoOffline() {
      FirebaseDatabase.AnyInstance.GoOffline();
    }

    /// <summary>
    ///   Manually reestablish a connection to the Firebase Database server and enable
    ///   automatic reconnection.
    /// </summary>
    /// <remarks>
    ///   Manually reestablish a connection to the Firebase Database server and enable
    ///   automatic reconnection.
    ///   Note: Invoking this method will impact all Firebase Database connections.
    /// </remarks>
    public static void GoOnline() {
      FirebaseDatabase.AnyInstance.GoOnline();
    }

    /// The full location url for this reference.
    public override string ToString() {
      return internalReference.url();
    }

    /// <value>The last token in the location pointed to by this reference</value>
    public string Key {
      get { return internalReference.is_root() ? null : internalReference.key_string(); }
    }

    /// Returns true if both objects reference the same database path.
    public override bool Equals(object other) {
      return other is DatabaseReference && ToString().Equals(other.ToString());
    }

    /// A hash code based on the string path of the reference.
    public override int GetHashCode() {
      return ToString().GetHashCode();
    }

    /// Creates a wrapper around the internal task returned by the SWIG classes.
    internal Task WrapInternalDefaultTask(Task it) {
      TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
      it.ContinueWith(task => {
        // By calling CheckTaskStatus(), it keeps a reference to this, which holds a reference to
        // FirebaseDatabase.  This prevents FirebaseDatabase from being GCed until the end of this
        // block.
        if (CheckTaskStatus<bool>(task, tcs)){
          tcs.SetResult(true);
        }
      });
      return tcs.Task;
    }
  }
}
