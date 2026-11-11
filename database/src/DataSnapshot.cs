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
    private readonly FirebaseDatabase _database;
    private readonly RtdbPath _path;
    private readonly RtdbNode _node;
    private readonly RtdbIndex _index;

    internal DataSnapshot(FirebaseDatabase database, RtdbPath path, RtdbNode node, RtdbIndex index) {
      _database = database;
      _path = path;
      _node = node ?? EmptyNode.Instance;
      _index = index ?? RtdbIndex.Priority;
    }

    /// <summary>Indicates whether this snapshot has any children</summary>
    /// <returns>True if the snapshot has any children, otherwise false</returns>
    public bool HasChildren => !_node.IsLeaf && _node.ChildrenCount > 0;

    /// <summary>Returns true if the snapshot contains a non-null value.</summary>
    /// <returns>True if the snapshot contains a non-null value, otherwise false</returns>
    public bool Exists => !_node.IsEmpty;

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
    public object Value => GetValue(false);

    /// The number of immediate children in the this snapshot.
    public long ChildrenCount => _node.ChildrenCount;

    /// <summary>Used to obtain a reference to the source location for this Snapshot.</summary>
    /// <returns>
    ///   A DatabaseReference corresponding to the location that this snapshot came from
    /// </returns>
    public DatabaseReference Reference => new DatabaseReference(_database, _path);

    /// The key name for the source location of this snapshot.
    public string Key => _path.Back;

    /// <summary>Gives access to all of the immediate children of this Snapshot.</summary>
    /// <remarks>
    ///   Gives access to all of the immediate children of this Snapshot.
    ///   Can be used in native for loops:
    /// </remarks>
    /// <returns>The immediate children of this snapshot</returns>
    public IEnumerable<DataSnapshot> Children {
      get {
        if (_node is not ChildrenNode childrenNode || childrenNode.IsEmpty) {
          yield break;
        }
        if (_index.OrderBy == QueryOrderBy.Key) {
          foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
            yield return new DataSnapshot(_database, _path.ChildKey(kv.Key), kv.Value, RtdbIndex.Priority);
          }
          yield break;
        }

        // Fast path when no child has a priority under default PriorityIndex: Children map is already in ChildKeyComparer order!
        if (_index.OrderBy == QueryOrderBy.Priority) {
          bool anyPriority = false;
          foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
            if (!kv.Value.Priority.IsEmpty) {
              anyPriority = true;
              break;
            }
          }
          if (!anyPriority) {
            foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
              yield return new DataSnapshot(_database, _path.ChildKey(kv.Key), kv.Value, RtdbIndex.Priority);
            }
            yield break;
          }
        }

        // General indexed traversal using SortedSet (0 LOH allocations even for huge child counts).
        var ordered = new SortedSet<NamedNode>(_index);
        foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
          ordered.Add(new NamedNode(kv.Key, kv.Value));
        }
        foreach (NamedNode item in ordered) {
          yield return new DataSnapshot(_database, _path.ChildKey(item.Key), item.Node, RtdbIndex.Priority);
        }
      }
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
    public object Priority => _node.Priority.GetValue(false);

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
      if (string.IsNullOrEmpty(path)) {
        return new DataSnapshot(_database, _path, EmptyNode.Instance, RtdbIndex.Priority);
      }
      RtdbPath rel = RtdbPath.ParseUnchecked(path);
      if (rel.IsEmpty) {
        return new DataSnapshot(_database, _path, EmptyNode.Instance, RtdbIndex.Priority);
      }
      return new DataSnapshot(_database, _path.Child(rel), _node.GetChild(rel), RtdbIndex.Priority);
    }

    /// <summary>
    ///   Can be used to determine if this DataSnapshot has data at a particular location
    /// </summary>
    /// <param name="path">A relative path to the location of child data</param>
    /// <returns>Whether or not the specified child location has data</returns>
    public bool HasChild(string path) {
      if (string.IsNullOrEmpty(path)) return false;
      RtdbPath rel = RtdbPath.ParseUnchecked(path);
      if (rel.IsEmpty) return false;
      return !_node.GetChild(rel).IsEmpty;
    }

    /// <summary>GetRawJsonValue() returns the data contained in this snapshot
    /// as a json serialized string.
    /// </summary>
    /// <remarks />
    /// GetRawJsonValue() returns the data contained in this snapshot as a json string.
    /// <returns>The data contained in this snapshot as json. Return null if no data.</returns>
    public string GetRawJsonValue() {
      if (_node.IsEmpty) {
        return null;
      }
      return Json.Serialize(GetValue(false));
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
    public object GetValue(bool useExportFormat) => _node.GetValue(useExportFormat);

    /// A string representing the DataSnapshot as a key, value pair.
    /// It returns the following form:
    ///   `DataSnapshot { key = {Key}, value = {Value} };`
    public override string ToString() => "DataSnapshot { key = " + Key + ", value = " + Value + " }";
  }
}
