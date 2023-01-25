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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Firebase.Database.Internal;

namespace Firebase.Database {
  /// <summary>
  ///   The Query class (and its subclass,
  ///   <see cref="DatabaseReference" />
  ///   ) are used for reading data.
  ///   Listeners are attached, and they will be triggered when the corresponding data changes.
  ///   <br /><br />
  ///   Instances of Query are obtained by calling StartAt(), EndAt(), or Limit() on a
  ///   DatabaseReference.
  /// </summary>
  public class Query {
    private readonly InternalQuery internalQuery;
    // The query only keeps a reference to the database to ensure that it's kept alive,
    // as the underlying C++ code needs that.
    private readonly FirebaseDatabase database;
    private readonly InternalValueListener valueListener;
    private readonly InternalChildListener childListener;

    internal Query(InternalQuery internalQuery, FirebaseDatabase database) {
      if (!internalQuery.is_valid()) {
        // This is a bit awkward - if misused, the C++ SDK simply returns an invalid Query and
        // *logs* the error. Which means that we can't access the cause of the error and have
        // to refer the user to the log.
        // Also, we throw an ArgumentException, instead of a DatabaseException, because that's
        // what the old C# implementation did.
        throw new ArgumentException("Resulting query is invalid, please check error log for details");
      }
      this.internalQuery = internalQuery;
      this.database = database;
      valueListener = new InternalValueListener(internalQuery, database);
      childListener = new InternalChildListener(internalQuery, database);
    }

    /// <summary>Event for changes in the data at this location.</summary>
    /// <remarks>
    ///   Register a handler to observe changes at the location of this Query object.
    ///   Each time time the data changes, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ValueChangedEventArgs> ValueChanged {
      add { valueListener.ValueChanged += value; }
      remove { valueListener.ValueChanged -= value; }
    }

