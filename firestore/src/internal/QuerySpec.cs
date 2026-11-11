// Copyright 2026 Google LLC
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

namespace Firebase.Firestore.Internal {
  internal enum FieldFilterOp : byte {
    LessThan = 0,
    LessThanOrEqual = 1,
    GreaterThan = 2,
    GreaterThanOrEqual = 3,
    Equal = 4,
    NotEqual = 5,
    ArrayContains = 6,
    In = 7,
    ArrayContainsAny = 8,
    NotIn = 9,
  }

  internal enum CompositeFilterOp : byte {
    And = 0,
    Or = 1,
  }

  internal enum LimitKind : byte {
    None = 0,
    First = 1,
    Last = 2,
  }

  internal sealed class FilterSpec : IEquatable<FilterSpec> {
    internal bool IsComposite { get; }
    // Field filter members:
    internal FieldPath Field { get; }
    internal FieldFilterOp Op { get; }
    internal FirestoreValue Operand { get; }
    // Composite filter members:
    internal CompositeFilterOp CompositeOp { get; }
    internal FilterSpec[] Children { get; }

    private FilterSpec(FieldPath field, FieldFilterOp op, FirestoreValue operand) {
      IsComposite = false;
      Field = field;
      Op = op;
      Operand = operand;
    }

    private FilterSpec(CompositeFilterOp compositeOp, FilterSpec[] children) {
      IsComposite = true;
      CompositeOp = compositeOp;
      Children = children ?? Array.Empty<FilterSpec>();
    }

    internal static FilterSpec ForField(FieldPath field, FieldFilterOp op, FirestoreValue operand) =>
        new FilterSpec(field, op, operand);

    internal static FilterSpec ForComposite(CompositeFilterOp op, FilterSpec[] children) =>
        new FilterSpec(op, children);

    internal bool IsEmptyComposite => IsComposite && Children.Length == 0;

    internal bool Matches(MutableDocument doc, string projectId, string databaseId) {
      if (!doc.Exists) return false;
      if (IsComposite) {
        if (Children.Length == 0) return true;
        if (CompositeOp == CompositeFilterOp.And) {
          for (int i = 0; i < Children.Length; i++) {
            if (!Children[i].Matches(doc, projectId, databaseId)) return false;
          }
          return true;
        }
        for (int i = 0; i < Children.Length; i++) {
          if (Children[i].Matches(doc, projectId, databaseId)) return true;
        }
        return false;
      }

      FirestoreValue fieldVal;
      if (Field.IsDocumentId) {
        fieldVal = FirestoreValue.FromReference(
            new DocumentReferenceValue(projectId, databaseId, doc.Path));
      } else if (!FirestoreValue.TryGetFieldByPath(doc.Fields, Field, out fieldVal)) {
        // Missing field only matches NotEqual / NotIn?
        // Wait: in Firestore, NotEqual(x) and NotIn([...]) ONLY match documents WHERE THE FIELD EXISTS!
        // Missing fields NEVER match ANY filter in Firestore (not even != null or NotIn)!
        return false;
      }

      return EvaluateFieldFilter(fieldVal, Op, Operand);
    }

