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

using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Database.Internal;

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
    private InternalMutableData internalData;
    // We only keep a reference to the database to ensure that it's kept alive,
    // as the underlying C++ code needs that.
    // Note: this might be overkill for this class, as the MutableData should only be
    // used while a transaction is ongoing and the InternalTransactionHandler will also
    // keep the Database alive.
    private FirebaseDatabase database;

    /// <param name="node">The data</param>
    internal MutableData(InternalMutableData internalData, FirebaseDatabase database) {
      this.internalData = internalData;
      this.database = database;
    }

    /// True if the data at this location has children, false otherwise.
    public bool HasChildren {
      get { return internalData.children_count() != 0; }
    }

    /// The number of immediate children at this location.
    public long ChildrenCount {
      get { return internalData.children_count(); }
    }

    /// This is a simple wrapper around the enumerator returned by the internal
    /// SWIG class.
    private sealed class ChildrenEnumerator : IEnumerator<MutableData> {
      private MutableDataChildrenEnumerator internalEnumerator;
      private FirebaseDatabase database;
      public ChildrenEnumerator(
          MutableDataChildrenEnumerator internalEnumerator, FirebaseDatabase database) {
        this.internalEnumerator = internalEnumerator;
        this.database = database;
      }
      public MutableData Current {
        get { return new MutableData(internalEnumerator.Current(), database); }
      }
      object IEnumerator.Current {
        get { return new MutableData(internalEnumerator.Current(), database); }
      }
      public bool MoveNext() {
        return internalEnumerator.MoveNext();
      }
      public void Reset() {
        internalEnumerator.Reset();
      }
      // We only implement Dispose because IEnumerator<T> extends IDisposable
      public void Dispose() {
        internalEnumerator.Dispose();
      }
    }

    private sealed class ChildrenEnumerable : IEnumerable<MutableData> {
      private InternalMutableData internalData;
      private FirebaseDatabase database;
      public ChildrenEnumerable(InternalMutableData internalData, FirebaseDatabase database) {
        this.internalData = internalData;
        this.database = database;
      }
      public IEnumerator<MutableData> GetEnumerator() {
        return new ChildrenEnumerator(internalData.ChildrenEnumerator(), database);
      }
      IEnumerator IEnumerable.GetEnumerator() {
        return new ChildrenEnumerator(internalData.ChildrenEnumerator(), database);
      }
    }

    /// <summary>
    ///   Used to iterate over the immediate children at this location
    /// </summary>
    /// <remarks>
    ///   Used to iterate over the immediate children at this location
    /// </remarks>
    /// <returns>The immediate children at this location</returns>
    public IEnumerable<MutableData> Children {
      get { return new ChildrenEnumerable(internalData, database); }
    }

    /// The key name of this location, or null if it is the top-most location
    public string Key {
      get { return internalData.key(); }
    }

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
      get { return internalData.value().ToObject(VariantExtension.KeyOptions.UseStringKeys); }
      set { internalData.set_value(Utilities.MakeVariant(value)); }
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
      get { return internalData.priority().ToObject(VariantExtension.KeyOptions.UseStringKeys); }
      set { internalData.set_priority(Utilities.MakePriorityVariant(value)); }
    }

    /// Determines if data exists at the given path.
    /// <param name="path">A relative path</param>
    /// <returns>True if data exists at the given path, otherwise false</returns>
    public bool HasChild(string path) {
      return internalData.HasChild(path);
    }

    /// <summary>
    ///   Used to obtain a MutableData instance that encapsulates the data and priority
    ///   at the given relative path.
    /// </summary>
    /// <param name="path">A relative path</param>
    /// <returns>An instance encapsulating the data and priority at the given path</returns>
    public MutableData Child(string path) {
      return new MutableData(internalData.GetChild(path), database);
    }

    /// Two MutableData are considered equal if they contain the same references and priority.
    public override bool Equals(object o) {
      // TODO(phohmeyer): Can we just remove this function + the hashcode one?
      // I don't see any valid reason to compare two MutableData objects.
      // And using them as keys in a hashmap / putting them into a set sounds like
      // a real bad idea. For now, fallback to the standard object implementation,
      // so code that somehow does that still compiles and does something reasonable.
      return base.Equals(o);
    }

    /// Overriden to ensure two objects that are Equal have the same hash.
    public override int GetHashCode() {
      return base.GetHashCode();
    }

    /// Representation of the mutable data as a string containing a key, value pair.
    public override string ToString() {
      // Note: this ToString function is different from the one in the old C# implementation,
      // but should be good enough. Especially as it should mostly be used in debugging.
      return "MutableData { key = " + Key + ", value = " + Value + " }";
    }
  }
}
