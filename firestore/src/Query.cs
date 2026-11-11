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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A query which you can read or listen to. You can also construct refined <c>Query</c> objects
  /// by adding filters, ordering, and other constraints.
  /// </summary>
  /// <remarks>
  /// <see cref="CollectionReference"/> derives from this class as a "return-all" query against the
  /// collection it refers to.
  /// </remarks>
  public class Query {
    private readonly QuerySpec _spec;
    private readonly FirebaseFirestore _firestore;

    internal QuerySpec Spec => _spec;

    internal Query(QuerySpec spec, FirebaseFirestore firestore) {
      _spec = Preconditions.CheckNotNull(spec, nameof(spec));
      _firestore = Preconditions.CheckNotNull(firestore, nameof(firestore));
    }

    /// <summary>
    /// The Cloud Firestore instance associated with this query.
    /// </summary>
    public FirebaseFirestore Firestore => _firestore;

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
    public AggregateQuery Count => new AggregateQuery(this);

    /// <summary>
    /// Creates and returns a new Query with the additional filter.
    /// </summary>
    /// <param name="filter">The new filter to apply to the existing query.</param>
    /// <returns>
    /// The created Query.
    /// </returns>
    public Query Where(Filter filter) {
      Preconditions.CheckNotNull(filter, nameof(filter));
      FilterSpec bound = filter.BindToQuery(this);
      return new Query(_spec.WithFilter(bound), _firestore);
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
      return Where(Filter.EqualTo(fieldPath, value));
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
      return Where(Filter.EqualTo(fieldPath, value));
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
      return Where(Filter.NotEqualTo(fieldPath, value));
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
      return Where(Filter.NotEqualTo(fieldPath, value));
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
      return Where(Filter.LessThan(fieldPath, value));
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
      return Where(Filter.LessThan(fieldPath, value));
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
      return Where(Filter.LessThanOrEqualTo(fieldPath, value));
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
      return Where(Filter.LessThanOrEqualTo(fieldPath, value));
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
      return Where(Filter.GreaterThan(fieldPath, value));
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
      return Where(Filter.GreaterThan(fieldPath, value));
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
      return Where(Filter.GreaterThanOrEqualTo(fieldPath, value));
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
      return Where(Filter.GreaterThanOrEqualTo(fieldPath, value));
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
      return Where(Filter.ArrayContains(fieldPath, value));
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
      return Where(Filter.ArrayContains(fieldPath, value));
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
      return Where(Filter.ArrayContainsAny(fieldPath, values));
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
      return Where(Filter.ArrayContainsAny(fieldPath, values));
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
      return Where(Filter.In(fieldPath, values));
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
      return Where(Filter.In(fieldPath, values));
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
      return Where(Filter.NotIn(fieldPath, values));
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
      return Where(Filter.NotIn(fieldPath, values));
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
      return OrderBy(FieldPath.FromDotSeparatedString(fieldPath));
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
      return new Query(_spec.WithOrderBy(new OrderBySpec(fieldPath, descending: false)), _firestore);
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
      return OrderByDescending(FieldPath.FromDotSeparatedString(fieldPath));
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
      return new Query(_spec.WithOrderBy(new OrderBySpec(fieldPath, descending: true)), _firestore);
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
      if (limit <= 0) {
        throw new ArgumentException("Limit must be positive.", nameof(limit));
      }
      return new Query(_spec.WithLimit(limit, LimitKind.First), _firestore);
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
      if (limit <= 0) {
        throw new ArgumentException("Limit must be positive.", nameof(limit));
      }
      return new Query(_spec.WithLimit(limit, LimitKind.Last), _firestore);
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
      return new Query(_spec.WithStartBound(BuildBoundFromSnapshot(snapshot, inclusive: true)), _firestore);
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
      return new Query(_spec.WithStartBound(BuildBoundFromValues(fieldValues, inclusive: true)), _firestore);
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
      return new Query(_spec.WithStartBound(BuildBoundFromSnapshot(snapshot, inclusive: false)), _firestore);
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
      return new Query(_spec.WithStartBound(BuildBoundFromValues(fieldValues, inclusive: false)), _firestore);
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
      return new Query(_spec.WithEndBound(BuildBoundFromSnapshot(snapshot, inclusive: false)), _firestore);
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
      return new Query(_spec.WithEndBound(BuildBoundFromValues(fieldValues, inclusive: false)), _firestore);
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
      return new Query(_spec.WithEndBound(BuildBoundFromSnapshot(snapshot, inclusive: true)), _firestore);
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
      return new Query(_spec.WithEndBound(BuildBoundFromValues(fieldValues, inclusive: true)), _firestore);
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
      return _firestore.Core.GetQuerySnapshotAsync(this, source);
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
    public ListenerRegistration Listen(
        MetadataChanges metadataChanges, Action<QuerySnapshot> callback) {
      Preconditions.CheckNotNull(callback, nameof(callback));
      return _firestore.Core.ListenQuery(this, metadataChanges, callback);
    }

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as Query);

    /// <inheritdoc />
    public bool Equals(Query other) =>
        other != null &&
        ReferenceEquals(_firestore, other._firestore) &&
        _spec.Equals(other._spec);

    /// <inheritdoc />
    public override int GetHashCode() =>
        _firestore.GetHashCode() * 31 + _spec.GetHashCode();

    #region Internal Filter & Cursor Conversion Helpers

    internal FirestoreValue ConvertFilterValue(
        FieldPath fieldPath, FieldFilterOp op, object rawValue, bool insideInArray) {
      if (fieldPath.IsDocumentId) {
        return ConvertDocumentIdValue(rawValue, allowNull: false);
      }
      FirestoreValue v = ValueSerializer.Serialize(SerializationContext.Default, rawValue);
      MutationSpec.ValidateNonSentinelValue(v, _firestore.ProjectId, _firestore.DatabaseId);
      if (insideInArray && v.IsNull && op == FieldFilterOp.NotIn) {
        // Note: Firestore allows null inside NotIn? Actually Firestore rejects null in NotIn on
        // backend or treats it as matching nothing; keep wire-compatible!
      }
      return v;
    }

    private FirestoreValue ConvertDocumentIdValue(object rawValue, bool allowNull) {
      if (rawValue == null) {
        if (allowNull) return FirestoreValue.Null;
        throw new ArgumentException("Invalid query. Document ID filter or cursor value cannot be null.");
      }
      if (rawValue is string strId) {
        if (string.IsNullOrEmpty(strId)) {
          throw new ArgumentException("Invalid query. Document ID cannot be empty.");
        }
        if (!_spec.IsCollectionGroup && strId.IndexOf('/') >= 0) {
          throw new ArgumentException(
              $"Invalid query. When querying a collection, document ID '{strId}' must not contain '/'.");
        }
        ResourcePath rel = ResourcePath.ParseRelative(strId, nameof(rawValue));
        if (_spec.IsCollectionGroup) {
          if (!rel.IsDocument) {
            throw new ArgumentException(
                $"Invalid query. When querying a collection group by FieldPath.DocumentId, " +
                $"the value provided must result in a valid document path, but '{strId}' has {rel.Count} segments.");
          }
          return FirestoreValue.FromReference(
              new DocumentReferenceValue(_firestore.ProjectId, _firestore.DatabaseId, rel));
        }
        ResourcePath fullPath = _spec.Path.Append(rel);
        return FirestoreValue.FromReference(
            new DocumentReferenceValue(_firestore.ProjectId, _firestore.DatabaseId, fullPath));
      }
      if (rawValue is DocumentReference docRef) {
        if (!string.Equals(docRef.Firestore.ProjectId, _firestore.ProjectId, StringComparison.Ordinal) ||
            !string.Equals(docRef.Firestore.DatabaseId, _firestore.DatabaseId, StringComparison.Ordinal)) {
          throw new ArgumentException(
              "Invalid query. DocumentReference belongs to a different Firestore database.");
        }
        if (!_spec.IsCollectionGroup && !_spec.Path.IsImmediateParentOf(docRef.ResourcePath)) {
          throw new ArgumentException(
              $"Invalid query. '{docRef.Path}' is not a direct child of '{_spec.Path}'.");
        }
        return FirestoreValue.FromReference(new DocumentReferenceValue(
            _firestore.ProjectId, _firestore.DatabaseId, docRef.ResourcePath));
      }
      throw new ArgumentException(
          "Invalid query. Expected a string or DocumentReference for FieldPath.DocumentId, " +
          $"but got {rawValue.GetType().FullName}.");
    }

    private BoundSpec BuildBoundFromValues(object[] fieldValues, bool inclusive) {
      if (fieldValues.Length == 0) {
        throw new ArgumentException("Cursor fieldValues must not be empty.", nameof(fieldValues));
      }
      OrderBySpec[] norm = _spec.NormalizedOrderBys;
      if (fieldValues.Length > norm.Length) {
        throw new ArgumentException(
            "Too many cursor arguments provided; must not exceed the number of OrderBy clauses.",
            nameof(fieldValues));
      }
      var pos = new FirestoreValue[fieldValues.Length];
      for (int i = 0; i < fieldValues.Length; i++) {
        if (norm[i].Field.IsDocumentId) {
          pos[i] = ConvertDocumentIdValue(fieldValues[i], allowNull: false);
        } else {
          FirestoreValue fv = ValueSerializer.Serialize(SerializationContext.Default, fieldValues[i]);
          MutationSpec.ValidateNonSentinelValue(fv, _firestore.ProjectId, _firestore.DatabaseId);
          pos[i] = fv;
        }
      }
      return new BoundSpec(pos, inclusive);
    }

    private BoundSpec BuildBoundFromSnapshot(DocumentSnapshot snapshot, bool inclusive) {
      if (!snapshot.Exists) {
        throw new ArgumentException(
            "Invalid query. Can't use a non-existent DocumentSnapshot to start or end a query.",
            nameof(snapshot));
      }
      ResourcePath snapPath = snapshot.Reference.ResourcePath;
      if (_spec.IsCollectionGroup) {
        if (!string.Equals(snapPath.Parent?.Id, _spec.CollectionGroup, StringComparison.Ordinal)) {
          throw new ArgumentException(
              "Invalid query. DocumentSnapshot does not belong to the target collection group.",
              nameof(snapshot));
        }
      } else if (!_spec.Path.IsImmediateParentOf(snapPath)) {
        throw new ArgumentException(
            "Invalid query. DocumentSnapshot is not a child of the queried collection.",
            nameof(snapshot));
      }

      OrderBySpec[] norm = _spec.NormalizedOrderBys;
      var pos = new FirestoreValue[norm.Length];
      for (int i = 0; i < norm.Length; i++) {
        FieldPath f = norm[i].Field;
        if (f.IsDocumentId) {
          pos[i] = FirestoreValue.FromReference(
              new DocumentReferenceValue(_firestore.ProjectId, _firestore.DatabaseId, snapPath));
        } else if (FirestoreValue.TryGetFieldByPath(snapshot.InternalFields, f, out FirestoreValue v)) {
          // Resolve pending server timestamps to estimate in cursor bounds (matching C++/JS SDK).
          pos[i] = v.ResolveServerTimestamp(ServerTimestampBehavior.Estimate);
        } else {
          throw new ArgumentException(
              $"Invalid query. You are trying to start or end a query using a document for which " +
              $"the field '{f}' (used as the OrderBy) does not exist.",
              nameof(snapshot));
        }
      }
      return new BoundSpec(pos, inclusive);
    }

    #endregion
  }
}