    private static bool EvaluateFieldFilter(
        FirestoreValue fieldVal, FieldFilterOp op, FirestoreValue operand) {
      // Handle null and NaN unary semantics first:
      if (operand.IsNull) {
        if (op == FieldFilterOp.Equal) return fieldVal.IsNull;
        if (op == FieldFilterOp.NotEqual) return !fieldVal.IsNull;
        return false;
      }
      if (operand.Kind == FirestoreValueKind.Double && double.IsNaN(operand.DoubleValue)) {
        bool isFieldNaN = fieldVal.Kind == FirestoreValueKind.Double &&
                          double.IsNaN(fieldVal.DoubleValue);
        if (op == FieldFilterOp.Equal) return isFieldNaN;
        if (op == FieldFilterOp.NotEqual) return !fieldVal.IsNull && !isFieldNaN;
        return false;
      }

      switch (op) {
        case FieldFilterOp.Equal:
          return FirestoreValue.ValueEquals(fieldVal, operand);

        case FieldFilterOp.NotEqual:
          // In Firestore, `field != X` does NOT match `null` or `NaN`, and matches cross-types!
          if (fieldVal.IsNull) return false;
          if (fieldVal.Kind == FirestoreValueKind.Double && double.IsNaN(fieldVal.DoubleValue)) {
            return false;
          }
          return !FirestoreValue.ValueEquals(fieldVal, operand);

        case FieldFilterOp.LessThan:
        case FieldFilterOp.LessThanOrEqual:
        case FieldFilterOp.GreaterThan:
        case FieldFilterOp.GreaterThanOrEqual: {
          // Inequalities in Firestore ONLY match values of the SAME type order (and never NaN)!
          if (FirestoreValue.GetTypeOrder(fieldVal.Kind) !=
              FirestoreValue.GetTypeOrder(operand.Kind)) {
            return false;
          }
          if (fieldVal.Kind == FirestoreValueKind.Double && double.IsNaN(fieldVal.DoubleValue)) {
            return false;
          }
          int cmp = FirestoreValue.Compare(fieldVal, operand);
          return op switch {
            FieldFilterOp.LessThan => cmp < 0,
            FieldFilterOp.LessThanOrEqual => cmp <= 0,
            FieldFilterOp.GreaterThan => cmp > 0,
            FieldFilterOp.GreaterThanOrEqual => cmp >= 0,
            _ => false,
          };
        }

        case FieldFilterOp.ArrayContains: {
          if (fieldVal.Kind != FirestoreValueKind.Array) return false;
          IReadOnlyList<FirestoreValue> arr = fieldVal.ArrayValue;
          for (int i = 0; i < arr.Count; i++) {
            if (FirestoreValue.ValueEquals(arr[i], operand)) return true;
          }
          return false;
        }

        case FieldFilterOp.In: {
          IReadOnlyList<FirestoreValue> set = operand.ArrayValue;
          for (int i = 0; i < set.Count; i++) {
            if (FirestoreValue.ValueEquals(fieldVal, set[i])) return true;
          }
          return false;
        }

        case FieldFilterOp.NotIn: {
          if (fieldVal.IsNull) return false;
          if (fieldVal.Kind == FirestoreValueKind.Double && double.IsNaN(fieldVal.DoubleValue)) {
            return false;
          }
          IReadOnlyList<FirestoreValue> set = operand.ArrayValue;
          for (int i = 0; i < set.Count; i++) {
            if (set[i].IsNull) return false;
            if (set[i].Kind == FirestoreValueKind.Double && double.IsNaN(set[i].DoubleValue)) {
              return false;
            }
            if (FirestoreValue.ValueEquals(fieldVal, set[i])) return false;
          }
          return true;
        }

        case FieldFilterOp.ArrayContainsAny: {
          if (fieldVal.Kind != FirestoreValueKind.Array) return false;
          IReadOnlyList<FirestoreValue> arr = fieldVal.ArrayValue;
          IReadOnlyList<FirestoreValue> set = operand.ArrayValue;
          for (int i = 0; i < arr.Count; i++) {
            for (int j = 0; j < set.Count; j++) {
              if (FirestoreValue.ValueEquals(arr[i], set[j])) return true;
            }
          }
          return false;
        }

        default:
          return false;
      }
    }

    internal void CollectInequalityFields(List<FieldPath> result) {
      if (IsComposite) {
        for (int i = 0; i < Children.Length; i++) {
          Children[i].CollectInequalityFields(result);
        }
        return;
      }
      if (Op == FieldFilterOp.LessThan ||
          Op == FieldFilterOp.LessThanOrEqual ||
          Op == FieldFilterOp.GreaterThan ||
          Op == FieldFilterOp.GreaterThanOrEqual ||
          Op == FieldFilterOp.NotEqual ||
          Op == FieldFilterOp.NotIn) {
        for (int i = 0; i < result.Count; i++) {
          if (result[i].Equals(Field)) return;
        }
        result.Add(Field);
      }
    }

