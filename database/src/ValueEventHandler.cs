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
  /// Event arguments passed with the <see cref="Query.ValueChanged"/> Event.
  /// </summary>
  public sealed class ValueChangedEventArgs : EventArgs {
    internal ValueChangedEventArgs(DataSnapshot snapshot) {
      Snapshot = snapshot;
    }

    internal ValueChangedEventArgs(DatabaseError error) {
      DatabaseError = error;
    }

    /// <summary>
    /// Gets the snapshot for this value update event if it exists.
    /// </summary>
    /// <value>The snapshot.</value>
    public DataSnapshot Snapshot { get; private set; }

    /// <summary>
    /// Gets the database error if one exists.
    /// </summary>
    /// <value>The database error.</value>
    public DatabaseError DatabaseError { get; private set; }
  }

}
