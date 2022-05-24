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

using System.Collections;
using System.Collections.Generic;

using Firebase.Database.Internal;
using Google.MiniJSON;

namespace Firebase.Database {
  /// <summary>
  ///   A DataSnapshot instance contains data from a Firebase Database location.
  /// </summary>
  /// <remarks>
  ///   A DataSnapshot instance contains data from a Firebase Database location.
  ///   Any time you read <cref>FirebaseDatabase</cref>
  ///   data, you receive the data as a DataSnapshot.
  ///   <br /><br />
  ///   DataSnapshots are passed to events
  ///   <see>
  ///     <cref>Query.ValueChanged</cref>
  ///   </see>
  ///   ,
  ///   <see>
  ///     <cref>Query.ChildChanged</cref>
  ///   </see>
  ///   ,
  ///   <see>
  ///     <cref>Query.ChildMoved</cref>
  ///   </see>
  ///   ,
  ///   <see>
  ///     <cref>Query.ChildRemoved</cref>
  ///   </see>
  ///   , or
  ///   <see>
  ///     <cref>Query.ChildAdded</cref>
  ///   </see>
  ///   .
  ///   <br /><br />
  ///   They are efficiently-generated immutable copies of the data at a
  ///   FirebaseDatabase location.
  ///   They can't be modified and will never change. To modify data at a location,
  ///   use a
  ///   <see cref="DatabaseReference">DatabaseReference</see>
  ///   reference
  ///   (e.g. with
  ///   <see>
  ///     <cref>DatabaseReference.SetValueAsync(object)</cref>
  ///   </see>).
  /// </remarks>
  public sealed class DataSnapshot {
    private readonly InternalDataSnapshot internalSnapshot;
    // We only keeps a reference to the database to ensure that it's kept alive,
    // as the underlying C++ code needs that.
    private readonly FirebaseDatabase database;
    // If the DataSnapshot is created via Child, we want to hold onto a reference of the original
    // DataSnapshot that triggered it, to prevent the underlying C++ snapshot from being deleted.
    private readonly DataSnapshot parentSnapshot;
    // If the DataSnapshot is created via Children, we want to hold onto a reference to the List
    // of Children that this originated from, to prevent the underlying C++ list from being deleted.
    private readonly DataSnapshotList parentList;

    private DataSnapshot(InternalDataSnapshot internalSnapshot, FirebaseDatabase database,
                         DataSnapshot parentSnapshot, DataSnapshotList parentList) {
      this.internalSnapshot = internalSnapshot;
      this.database = database;
      this.parentSnapshot = parentSnapshot;
      this.parentList = parentList;
    }

    internal static DataSnapshot CreateSnapshot(
        InternalDataSnapshot internalSnapshot, FirebaseDatabase database) {
      return new DataSnapshot(internalSnapshot, database, null, null);
    }

    private static DataSnapshot CreateSnapshot(
        InternalDataSnapshot internalSnapshot, FirebaseDatabase database, DataSnapshot parent) {
      return new DataSnapshot(internalSnapshot, database, parent, null);
    }

    private static DataSnapshot CreateSnapshot(
        InternalDataSnapshot internalSnapshot, FirebaseDatabase database, DataSnapshotList list) {
      return new DataSnapshot(internalSnapshot, database, null, list);
    }

    /// <summary>Indicates whether this snapshot has any children</summary>
    /// <returns>True if the snapshot has any children, otherwise false</returns>
    public bool HasChildren {
      get { return internalSnapshot.has_children(); }
    }

    /// <summary>Returns true if the snapshot contains a non-null value.</summary>
    /// <returns>True if the snapshot contains a non-null value, otherwise false</returns>
    public bool Exists {
      get { return internalSnapshot.exists(); }
    }

    /// <summary>Value returns the data contained in this snapshot as native types.</summary>
    /// <remarks>
    ///   Value returns the data contained in this snapshot as native types.
    ///   The possible types returned are:
    ///   - bool
    ///   - string
    ///   - long
    ///   - double
    ///   - IDictionary{string, object}
    ///   - List{object}
    ///   This list is recursive; the possible types for
    ///   <see cref="object" />
    ///   in the above list is given by the same list. These types correspond to the
    ///   types available in JSON.
    /// </remarks>
    /// <returns>The data contained in this snapshot as native types</returns>
    public object Value {
      get { return GetValue(false); }
    }

    /// The number of immediate children in the this snapshot.
    public long ChildrenCount {
      get { return internalSnapshot.children_count(); }
    }

    /// <summary>Used to obtain a reference to the source location for this Snapshot.</summary>
    /// <returns>
    ///   A DatabaseReference corresponding to the location that this snapshot came from
    /// </returns>
    public DatabaseReference Reference {
      get { return new DatabaseReference(internalSnapshot.GetReference(), database); }
    }

    /// The key name for the source location of this snapshot.
    public string Key {
      get { return internalSnapshot.key_string(); }
    }

