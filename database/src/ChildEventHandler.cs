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

namespace Firebase.Database {
  /// <summary>
  /// Child changed event arguments.  This data is sent when any of the
  /// following events are raised.
  ///   <see cref="Query.ChildAdded" />
  ///  , <see cref="Query.ChildChanged" />
  ///  , <see cref="Query.ChildRemoved" />
  ///  , or <see cref="Query.ChildMoved" />
  /// </summary>
  public sealed class ChildChangedEventArgs : EventArgs {
    internal ChildChangedEventArgs(DataSnapshot snapshot, string previousChildName) {
      PreviousChildName = previousChildName;
      Snapshot = snapshot;
    }

    internal ChildChangedEventArgs(DatabaseError error) {
      DatabaseError = error;
    }

    /// <summary>
    /// Gets the data snapshot for this update if it exists.
    /// </summary>
    /// <value>The snapshot.</value>
    public DataSnapshot Snapshot { get; private set; }

    /// <summary>
    /// The presence of a <see cref="DatabaseError" /> indicates that
    /// there was an issue subscribing the event to the given
    /// <see cref="DatabaseReference" /> location.
    /// </summary>
    /// <value>The database error.</value>
    public DatabaseError DatabaseError { get; private set; }

    /// <summary>
    /// The key name of sibling location ordered before the new child.
    /// This will be null for the first child node of a location.
    /// </summary>
    /// <value>The name of the previous child.</value>
    public string PreviousChildName { get; private set; }
  }
}
