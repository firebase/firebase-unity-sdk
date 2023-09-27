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

using System.Collections.Generic;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
    
    /// <summary>
    /// A Filter represents a restriction on one or more field values and can be used to refine the results of a Query.
    /// </summary>
    public sealed class Filter {
        private readonly FilterProxy _proxy;

        private Filter(FilterProxy proxy) {
            _proxy = Util.NotNull(proxy);
        }

        internal FilterProxy Proxy {
            get {
                return _proxy;
            }
        }


        /// <summary>
        /// Creates a new filter for checking that the given array field contains the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field containing an array to search.</param>
        /// <param name="value">The value that must be contained in the array.</param>
        /// <returns></returns>
        public static Filter ArrayContains(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.ArrayContains(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }

        /// <summary>
        /// Creates a new filter for checking that the given array field contains any of the given values.
        /// </summary>
        /// <param name="fieldPath">The name of the field containing an array to search.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter ArrayContainsAny(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterArrayContainsAny(fieldPath, array));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter EqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.EqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is not equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter NotEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.NotEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is greater than the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter GreaterThan(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThan(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is greater than or equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter GreaterThanOrEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThanOrEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is less than the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter LessThan(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThan(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is less than or equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter LessThanOrEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThanOrEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field equals any of the given values.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns></returns>
        public static Filter In(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterIn(fieldPath, array));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field does not equal any of the given values.
        /// </summary>
        /// <param name="fieldPath">The name of the field to compare.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter NotIn(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterNotIn(fieldPath, array));
        }

        /// <summary>
        /// Creates a new filter for checking that the given array field contains the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field containing an array to search.</param>
        /// <param name="value">The value that must be contained in the array.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter ArrayContains(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.ArrayContains(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given array field contains any of the given values.
        /// </summary>
        /// <param name="fieldPath">The path of the field containing an array to search.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter ArrayContainsAny(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterArrayContainsAny(fieldPath.ConvertToProxy(), array));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field containing an array to search.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter EqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.EqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is not equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter NotEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.NotEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is greater than the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter GreaterThan(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThan(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is greater than or equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter GreaterThanOrEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThanOrEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is less than the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter LessThan(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThan(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field is less than or equal to the given value.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="value">The value for comparison.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter LessThanOrEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThanOrEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field equals any of the given values.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter In(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterIn(fieldPath.ConvertToProxy(), array));
        }
        
        /// <summary>
        /// Creates a new filter for checking that the given field does not equal any of the given values.
        /// </summary>
        /// <param name="fieldPath">The path of the field to compare.</param>
        /// <param name="values">The list of values to match.</param>
        /// <returns>The newly created filter.</returns>
        public static Filter NotIn(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterNotIn(fieldPath.ConvertToProxy(), array));
        }

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
            if (filters.Length == 1) {
                return filters[0];
            }
            var filterVector = new FilterVector();
            foreach (Filter filter in filters) {
                filterVector.PushBack(filter.Proxy);
            }
            return new Filter(FirestoreCpp.FilterAnd(filterVector));
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
            if (filters.Length == 1) {
                return filters[0];
            }
            var filterVector = new FilterVector();
            foreach (Filter filter in filters) {
                filterVector.PushBack(filter.Proxy);
            }
            return new Filter(FirestoreCpp.FilterOr(filterVector));
        }
    }
    
}