    /// This is a simple wrapper around the enumerator returned by the internal
    /// SWIG class.
    private sealed class DataSnapshotEnumerator : IEnumerator<DataSnapshot> {
      private IEnumerator<InternalDataSnapshot> internalEnumerator;
      private FirebaseDatabase database;
      private DataSnapshotList parentList;
      public DataSnapshotEnumerator(
          IEnumerator<InternalDataSnapshot> internalEnumerator, FirebaseDatabase database,
          DataSnapshotList parentList) {
        this.internalEnumerator = internalEnumerator;
        this.database = database;
        this.parentList = parentList;
      }
      public DataSnapshot Current {
        get { return CreateSnapshot(internalEnumerator.Current, database, parentList); }
      }
      object IEnumerator.Current {
        get { return CreateSnapshot(internalEnumerator.Current, database, parentList); }
      }
      public bool MoveNext() {
        return internalEnumerator.MoveNext();
      }
      public void Reset() {
        internalEnumerator.Reset();
      }
      public void Dispose() {
        internalEnumerator.Dispose();
      }
    }

    /// This is a simple wrapper around the children list returned by the
    /// internal SWIG class.
    private sealed class DataSnapshotList : IEnumerable<DataSnapshot> {
      private IEnumerable<InternalDataSnapshot> internalList;
      private FirebaseDatabase database;
      public DataSnapshotList(
          IEnumerable<InternalDataSnapshot> internalList, FirebaseDatabase database) {
        this.internalList = internalList;
        this.database = database;
      }
      public IEnumerator<DataSnapshot> GetEnumerator() {
        return new DataSnapshotEnumerator(internalList.GetEnumerator(), database, this);
      }
      IEnumerator IEnumerable.GetEnumerator() {
        return new DataSnapshotEnumerator(internalList.GetEnumerator(), database, this);
      }
    }

    /// <summary>Gives access to all of the immediate children of this Snapshot.</summary>
    /// <remarks>
    ///   Gives access to all of the immediate children of this Snapshot.
    ///   Can be used in native for loops:
    /// </remarks>
    /// <returns>The immediate children of this snapshot</returns>
    public IEnumerable<DataSnapshot> Children {
      get { return new DataSnapshotList(internalSnapshot.children(), database); }
    }

    /// <summary>
    ///   Returns the priority of the data contained in this snapshot as a native type.
    /// </summary>
    /// <remarks>
    ///   Returns the priority of the data contained in this snapshot as a native type.
    ///   Possible return types:
    ///   - double
    ///   - string
    ///   Note that null is also allowed.
    /// </remarks>
    /// <returns>the priority of the data contained in this snapshot as a native type</returns>
    public object Priority {
      get { return internalSnapshot.priority().ToObject(VariantExtension.KeyOptions.UseStringKeys); }
    }

    /// <summary>Get a DataSnapshot for the location at the specified relative path.</summary>
    /// <remarks>
    ///   Get a DataSnapshot for the location at the specified relative path.
    ///   The relative path can either be a simple child key (e.g. 'fred')
    ///   or a deeper slash-separated path (e.g. 'fred/name/first'). If the child
    ///   location has no data, an empty DataSnapshot is returned.
    /// </remarks>
    /// <param name="path">A relative path to the location of child data</param>
    /// <returns>The DataSnapshot for the child location</returns>
    public DataSnapshot Child(string path) {
      // Do not use CreateSnapshot here, because we do return an empty DataSnapshot here.
      return new DataSnapshot(internalSnapshot.Child(path), database, this, null);
    }

    /// <summary>
    ///   Can be used to determine if this DataSnapshot has data at a particular location
    /// </summary>
    /// <param name="path">A relative path to the location of child data</param>
    /// <returns>Whether or not the specified child location has data</returns>
    public bool HasChild(string path) {
      return internalSnapshot.HasChild(path);
    }

    /// <summary>GetRawJsonValue() returns the data contained in this snapshot
    /// as a json serialized string.
    /// </summary>
    /// <remarks />
    /// GetRawJsonValue() returns the data contained in this snapshot as a json string.
    /// <returns>The data contained in this snapshot as json. Return null if no data.</returns>
    public string GetRawJsonValue() {
      // This is to align the behavior on mobile and on desktop (C# implementation).
      if (!internalSnapshot.exists()) {
        return null;
      }

      return Json.Serialize(
          internalSnapshot.value().ToObject(VariantExtension.KeyOptions.UseStringKeys));
    }

    /// <summary>GetValue() returns the data contained in this snapshot as native types.</summary>
    /// <remarks>
    ///   GetValue() returns the data contained in this snapshot as native types.
    ///   The possible types returned are:
    ///   - bool
    ///   - string
    ///   - long
    ///   - double
    ///   - IDictionary{string, object}
    ///   - List{object}
    ///   This list is recursive; the possible types for
    ///   <see cref="object" />
    ///   in the above list is given by the same list. These types correspond to the
    ///   types available in JSON.
    ///   If useExportFormat is set to true, priority information will be included in the output.
    ///   Priority information shows up as a .priority key in a map. For data that would not
    ///   otherwise be a map, the map will also include a .value key with the data.
    /// </remarks>
    /// <param name="useExportFormat">Whether or not to include priority information</param>
    /// <returns>The data, along with its priority, in native types</returns>
    public object GetValue(bool useExportFormat) {
      // TODO(phohmeyer): implement useExportFormat
      return internalSnapshot.value().ToObject(VariantExtension.KeyOptions.UseStringKeys);
    }

    /// A string representing the DataSnapshot as a key, value pair.
    /// It returns the following form:
    ///   `DataSnapshot { key = {Key}, value = {Value} };`
    public override string ToString() {
      return "DataSnapshot { key = " + Key + ", value = " + Value + " }";
    }
  }
}
