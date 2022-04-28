#include <cstring>
#include <iostream>
#include <string>
#include <thread>

#include "firebase/firestore.h"

extern "C" {
void * Firebase_Firestore_CSharp_FirestoreProxy_GetInstance__SWIG_1(void * jarg1);
void * Firebase_Firestore_CSharp_FirestoreProxy_Collection__SWIG_0(void * jarg1, char * jarg2);
void * Firebase_Firestore_CSharp_CollectionReferenceProxy_Document__SWIG_0(void * jarg1);
unsigned int Firebase_Firestore_CSharp_DocumentReferenceProxy_is_valid(void * jarg1);
void * Firebase_Firestore_CSharp_new_SetOptionsProxy();
void * Firebase_Firestore_CSharp_FieldValueProxy_String(char * jarg1);
void * Firebase_Firestore_CSharp_new_FieldToValueMap();
void Firebase_Firestore_CSharp_FieldToValueMap_Insert(void * jarg1, char * jarg2, void * jarg3);
void * Firebase_Firestore_CSharp_ConvertMapToFieldValue(void * jarg1);
unsigned int Firebase_Firestore_CSharp_FieldValueProxy_is_map(void * jarg1);
void * Firebase_Firestore_CSharp_DocumentReferenceSet(void * jarg1, void * jarg2, void * jarg3);
void Firebase_Firestore_CSharp_delete_FieldToValueMap(void * jarg1);
}  // extern "C"

namespace {

void Log(const std::string& s) {
  std::cout << s << std::endl;
}

void Log(const std::string& s, bool b) {
  std::cout << s << (b ? "true" : "false") << std::endl;
}

}  // namespace

int main(int argc, char** argv) {
  Log("App::Create()");
  ::firebase::App* app = ::firebase::App::Create();
  if (!app) {
    Log("ERROR: App::Create() returned null");
    return 1;
  }

  ::firebase::firestore::Firestore::set_log_level(::firebase::LogLevel::kLogLevelDebug);

  Log("Firebase_Firestore_CSharp_FirestoreProxy_GetInstance__SWIG_1()");
  ::firebase::firestore::Firestore* db = static_cast<::firebase::firestore::Firestore*>(
    Firebase_Firestore_CSharp_FirestoreProxy_GetInstance__SWIG_1(app));
  if (!db) {
    Log("ERROR: Firebase_Firestore_CSharp_FirestoreProxy_GetInstance__SWIG_1() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_FirestoreProxy_Collection__SWIG_0()");
  const char* collection_name_const = "MyCoolCollection";
  char collection_name[std::strlen(collection_name_const) + 1] = {};
  std::strncpy(collection_name, collection_name_const, std::strlen(collection_name_const));
  ::firebase::firestore::CollectionReference* collection = static_cast<::firebase::firestore::CollectionReference*>(
    Firebase_Firestore_CSharp_FirestoreProxy_Collection__SWIG_0(db, collection_name));
  if (!collection) {
    Log("ERROR: Firebase_Firestore_CSharp_FirestoreProxy_Collection__SWIG_0() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_CollectionReferenceProxy_Document__SWIG_0()");
  ::firebase::firestore::DocumentReference* document = static_cast<::firebase::firestore::DocumentReference*>(
    Firebase_Firestore_CSharp_CollectionReferenceProxy_Document__SWIG_0(collection));
  if (!document) {
    Log("ERROR: Firebase_Firestore_CSharp_CollectionReferenceProxy_Document__SWIG_0() returned null");
    return 1;
  }

  bool document_is_valid = Firebase_Firestore_CSharp_DocumentReferenceProxy_is_valid(document);
  Log("Firebase_Firestore_CSharp_DocumentReferenceProxy_is_valid returned: ", document_is_valid);

  Log("Firebase_Firestore_CSharp_new_SetOptionsProxy()");
  ::firebase::firestore::SetOptions* set_options = static_cast<::firebase::firestore::SetOptions*>(
    Firebase_Firestore_CSharp_new_SetOptionsProxy());
  if (!set_options) {
    Log("ERROR: Firebase_Firestore_CSharp_new_SetOptionsProxy() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_FieldValueProxy_String()");
  const char* field_value_str_const = "MyCoolFieldValue";
  char field_value_str[std::strlen(field_value_str_const) + 1] = {};
  std::strncpy(field_value_str, field_value_str_const, std::strlen(field_value_str_const));
  ::firebase::firestore::FieldValue* field_value = static_cast<::firebase::firestore::FieldValue*>(
    Firebase_Firestore_CSharp_FieldValueProxy_String(field_value_str));
  if (!field_value) {
    Log("ERROR: Firebase_Firestore_CSharp_FieldValueProxy_String() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_new_FieldToValueMap()");
  void* field_value_map = Firebase_Firestore_CSharp_new_FieldToValueMap();
  if (!field_value_map) {
    Log("ERROR: Firebase_Firestore_CSharp_new_FieldToValueMap() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_FieldToValueMap_Insert()");
  const char* field_name_const = "TestFieldName";
  char field_name[std::strlen(field_name_const) + 1] = {};
  std::strncpy(field_name, field_name_const, std::strlen(field_name_const));
  Firebase_Firestore_CSharp_FieldToValueMap_Insert(field_value_map, field_name, field_value);

  Log("Firebase_Firestore_CSharp_ConvertMapToFieldValue()");
  ::firebase::firestore::FieldValue* map_value = static_cast<::firebase::firestore::FieldValue*>(
    Firebase_Firestore_CSharp_ConvertMapToFieldValue(field_value_map));
  if (!map_value) {
    Log("ERROR: Firebase_Firestore_CSharp_ConvertMapToFieldValue() returned null");
    return 1;
  }

  Log("Firebase_Firestore_CSharp_FieldValueProxy_is_map()");
  bool is_map = Firebase_Firestore_CSharp_FieldValueProxy_is_map(map_value);
  Log("Firebase_Firestore_CSharp_FieldValueProxy_is_map() returned ", is_map);

  Log("Firebase_Firestore_CSharp_DocumentReferenceSet()");
  ::firebase::Future<void>* set_future = static_cast<::firebase::Future<void>*>(
    Firebase_Firestore_CSharp_DocumentReferenceSet(document, map_value, set_options));
  if (!set_future) {
    Log("ERROR: Firebase_Firestore_CSharp_DocumentReferenceSet() returned null");
    return 1;
  }

  Log("Waiting for set operation to complete");
  while (set_future->status() == ::firebase::FutureStatus::kFutureStatusPending) {
    std::this_thread::yield();
  }
  Log("Set operation has completed");

  Log("delete set_future");
  delete set_future;

  Log("delete map_value");
  delete map_value;

  Log("delete field_value_map");
  Firebase_Firestore_CSharp_delete_FieldToValueMap(field_value_map);

  Log("delete field_value");
  delete field_value;

  Log("delete set_options");
  delete set_options;

  Log("delete document");
  delete document;

  Log("delete collection");
  delete collection;

  Log("delete db");
  delete db;

  Log("delete app");
  delete app;

  Log("Success!");
  return 0;
}
