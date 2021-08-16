/*
 * Copyright 2019 Google LLC
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

/*
 * A collection of code snippets for the Cloud Firestore Unity plugin. These snippets were modelled
 * after the reference docs/snippets, which can be found here:
 * https://firebase.google.com/docs/firestore.
 *
 * Note that not all of the Firestore API has been implemented yet, so some snippets are
 * incomplete/missing.
 */

using Firebase.Extensions;
using Firebase.Firestore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class Snippets {
  // https://firebase.google.com/docs/firestore/quickstart#initialize
  public static void Initialize() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;
  }

  // https://firebase.google.com/docs/firestore/quickstart#add_data
  public static void AddData1() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference docRef = db.Collection("users").Document("alovelace");
    Dictionary<string, object> user = new Dictionary<string, object>
    {
            { "First", "Ada" },
            { "Last", "Lovelace" },
            { "Born", 1815 },
        };
    docRef.SetAsync(user).ContinueWithOnMainThread(task => {
      Debug.Log("Added data to the alovelace document in the users collection.");
    });
  }

  public static void AddData2() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference docRef = db.Collection("users").Document("aturing");
    Dictionary<string, object> user = new Dictionary<string, object>
    {
            { "First", "Alan" },
            { "Middle", "Mathison" },
            { "Last", "Turing" },
            { "Born", 1912 }
        };
    docRef.SetAsync(user).ContinueWithOnMainThread(task => {
      Debug.Log("Added data to the aturing document in the users collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/quickstart#read_data
  public static void ReadData() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    CollectionReference usersRef = db.Collection("users");
    usersRef.GetSnapshotAsync().ContinueWithOnMainThread(task => {
      QuerySnapshot snapshot = task.Result;
      foreach (DocumentSnapshot document in snapshot.Documents) {
        Debug.Log(String.Format("User: {0}", document.Id));
        Dictionary<string, object> documentDictionary = document.ToDictionary();
        Debug.Log(String.Format("First: {0}", documentDictionary["First"]));
        if (documentDictionary.ContainsKey("Middle")) {
          Debug.Log(String.Format("Middle: {0}", documentDictionary["Middle"]));
        }
        Debug.Log(String.Format("Last: {0}", documentDictionary["Last"]));
        Debug.Log(String.Format("Born: {0}", documentDictionary["Born"]));
      }
      Debug.Log("Read all data from the users collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#set_a_document
  public static void AddDocAsMap() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference docRef = db.Collection("cities").Document("LA");
    Dictionary<string, object> city = new Dictionary<string, object>
    {
            { "Name", "Los Angeles" },
            { "State", "CA" },
            { "Country", "USA" }
        };
    docRef.SetAsync(city).ContinueWithOnMainThread(task => {
      Debug.Log("Added data to the LA document in the cities collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#data_types
  public static void AddDocDataTypes() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference docRef = db.Collection("data").Document("one");
    Dictionary<string, object> docData = new Dictionary<string, object>
    {
            { "stringExample", "Hello World" },
            { "booleanExample", false },
            { "numberExample", 3.14159265 },
            { "nullExample", null },
            { "arrayExample", new List<object>() { 5, true, "Hello" } },
            { "objectExample", new Dictionary<string, object>
                {
                    { "a", 5 },
                    { "b", true },
                }
            },
        };

    docRef.SetAsync(docData).ContinueWithOnMainThread(task => {
      Debug.Log(
              "Set multiple data-type data for the one document in the data collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#add_a_document
  public static void SetRequiresId() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    Dictionary<string, object> city = new Dictionary<string, object>
    {
            { "Name", "Phuket" },
            { "Country", "Thailand" }
        };
    db.Collection("cities").Document("new-city-id").SetAsync(city)
        .ContinueWithOnMainThread(task => {
          Debug.Log("Added document with ID: new-city-id.");
        });
  }

  public static void AddDocDataWithAutoId() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    Dictionary<string, object> city = new Dictionary<string, object>
    {
            { "Name", "Tokyo" },
            { "Country", "Japan" }
        };
    db.Collection("cities").AddAsync(city).ContinueWithOnMainThread(task => {
      DocumentReference addedDocRef = task.Result;
      Debug.Log(String.Format("Added document with ID: {0}.", addedDocRef.Id));
    });
  }

  public static void AddDocDataAfterAutoId() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    Dictionary<string, object> city = new Dictionary<string, object>
    {
            { "Name", "Moscow" },
            { "Country", "Russia" }
        };
    DocumentReference addedDocRef = db.Collection("cities").Document();
    Debug.Log(String.Format("Added document with ID: {0}.", addedDocRef.Id));
    addedDocRef.SetAsync(city).ContinueWithOnMainThread(task => {
      Debug.Log(String.Format(
            "Added data to the {0} document in the cities collection.", addedDocRef.Id));
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#update-data
  public static void UpdateDoc() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference cityRef = db.Collection("cities").Document("new-city-id");
    Dictionary<string, object> updates = new Dictionary<string, object>
    {
            { "Capital", false }
        };
    // You can also update a single field with: cityRef.UpdateAsync("Capital", false);
    cityRef.UpdateAsync(updates).ContinueWithOnMainThread(task => {
      Debug.Log(
          "Updated the Capital field of the new-city-id document in the cities collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#server_timestamp
  public static void UpdateServerTimestamp() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference cityRef = db.Collection("cities").Document("new-city-id");
    cityRef.UpdateAsync("Timestamp", FieldValue.ServerTimestamp)
        .ContinueWithOnMainThread(task => {
          Debug.Log(
                  "Updated the Timestamp field of the new-city-id document in the cities "
                  + "collection.");
        });
  }

  // https://firebase.google.com/docs/firestore/manage-data/add-data#update_fields_in_nested_objects
  public static void UpdateNestedFields() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    DocumentReference frankDocRef = db.Collection("users").Document("frank");
    Dictionary<string, object> initialData = new Dictionary<string, object>
    {
            { "Name", "Frank" },
            { "Age", 12 }
        };

    Dictionary<string, object> favorites = new Dictionary<string, object>
    {
            { "Food", "Pizza" },
            { "Color", "Blue" },
            { "Subject", "Recess" },
        };
    initialData.Add("Favorites", favorites);
    frankDocRef.SetAsync(initialData).ContinueWithOnMainThread(task => {

      // Update age and favorite color
      Dictionary<string, object> updates = new Dictionary<string, object>
      {
                { "Age", 13 },
                { "Favorites.Color", "Red" },
            };

      // Asynchronously update the document
      return frankDocRef.UpdateAsync(updates);
    }).ContinueWithOnMainThread(task => {
      Debug.Log(
          "Updated the age and favorite color fields of the Frank document in "
          + "the users collection.");
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/delete-data#delete_documents
  public static void DeleteDoc() {
    EnsureExampleDataPresent().ContinueWithOnMainThread(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      DocumentReference cityRef = db.Collection("cities").Document("DC");
      cityRef.DeleteAsync();
    });
  }

  // https://firebase.google.com/docs/firestore/manage-data/delete-data#fields
  public static void DeleteField() {
    EnsureExampleDataPresent().ContinueWithOnMainThread(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      DocumentReference cityRef = db.Collection("cities").Document("BJ");
      Dictionary<string, object> updates = new Dictionary<string, object>
      {
                { "Capital", FieldValue.Delete }
            };

      cityRef.UpdateAsync(updates);
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/get-data#example_data
  private static Task EnsureExampleDataPresent() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    CollectionReference citiesRef = db.Collection("cities");
    return citiesRef.Document("SF").SetAsync(new Dictionary<string, object>(){
            { "Name", "San Francisco" },
            { "State", "CA" },
            { "Country", "USA" },
            { "Capital", false },
            { "Population", 860000 }
        }).ContinueWithOnMainThread(task =>
            citiesRef.Document("LA").SetAsync(new Dictionary<string, object>(){
                { "Name", "Los Angeles" },
                { "State", "CA" },
                { "Country", "USA" },
                { "Capital", false },
                { "Population", 3900000 }
            })
    ).ContinueWithOnMainThread(task =>
        citiesRef.Document("DC").SetAsync(new Dictionary<string, object>(){
                { "Name", "Washington D.C." },
                { "State", null },
                { "Country", "USA" },
                { "Capital", true },
                { "Population", 680000 }
        })
    ).ContinueWithOnMainThread(task =>
        citiesRef.Document("TOK").SetAsync(new Dictionary<string, object>(){
                { "Name", "Tokyo" },
                { "State", null },
                { "Country", "Japan" },
                { "Capital", true },
                { "Population", 9000000 }
        })
    ).ContinueWithOnMainThread(task =>
        citiesRef.Document("BJ").SetAsync(new Dictionary<string, object>(){
                { "Name", "Beijing" },
                { "State", null },
                { "Country", "China" },
                { "Capital", true },
                { "Population", 21500000 }
        })
    );
  }

  // https://firebase.google.com/docs/firestore/query-data/get-data#get_a_document
  public static void GetDocument() {
    EnsureExampleDataPresent().ContinueWithOnMainThread<Task<DocumentSnapshot>>(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      DocumentReference docRef = db.Collection("cities").Document("SF");
      return docRef.GetSnapshotAsync();
    }).Unwrap().ContinueWithOnMainThread(task => {
      DocumentSnapshot snapshot = task.Result;
      if (snapshot.Exists) {
        Debug.Log(String.Format("Document data for {0} document:", snapshot.Id));
        Dictionary<string, object> city = snapshot.ToDictionary();
        foreach (KeyValuePair<string, object> pair in city) {
          Debug.Log(String.Format("{0}: {1}", pair.Key, pair.Value));
        }
      } else {
        Debug.Log(String.Format("Document {0} does not exist!", snapshot.Id));
      }
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/get-data#get_multiple_documents_from_a_collection
  public static void GetMultipleDocs() {
    EnsureExampleDataPresent().ContinueWithOnMainThread<Task<QuerySnapshot>>(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      Query capitalQuery = db.Collection("cities").WhereEqualTo("Capital", true);
      return capitalQuery.GetSnapshotAsync();
    }).Unwrap().ContinueWithOnMainThread(task => {
      QuerySnapshot capitalQuerySnapshot = task.Result;
      foreach (DocumentSnapshot documentSnapshot in capitalQuerySnapshot.Documents) {
        Debug.Log(String.Format("Document data for {0} document:", documentSnapshot.Id));
        Dictionary<string, object> city = documentSnapshot.ToDictionary();
        foreach (KeyValuePair<string, object> pair in city) {
          Debug.Log(String.Format("{0}: {1}", pair.Key, pair.Value));
        }

        // Newline to separate entries
        Debug.Log("");
      }
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/get-data#get_all_documents_in_a_collection
  public static void GetAllDocs() {
    EnsureExampleDataPresent().ContinueWithOnMainThread<Task<QuerySnapshot>>(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      Query allCitiesQuery = db.Collection("cities");
      return allCitiesQuery.GetSnapshotAsync();
    }).Unwrap().ContinueWithOnMainThread(task => {
      QuerySnapshot allCitiesQuerySnapshot = task.Result;
      foreach (DocumentSnapshot documentSnapshot in allCitiesQuerySnapshot.Documents) {
        Debug.Log(String.Format("Document data for {0} document:", documentSnapshot.Id));
        Dictionary<string, object> city = documentSnapshot.ToDictionary();
        foreach (KeyValuePair<string, object> pair in city) {
          Debug.Log(String.Format("{0}: {1}", pair.Key, pair.Value));
        }

        // Newline to separate entries
        Debug.Log("");
      }
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/listen
  public static void ListenDocument() {
    EnsureExampleDataPresent().ContinueWithOnMainThread(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      DocumentReference docRef = db.Collection("cities").Document("SF");
      ListenerRegistration listener = docRef.Listen(snapshot => {
        Debug.Log("Callback received document snapshot.");
        Debug.Log(String.Format("Document data for {0} document:", snapshot.Id));
        Dictionary<string, object> city = snapshot.ToDictionary();
        foreach (KeyValuePair<string, object> pair in city) {
          Debug.Log(String.Format("{0}: {1}", pair.Key, pair.Value));
        }
      });
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/listen#listen_to_multiple_documents_in_a_collection
  public static void ListenMultiple() {
    EnsureExampleDataPresent().ContinueWithOnMainThread(task => {
      FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

      CollectionReference citiesRef = db.Collection("cities");
      Query query = db.Collection("cities").WhereEqualTo("State", "CA");

      ListenerRegistration listener = query.Listen(snapshot => {
        Debug.Log("Callback received query snapshot.");
        Debug.Log("Current cities in California:");
        foreach (DocumentSnapshot documentSnapshot in snapshot.Documents) {
          Debug.Log(documentSnapshot.Id);
        }
      });
    });
  }

  // https://firebase.google.com/docs/firestore/query-data/listen#detach_a_listener
  public static void DetachListener() {
    FirebaseFirestore db = FirebaseFirestore.DefaultInstance;

    CollectionReference citiesRef = db.Collection("cities");
    Query query = db.Collection("cities").WhereEqualTo("State", "CA");

    ListenerRegistration listener = query.Listen(snapshot => { });

    // Detach the listener
    listener.Stop();
  }
}
