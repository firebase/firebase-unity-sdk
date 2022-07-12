// Copyright 2021 Google LLC
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

using Firebase.Firestore;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase.Sample.Firestore {

  public sealed class InvalidArgumentsTestCase {
    public string name;
    public Action<UIHandlerAutomated> action;
  }

  // Tests that passing invalid input to SWIG-wrapped functions results in a C# exception instead
  // of a crash.
  public sealed class InvalidArgumentsTest {
    public static InvalidArgumentsTestCase[] TestCases {
      get {
        return new InvalidArgumentsTestCase[] {
          new InvalidArgumentsTestCase { name = "CollectionReference_AddAsync_NullDocumentData",
                                         action = CollectionReference_AddAsync_NullDocumentData },
          new InvalidArgumentsTestCase {
            name = "CollectionReference_AddAsync_DocumentDataWithEmptyKey",
            action = CollectionReference_AddAsync_DocumentDataWithEmptyKey
          },
          new InvalidArgumentsTestCase {
            name = "CollectionReference_AddAsync_InvalidDocumentDataType",
            action = CollectionReference_AddAsync_InvalidDocumentDataType
          },
          new InvalidArgumentsTestCase { name = "CollectionReference_Document_NullStringPath",
                                         action = CollectionReference_Document_NullStringPath },
          new InvalidArgumentsTestCase { name = "CollectionReference_Document_EmptyStringPath",
                                         action = CollectionReference_Document_EmptyStringPath },
          new InvalidArgumentsTestCase {
            name = "CollectionReference_Document_EvenNumberOfPathSegments",
            action = CollectionReference_Document_EvenNumberOfPathSegments
          },
          new InvalidArgumentsTestCase { name = "DocumentReference_Collection_NullStringPath",
                                         action = DocumentReference_Collection_NullStringPath },
          new InvalidArgumentsTestCase { name = "DocumentReference_Collection_EmptyStringPath",
                                         action = DocumentReference_Collection_EmptyStringPath },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_Collection_EvenNumberOfPathSegments",
            action = DocumentReference_Collection_EvenNumberOfPathSegments
          },
          new InvalidArgumentsTestCase { name = "DocumentReference_Listen_1Arg_NullCallback",
                                         action = DocumentReference_Listen_1Arg_NullCallback },
          new InvalidArgumentsTestCase { name = "DocumentReference_Listen_2Args_NullCallback",
                                         action = DocumentReference_Listen_2Args_NullCallback },
          new InvalidArgumentsTestCase { name = "DocumentReference_SetAsync_NullDocumentData",
                                         action = DocumentReference_SetAsync_NullDocumentData },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_SetAsync_DocumentDataWithEmptyKey",
            action = DocumentReference_SetAsync_DocumentDataWithEmptyKey
          },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_SetAsync_InvalidDocumentDataType",
            action = DocumentReference_SetAsync_InvalidDocumentDataType
          },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_UpdateAsync_NullStringKeyedDictionary",
            action = DocumentReference_UpdateAsync_NullStringKeyedDictionary
          },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_UpdateAsync_EmptyStringKeyedDictionary",
            action = DocumentReference_UpdateAsync_EmptyStringKeyedDictionary
          },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_UpdateAsync_StringKeyedDictionaryWithEmptyKey",
            action = DocumentReference_UpdateAsync_StringKeyedDictionaryWithEmptyKey
          },
          new InvalidArgumentsTestCase { name = "DocumentReference_UpdateAsync_NullStringField",
                                         action = DocumentReference_UpdateAsync_NullStringField },
          new InvalidArgumentsTestCase { name = "DocumentReference_UpdateAsync_EmptyStringField",
                                         action = DocumentReference_UpdateAsync_EmptyStringField },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_UpdateAsync_NullFieldPathKeyedDictionary",
            action = DocumentReference_UpdateAsync_NullFieldPathKeyedDictionary
          },
          new InvalidArgumentsTestCase {
            name = "DocumentReference_UpdateAsync_EmptyFieldPathKeyedDictionary",
            action = DocumentReference_UpdateAsync_EmptyFieldPathKeyedDictionary
          },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_ContainsField_NullStringPath",
                                         action = DocumentSnapshot_ContainsField_NullStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_ContainsField_EmptyStringPath",
                                         action = DocumentSnapshot_ContainsField_EmptyStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_ContainsField_NullFieldPath",
                                         action = DocumentSnapshot_ContainsField_NullFieldPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_GetValue_NullStringPath",
                                         action = DocumentSnapshot_GetValue_NullStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_GetValue_EmptyStringPath",
                                         action = DocumentSnapshot_GetValue_EmptyStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_GetValue_NullFieldPath",
                                         action = DocumentSnapshot_GetValue_NullFieldPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_TryGetValue_NullStringPath",
                                         action = DocumentSnapshot_TryGetValue_NullStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_TryGetValue_EmptyStringPath",
                                         action = DocumentSnapshot_TryGetValue_EmptyStringPath },
          new InvalidArgumentsTestCase { name = "DocumentSnapshot_TryGetValue_NullFieldPath",
                                         action = DocumentSnapshot_TryGetValue_NullFieldPath },
          new InvalidArgumentsTestCase { name = "FieldPath_Constructor_NullStringArray",
                                         action = FieldPath_Constructor_NullStringArray },
          new InvalidArgumentsTestCase { name = "FieldPath_Constructor_StringArrayWithNullElement",
                                         action =
                                             FieldPath_Constructor_StringArrayWithNullElement },
          new InvalidArgumentsTestCase { name = "FieldPath_Constructor_EmptyStringArray",
                                         action = FieldPath_Constructor_EmptyStringArray },
          new InvalidArgumentsTestCase { name = "FieldPath_Constructor_StringArrayWithEmptyString",
                                         action =
                                             FieldPath_Constructor_StringArrayWithEmptyString },
          new InvalidArgumentsTestCase { name = "FieldValue_ArrayRemove_NullArray",
                                         action = FieldValue_ArrayRemove_NullArray },
          new InvalidArgumentsTestCase { name = "FieldValue_ArrayUnion_NullArray",
                                         action = FieldValue_ArrayUnion_NullArray },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_GetInstance_Null",
                                         action = FirebaseFirestore_GetInstance_Null },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_GetInstance_DisposedApp",
                                         action = FirebaseFirestore_GetInstance_DisposedApp },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_Collection_NullStringPath",
                                         action = FirebaseFirestore_Collection_NullStringPath },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_Collection_EmptyStringPath",
                                         action = FirebaseFirestore_Collection_EmptyStringPath },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_Collection_EvenNumberOfPathSegments",
            action = FirebaseFirestore_Collection_EvenNumberOfPathSegments
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_CollectionGroup_NullCollectionId",
            action = FirebaseFirestore_CollectionGroup_NullCollectionId
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_CollectionGroup_EmptyCollectionId",
            action = FirebaseFirestore_CollectionGroup_EmptyCollectionId
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_CollectionGroup_CollectionIdContainsSlash",
            action = FirebaseFirestore_CollectionGroup_CollectionIdContainsSlash
          },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_Document_NullStringPath",
                                         action = FirebaseFirestore_Document_NullStringPath },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_Document_EmptyStringPath",
                                         action = FirebaseFirestore_Document_EmptyStringPath },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_Document_OddNumberOfPathSegments",
            action = FirebaseFirestore_Document_OddNumberOfPathSegments
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_ListenForSnapshotsInSync_NullCallback",
            action = FirebaseFirestore_ListenForSnapshotsInSync_NullCallback
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_NullCallback",
            action = FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_NullCallback
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_RunTransactionAsync_WithTypeParameter_NullCallback",
            action = FirebaseFirestore_RunTransactionAsync_WithTypeParameter_NullCallback
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_WithOptions_NullCallback",
            action = FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_WithOptions_NullCallback
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_RunTransactionAsync_WithTypeParameter_WithOptions_NullCallback",
            action = FirebaseFirestore_RunTransactionAsync_WithTypeParameter_WithOptions_NullCallback
          },
          new InvalidArgumentsTestCase { name = "FirebaseFirestoreSettings_Host_Null",
                                         action = FirebaseFirestoreSettings_Host_Null },
          new InvalidArgumentsTestCase { name = "FirebaseFirestoreSettings_Host_EmptyString",
                                         action = FirebaseFirestoreSettings_Host_EmptyString },
          new InvalidArgumentsTestCase { name = "Query_EndAt_NullDocumentSnapshot",
                                         action = Query_EndAt_NullDocumentSnapshot },
          new InvalidArgumentsTestCase { name = "Query_EndAt_NullArray",
                                         action = Query_EndAt_NullArray },
          new InvalidArgumentsTestCase { name = "Query_EndAt_ArrayWithNullElement",
                                         action = Query_EndAt_ArrayWithNullElement },
          new InvalidArgumentsTestCase { name = "Query_EndBefore_NullDocumentSnapshot",
                                         action = Query_EndBefore_NullDocumentSnapshot },
          new InvalidArgumentsTestCase { name = "Query_EndBefore_NullArray",
                                         action = Query_EndBefore_NullArray },
          new InvalidArgumentsTestCase { name = "Query_Limit_0", action = Query_Limit_0 },
          new InvalidArgumentsTestCase { name = "Query_Limit_Negative1",
                                         action = Query_Limit_Negative1 },
          new InvalidArgumentsTestCase { name = "Query_LimitToLast_0",
                                         action = Query_LimitToLast_0 },
          new InvalidArgumentsTestCase { name = "Query_LimitToLast_Negative1",
                                         action = Query_LimitToLast_Negative1 },
          new InvalidArgumentsTestCase { name = "Query_OrderBy_NullPathString",
                                         action = Query_OrderBy_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_OrderBy_EmptyPathString",
                                         action = Query_OrderBy_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_OrderBy_NullFieldPath",
                                         action = Query_OrderBy_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_OrderByDescending_NullPathString",
                                         action = Query_OrderByDescending_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_OrderByDescending_EmptyPathString",
                                         action = Query_OrderByDescending_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_OrderByDescending_NullFieldPath",
                                         action = Query_OrderByDescending_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_StartAfter_NullDocumentSnapshot",
                                         action = Query_StartAfter_NullDocumentSnapshot },
          new InvalidArgumentsTestCase { name = "Query_StartAfter_NullArray",
                                         action = Query_StartAfter_NullArray },
          new InvalidArgumentsTestCase { name = "Query_StartAt_NullDocumentSnapshot",
                                         action = Query_StartAt_NullDocumentSnapshot },
          new InvalidArgumentsTestCase { name = "Query_StartAt_NullArray",
                                         action = Query_StartAt_NullArray },
          new InvalidArgumentsTestCase { name = "Query_StartAt_ArrayWithNullElement",
                                         action = Query_StartAt_ArrayWithNullElement },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContains_NullFieldPath",
                                         action = Query_WhereArrayContains_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContains_NullPathString",
                                         action = Query_WhereArrayContains_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContains_EmptyPathString",
                                         action = Query_WhereArrayContains_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContainsAny_NullFieldPath",
                                         action = Query_WhereArrayContainsAny_NullFieldPath },
          new InvalidArgumentsTestCase {
            name = "Query_WhereArrayContainsAny_NonNullFieldPath_NullValues",
            action = Query_WhereArrayContainsAny_NonNullFieldPath_NullValues
          },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContainsAny_NullPathString",
                                         action = Query_WhereArrayContainsAny_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereArrayContainsAny_EmptyPathString",
                                         action = Query_WhereArrayContainsAny_EmptyPathString },
          new InvalidArgumentsTestCase {
            name = "Query_WhereArrayContainsAny_NonNullPathString_NullValues",
            action = Query_WhereArrayContainsAny_NonNullPathString_NullValues
          },
          new InvalidArgumentsTestCase { name = "Query_WhereEqualTo_NullPathString",
                                         action = Query_WhereEqualTo_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereEqualTo_EmptyPathString",
                                         action = Query_WhereEqualTo_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereEqualTo_NullFieldPath",
                                         action = Query_WhereEqualTo_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThan_NullPathString",
                                         action = Query_WhereGreaterThan_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThan_EmptyPathString",
                                         action = Query_WhereGreaterThan_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThan_NullFieldPath",
                                         action = Query_WhereGreaterThan_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThanOrEqualTo_NullPathString",
                                         action = Query_WhereGreaterThanOrEqualTo_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThanOrEqualTo_EmptyPathString",
                                         action = Query_WhereGreaterThanOrEqualTo_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereGreaterThanOrEqualTo_NullFieldPath",
                                         action = Query_WhereGreaterThanOrEqualTo_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereIn_NullFieldPath",
                                         action = Query_WhereIn_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereIn_NonNullFieldPath_NullValues",
                                         action = Query_WhereIn_NonNullFieldPath_NullValues },
          new InvalidArgumentsTestCase { name = "Query_WhereIn_NullPathString",
                                         action = Query_WhereIn_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereIn_EmptyPathString",
                                         action = Query_WhereIn_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereIn_NonNullPathString_NullValues",
                                         action = Query_WhereIn_NonNullPathString_NullValues },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThan_NullPathString",
                                         action = Query_WhereLessThan_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThan_EmptyPathString",
                                         action = Query_WhereLessThan_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThan_NullFieldPath",
                                         action = Query_WhereLessThan_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThanOrEqualTo_NullPathString",
                                         action = Query_WhereLessThanOrEqualTo_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThanOrEqualTo_EmptyPathString",
                                         action = Query_WhereLessThanOrEqualTo_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereLessThanOrEqualTo_NullFieldPath",
                                         action = Query_WhereLessThanOrEqualTo_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereNotEqualTo_NullPathString",
                                         action = Query_WhereNotEqualTo_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereNotEqualTo_EmptyPathString",
                                         action = Query_WhereNotEqualTo_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereNotEqualTo_NullFieldPath",
                                         action = Query_WhereNotEqualTo_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereNotIn_NullFieldPath",
                                         action = Query_WhereNotIn_NullFieldPath },
          new InvalidArgumentsTestCase { name = "Query_WhereNotIn_NonNullFieldPath_NullValues",
                                         action = Query_WhereNotIn_NonNullFieldPath_NullValues },
          new InvalidArgumentsTestCase { name = "Query_WhereNotIn_NullPathString",
                                         action = Query_WhereNotIn_NullPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereNotIn_EmptyPathString",
                                         action = Query_WhereNotIn_EmptyPathString },
          new InvalidArgumentsTestCase { name = "Query_WhereNotIn_NonNullPathString_NullValues",
                                         action = Query_WhereNotIn_NonNullPathString_NullValues },
          new InvalidArgumentsTestCase { name = "Transaction_Delete_NullDocumentReference",
                                         action = Transaction_Delete_NullDocumentReference },
          new InvalidArgumentsTestCase {
            name = "Transaction_GetSnapshotAsync_NullDocumentReference",
            action = Transaction_GetSnapshotAsync_NullDocumentReference
          },
          new InvalidArgumentsTestCase { name = "Transaction_Set_NullDocumentReference",
                                         action = Transaction_Set_NullDocumentReference },
          new InvalidArgumentsTestCase { name = "Transaction_Set_NullDocumentData",
                                         action = Transaction_Set_NullDocumentData },
          new InvalidArgumentsTestCase { name = "Transaction_Set_DocumentDataWithEmptyKey",
                                         action = Transaction_Set_DocumentDataWithEmptyKey },
          new InvalidArgumentsTestCase { name = "Transaction_Set_InvalidDocumentDataType",
                                         action = Transaction_Set_InvalidDocumentDataType },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NullDocumentReference_NonNullStringKeyDictionary",
            action = Transaction_Update_NullDocumentReference_NonNullStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_NullStringKeyDictionary",
            action = Transaction_Update_NonNullDocumentReference_NullStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_EmptyStringKeyDictionary",
            action = Transaction_Update_NonNullDocumentReference_EmptyStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey",
            action = Transaction_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NullDocumentReference_NonNullFieldString",
            action = Transaction_Update_NullDocumentReference_NonNullFieldString
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_NullFieldString",
            action = Transaction_Update_NonNullDocumentReference_NullFieldString
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NullDocumentReference_NonNullFieldPathKeyDictionary",
            action = Transaction_Update_NullDocumentReference_NonNullFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_NullFieldPathKeyDictionary",
            action = Transaction_Update_NonNullDocumentReference_NullFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "Transaction_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary",
            action = Transaction_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase { name = "WriteBatch_Delete_NullDocumentReference",
                                         action = WriteBatch_Delete_NullDocumentReference },
          new InvalidArgumentsTestCase { name = "WriteBatch_Set_NullDocumentReference",
                                         action = WriteBatch_Set_NullDocumentReference },
          new InvalidArgumentsTestCase { name = "WriteBatch_Set_NullDocumentData",
                                         action = WriteBatch_Set_NullDocumentData },
          new InvalidArgumentsTestCase { name = "WriteBatch_Set_DocumentDataWithEmptyKey",
                                         action = WriteBatch_Set_DocumentDataWithEmptyKey },
          new InvalidArgumentsTestCase { name = "WriteBatch_Set_InvalidDocumentDataType",
                                         action = WriteBatch_Set_InvalidDocumentDataType },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NullDocumentReference_NonNullStringKeyDictionary",
            action = WriteBatch_Update_NullDocumentReference_NonNullStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_NullStringKeyDictionary",
            action = WriteBatch_Update_NonNullDocumentReference_NullStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_EmptyStringKeyDictionary",
            action = WriteBatch_Update_NonNullDocumentReference_EmptyStringKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey",
            action = WriteBatch_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NullDocumentReference_NonNullFieldString",
            action = WriteBatch_Update_NullDocumentReference_NonNullFieldString
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_NullFieldString",
            action = WriteBatch_Update_NonNullDocumentReference_NullFieldString
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NullDocumentReference_NonNullFieldPathKeyDictionary",
            action = WriteBatch_Update_NullDocumentReference_NonNullFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_NullFieldPathKeyDictionary",
            action = WriteBatch_Update_NonNullDocumentReference_NullFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase {
            name = "WriteBatch_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary",
            action = WriteBatch_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary
          },
          new InvalidArgumentsTestCase { name = "FirebaseFirestore_LoadBundleAsync_NullBundle",
                                         action = FirebaseFirestore_LoadBundleAsync_NullBundle },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_LoadBundleAsync_NonNullBundle_NullHandler",
            action = FirebaseFirestore_LoadBundleAsync_NonNullBundle_NullHandler
          },
          new InvalidArgumentsTestCase {
            name = "FirebaseFirestore_GetNamedQueryAsync_NullQueryName",
            action = FirebaseFirestore_GetNamedQueryAsync_NullQueryName
          },
        };
      }
    }

    private static void CollectionReference_AddAsync_NullDocumentData(UIHandlerAutomated handler) {
      CollectionReference collection = handler.db.Collection("a");
      handler.AssertException(typeof(ArgumentNullException), () => collection.AddAsync(null));
    }

    private static void CollectionReference_AddAsync_DocumentDataWithEmptyKey(
        UIHandlerAutomated handler) {
      CollectionReference collection = handler.db.Collection("a");
      handler.AssertTaskFaults(collection.AddAsync(new Dictionary<string, object> { { "", 42 } }));
    }

    private static void CollectionReference_AddAsync_InvalidDocumentDataType(
        UIHandlerAutomated handler) {
      CollectionReference collection = handler.db.Collection("a");
      handler.AssertException(typeof(ArgumentException), () => collection.AddAsync(42));
    }

    private static void CollectionReference_Document_NullStringPath(UIHandlerAutomated handler) {
      CollectionReference collection = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => collection.Document(null));
    }

    private static void CollectionReference_Document_EmptyStringPath(UIHandlerAutomated handler) {
      CollectionReference collection = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => collection.Document(""));
    }

    private static void CollectionReference_Document_EvenNumberOfPathSegments(
        UIHandlerAutomated handler) {
      CollectionReference collection = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => collection.Document("b/c"));
    }

    private static void DocumentReference_Collection_NullStringPath(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException), () => doc.Collection(null));
    }

    private static void DocumentReference_Collection_EmptyStringPath(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentException), () => doc.Collection(""));
    }

    private static void DocumentReference_Collection_EvenNumberOfPathSegments(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentException), () => doc.Collection("a/b"));
    }

    private static void DocumentReference_Listen_1Arg_NullCallback(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException), () => doc.Listen(null));
    }

    private static void DocumentReference_Listen_2Args_NullCallback(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => doc.Listen(MetadataChanges.Include, null));
    }

    private static void DocumentReference_SetAsync_NullDocumentData(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException), () => doc.SetAsync(null, null));
    }

    private static void DocumentReference_SetAsync_DocumentDataWithEmptyKey(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskFaults(doc.SetAsync(new Dictionary<string, object> { { "", 42 } }, null));
    }

    private static void DocumentReference_SetAsync_InvalidDocumentDataType(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentException), () => doc.SetAsync(42, null));
    }

    private static void DocumentReference_UpdateAsync_NullStringKeyedDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => doc.UpdateAsync((IDictionary<string, object>)null));
    }

    private static void DocumentReference_UpdateAsync_EmptyStringKeyedDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskSucceeds(doc.UpdateAsync(new Dictionary<string, object>()));
    }

    private static void DocumentReference_UpdateAsync_StringKeyedDictionaryWithEmptyKey(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskFaults(doc.UpdateAsync(new Dictionary<string, object> { { "", 42 } }));
    }

    private static void DocumentReference_UpdateAsync_NullStringField(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException), () => doc.UpdateAsync(null, 42));
    }

    private static void DocumentReference_UpdateAsync_EmptyStringField(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskFaults(doc.UpdateAsync("", 42));
    }

    private static void DocumentReference_UpdateAsync_NullFieldPathKeyedDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => doc.UpdateAsync((IDictionary<FieldPath, object>)null));
    }

    private static void DocumentReference_UpdateAsync_EmptyFieldPathKeyedDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskSucceeds(doc.UpdateAsync(new Dictionary<FieldPath, object>()));
    }

    private static void DocumentSnapshot_ContainsField_NullStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => snapshot.ContainsField((string)null));
      });
    }

    private static void DocumentSnapshot_ContainsField_EmptyStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(typeof(ArgumentException), () => snapshot.ContainsField(""));
      });
    }

    private static void DocumentSnapshot_ContainsField_NullFieldPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => snapshot.ContainsField((FieldPath)null));
      });
    }

    private static void DocumentSnapshot_GetValue_NullStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(
            typeof(ArgumentNullException),
            () => snapshot.GetValue<object>((string)null, ServerTimestampBehavior.None));
      });
    }

    private static void DocumentSnapshot_GetValue_EmptyStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(typeof(ArgumentException),
                                () => snapshot.GetValue<object>("", ServerTimestampBehavior.None));
      });
    }

    private static void DocumentSnapshot_GetValue_NullFieldPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        handler.AssertException(
            typeof(ArgumentNullException),
            () => snapshot.GetValue<object>((FieldPath)null, ServerTimestampBehavior.None));
      });
    }

    private static void DocumentSnapshot_TryGetValue_NullStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        object value = null;
        handler.AssertException(
            typeof(ArgumentNullException),
            () => snapshot.TryGetValue((string)null, out value, ServerTimestampBehavior.None));
      });
    }

    private static void DocumentSnapshot_TryGetValue_EmptyStringPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        object value = null;
        handler.AssertException(
            typeof(ArgumentException),
            () => snapshot.TryGetValue("", out value, ServerTimestampBehavior.None));
      });
    }

    private static void DocumentSnapshot_TryGetValue_NullFieldPath(UIHandlerAutomated handler) {
      RunWithTestDocumentSnapshot(handler, snapshot => {
        object value = null;
        handler.AssertException(
            typeof(ArgumentNullException),
            () => snapshot.TryGetValue((FieldPath)null, out value, ServerTimestampBehavior.None));
      });
    }

    private static void FieldPath_Constructor_NullStringArray(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException), () => new FieldPath(null));
    }

    private static void FieldPath_Constructor_StringArrayWithNullElement(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException),
                              () => new FieldPath(new string[] { null }));
    }

    private static void FieldPath_Constructor_EmptyStringArray(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => new FieldPath(new string[0]));
    }

    private static void FieldPath_Constructor_StringArrayWithEmptyString(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => new FieldPath(new string[] { "" }));
    }

    private static void FieldValue_ArrayRemove_NullArray(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException), () => FieldValue.ArrayRemove(null));
    }

    private static void FieldValue_ArrayUnion_NullArray(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException), () => FieldValue.ArrayUnion(null));
    }

    private static void FirebaseFirestore_GetInstance_Null(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => FirebaseFirestore.GetInstance(null));
    }

    private static void FirebaseFirestore_GetInstance_DisposedApp(UIHandlerAutomated handler) {
      FirebaseApp disposedApp =
          FirebaseApp.Create(handler.db.App.Options, "test-getinstance-disposedapp");
      disposedApp.Dispose();
      handler.AssertException(typeof(ArgumentException),
                              () => FirebaseFirestore.GetInstance(disposedApp));
    }

    private static void FirebaseFirestore_Collection_NullStringPath(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException), () => handler.db.Collection(null));
    }

    private static void FirebaseFirestore_Collection_EmptyStringPath(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.Collection(""));
    }

    private static void FirebaseFirestore_Collection_EvenNumberOfPathSegments(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.Collection("a/b"));
    }

    private static void FirebaseFirestore_CollectionGroup_NullCollectionId(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.CollectionGroup(null));
    }

    private static void FirebaseFirestore_CollectionGroup_EmptyCollectionId(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.CollectionGroup(""));
    }

    private static void FirebaseFirestore_CollectionGroup_CollectionIdContainsSlash(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.CollectionGroup("a/b"));
    }

    private static void FirebaseFirestore_Document_NullStringPath(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException), () => handler.db.Document(null));
    }

    private static void FirebaseFirestore_Document_EmptyStringPath(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.Document(""));
    }

    private static void FirebaseFirestore_Document_OddNumberOfPathSegments(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentException), () => handler.db.Document("a/b/c"));
    }

    private static void FirebaseFirestore_ListenForSnapshotsInSync_NullCallback(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.ListenForSnapshotsInSync(null));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_NullCallback(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.RunTransactionAsync(null));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithTypeParameter_NullCallback(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.RunTransactionAsync<object>(null));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_WithOptions_NullCallback(
        UIHandlerAutomated handler) {
      var options = new TransactionOptions();
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.RunTransactionAsync(options, null));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithTypeParameter_WithOptions_NullCallback(
        UIHandlerAutomated handler) {
      var options = new TransactionOptions();
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.RunTransactionAsync<object>(options, null));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithoutTypeParameter_WithOptions_NullOptions(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
          () => handler.db.RunTransactionAsync(null, tx => tx.GetSnapshotAsync(doc)));
    }

    private static void FirebaseFirestore_RunTransactionAsync_WithTypeParameter_WithOptions_NullOptions(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
          () => handler.db.RunTransactionAsync<object>(null, tx => tx.GetSnapshotAsync(doc)
              .ContinueWith(snapshot => new object()))
      );
    }

    private static void FirebaseFirestoreSettings_Host_Null(UIHandlerAutomated handler) {
      FirebaseFirestoreSettings settings = handler.db.Settings;
      handler.AssertException(typeof(ArgumentNullException), () => settings.Host = null);
    }

    private static void FirebaseFirestoreSettings_Host_EmptyString(UIHandlerAutomated handler) {
      FirebaseFirestoreSettings settings = handler.db.Settings;
      handler.AssertException(typeof(ArgumentException), () => settings.Host = "");
    }

    private static void Query_EndAt_NullDocumentSnapshot(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.EndAt((DocumentSnapshot)null));
    }

    private static void Query_EndAt_NullArray(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => query.EndAt((object[])null));
    }

    private static void Query_EndAt_ArrayWithNullElement(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.EndAt(new object[] { null }));
    }

    private static void Query_EndBefore_NullDocumentSnapshot(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.EndBefore((DocumentSnapshot)null));
    }

    private static void Query_EndBefore_NullArray(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => query.EndBefore((object[])null));
    }

    private static void Query_Limit_0(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.Limit(0));
    }

    private static void Query_Limit_Negative1(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.Limit(-1));
    }

    private static void Query_LimitToLast_0(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.LimitToLast(0));
    }

    private static void Query_LimitToLast_Negative1(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.LimitToLast(-1));
    }

    private static void Query_OrderBy_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => query.OrderBy((string)null));
    }

    private static void Query_OrderBy_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.OrderBy(""));
    }

    private static void Query_OrderBy_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => query.OrderBy((FieldPath)null));
    }

    private static void Query_OrderByDescending_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.OrderByDescending((string)null));
    }

    private static void Query_OrderByDescending_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.OrderByDescending(""));
    }

    private static void Query_OrderByDescending_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.OrderByDescending((FieldPath)null));
    }

    private static void Query_StartAfter_NullDocumentSnapshot(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.StartAfter((DocumentSnapshot)null));
    }

    private static void Query_StartAfter_NullArray(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.StartAfter((object[])null));
    }

    private static void Query_StartAt_NullDocumentSnapshot(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.StartAt((DocumentSnapshot)null));
    }

    private static void Query_StartAt_NullArray(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException), () => query.StartAt((object[])null));
    }

    private static void Query_StartAt_ArrayWithNullElement(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException),
                              () => query.StartAt(new object[] { null }));
    }

    private static void Query_WhereArrayContains_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContains((FieldPath)null, ""));
    }

    private static void Query_WhereArrayContains_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContains((string)null, ""));
    }

    private static void Query_WhereArrayContains_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.WhereArrayContains("", 42));
    }

    private static void Query_WhereArrayContainsAny_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { "" };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContainsAny((FieldPath)null, values));
    }

    private static void Query_WhereArrayContainsAny_NonNullFieldPath_NullValues(
        UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContainsAny(fieldPath, null));
    }

    private static void Query_WhereArrayContainsAny_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { "" };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContainsAny((string)null, values));
    }

    private static void Query_WhereArrayContainsAny_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { "" };
      handler.AssertException(typeof(ArgumentException),
                              () => query.WhereArrayContainsAny("", values));
    }

    private static void Query_WhereArrayContainsAny_NonNullPathString_NullValues(
        UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      string pathString = "a/b";
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereArrayContainsAny(pathString, null));
    }

    private static void Query_WhereEqualTo_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereEqualTo((string)null, 42));
    }

    private static void Query_WhereEqualTo_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.WhereEqualTo("", 42));
    }

    private static void Query_WhereEqualTo_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereEqualTo((FieldPath)null, 42));
    }

    private static void Query_WhereGreaterThan_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereGreaterThan((string)null, 42));
    }

    private static void Query_WhereGreaterThan_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.WhereGreaterThan("", 42));
    }

    private static void Query_WhereGreaterThan_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereGreaterThan((FieldPath)null, 42));
    }

    private static void Query_WhereGreaterThanOrEqualTo_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereGreaterThanOrEqualTo((string)null, 42));
    }

    private static void Query_WhereGreaterThanOrEqualTo_EmptyPathString(
        UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException),
                              () => query.WhereGreaterThanOrEqualTo("", 42));
    }

    private static void Query_WhereGreaterThanOrEqualTo_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereGreaterThanOrEqualTo((FieldPath)null, 42));
    }

    private static void Query_WhereIn_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereIn((FieldPath)null, values));
    }

    private static void Query_WhereIn_NonNullFieldPath_NullValues(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      handler.AssertException(typeof(ArgumentNullException), () => query.WhereIn(fieldPath, null));
    }

    private static void Query_WhereIn_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereIn((string)null, values));
    }

    private static void Query_WhereIn_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentException), () => query.WhereIn("", values));
    }

    private static void Query_WhereIn_NonNullPathString_NullValues(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      string fieldPath = "a/b";
      handler.AssertException(typeof(ArgumentNullException), () => query.WhereIn(fieldPath, null));
    }

    private static void Query_WhereLessThan_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereLessThan((string)null, 42));
    }

    private static void Query_WhereLessThan_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.WhereLessThan("", 42));
    }

    private static void Query_WhereLessThan_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereLessThan((FieldPath)null, 42));
    }

    private static void Query_WhereLessThanOrEqualTo_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereLessThanOrEqualTo((string)null, 42));
    }

    private static void Query_WhereLessThanOrEqualTo_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException),
                              () => query.WhereLessThanOrEqualTo("", 42));
    }

    private static void Query_WhereLessThanOrEqualTo_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereLessThanOrEqualTo((FieldPath)null, 42));
    }

    private static void Query_WhereNotEqualTo_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotEqualTo((string)null, 42));
    }

    private static void Query_WhereNotEqualTo_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentException), () => query.WhereNotEqualTo("", 42));
    }

    private static void Query_WhereNotEqualTo_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotEqualTo((FieldPath)null, 42));
    }

    private static void Query_WhereNotIn_NullFieldPath(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotIn((FieldPath)null, values));
    }

    private static void Query_WhereNotIn_NonNullFieldPath_NullValues(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      FieldPath fieldPath = new FieldPath(new string[] { "a", "b" });
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotIn(fieldPath, null));
    }

    private static void Query_WhereNotIn_NullPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotIn((string)null, values));
    }

    private static void Query_WhereNotIn_EmptyPathString(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      List<object> values = new List<object> { 42 };
      handler.AssertException(typeof(ArgumentException), () => query.WhereNotIn("", values));
    }

    private static void Query_WhereNotIn_NonNullPathString_NullValues(UIHandlerAutomated handler) {
      Query query = handler.TestCollection();
      string fieldPath = "a/b";
      handler.AssertException(typeof(ArgumentNullException),
                              () => query.WhereNotIn(fieldPath, null));
    }

    private static void Transaction_Delete_NullDocumentReference(UIHandlerAutomated handler) {
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException), () => transaction.Delete(null));
      });
    }

    private static void Transaction_GetSnapshotAsync_NullDocumentReference(
        UIHandlerAutomated handler) {
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.GetSnapshotAsync(null));
      });
    }

    private static void Transaction_Set_NullDocumentReference(UIHandlerAutomated handler) {
      object documentData = handler.TestData();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Set(null, documentData, null));
      });
    }

    private static void Transaction_Set_NullDocumentData(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Set(doc, null, null));
      });
    }

    private static void Transaction_Set_DocumentDataWithEmptyKey(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(
            typeof(ArgumentException),
            () => transaction.Set(doc, new Dictionary<string, object> { { "", 42 } }, null));
      });
    }

    private static void Transaction_Set_InvalidDocumentDataType(UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentException), () => transaction.Set(doc, 42, null));
      });
    }

    private static void Transaction_Update_NullDocumentReference_NonNullStringKeyDictionary(
        UIHandlerAutomated handler) {
      RunWithTransaction(handler, transaction => {
        handler.AssertException(
            typeof(ArgumentNullException),
            () => transaction.Update(null, new Dictionary<string, object> { { "key", 42 } }));
      });
    }

    private static void Transaction_Update_NonNullDocumentReference_NullStringKeyDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Update(doc, (IDictionary<string, object>)null));
      });
    }

    private static void Transaction_Update_NonNullDocumentReference_EmptyStringKeyDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskSucceeds(handler.db.RunTransactionAsync(transaction => {
        return transaction.GetSnapshotAsync(doc).ContinueWith(
            snapshot => { transaction.Update(doc, new Dictionary<string, object>()); });
      }));
    }

    private static void Transaction_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(
            typeof(ArgumentException),
            () => transaction.Update(doc, new Dictionary<string, object> { { "", 42 } }));
      });
    }

    private static void Transaction_Update_NullDocumentReference_NonNullFieldString(
        UIHandlerAutomated handler) {
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Update(null, "fieldName", 42));
      });
    }

    private static void Transaction_Update_NonNullDocumentReference_NullFieldString(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Update(doc, (string)null, 42));
      });
    }

    private static void Transaction_Update_NullDocumentReference_NonNullFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      var nonNullFieldPathKeyDictionary =
          new Dictionary<FieldPath, object> { { new FieldPath(new string[] { "a", "b" }), 42 } };
      RunWithTransaction(handler, transaction => {
        handler.AssertException(typeof(ArgumentNullException),
                                () => transaction.Update(null, nonNullFieldPathKeyDictionary));
      });
    }

    private static void Transaction_Update_NonNullDocumentReference_NullFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      RunWithTransaction(handler, transaction => {
        handler.AssertException(
            typeof(ArgumentNullException),
            () => transaction.Update(doc, (IDictionary<FieldPath, object>)null));
      });
    }

    private static void Transaction_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      handler.AssertTaskSucceeds(handler.db.RunTransactionAsync(transaction => {
        return transaction.GetSnapshotAsync(doc).ContinueWith(
            snapshot => { transaction.Update(doc, new Dictionary<FieldPath, object>()); });
      }));
    }

    private static void WriteBatch_Delete_NullDocumentReference(UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      handler.AssertException(typeof(ArgumentNullException), () => writeBatch.Delete(null));
    }

    private static void WriteBatch_Set_NullDocumentReference(UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      var nonNullDocumentData = handler.TestData();
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Set(null, nonNullDocumentData, null));
    }

    private static void WriteBatch_Set_NullDocumentData(UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException), () => writeBatch.Set(doc, null, null));
    }

    private static void WriteBatch_Set_DocumentDataWithEmptyKey(UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(
          typeof(ArgumentException),
          () => writeBatch.Set(doc, new Dictionary<string, object> { { "", 42 } }, null));
    }

    private static void WriteBatch_Set_InvalidDocumentDataType(UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentException), () => writeBatch.Set(doc, 42, null));
    }

    private static void WriteBatch_Update_NullDocumentReference_NonNullStringKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      handler.AssertException(
          typeof(ArgumentNullException),
          () => writeBatch.Update(null, new Dictionary<string, object> { { "key", 42 } }));
    }

    private static void WriteBatch_Update_NonNullDocumentReference_NullStringKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Update(doc, (IDictionary<string, object>)null));
    }

    private static void WriteBatch_Update_NonNullDocumentReference_EmptyStringKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      writeBatch.Update(doc, new Dictionary<string, object>());
      handler.AssertTaskSucceeds(writeBatch.CommitAsync());
    }

    private static void WriteBatch_Update_NonNullDocumentReference_StringKeyDictionaryWithEmptyKey(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(
          typeof(ArgumentException),
          () => writeBatch.Update(doc, new Dictionary<string, object> { { "", 42 } }));
    }

    private static void WriteBatch_Update_NullDocumentReference_NonNullFieldString(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Update(null, "fieldName", 42));
    }

    private static void WriteBatch_Update_NonNullDocumentReference_NullFieldString(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Update(doc, (string)null, 42));
    }

    private static void WriteBatch_Update_NullDocumentReference_NonNullFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      var nonNullFieldPathKeyDictionary =
          new Dictionary<FieldPath, object> { { new FieldPath(new string[] { "a", "b" }), 42 } };
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Update(null, nonNullFieldPathKeyDictionary));
    }

    private static void WriteBatch_Update_NonNullDocumentReference_NullFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertException(typeof(ArgumentNullException),
                              () => writeBatch.Update(doc, (IDictionary<FieldPath, object>)null));
    }

    private static void WriteBatch_Update_NonNullDocumentReference_EmptyFieldPathKeyDictionary(
        UIHandlerAutomated handler) {
      WriteBatch writeBatch = handler.db.StartBatch();
      DocumentReference doc = handler.TestDocument();
      handler.AssertTaskSucceeds(doc.SetAsync(handler.TestData(), null));
      writeBatch.Update(doc, new Dictionary<FieldPath, object>());
      handler.AssertTaskSucceeds(writeBatch.CommitAsync());
    }

    private static void FirebaseFirestore_LoadBundleAsync_NullBundle(UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.LoadBundleAsync(null as string));
      handler.AssertException(
          typeof(ArgumentNullException),
          () => handler.db.LoadBundleAsync(null as string, (sender, progress) => {}));
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.LoadBundleAsync(null as byte[]));
      handler.AssertException(
          typeof(ArgumentNullException),
          () => handler.db.LoadBundleAsync(null as byte[], (sender, progress) => {}));
    }

    private static void FirebaseFirestore_LoadBundleAsync_NonNullBundle_NullHandler(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.LoadBundleAsync("", null));
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.LoadBundleAsync(new byte[] {}, null));
    }

    private static void FirebaseFirestore_GetNamedQueryAsync_NullQueryName(
        UIHandlerAutomated handler) {
      handler.AssertException(typeof(ArgumentNullException),
                              () => handler.db.GetNamedQueryAsync(null));
    }

    /**
     * Starts a transaction and invokes the given action with the `Transaction` object synchronously
     * in the calling thread. This enables the caller to use standard asserts since any exceptions
     * they throw will be thrown in the calling thread's context and bubble up to the test runner.
     */
    private static void RunWithTransaction(UIHandlerAutomated handler, Action<Transaction> action) {
      var taskCompletionSource = new TaskCompletionSource<object>();
      Transaction capturedTransaction = null;

      Task transactionTask = handler.db.RunTransactionAsync(lambdaTransaction => {
        Interlocked.Exchange(ref capturedTransaction, lambdaTransaction);
        return taskCompletionSource.Task;
      });

      try {
        Transaction transaction = null;
        while (true) {
          transaction = Interlocked.Exchange(ref capturedTransaction, null);
          if (transaction != null) {
            break;
          }
        }
        action(transaction);
      } finally { taskCompletionSource.SetResult(null); }

      handler.AssertTaskSucceeds(transactionTask);
    }

    /**
     * Creates a `DocumentSnapshot` then invokes the given action with it synchronously in the
     * calling thread. This enables the caller to use standard asserts since any exceptions
     * they throw will be thrown in the calling thread's context and bubble up to the test runner.
     */
    private static void RunWithTestDocumentSnapshot(UIHandlerAutomated handler,
                                                    Action<DocumentSnapshot> action) {
      DocumentReference doc = handler.TestDocument();
      doc.SetAsync(handler.TestData());
      Task<DocumentSnapshot> task = doc.GetSnapshotAsync(Source.Cache);
      handler.AssertTaskSucceeds(task);
      DocumentSnapshot snapshot = task.Result;
      action(snapshot);
    }
  }
}
