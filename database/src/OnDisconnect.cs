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
using System.Threading.Tasks;
using Firebase.Database.Internal;

namespace Firebase.Database {
  /// <summary>
  ///   The OnDisconnect class is used to manage operations that will be Run on the server
  ///   when this client disconnects.
  /// </summary>
  /// <remarks>
  ///   The OnDisconnect class is used to manage operations that will be Run on the server
  ///   when this client disconnects. It can be used to add or Remove data based on a client's
  ///   connection status. It is very useful in applications looking for 'presence' functionality.
  ///   <br /><br />
  ///   Instances of this class are obtained by calling
  ///   <see>
  ///     onDisconnect
  ///     <cref>DatabaseReference.onDisconnect()</cref>
  ///   </see>
  ///   on a Firebase Database ref.
  /// </remarks>
  public sealed class OnDisconnect {
    private readonly DatabaseRepo _repo;
    private readonly RtdbPath _path;

    internal OnDisconnect(DatabaseRepo repo, RtdbPath path) {
      _repo = repo;
      _path = path;
    }

    /// <summary>
    ///   Ensure the data at this location is set to the specified value when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    /// </summary>
    /// <remarks>
    ///   Ensure the data at this location is set to the specified value when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    ///   <br /><br />
    ///   This method is especially useful for implementing "presence" systems,
    ///   where a value should be changed or cleared when a user disconnects
    ///   so that they appear "offline" to other users.
    /// </remarks>
    /// <param name="value">The value to be set when a disconnect occurs</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetValue(object value) {
      RtdbNode node = RtdbNodeFactory.NodeFromObject(value);
      return _repo.OnDisconnectSetValueAsync(_path, node);
    }

    /// <summary>
    ///   Ensure the data at this location is set to the specified value and priority when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    /// </summary>
    /// <remarks>
    ///   Ensure the data at this location is set to the specified value and priority when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    ///   <br /><br />
    ///   This method is especially useful for implementing "presence" systems,
    ///   where a value should be changed or cleared when a user disconnects
    ///   so that they appear "offline" to other users.
    /// </remarks>
    /// <param name="value">The value to be set when a disconnect occurs</param>
    /// <param name="priority">The priority to be set when a disconnect occurs</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetValue(object value, string priority) {
      RtdbNode prio = RtdbNodeFactory.ParsePriority(priority);
      RtdbNode node = RtdbNodeFactory.NodeFromObject(value, prio);
      return _repo.OnDisconnectSetValueAsync(_path, node);
    }

    /// <summary>
    ///   Ensure the data at this location is set to the specified value and priority when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    /// </summary>
    /// <remarks>
    ///   Ensure the data at this location is set to the specified value and priority when
    ///   the client is disconnected (due to closing the browser, navigating
    ///   to a new page, or network issues).
    ///   <br /><br />
    ///   This method is especially useful for implementing "presence" systems,
    ///   where a value should be changed or cleared when a user disconnects
    ///   so that they appear "offline" to other users.
    /// </remarks>
    /// <param name="value">The value to be set when a disconnect occurs</param>
    /// <param name="priority">The priority to be set when a disconnect occurs</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task SetValue(object value, double priority) {
      RtdbNode prio = RtdbNodeFactory.ParsePriority(priority);
      RtdbNode node = RtdbNodeFactory.NodeFromObject(value, prio);
      return _repo.OnDisconnectSetValueAsync(_path, node);
    }

    // Update
    /// <summary>
    ///   Ensure the data has the specified child values updated when
    ///   the client is disconnected
    /// </summary>
    /// <param name="update">The paths to update, along with their desired values</param>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task UpdateChildren(IDictionary<string, object> update) {
      return _repo.OnDisconnectUpdateChildrenAsync(_path, update);
    }

    // Remove
    /// <summary>Remove the value at this location when the client disconnects</summary>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task RemoveValue() {
      return _repo.OnDisconnectSetValueAsync(_path, EmptyNode.Instance);
    }

    // Cancel the operation
    /// <summary>Cancel any disconnect operations that are queued up at this location</summary>
    /// <returns>
    ///   The
    ///   <see cref="Task{TResult}" />
    ///   for this operation.
    /// </returns>
    public Task Cancel() {
      return _repo.OnDisconnectCancelAsync(_path);
    }
  }
}