    public bool Equals(FilterSpec other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null || IsComposite != other.IsComposite) return false;
      if (!IsComposite) {
        return Op == other.Op && Field.Equals(other.Field) && Operand.Equals(other.Operand);
      }
      if (CompositeOp != other.CompositeOp || Children.Length != other.Children.Length) return false;
      for (int i = 0; i < Children.Length; i++) {
        if (!Children[i].Equals(other.Children[i])) return false;
      }
      return true;
    }

    public override bool Equals(object obj) => Equals(obj as FilterSpec);

    public override int GetHashCode() {
      if (!IsComposite) {
        int h = (int)Op * 397 ^ Field.GetHashCode();
        return h * 31 ^ Operand.GetHashCode();
      }
      int hc = ((int)CompositeOp + 17) * 397;
      for (int i = 0; i < Children.Length; i++) {
        hc = hc * 31 + Children[i].GetHashCode();
      }
      return hc;
    }
  }

  internal readonly struct OrderBySpec : IEquatable<OrderBySpec> {
    internal FieldPath Field { get; }
    internal bool Descending { get; }

    internal OrderBySpec(FieldPath field, bool descending) {
      Field = field;
      Descending = descending;
    }

    internal OrderBySpec Flip() => new OrderBySpec(Field, !Descending);

    public bool Equals(OrderBySpec other) =>
        Descending == other.Descending && Field.Equals(other.Field);

    public override bool Equals(object obj) => obj is OrderBySpec other && Equals(other);

    public override int GetHashCode() => (Field.GetHashCode() * 31) ^ (Descending ? 1 : 0);
  }

  internal sealed class BoundSpec : IEquatable<BoundSpec> {
    internal FirestoreValue[] Position { get; }
    internal bool Inclusive { get; }

    internal BoundSpec(FirestoreValue[] position, bool inclusive) {
      Position = position ?? Array.Empty<FirestoreValue>();
      Inclusive = inclusive;
    }

    public bool Equals(BoundSpec other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null || Inclusive != other.Inclusive ||
          Position.Length != other.Position.Length) {
        return false;
      }
      for (int i = 0; i < Position.Length; i++) {
        if (!Position[i].Equals(other.Position[i])) return false;
      }
      return true;
    }

    public override bool Equals(object obj) => Equals(obj as BoundSpec);

    public override int GetHashCode() {
      int h = Inclusive ? 1231 : 1237;
      for (int i = 0; i < Position.Length; i++) {
        h = h * 31 + Position[i].GetHashCode();
      }
      return h;
    }
  }

  internal sealed class QuerySpec : IEquatable<QuerySpec> {
    internal ResourcePath Path { get; }
    internal string CollectionGroup { get; }
    internal FilterSpec[] Filters { get; }
    internal OrderBySpec[] ExplicitOrderBys { get; }
    internal BoundSpec StartBound { get; }
    internal BoundSpec EndBound { get; }
    internal int? Limit { get; }
    internal LimitKind LimitType { get; }

    private OrderBySpec[] _normalizedOrderBys;

    internal QuerySpec(
        ResourcePath path,
        string collectionGroup,
        FilterSpec[] filters = null,
        OrderBySpec[] explicitOrderBys = null,
        BoundSpec startBound = null,
        BoundSpec endBound = null,
        int? limit = null,
        LimitKind limitType = LimitKind.None) {
      Path = path ?? ResourcePath.Empty;
      CollectionGroup = collectionGroup;
      Filters = filters ?? Array.Empty<FilterSpec>();
      ExplicitOrderBys = explicitOrderBys ?? Array.Empty<OrderBySpec>();
      StartBound = startBound;
      EndBound = endBound;
      Limit = limit;
      LimitType = limitType;
    }

    internal bool IsCollectionGroup => CollectionGroup != null;
    internal string CollectionId => IsCollectionGroup ? CollectionGroup : Path.Id;

    internal QuerySpec WithFilter(FilterSpec filter) {
      if (filter == null || filter.IsEmptyComposite) return this;
      var copy = new FilterSpec[Filters.Length + 1];
      Array.Copy(Filters, 0, copy, 0, Filters.Length);
      copy[Filters.Length] = filter;
      return new QuerySpec(
          Path, CollectionGroup, copy, ExplicitOrderBys, StartBound, EndBound, Limit, LimitType);
    }

    internal QuerySpec WithOrderBy(OrderBySpec orderBy) {
      // Cannot specify additional OrderBy after StartAt/StartAfter/EndAt/EndBefore!
      if (StartBound != null || EndBound != null) {
        throw new InvalidOperationException(
            "Invalid query. You must not call StartAt(), StartAfter(), EndAt(), or EndBefore() " +
            "before calling OrderBy().");
      }
      for (int i = 0; i < ExplicitOrderBys.Length; i++) {
        if (ExplicitOrderBys[i].Field.Equals(FieldPath.DocumentId)) {
          throw new ArgumentException(
              "Invalid query. You must not specify an OrderBy() clause after ordering by FieldPath.DocumentId.");
        }
        if (ExplicitOrderBys[i].Field.Equals(orderBy.Field)) {
          throw new ArgumentException(
              $"Invalid query. OrderBy('{orderBy.Field}') was already specified.");
        }
      }
      var copy = new OrderBySpec[ExplicitOrderBys.Length + 1];
      Array.Copy(ExplicitOrderBys, 0, copy, 0, ExplicitOrderBys.Length);
      copy[ExplicitOrderBys.Length] = orderBy;
      return new QuerySpec(
          Path, CollectionGroup, Filters, copy, StartBound, EndBound, Limit, LimitType);
    }

    internal QuerySpec WithStartBound(BoundSpec bound) => new QuerySpec(
        Path, CollectionGroup, Filters, ExplicitOrderBys, bound, EndBound, Limit, LimitType);

    internal QuerySpec WithEndBound(BoundSpec bound) => new QuerySpec(
        Path, CollectionGroup, Filters, ExplicitOrderBys, StartBound, bound, Limit, LimitType);

    internal QuerySpec WithLimit(int limit, LimitKind kind) => new QuerySpec(
        Path, CollectionGroup, Filters, ExplicitOrderBys, StartBound, EndBound, limit, kind);

    internal OrderBySpec[] NormalizedOrderBys {
      get {
        if (_normalizedOrderBys != null) return _normalizedOrderBys;
        var list = new List<OrderBySpec>(ExplicitOrderBys.Length + 2);
        bool hasDocId = false;
        for (int i = 0; i < ExplicitOrderBys.Length; i++) {
          list.Add(ExplicitOrderBys[i]);
          if (ExplicitOrderBys[i].Field.IsDocumentId) hasDocId = true;
        }
        if (ExplicitOrderBys.Length == 0) {
          var inequalities = new List<FieldPath>();
          for (int i = 0; i < Filters.Length; i++) {
            Filters[i].CollectInequalityFields(inequalities);
          }
          if (inequalities.Count > 0) {
            FieldPath ineqField = inequalities[0];
            if (ineqField.IsDocumentId) {
              hasDocId = true;
            }
            list.Add(new OrderBySpec(ineqField, descending: false));
          }
        }
        if (!hasDocId) {
          bool lastDesc = list.Count > 0 && list[list.Count - 1].Descending;
          list.Add(new OrderBySpec(FieldPath.DocumentId, lastDesc));
        }
        _normalizedOrderBys = list.ToArray();
        return _normalizedOrderBys;
      }
    }

    internal void ValidateForExecution() {
      if (LimitType == LimitKind.Last && ExplicitOrderBys.Length == 0) {
        throw new InvalidOperationException(
            "limitToLast() queries require specifying at least one OrderBy() clause.");
      }
    }

    /// <summary>
    /// Converts a <c>LimitToLast</c> query into its wire target query (flipped orderBys and swapped
    /// start/end bounds), matching the Firestore JS and C++ SDK <c>Query::ToTarget()</c> rule.
    /// </summary>
    internal void GetWireTargetParts(
        out OrderBySpec[] wireOrderBys, out BoundSpec wireStart, out BoundSpec wireEnd) {
      OrderBySpec[] norm = NormalizedOrderBys;
      if (LimitType != LimitKind.Last) {
        wireOrderBys = norm;
        wireStart = StartBound;
        wireEnd = EndBound;
        return;
      }
      wireOrderBys = new OrderBySpec[norm.Length];
      for (int i = 0; i < norm.Length; i++) {
        wireOrderBys[i] = norm[i].Flip();
      }
      wireStart = EndBound;
      wireEnd = StartBound;
    }

    internal List<MutableDocument> EvaluateLocally(
        IEnumerable<MutableDocument> candidates, string projectId, string databaseId) {
      ValidateForExecution();
      var matched = new List<MutableDocument>();
      foreach (MutableDocument doc in candidates) {
        if (!doc.Exists) continue;
        if (IsCollectionGroup) {
          if (!doc.Path.IsDocument ||
              !string.Equals(doc.Path.Parent.Id, CollectionGroup, StringComparison.Ordinal)) {
            continue;
          }
        } else {
          if (!Path.IsImmediateParentOf(doc.Path)) continue;
        }
        bool allPass = true;
        for (int i = 0; i < Filters.Length; i++) {
          if (!Filters[i].Matches(doc, projectId, databaseId)) {
            allPass = false;
            break;
          }
        }
        if (!allPass) continue;

        // Every explicit OrderBy field (except __name__) must exist on the document!
        for (int i = 0; i < ExplicitOrderBys.Length; i++) {
          FieldPath f = ExplicitOrderBys[i].Field;
          if (!f.IsDocumentId && !FirestoreValue.TryGetFieldByPath(doc.Fields, f, out _)) {
            allPass = false;
            break;
          }
        }
        if (!allPass) continue;

        OrderBySpec[] orderBys = NormalizedOrderBys;
        if (StartBound != null && !SortsAfterStart(doc, orderBys, StartBound, projectId, databaseId)) {
          continue;
        }
        if (EndBound != null && !SortsBeforeEnd(doc, orderBys, EndBound, projectId, databaseId)) {
          continue;
        }
        matched.Add(doc);
      }

      OrderBySpec[] sortOrders = NormalizedOrderBys;
      matched.Sort((a, b) => CompareDocuments(a, b, sortOrders, projectId, databaseId));

      if (Limit.HasValue) {
        int lim = Limit.Value;
        if (matched.Count > lim) {
          if (LimitType == LimitKind.Last) {
            matched.RemoveRange(0, matched.Count - lim);
          } else {
            matched.RemoveRange(lim, matched.Count - lim);
          }
        }
      }
      return matched;
    }

    internal int CompareDocuments(
        MutableDocument a, MutableDocument b, string projectId, string databaseId) =>
        CompareDocuments(a, b, NormalizedOrderBys, projectId, databaseId);

    private static int CompareDocuments(
        MutableDocument a,
        MutableDocument b,
        OrderBySpec[] orderBys,
        string projectId,
        string databaseId) {
      for (int i = 0; i < orderBys.Length; i++) {
        OrderBySpec ob = orderBys[i];
        FirestoreValue va = ExtractOrderValue(a, ob.Field, projectId, databaseId);
        FirestoreValue vb = ExtractOrderValue(b, ob.Field, projectId, databaseId);
        int cmp = FirestoreValue.Compare(va, vb);
        if (cmp != 0) return ob.Descending ? -cmp : cmp;
      }
      return 0;
    }

    private static bool SortsAfterStart(
        MutableDocument doc,
        OrderBySpec[] orderBys,
        BoundSpec bound,
        string projectId,
        string databaseId) {
      int cmp = CompareDocToBound(doc, orderBys, bound, projectId, databaseId);
      return bound.Inclusive ? cmp >= 0 : cmp > 0;
    }

    private static bool SortsBeforeEnd(
        MutableDocument doc,
        OrderBySpec[] orderBys,
        BoundSpec bound,
        string projectId,
        string databaseId) {
      int cmp = CompareDocToBound(doc, orderBys, bound, projectId, databaseId);
      return bound.Inclusive ? cmp <= 0 : cmp < 0;
    }

    private static int CompareDocToBound(
        MutableDocument doc,
        OrderBySpec[] orderBys,
        BoundSpec bound,
        string projectId,
        string databaseId) {
      int len = Math.Min(bound.Position.Length, orderBys.Length);
      for (int i = 0; i < len; i++) {
        OrderBySpec ob = orderBys[i];
        FirestoreValue docVal = ExtractOrderValue(doc, ob.Field, projectId, databaseId);
        int cmp = FirestoreValue.Compare(docVal, bound.Position[i]);
        if (cmp != 0) return ob.Descending ? -cmp : cmp;
      }
      return 0;
    }

    private static FirestoreValue ExtractOrderValue(
        MutableDocument doc, FieldPath field, string projectId, string databaseId) {
      if (field.IsDocumentId) {
        return FirestoreValue.FromReference(
            new DocumentReferenceValue(projectId, databaseId, doc.Path));
      }
      return FirestoreValue.TryGetFieldByPath(doc.Fields, field, out FirestoreValue v)
          ? v
          : FirestoreValue.Null;
    }

    public bool Equals(QuerySpec other) {
      if (ReferenceEquals(this, other)) return true;
      if (other == null) return false;
      if (!Path.Equals(other.Path) ||
          !string.Equals(CollectionGroup, other.CollectionGroup, StringComparison.Ordinal) ||
          Limit != other.Limit ||
          LimitType != other.LimitType) {
        return false;
      }
      OrderBySpec[] oa = NormalizedOrderBys;
      OrderBySpec[] ob = other.NormalizedOrderBys;
      if (oa.Length != ob.Length) return false;
      for (int i = 0; i < oa.Length; i++) {
        if (!oa[i].Equals(ob[i])) return false;
      }
      if (Filters.Length != other.Filters.Length) return false;
      for (int i = 0; i < Filters.Length; i++) {
        if (!Filters[i].Equals(other.Filters[i])) return false;
      }
      if (!Equals(StartBound, other.StartBound) || !Equals(EndBound, other.EndBound)) {
        return false;
      }
      return true;
    }

    public override bool Equals(object obj) => Equals(obj as QuerySpec);

    public override int GetHashCode() {
      int h = Path.GetHashCode() * 31 +
              (CollectionGroup != null ? StringComparer.Ordinal.GetHashCode(CollectionGroup) : 0);
      OrderBySpec[] oa = NormalizedOrderBys;
      for (int i = 0; i < oa.Length; i++) h = h * 31 + oa[i].GetHashCode();
      for (int i = 0; i < Filters.Length; i++) h = h * 31 + Filters[i].GetHashCode();
      if (StartBound != null) h = h * 31 + StartBound.GetHashCode();
      if (EndBound != null) h = h * 31 + EndBound.GetHashCode();
      h = h * 31 + (Limit ?? -1);
      h = h * 31 + (int)LimitType;
      return h;
    }
  }
}
