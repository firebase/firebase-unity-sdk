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

using System.Collections.Generic;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
    
    public sealed class Filter {
        private Filter(FilterProxy proxy) {
            Proxy = proxy;
        }
        
        internal FilterProxy Proxy { get; }

        public static Filter ArrayContains(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.ArrayContains(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }

        public static Filter ArrayContainsAny(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterArrayContainsAny(fieldPath, array));
        }
        
        public static Filter EqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.EqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter NotEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.NotEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter GreaterThan(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThan(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter GreaterThanOrEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThanOrEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter LessThan(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThan(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter LessThanOrEqualTo(string fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThanOrEqualTo(fieldPath,
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter In(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterIn(fieldPath, array));
        }
        
        public static Filter NotIn(string fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterNotIn(fieldPath, array));
        }

        public static Filter ArrayContains(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.ArrayContains(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter ArrayContainsAny(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterArrayContainsAny(fieldPath.ConvertToProxy(), array));
        }
        
        public static Filter EqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.EqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter NotEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.NotEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter GreaterThan(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThan(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter GreaterThanOrEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.GreaterThanOrEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter LessThan(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThan(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter LessThanOrEqualTo(FieldPath fieldPath, object value) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            return new Filter(FilterProxy.LessThanOrEqualTo(fieldPath.ConvertToProxy(),
                ValueSerializer.Serialize(SerializationContext.Default, value)));
        }
        
        public static Filter In(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterIn(fieldPath.ConvertToProxy(), array));
        }
        
        public static Filter NotIn(FieldPath fieldPath, IEnumerable<object> values) {
            Preconditions.CheckNotNull(fieldPath, nameof(fieldPath));
            Preconditions.CheckNotNull(values, nameof(values));
            var array = ValueSerializer.Serialize(SerializationContext.Default, values);
            return new Filter(FirestoreCpp.FilterNotIn(fieldPath.ConvertToProxy(), array));
        }

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
    }
    
}

