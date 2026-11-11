// Copyright 2023 Google LLC
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
using System.Linq;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// A Filter represents a restriction on one or more field values and can be used to refine the results of a Query.
  /// </summary>
  public sealed class Filter {
    private readonly bool _isComposite;
    private readonly FieldPath _fieldPath;
    private readonly FieldFilterOp _fieldOp;
    private readonly object _rawOperand;
    private readonly object[] _rawListOperand;
    private readonly bool _isListOperand;
    private readonly CompositeFilterOp _compositeOp;
    private readonly Filter[] _children;

    private Filter(
        FieldPath fieldPath, FieldFilterOp op, object rawOperand, object[] rawList, bool isList) {
      _isComposite = false;
      _fieldPath = fieldPath;
      _fieldOp = op;
      _rawOperand = rawOperand;
      _rawListOperand = rawList;
      _isListOperand = isList;
    }

    private Filter(CompositeFilterOp op, Filter[] children) {
      _isComposite = true;
      _compositeOp = op;
      _children = children ?? Array.Empty<Filter>();
    }

    internal FilterSpec BindToQuery(Query query) {
      if (_isComposite) {
        var specs = new List<FilterSpec>(_children.Length);
        for (int i = 0; i < _children.Length; i++) {
          FilterSpec child = _children[i].BindToQuery(query);
          if (child != null && !child.IsEmptyComposite) {
            specs.Add(child);
          }
        }
        if (specs.Count == 0) return FilterSpec.ForComposite(_compositeOp, Array.Empty<FilterSpec>());
        if (specs.Count == 1) return specs[0];
        return FilterSpec.ForComposite(_compositeOp, specs.ToArray());
      }

      if (_isListOperand) {
        if (_rawListOperand.Length == 0) {
          throw new ArgumentException(
              $"Invalid Query. A non-empty array is required for '{_fieldOp}' filters.");
        }
        var items = new FirestoreValue[_rawListOperand.Length];
        for (int i = 0; i < _rawListOperand.Length; i++) {
          items[i] = query.ConvertFilterValue(_fieldPath, _fieldOp, _rawListOperand[i], insideInArray: true);
        }
        return FilterSpec.ForField(_fieldPath, _fieldOp, FirestoreValue.FromArray(items));
      }

      FirestoreValue boundVal =
          query.ConvertFilterValue(_fieldPath, _fieldOp, _rawOperand, insideInArray: false);
      return FilterSpec.ForField(_fieldPath, _fieldOp, boundVal);
    }

    private static Filter CreateScalar(string fieldPath, FieldFilterOp op, object value) {
      FieldPath fp = FieldPath.FromDotSeparatedString(fieldPath);
      return CreateScalar(fp, op, value);
    }

    private static Filter CreateScalar(FieldPath fieldPath, FieldFilterOp op, object value) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      // Validate serializability eagerly for non-DocumentId fields!
      if (!fieldPath.IsDocumentId && value != null) {
        FirestoreValue v = ValueSerializer.Serialize(SerializationContext.Default, value);
        if (v.IsSentinel) {
          throw new ArgumentException("FieldValue sentinels are not valid in query filters.");
        }
      }
      if ((op == FieldFilterOp.LessThan ||
           op == FieldFilterOp.LessThanOrEqual ||
           op == FieldFilterOp.GreaterThan ||
           op == FieldFilterOp.GreaterThanOrEqual) &&
          (value == null ||
           (value is double d && double.IsNaN(d)) ||
           (value is float f && float.IsNaN(f)))) {
        throw new ArgumentException("Invalid query. Null and NaN are only supported with EqualTo and NotEqualTo.");
      }
      if (op == FieldFilterOp.ArrayContains && fieldPath.IsDocumentId) {
        throw new ArgumentException("Invalid query. You can't perform ArrayContains queries on FieldPath.DocumentId.");
      }
      return new Filter(fieldPath, op, value, null, isList: false);
    }

    private static Filter CreateList(string fieldPath, FieldFilterOp op, IEnumerable<object> values) {
      FieldPath fp = FieldPath.FromDotSeparatedString(fieldPath);
      return CreateList(fp, op, values);
    }

    private static Filter CreateList(
        FieldPath fieldPath, FieldFilterOp op, IEnumerable<object> values) {
      Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
      Preconditions.CheckNotNull(values, nameof(values));
      object[] arr = values.ToArray();
      if (arr.Length == 0) {
        throw new ArgumentException("Filter value array must not be empty.", nameof(values));
      }
      if (op == FieldFilterOp.ArrayContainsAny && fieldPath.IsDocumentId) {
        throw new ArgumentException(
            "Invalid query. You can't perform ArrayContainsAny queries on FieldPath.DocumentId.");
      }
      if (!fieldPath.IsDocumentId) {
        for (int i = 0; i < arr.Length; i++) {
          FirestoreValue v = ValueSerializer.Serialize(SerializationContext.Default, arr[i]);
          if (v.IsSentinel) {
            throw new ArgumentException("FieldValue sentinels are not valid in query filters.");
          }
        }
      }
      return new Filter(fieldPath, op, null, arr, isList: true);
    }

    /// <summary>
    /// Creates a new filter for checking that the given array field contains the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field containing an array to search.</param>
    /// <param name="value">The value that must be contained in the array.</param>
    /// <returns></returns>
    public static Filter ArrayContains(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.ArrayContains, value);

    /// <summary>
    /// Creates a new filter for checking that the given array field contains any of the given values.
    /// </summary>
    /// <param name="fieldPath">The name of the field containing an array to search.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter ArrayContainsAny(string fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.ArrayContainsAny, values);

    /// <summary>
    /// Creates a new filter for checking that the given field is equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter EqualTo(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.Equal, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is not equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter NotEqualTo(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.NotEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is greater than the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter GreaterThan(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.GreaterThan, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is greater than or equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter GreaterThanOrEqualTo(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.GreaterThanOrEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is less than the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter LessThan(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.LessThan, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is less than or equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter LessThanOrEqualTo(string fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.LessThanOrEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field equals any of the given values.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns></returns>
    public static Filter In(string fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.In, values);

    /// <summary>
    /// Creates a new filter for checking that the given field does not equal any of the given values.
    /// </summary>
    /// <param name="fieldPath">The name of the field to compare.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter NotIn(string fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.NotIn, values);

    /// <summary>
    /// Creates a new filter for checking that the given array field contains the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field containing an array to search.</param>
    /// <param name="value">The value that must be contained in the array.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter ArrayContains(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.ArrayContains, value);

    /// <summary>
    /// Creates a new filter for checking that the given array field contains any of the given values.
    /// </summary>
    /// <param name="fieldPath">The path of the field containing an array to search.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter ArrayContainsAny(FieldPath fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.ArrayContainsAny, values);

    /// <summary>
    /// Creates a new filter for checking that the given field is equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field containing an array to search.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter EqualTo(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.Equal, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is not equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter NotEqualTo(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.NotEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is greater than the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter GreaterThan(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.GreaterThan, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is greater than or equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter GreaterThanOrEqualTo(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.GreaterThanOrEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is less than the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter LessThan(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.LessThan, value);

    /// <summary>
    /// Creates a new filter for checking that the given field is less than or equal to the given value.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="value">The value for comparison.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter LessThanOrEqualTo(FieldPath fieldPath, object value) =>
        CreateScalar(fieldPath, FieldFilterOp.LessThanOrEqual, value);

    /// <summary>
    /// Creates a new filter for checking that the given field equals any of the given values.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter In(FieldPath fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.In, values);

    /// <summary>
    /// Creates a new filter for checking that the given field does not equal any of the given values.
    /// </summary>
    /// <param name="fieldPath">The path of the field to compare.</param>
    /// <param name="values">The list of values to match.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter NotIn(FieldPath fieldPath, IEnumerable<object> values) =>
        CreateList(fieldPath, FieldFilterOp.NotIn, values);

    /// <summary>
    /// Creates a new filter that is a conjunction of the given filters. A conjunction filter includes a document
    /// if it satisfies all of the given filters.
    ///
    /// If no filter is given, the composite filter is a no-op, and if only one filter is given, the composite
    /// filter has the same behavior as the underlying filter.
    /// </summary>
    /// <param name="filters">The filters to perform a conjunction for.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter And(params Filter[] filters) {
      Preconditions.CheckNotNull(filters, nameof(filters));
      for (int i = 0; i < filters.Length; i++) {
        if (filters[i] == null) {
          throw new ArgumentException("Filters array must not contain null elements.", nameof(filters));
        }
      }
      if (filters.Length == 1) return filters[0];
      var copy = new Filter[filters.Length];
      Array.Copy(filters, copy, filters.Length);
      return new Filter(CompositeFilterOp.And, copy);
    }

    /// <summary>
    /// Creates a new filter that is a disjunction of the given filters. A disjunction filter includes a document
    /// if it satisfies any of the given filters.
    ///
    /// If no filter is given, the composite filter is a no-op, and if only one filter is given, the composite
    /// filter has the same behavior as the underlying filter.
    /// </summary>
    /// <param name="filters">The filters to perform a disjunction for.</param>
    /// <returns>The newly created filter.</returns>
    public static Filter Or(params Filter[] filters) {
      Preconditions.CheckNotNull(filters, nameof(filters));
      for (int i = 0; i < filters.Length; i++) {
        if (filters[i] == null) {
          throw new ArgumentException("Filters array must not contain null elements.", nameof(filters));
        }
      }
      if (filters.Length == 1) return filters[0];
      var copy = new Filter[filters.Length];
      Array.Copy(filters, copy, filters.Length);
      return new Filter(CompositeFilterOp.Or, copy);
    }
  }
}
