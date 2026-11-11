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
    internal readonly FirebaseDatabase DatabaseInstance;
    internal readonly QuerySpec Spec;

    internal Query(FirebaseDatabase database, QuerySpec spec) {
      DatabaseInstance = database ?? throw new ArgumentNullException(nameof(database));
      Spec = spec;
    }

    /// <summary>Event for changes in the data at this location.</summary>
    /// <remarks>
    ///   Register a handler to observe changes at the location of this Query object.
    ///   Each time time the data changes, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ValueChangedEventArgs> ValueChanged {
      add {
        if (value == null) return;
        foreach (Delegate d in value.GetInvocationList()) {
          DatabaseInstance.Repo.AddListener(
              Spec,
              new ListenerRegistration(Spec, (EventHandler<ValueChangedEventArgs>)d, isOneShot: false));
        }
      }
      remove {
        if (value == null) return;
        foreach (Delegate d in value.GetInvocationList()) {
          DatabaseInstance.Repo.RemoveListener(Spec, EventKind.ValueChanged, d);
        }
      }
    }

    /// <summary>Event raised when children nodes are added relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are added relative to this Query object.
    ///   Each time time children nodes are added, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildAdded {
      add => AddChildHandler(EventKind.ChildAdded, value);
      remove => RemoveChildHandler(EventKind.ChildAdded, value);
    }

    /// <summary>Event raised when children nodes are changed relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe changes to children relative to this Query object.
    ///   Each time time children nodes are changed, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildChanged {
      add => AddChildHandler(EventKind.ChildChanged, value);
      remove => RemoveChildHandler(EventKind.ChildChanged, value);
    }

    /// <summary>Event raised when children nodes are removed relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are removed relative to this Query object.
    ///   Each time time children nodes are removed, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildRemoved {
      add => AddChildHandler(EventKind.ChildRemoved, value);
      remove => RemoveChildHandler(EventKind.ChildRemoved, value);
    }

    /// <summary>Event raised when children nodes are moved relative to this location.</summary>
    /// <remarks>
    ///   Register a handler to observe when children are moved relative to this Query object.
    ///   Each time time children nodes are moved, your handler will be called with an
    ///   immutable snapshot of the data.
    /// </remarks>
    public event EventHandler<ChildChangedEventArgs> ChildMoved {
      add => AddChildHandler(EventKind.ChildMoved, value);
      remove => RemoveChildHandler(EventKind.ChildMoved, value);
    }

    /// <summary>Gets the value for this query asynchronously.</summary>
    public Task<DataSnapshot> GetValueAsync() => DatabaseInstance.Repo.GetValueAsync(Spec);

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
    public void KeepSynced(bool keepSynced) => DatabaseInstance.Repo.SetKeepSynced(Spec, keepSynced);

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(string value) =>
        WithParams(Spec.Params.WithStartAt(NodeFromStringBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(double value) =>
        WithParams(Spec.Params.WithStartAt(NodeFromDoubleBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(bool value) =>
        WithParams(Spec.Params.WithStartAt(new LeafNode(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(string value, string key) =>
        WithParams(Spec.Params.WithStartAt(NodeFromStringBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key name to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(double value, string key) =>
        WithParams(Spec.Params.WithStartAt(NodeFromDoubleBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value greater than or equal
    ///   to the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key greater than or equal to the given key.
    /// </summary>
    /// <param name="value">The priority to start at, inclusive</param>
    /// <param name="key">The key to start at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query StartAt(bool value, string key) =>
        WithParams(Spec.Params.WithStartAt(new LeafNode(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(string value) =>
        WithParams(Spec.Params.WithEndAt(NodeFromStringBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(double value) =>
        WithParams(Spec.Params.WithEndAt(NodeFromDoubleBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(bool value) =>
        WithParams(Spec.Params.WithEndAt(new LeafNode(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key key less than or equal to the given key.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(string value, string key) =>
        WithParams(Spec.Params.WithEndAt(NodeFromStringBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key less than or equal to the given key.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(double value, string key) =>
        WithParams(Spec.Params.WithEndAt(NodeFromDoubleBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with a value less than or equal to
    ///   the given value, using the given orderBy directive or priority as default, and
    ///   additionally only child nodes with a key less than or equal to the given key.
    /// </summary>
    /// <param name="value">The value to end at, inclusive</param>
    /// <param name="key">The key to end at, inclusive</param>
    /// <returns>A Query with the new constraint</returns>
    public Query EndAt(bool value, string key) =>
        WithParams(Spec.Params.WithEndAt(new LeafNode(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(string value) =>
        WithParams(Spec.Params.WithEqualTo(NodeFromStringBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(double value) =>
        WithParams(Spec.Params.WithEqualTo(NodeFromDoubleBound(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return child nodes with the given value.
    /// </summary>
    /// <param name="value">The value to query for</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(bool value) =>
        WithParams(Spec.Params.WithEqualTo(new LeafNode(value), null, hasKeyArg: false));

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as names are unique.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The key of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(string value, string key) =>
        WithParams(Spec.Params.WithEqualTo(NodeFromStringBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as keys are unique.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The key of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(double value, string key) =>
        WithParams(Spec.Params.WithEqualTo(NodeFromDoubleBound(value), key, hasKeyArg: true));

    /// <summary>
    ///   Create a query constrained to only return the child node with the given key and value.
    /// </summary>
    /// <remarks>
    ///   Create a query constrained to only return the child node with the given key and value.
    ///   Note that there is at most one such child as keys are unique.
    /// </remarks>
    /// <param name="value">The value to query for</param>
    /// <param name="key">The name of the child</param>
    /// <returns>A query with the new constraint</returns>
    public Query EqualTo(bool value, string key) =>
        WithParams(Spec.Params.WithEqualTo(new LeafNode(value), key, hasKeyArg: true));

    /// <summary>Create a query with limit and anchor it to the start of the window</summary>
    /// <param name="limit">The maximum number of child nodes to return</param>
    /// <returns>A Query with the new constraint</returns>
    public Query LimitToFirst(int limit) => WithParams(Spec.Params.WithLimitToFirst(limit));

    /// <summary>Create a query with limit and anchor it to the end of the window</summary>
    /// <param name="limit">The maximum number of child nodes to return</param>
    /// <returns>A Query with the new constraint</returns>
    public Query LimitToLast(int limit) => WithParams(Spec.Params.WithLimitToLast(limit));

    /// <summary>
    ///   Create a query in which child nodes are ordered by the values of the specified path.
    /// </summary>
    /// <param name="path">The path to the child node to use for sorting</param>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByChild(string path) {
      if (path == null) {
        throw new ArgumentException("OrderByChild path cannot be null.", nameof(path));
      }
      if (path == "$key" || path == ".key" || path == "$value" || path == ".value" ||
          path == "$priority" || path == ".priority") {
        throw new ArgumentException($"Can't use '{path}' as path, please use the dedicated OrderBy method instead.", nameof(path));
      }
      RtdbPath parsed = RtdbPath.ParseAndValidate(path, nameof(path), allowMetaKeys: false);
      if (parsed.IsEmpty || parsed.IsDotInfo) {
        throw new ArgumentException("OrderByChild path cannot be empty or point to .info.", nameof(path));
      }
      return WithParams(Spec.Params.WithOrderBy(QueryOrderBy.Child, parsed));
    }

    /// <summary>Create a query in which child nodes are ordered by their priorities.</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByPriority() =>
        WithParams(Spec.Params.WithOrderBy(QueryOrderBy.Priority, RtdbPath.Empty));

    /// <summary>Create a query in which child nodes are ordered by their keys.</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByKey() =>
        WithParams(Spec.Params.WithOrderBy(QueryOrderBy.Key, RtdbPath.Empty));

    /// <summary>Create a query in which nodes are ordered by their value</summary>
    /// <returns>A Query with the new constraint</returns>
    public Query OrderByValue() =>
        WithParams(Spec.Params.WithOrderBy(QueryOrderBy.Value, RtdbPath.Empty));

    /// <value>A DatabaseReference to this location</value>
    public DatabaseReference Reference => new DatabaseReference(DatabaseInstance, Spec.Path);

    private Query WithParams(RtdbQueryParams nextParams) =>
        new Query(DatabaseInstance, new QuerySpec(Spec.Path, nextParams));

    private void AddChildHandler(EventKind kind, EventHandler<ChildChangedEventArgs> handler) {
      if (handler == null) return;
      foreach (Delegate d in handler.GetInvocationList()) {
        DatabaseInstance.Repo.AddListener(
            Spec,
            new ListenerRegistration(Spec, kind, (EventHandler<ChildChangedEventArgs>)d));
      }
    }

    private void RemoveChildHandler(EventKind kind, EventHandler<ChildChangedEventArgs> handler) {
      if (handler == null) return;
      foreach (Delegate d in handler.GetInvocationList()) {
        DatabaseInstance.Repo.RemoveListener(Spec, kind, d);
      }
    }

    private static RtdbNode NodeFromStringBound(string value) =>
        value == null ? (RtdbNode)EmptyNode.Instance : new LeafNode(value);

    private static RtdbNode NodeFromDoubleBound(double value) {
      if (double.IsNaN(value) || double.IsInfinity(value)) {
        throw new ArgumentException("Query bound cannot be NaN or Infinity.", nameof(value));
      }
      return new LeafNode(value);
    }
  }
}