    /// <summary>Event raised when children nodes are added relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are added relative to this Query object.
    ///   Each time time children nodes are added, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildAdded {
      add { childListener.ChildAdded += value; }
      remove { childListener.ChildAdded -= value; }
    }

    /// <summary>Event raised when children nodes are changed relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe changes to children relative to this Query object.
    ///   Each time time children nodes are changed, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildChanged {
      add { childListener.ChildChanged += value; }
      remove { childListener.ChildChanged -= value; }
    }

    /// <summary>Event raised when children nodes are removed relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are removed relative to this Query object.
    ///   Each time time children nodes are removed, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildRemoved {
      add { childListener.ChildRemoved += value; }
      remove { childListener.ChildRemoved -= value; }
    }

    /// <summary>Event raised when children nodes are moved relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are moved relative to this Query object.
    ///   Each time time children nodes are moved, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildMoved {
      add { childListener.ChildMoved += value; }
      remove { childListener.ChildMoved -= value; }
    }

    /// Check if the task is faulted or canceled.
    /// This also keeps a reference to FirebaseDatabase (through Query) and prevents it from being
    /// Garbage-Collected until the task is done.
    [MethodImpl(MethodImplOptions.NoOptimization | MethodImplOptions.NoInlining)]
    internal bool CheckTaskStatus<TResult>(Task task, TaskCompletionSource<TResult> tcs) {
      if (task.IsFaulted) {
        tcs.SetException(new DatabaseException("Internal task faulted", task.Exception));
        return false;
      } else if (task.IsCanceled) {
        tcs.SetCanceled();
        return false;
      } else if (!task.IsCompleted) {
        // This should not happen - ContinueWith(), the block where this should be called from,
        // should not have been called if the task is neither faulted, canceled nor completed.
        tcs.SetException(new DatabaseException("Unexpected internal task state"));
        return false;
      }
      return true;
    }

    /// Creates a wrapper around the internal task returned by the SWIG classes.
    internal Task<DataSnapshot> WrapInternalDataSnapshotTask(Task<InternalDataSnapshot> it) {
      TaskCompletionSource<DataSnapshot> tcs = new TaskCompletionSource<DataSnapshot>();
      it.ContinueWith(task => {
        // By calling CheckTaskStatus(), it keeps a reference to this, which holds a reference to
        // FirebaseDatabase.  This prevents FirebaseDatabase from being GCed until the end of this
        // block.
        if (CheckTaskStatus<DataSnapshot>(task, tcs)){
          tcs.SetResult(DataSnapshot.CreateSnapshot(task.Result, database));
        }
      });
      return tcs.Task;
    }

    public Task<DataSnapshot> GetValueAsync() {
      return WrapInternalDataSnapshotTask(internalQuery.GetValueAsync());
    }

    /// <summary>
    ///   By calling `keepSynced(true)` on a location, the data for that location will automatically
    ///   be downloaded and kept in sync, even when no listeners are attached for that location.
    /// </summary>
    /// <remarks>
    ///   By calling `keepSynced(true)` on a location, the data for that location will automatically
    ///   be downloaded and kept in sync, even when no listeners are attached for that location.
    /// </remarks>
    /// <param name="keepSynced">
    ///   Pass `true` to keep this location synchronized, pass `false` to stop synchronization.
    /// </param>
    public void KeepSynced(bool keepSynced) {
      internalQuery.SetKeepSynchronized(keepSynced);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(string value) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(double value) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(bool value) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   StartAt(string value) instead.
    /// </remark>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(string value, string key) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   StartAt(double value) instead.
    /// </remark>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key name to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(double value, string key) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   StartAt(bool value) instead.
    /// </remark>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(bool value, string key) {
      return new Query(internalQuery.StartAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(string value) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(double value) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(bool value) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key key less than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   EndAt(string value) instead.
    /// </remark>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(string value, string key) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key less than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   EndAt(double value) instead.
    /// </remark>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(double value, string key) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key less than or equal to the given key.
    /// </summary>
    /// <remark>
    ///   <b>Known issue</b> This currently does not work properly on all platforms. Please use
    ///   EndAt(bool value) instead.
    /// </remark>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(bool value, string key) {
      return new Query(internalQuery.EndAt(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(string value) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(double value) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value.
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(bool value) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value)), database);
    }

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as names are unique. <br />
    ///   <br />
    ///   <b>Known issue</b> This currently does not work properly on iOS and tvOS. Please use
    ///   EqualTo(string value) instead.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The key of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(string value, string key) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as keys are unique. <br />
    ///   <br />
    ///   <b>Known issue</b> This currently does not work properly on iOS and tvOS. Please use
    ///   EqualTo(double value) instead.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The key of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(double value, string key) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as keys are unique. <br />
    ///   <br />
    ///   <b>Known issue</b> This currently does not work properly on iOS and tvOS. Please use
    ///   EqualTo(bool value) instead.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The name of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(bool value, string key) {
      return new Query(internalQuery.EqualTo(Utilities.MakeVariant(value), key), database);
    }

    /// <summary>Create a query with limit and anchor it to the start of the window</summary>
    /// <param name="limit">The maximum number of child nodes to return</param>
    /// <returns>A Query with the new constraint</returns>
    public Query LimitToFirst(int limit) {
      return new Query(internalQuery.LimitToFirst(checked((uint)limit)), database);
    }

    /// <summary>Create a query with limit and anchor it to the end of the window</summary>
    /// <param name="limit">The maximum number of child nodes to return</param>
    /// <returns>A Query with the new constraint</returns>
    public Query LimitToLast(int limit) {
      return new Query(internalQuery.LimitToLast(checked((uint)limit)), database);
    }

    /// <summary>
    ///   Create a query in which child nodes are ordered by the values of the specified path.
    /// </summary>
    /// <param name="path">The path to the child node to use for sorting</param>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByChild(string path) {
      return new Query(internalQuery.OrderByChild(path), database);
    }

    /// <summary>Create a query in which child nodes are ordered by their priorities.</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByPriority() {
      return new Query(internalQuery.OrderByPriority(), database);
    }

    /// <summary>Create a query in which child nodes are ordered by their keys.</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByKey() {
      return new Query(internalQuery.OrderByKey(), database);
    }

    /// <summary>Create a query in which nodes are ordered by their value</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByValue() {
      return new Query(internalQuery.OrderByValue(), database);
    }

    /// <value>A DatabaseReference to this location</value>
    public DatabaseReference Reference {
      get { return new DatabaseReference(internalQuery.GetReference(), database); }
    }
  }
}
