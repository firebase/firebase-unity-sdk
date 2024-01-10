// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Firebase.Firestore.Internal;
using Firebase.Platform;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Firebase.Firestore {
  using QuerySnapshotCallbackMap =
      ListenerRegistrationMap<Action<QuerySnapshotProxy, FirestoreError, string>>;

  /// <summary>
  /// A query which you can read or listen to. You can also construct refined <c>Query</c> objects
  /// by adding filters, ordering, and other constraints.
  /// </summary>
  /// <remarks>
  /// <see cref="CollectionReference"/> derives from this class as a "return-all" query against the
  /// collection it refers to.
  /// </remarks>
  public class Query {
    // _proxy could be either QueryProxy or CollectionReferenceProxy type.
    internal readonly QueryProxy _proxy;
    private readonly FirebaseFirestore _firestore;

    internal Query(QueryProxy proxy, FirebaseFirestore firestore) {
      _proxy = Util.NotNull(proxy);
      _firestore = Util.NotNull(firestore);
    }

    internal static void ClearCallbacksForOwner(FirebaseFirestore owner) {
      snapshotListenerCallbacks.ClearCallbacksForOwner(owner);
    }

    /// <summary>
    /// The Cloud Firestore instance associated with this query.
    /// </summary>
    public FirebaseFirestore Firestore {
      get {
        return _firestore;
      }
    }

    /// <summary>
    /// Returns a query that counts the documents in the result set of this query.
    ///
    /// The returned query, when executed, counts the documents in the result set
    /// of this query without actually downloading the documents.
    ///
    /// Using the returned query to count the documents is efficient because only
    /// the final count, not the documents' data, is downloaded. The returned query
    /// can count the documents in cases where the result set is prohibitively large
    /// to download entirely (thousands of documents).
    /// </summary>
    /// <returns>
    /// An aggregate query that counts the documents in the result set of this query.
    /// </returns>
    public AggregateQuery Count {
      get {
        return new AggregateQuery(_proxy.Count(), _firestore);
      }
    }

    /// <summary>
    /// Creates and returns a new Query with the additional filter.
    /// </summary>
    /// <param name="filter">The new filter to apply to the existing query.</param>
    /// <returns>
    /// The created Query.
    /// </returns>
    public Query Where(Filter filter) {
      Preconditions.CheckNotNull(filter, nameof(filter));
      return new Query(_proxy.Where(filter.Proxy), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be equal to the specified value.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereEqualTo(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereEqualTo(fieldPath, ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be equal to the specified value.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereEqualTo(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereEqualTo(fieldPath.ConvertToProxy(),
                                                 ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should not equal the specified value.
    ///
    /// A Query can have only one <c>WhereNotEqualTo()</c> filter, and it cannot be
    /// combined with <c>WhereNotIn()</c>.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereNotEqualTo(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereNotEqualTo(fieldPath, ValueSerializer.Serialize(
                                                             SerializationContext.Default, value)),
                       Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should not equal the specified value.
    ///
    /// A Query can have only one <c>WhereNotEqualTo()</c> filter, and it cannot be
    /// combined with <c>WhereNotIn()</c>.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereNotEqualTo(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(
          _proxy.WhereNotEqualTo(fieldPath.ConvertToProxy(),
                                 ValueSerializer.Serialize(SerializationContext.Default, value)),
          Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be less than the specified value.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereLessThan(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereLessThan(fieldPath, ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be less than the specified value.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereLessThan(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereLessThan(fieldPath.ConvertToProxy(),
                                                  ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be less than or equal to the specified
    /// value.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereLessThanOrEqualTo(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereLessThanOrEqualTo(fieldPath, ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be less than or equal to the specified
    /// value.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereLessThanOrEqualTo(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereLessThanOrEqualTo(fieldPath.ConvertToProxy(),
                                                           ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be greater than the specified value.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereGreaterThan(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereGreaterThan(fieldPath, ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be greater than the specified value.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereGreaterThan(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereGreaterThan(fieldPath.ConvertToProxy(),
                                                     ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be greater than or equal to the specified
    /// value.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereGreaterThanOrEqualTo(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereGreaterThanOrEqualTo(fieldPath, ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value should be greater than or equal to the specified
    /// value.
    /// </summary>
    /// <param name="fieldPath">The field path to filter on. Must not be <c>null</c>.</param>
    /// <param name="value">The value to compare in the filter.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereGreaterThanOrEqualTo(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereGreaterThanOrEqualTo(fieldPath.ConvertToProxy(),
                                                              ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field, the value must be an array, and that the array must contain
    /// the provided value.
    ///
    /// A Query can have only one <c>WhereArrayContains()</c> filter and it cannot be combined
    /// with <c>WhereArrayContainsAny()</c>.
    /// </summary>
    /// <param name="fieldPath">The name of the fields containing an array to search</param>
    /// <param name="value">The value that must be contained in the array.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereArrayContains(FieldPath fieldPath, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereArrayContains(fieldPath.ConvertToProxy(),
                                                       ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field, the value must be an array, and that the array must contain
    /// the provided value.
    ///
    /// A <c>Query</c> can have only one <c>WhereArrayContains()</c> filter and it cannot be
    /// combined with <c>WhereArrayContainsAny()</c>.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="value">The value that must be contained in the array.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereArrayContains(string fieldPath, object value) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.WhereArrayContains(fieldPath,
                                                       ValueSerializer.Serialize(SerializationContext.Default, value)), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must contain
    /// the specified field, the value must be an array, and that the array must contain at least one
    /// value from the provided list.
    ///
    /// A <c>Query</c> can have only one <c>WhereArrayContainsAny()</c> filter and it cannot be
    /// combined with <c>WhereArrayContains()</c> or <c>WhereIn()</c>.
    /// </summary>
    /// <param name="fieldPath">The name of the fields containing an array to search.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereArrayContainsAny(FieldPath fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereArrayContainsAny(_proxy, fieldPath.ConvertToProxy(), array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must contain
    /// the specified field, the value must be an array, and that the array must contain at least one
    /// value from the provided list.
    ///
    /// A <c>Query</c> can have only one <c>WhereArrayContainsAny()</c> filter and it cannot be
    /// combined with <c>WhereArrayContains()</c> or <c>WhereIn()</c>.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereArrayContainsAny(string fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereArrayContainsAny(_proxy, fieldPath, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value must equal one of the values from the
    /// provided list.
    ///
    /// A <c>Query</c> can have only one <c>WhereIn()</c> filter and it cannot be combined
    /// with <c>WhereArrayContainsAny()</c>.
    /// </summary>
    /// <param name="fieldPath">The name of the fields containing an array to search.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereIn(FieldPath fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereIn(_proxy, fieldPath.ConvertToProxy(), array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value must equal one of the values from the
    /// provided list.
    ///
    /// A <c>Query</c> can have only one <c>WhereIn()</c> filter and it cannot be combined
    /// with <c>WhereArrayContainsAny()</c>.
    /// </summary>
    /// <param name="fieldPath">The dot-separated field path to filter on. Must not be <c>null</c>
    /// or empty.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereIn(string fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereIn(_proxy, fieldPath, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value must not equal any value from the
    /// provided list.
    ///
    /// One special case is that <c>WhereNotIn</c> cannot match null values. To query for
    /// documents where a field exists and is null, use <c>WhereNotEqualTo</c>, which can handle
    /// this special case.
    ///
    /// A <c>Query</c> can have only one <c>WhereNotIn()</c> filter, and it cannot be
    /// combined with <c>WhereArrayContains()</c>, <c>WhereArrayContainsAny()</c>,
    /// <c>WhereIn()</c>, or <c>WhereNotEqualTo()</c>.
    /// </summary>
    /// <param name="fieldPath">The name of the fields containing an array to search.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereNotIn(FieldPath fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereNotIn(_proxy, fieldPath.ConvertToProxy(), array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> with the additional filter that documents must
    /// contain the specified field and the value must not equal any value from the
    /// provided list.
    ///
    /// One special case is that <c>WhereNotIn</c> cannot match null values. To query for
    /// documents where a field exists and is null, use <c>WhereNotEqualTo</c>, which can handle
    /// this special case.
    ///
    /// A <c>Query</c> can have only one <c>WhereNotIn()</c> filter, and it cannot be
    /// combined with <c>WhereArrayContains()</c>, <c>WhereArrayContainsAny()</c>,
    /// <c>WhereIn()</c>, or <c>WhereNotEqualTo()</c>.
    /// </summary>
    /// <param name="fieldPath">The name of the fields containing an array to search.</param>
    /// <param name="values">The list that contains the values to match.</param>
    /// <returns>A new query based on the current one, but with the additional filter applied.
    /// </returns>
    public Query WhereNotIn(string fieldPath, IEnumerable<object> values) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      var array = ValueSerializer.Serialize(SerializationContext.Default, values);
      var query = FirestoreCpp.QueryWhereNotIn(_proxy, fieldPath, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that's additionally sorted by the specified field.
    /// </summary>
    /// <remarks>
    /// <para>Unlike OrderBy in LINQ, this call makes each additional ordering subordinate to the
    /// preceding ones. This means that <c>query.OrderBy("foo").OrderBy("bar")</c> in Cloud
    /// Firestore is similar to <c>query.OrderBy(x => x.Foo).ThenBy(x => x.Bar)</c> in LINQ.
    /// </para>
    ///
    /// <para>This method cannot be called after a start/end cursor has been specified with
    /// <see cref="StartAt(DocumentSnapshot)"/>, <see cref="StartAfter(DocumentSnapshot)"/>,
    /// <see cref="EndAt(DocumentSnapshot)"/> or <see cref="EndBefore(DocumentSnapshot)"/> or
    /// other overloads. </para>
    /// </remarks>
    /// <param name="fieldPath">The dot-separated field path to order by. Must not be <c>null</c> or
    /// empty.</param>
    /// <returns>A new query based on the current one, but with the additional specified ordering
    /// applied.</returns>
    public Query OrderBy(string fieldPath) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.OrderBy(fieldPath), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that's additionally sorted by the specified field in
    /// descending order.
    /// </summary>
    /// <remarks>
    /// <para>Unlike OrderByDescending in LINQ, this call makes each additional ordering subordinate
    /// to the preceding ones. This means that
    /// <c>query.OrderByDescending("foo").OrderByDescending("bar")</c> in Cloud Firestore is similar
    /// to <c>query.OrderByDescending(x => x.Foo).ThenByDescending(x => x.Bar)</c> in LINQ. </para>
    ///
    /// <para>This method cannot be called after a start/end cursor has been specified with
    /// <see cref="StartAt(DocumentSnapshot)"/>, <see cref="StartAfter(DocumentSnapshot)"/>,
    /// <see cref="EndAt(DocumentSnapshot)"/> or <see cref="EndBefore(DocumentSnapshot)"/> or
    /// other overloads. </para>
    /// </remarks>
    /// <param name="fieldPath">The dot-separated field path to order by. Must not be <c>null</c> or
    /// empty.</param>
    /// <returns>A new query based on the current one, but with the additional specified ordering
    /// applied.</returns>
    public Query OrderByDescending(string fieldPath) {
      Preconditions.CheckNotNullOrEmpty(fieldPath, nameof(fieldPath));
      return new Query(_proxy.OrderBy(fieldPath, QueryProxy.Direction.Descending), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that's additionally sorted by the specified field.
    /// </summary>
    /// <remarks>
    /// <para>Unlike OrderBy in LINQ, this call makes each additional ordering subordinate to the
    /// preceding ones. This means that <c>query.OrderBy("foo").OrderBy("bar")</c> in Cloud
    /// Firestore is similar to <c>query.OrderBy(x => x.Foo).ThenBy(x => x.Bar)</c> in LINQ.
    /// </para>
    ///
    /// <para>This method cannot be called after a start/end cursor has been specified with
    /// <see cref="StartAt(DocumentSnapshot)"/>, <see cref="StartAfter(DocumentSnapshot)"/>,
    /// <see cref="EndAt(DocumentSnapshot)"/> or <see cref="EndBefore(DocumentSnapshot)"/> or
    /// other overloads. </para>
    /// </remarks>
    /// <param name="fieldPath">The field path to order by. Must not be <c>null</c>.</param>
    /// <returns>A new query based on the current one, but with the additional specified ordering
    /// applied.</returns>
    public Query OrderBy(FieldPath fieldPath) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.OrderBy(fieldPath.ConvertToProxy()), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that's additionally sorted by the specified field in
    /// descending order.
    /// </summary>
    /// <remarks>
    /// <para>Unlike OrderByDescending in LINQ, this call makes each additional ordering subordinate
    /// to the preceding ones. This means that
    /// <c>query.OrderByDescending("foo").OrderByDescending("bar")</c> in Cloud Firestore is similar
    /// to <c>query.OrderByDescending(x => x.Foo).ThenByDescending(x => x.Bar)</c> in LINQ. </para>
    ///
    /// <para>This method cannot be called after a start/end cursor has been specified with
    /// <see cref="StartAt(DocumentSnapshot)"/>, <see cref="StartAfter(DocumentSnapshot)"/>,
    /// <see cref="EndAt(DocumentSnapshot)"/> or <see cref="EndBefore(DocumentSnapshot)"/> or
    /// other overloads. </para>
    /// </remarks>
    /// <param name="fieldPath">The field path to order by. Must not be <c>null</c>.</param>
    /// <returns>A new query based on the current one, but with the additional specified ordering
    /// applied.</returns>
    public Query OrderByDescending(FieldPath fieldPath) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      return new Query(_proxy.OrderBy(fieldPath.ConvertToProxy(),
                       QueryProxy.Direction.Descending), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that only returns the last matching documents up to the
    /// specified number.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously-specified limit in the query.
    /// </remarks>
    /// <param name="limit">The maximum number of items to return. Must be greater than 0.</param>
    /// <returns>A new query based on the current one, but with the specified limit applied.
    /// </returns>
    public Query Limit(int limit) {
      return new Query(_proxy.Limit(limit), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that only returns the last matching documents up to the
    /// specified number.
    /// </summary>
    /// <remarks>
    /// You must specify at least one <c>OrderBy</c> clause for <c>LimitToLast</c> queries,
    /// otherwise an exception will be thrown during execution.
    ///
    /// This call replaces any previously-specified limit in the query.
    /// </remarks>
    /// <param name="limit">The maximum number of items to return. Must be greater than 0.</param>
    /// <returns>A new query based on the current one, but with the specified limit applied.
    /// </returns>
    public Query LimitToLast(int limit) {
      return new Query(_proxy.LimitToLast(limit), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that starts at the provided document (inclusive). The
    /// starting position is relative to the order of the query. The document must contain all of
    /// the fields provided in order-by clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified start position in the query.
    /// </remarks>
    /// <param name="snapshot">The snapshot of the document to start at.</param>
    /// <returns>A new query based on the current one, but with the specified start position.</returns>
    public Query StartAt(DocumentSnapshot snapshot) {
      Preconditions.CheckNotNull(snapshot, nameof(snapshot));
      return new Query(_proxy.StartAt(snapshot.Proxy), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that starts at the provided fields relative to the
    /// order of the query. The order of the field values must match the order of the order-by
    /// clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified start position in the query.
    /// </remarks>
    /// <param name="fieldValues">The field values. The <c>fieldValues</c> array must not be
    /// <c>null</c> or empty (though elements of the array may be), or have more values than query
    /// has orderings.</param>
    /// <returns>A new query based on the current one, but with the specified start position.
    /// </returns>
    public Query StartAt(params object[] fieldValues) {
      Preconditions.CheckNotNull(fieldValues, nameof(fieldValues));
      var array = ValueSerializer.Serialize(SerializationContext.Default, fieldValues);
      var query = FirestoreCpp.QueryStartAt(_proxy, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that starts after the provided document (exclusive).
    /// The starting position is relative to the order of the query. The document must contain all
    /// of the fields provided in the order-by clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified start position in the query.
    /// </remarks>
    /// <param name="snapshot">The snapshot of the document to start after.</param>
    /// <returns>A new query based on the current one, but with the specified start position.
    /// </returns>
    public Query StartAfter(DocumentSnapshot snapshot) {
      Preconditions.CheckNotNull(snapshot, nameof(snapshot));
      return new Query(_proxy.StartAfter(snapshot.Proxy), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that starts after the provided fields relative to the
    /// order of the query. The order of the field values must match the order of the order-by
    /// clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified start position in the query.
    /// </remarks>
    /// <param name="fieldValues">The field values. The <c>fieldValues</c> array must not be
    /// <c>null</c> or empty (though elements of the array may be), or have more values than query
    /// has orderings.</param>
    /// <returns>A new query based on the current one, but with the specified start position.
    /// </returns>
    public Query StartAfter(params object[] fieldValues) {
      Preconditions.CheckNotNull(fieldValues, nameof(fieldValues));
      var array = ValueSerializer.Serialize(SerializationContext.Default, fieldValues);
      var query = FirestoreCpp.QueryStartAfter(_proxy, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that ends before the provided document (exclusive).
    /// The end position is relative to the order of the query. The document must contain all of the
    /// fields provided in the order-by clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified end position in the query.
    /// </remarks>
    /// <param name="snapshot">The snapshot of the document to end before.</param>
    /// <returns>A new query based on the current one, but with the specified end position.
    /// </returns>
    public Query EndBefore(DocumentSnapshot snapshot) {
      Preconditions.CheckNotNull(snapshot, nameof(snapshot));
      return new Query(_proxy.EndBefore(snapshot.Proxy), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that ends before the provided fields relative to the
    /// order of the query. The order of the field values must match the order of the order-by
    /// clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified end position in the query.
    /// </remarks>
    /// <param name="fieldValues">The field values. The <c>fieldValues</c> array must not be
    /// <c>null</c> or empty (though elements of the array may be), or have more values than query
    /// has orderings.</param>
    /// <returns>A new query based on the current one, but with the specified end position.
    /// </returns>
    public Query EndBefore(params object[] fieldValues) {
      Preconditions.CheckNotNull(fieldValues, nameof(fieldValues));
      var array = ValueSerializer.Serialize(SerializationContext.Default, fieldValues);
      var query = FirestoreCpp.QueryEndBefore(_proxy, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that ends at the provided document (inclusive). The
    /// end position is relative to the order of the query. The document must contain all of the
    /// fields provided in the order-by clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified end position in the query.
    /// </remarks>
    /// <param name="snapshot">The snapshot of the document to end at.</param>
    /// <returns>A new query based on the current one, but with the specified end position.
    /// </returns>
    public Query EndAt(DocumentSnapshot snapshot) {
      Preconditions.CheckNotNull(snapshot, nameof(snapshot));
      return new Query(_proxy.EndAt(snapshot.Proxy), Firestore);
    }

    /// <summary>
    /// Creates and returns a new <c>Query</c> that ends at the provided fields relative to the
    /// order of the query. The order of the field values must match the order of the order-by
    /// clauses of the query.
    /// </summary>
    /// <remarks>
    /// This call replaces any previously specified end position in the query.
    /// </remarks>
    /// <param name="fieldValues">The field values. The <c>fieldValues</c> array must not be
    /// <c>null</c> or empty (though elements of the array may be), or have more values than query
    /// has orderings.</param>
    /// <returns>A new query based on the current one, but with the specified end position.
    /// </returns>
    public Query EndAt(params object[] fieldValues) {
      Preconditions.CheckNotNull(fieldValues, nameof(fieldValues));
      var array = ValueSerializer.Serialize(SerializationContext.Default, fieldValues);
      var query = FirestoreCpp.QueryEndAt(_proxy, array);
      return new Query(query, Firestore);
    }

    /// <summary>
    /// Asynchronously executes the query and returns all matching documents as a
    /// <c>QuerySnapshot</c>.
    /// </summary>
    /// <remarks>
    /// <para>By default, <c>GetSnapshotAsync</c> attempts to provide up-to-date data when possible
    /// by waiting for data from the server, but it may return cached data or fail if you are
    /// offline and the server cannot be reached. This behavior can be altered via the <c>source</c>
    /// parameter.</para>
    /// </remarks>
    /// <param name="source">indicates whether the results should be fetched from the cache only
    /// (<c>Source.Cache</c>), the server only (<c>Source.Server</c>), or to attempt the server and
    /// fall back to the cache (<c>Source.Default</c>).</param>
    /// <returns>A snapshot of documents matching the query.</returns>
    public Task<QuerySnapshot> GetSnapshotAsync(Source source = Source.Default) {
      var sourceProxy = Enums.Convert(source);
      return Util.MapResult(_proxy.GetAsync(sourceProxy), taskResult => {
        return new QuerySnapshot(taskResult, Firestore);
      });
    }

    /// <summary>
    /// Starts listening to changes to the query results described by this <c>Query</c>.
    /// </summary>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(Action<QuerySnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      return Listen(MetadataChanges.Exclude, callback);
    }

    /// <summary>
    /// Starts listening to changes to the query results described by this <c>Query</c>.
    /// </summary>
    /// <param name="metadataChanges">Indicates whether metadata-only changes (i.e. only
    /// <c>QuerySnapshot.Metadata</c> changed) should trigger snapshot events.</param>
    /// <param name="callback">The callback to invoke each time the query results change. Must not
    /// be <c>null</c>. The callback will be invoked on the main thread.</param>
    /// <returns>A <see cref="ListenerRegistration"/> which may be used to stop listening
    /// gracefully.</returns>
    public ListenerRegistration Listen(MetadataChanges metadataChanges, Action<QuerySnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      var tcs = new TaskCompletionSource<object>();
      int uid = snapshotListenerCallbacks.Register(Firestore, (snapshotProxy, errorCode,
                                                               errorMessage) => {
        if (errorCode != FirestoreError.Ok) {
          tcs.SetException(new FirestoreException(errorCode, errorMessage));
        } else {
          FirebaseHandler.RunOnMainThread<object>(() => {
            callback(new QuerySnapshot(snapshotProxy, Firestore));
            return null;
          });
        }
      });

      var metadataChangesProxy = Enums.Convert(metadataChanges);
      var listener = FirestoreCpp.AddQuerySnapshotListener(_proxy, metadataChangesProxy, uid,
                                                           querySnapshotsHandler);

      return new ListenerRegistration(snapshotListenerCallbacks, uid, tcs, listener);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Query);

    /// <inheritdoc />
    public bool Equals(Query other) => other != null
                                       && FirestoreCpp.QueryEquals(_proxy, other._proxy);

    /// <inheritdoc />
    public override int GetHashCode() {
      return FirestoreCpp.QueryHashCode(_proxy);
    }

    private static QuerySnapshotCallbackMap snapshotListenerCallbacks = new QuerySnapshotCallbackMap();
    internal delegate void ListenerDelegate(int callbackId, IntPtr snapshotPtr,
                                            FirestoreError errorCode, string errorMessage);
    private static ListenerDelegate querySnapshotsHandler = new ListenerDelegate(QuerySnapshotsHandler);


    [MonoPInvokeCallback(typeof(ListenerDelegate))]
    private static void QuerySnapshotsHandler(int callbackId, IntPtr snapshotPtr,
                                              FirestoreError errorCode, string errorMessage) {
      try {
        // Create the proxy object _before_ doing anything else to ensure that the C++ object's
        // memory does not get leaked (https://github.com/firebase/firebase-unity-sdk/issues/49).
        var querySnapshotProxy = new QuerySnapshotProxy(snapshotPtr, /*cMemoryOwn=*/true);

        Action<QuerySnapshotProxy, FirestoreError, string> callback;
        if (snapshotListenerCallbacks.TryGetCallback(callbackId, out callback)) {
          callback(querySnapshotProxy, errorCode, errorMessage);
        }

      } catch (Exception e) {
        Util.OnPInvokeManagedException(e, nameof(QuerySnapshotsHandler));
      }
    }
  }
}
