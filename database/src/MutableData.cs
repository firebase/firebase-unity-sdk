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

using System.Collections.Generic;
using Firebase.Database.Internal;

namespace Firebase.Database.Internal {
  internal sealed class MutableNodeHolder {
    internal RtdbNode RootNode { get; set; }

    internal MutableNodeHolder(RtdbNode initialNode) {
      RootNode = initialNode ?? EmptyNode.Instance;
    }
  }
}

namespace Firebase.Database {
  /// <summary>
  ///   Instances of this class encapsulate the data and priority at a location.
  /// </summary>
  /// <remarks>
  ///   Instances of this class encapsulate the data and priority at a location.
  ///   It is used in transactions, and it is intended to be inspected and then updated
  ///   to the desired data at that location.
  ///   <br /><br />
  ///   Note that changes made to a child MutableData instance will be visible to the
  ///   parent and vice versa.
  /// </remarks>
  public sealed class MutableData {
    private readonly MutableNodeHolder _holder;
    private readonly RtdbPath _path;

    internal MutableData(MutableNodeHolder holder, RtdbPath path) {
      _holder = holder;
      _path = path;
    }

    private RtdbNode CurrentNode => _holder.RootNode.GetChild(_path);

    /// True if the data at this location has children, false otherwise.
    public bool HasChildren {
      get {
        RtdbNode n = CurrentNode;
        return !n.IsLeaf && n.ChildrenCount > 0;
      }
    }

    /// The number of immediate children at this location.
    public long ChildrenCount => CurrentNode.ChildrenCount;

    /// <summary>
    ///   Used to iterate over the immediate children at this location
    /// </summary>
    /// <remarks>
    ///   Used to iterate over the immediate children at this location
    /// </remarks>
    /// <returns>The immediate children at this location</returns>
    public IEnumerable<MutableData> Children {
      get {
        if (CurrentNode is not ChildrenNode childrenNode || childrenNode.IsEmpty) {
          yield break;
        }
        var ordered = new SortedSet<NamedNode>(RtdbIndex.Priority);
        foreach (KeyValuePair<string, RtdbNode> kv in childrenNode.Children) {
          ordered.Add(new NamedNode(kv.Key, kv.Value));
        }
        foreach (NamedNode item in ordered) {
          yield return new MutableData(_holder, _path.ChildKey(item.Key));
        }
      }
    }

    /// The key name of this location, or null if it is the top-most location
    public string Key => _path.Back;

    /// <summary>getValue() returns the data contained in this instance as native types.</summary>
    /// <remarks>
    ///   getValue() returns the data contained in this instance as native types.
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
    /// <returns>The data contained in this instance as native types</returns>
    public object Value {
      get => CurrentNode.GetValue(false);
      set {
        RtdbNode newNode = RtdbNodeFactory.NodeFromObject(value);
        _holder.RootNode = _holder.RootNode.UpdateChild(_path, newNode);
      }
    }

    /// <summary>Gets the current priority at this location.</summary>
    /// <remarks>
    ///   Gets the current priority at this location. The possible return types are:
    ///   <ul>
    ///     <li>Double</li>
    ///     <li>String</li>
    ///   </ul>
    ///   Note that null is allowed
    /// </remarks>
    /// <returns>The priority at this location as a native type</returns>
    public object Priority {
      get => CurrentNode.Priority.GetValue(false);
      set {
        RtdbNode prio = RtdbNodeFactory.ParsePriority(value);
        RtdbNode updated = CurrentNode.WithPriority(prio);
        _holder.RootNode = _holder.RootNode.UpdateChild(_path, updated);
      }
    }

    /// Determines if data exists at the given path.
    /// <param name="path">A relative path</param>
    /// <returns>True if data exists at the given path, otherwise false</returns>
    public bool HasChild(string path) {
      if (string.IsNullOrEmpty(path)) return false;
      RtdbPath rel = RtdbPath.ParseUnchecked(path);
      if (rel.IsEmpty) return false;
      return !CurrentNode.GetChild(rel).IsEmpty;
    }

    /// <summary>
    ///   Used to obtain a MutableData instance that encapsulates the data and priority
    ///   at the given relative path.
    /// </summary>
    /// <param name="path">A relative path</param>
    /// <returns>An instance encapsulating the data and priority at the given path</returns>
    public MutableData Child(string path) {
      if (string.IsNullOrEmpty(path)) {
        return new MutableData(new MutableNodeHolder(EmptyNode.Instance), RtdbPath.Empty);
      }
      RtdbPath rel = RtdbPath.ParseUnchecked(path);
      if (rel.IsEmpty) {
        return new MutableData(new MutableNodeHolder(EmptyNode.Instance), RtdbPath.Empty);
      }
      return new MutableData(_holder, _path.Child(rel));
    }

    /// Two MutableData are considered equal if they contain the same references and priority.
    public override bool Equals(object o) =>
        o is MutableData other && ReferenceEquals(_holder, other._holder) && _path.Equals(other._path);

    /// Overriden to ensure two objects that are Equal have the same hash.
    public override int GetHashCode() =>
        unchecked((_holder?.GetHashCode() ?? 0) * 397 ^ _path.GetHashCode());

    /// Representation of the mutable data as a string containing a key, value pair.
    public override string ToString() => "MutableData { key = " + Key + ", value = " + Value + " }";
  }
}
