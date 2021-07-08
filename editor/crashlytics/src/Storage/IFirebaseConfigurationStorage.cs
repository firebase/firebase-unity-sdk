/*
 * Copyright 2019 Google LLC
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Firebase.Crashlytics.Editor.Test")]

namespace Firebase.Crashlytics.Editor {

  /// <summary>
  /// A storage interface for a persistent key/value store. The original use case
  /// was to provide this for the UnityEditor which provides several modes --
  /// EditorPrefs, PlayerPrefs, or ScriptableObjects.
  /// </summary>
  public interface IFirebaseConfigurationStorage {

    /// <summary>
    /// Fetch the string value for the specified key.
    /// </summary>
    /// <returns>A string value if the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    string FetchString(string key);

    /// <summary>
    /// Fetch the int value for the specified key.
    /// </summary>
    /// <returns>An int value if the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    int FetchInt(string key);

    /// <summary>
    /// Fetch the float value for the specified key.
    /// </summary>
    /// <returns>A float value the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    float FetchFloat(string key);

    /// <summary>
    /// Fetch the bool value for the specified key.
    /// </summary>
    /// <returns>A bool value the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    bool FetchBool(string key);

    /// <summary>
    /// Checks if a particular key exists
    /// </summary>
    /// <returns><c>true</c>, if key exists, <c>false</c> otherwise.</returns>
    /// <param name="key">Key.</param>
    bool HasKey(string key);

    /// <summary>
    /// Store a specified value of type string by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    void Store(string key, string value);

    /// <summary>
    /// Store a specified value of type int by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    void Store(string key, int value);

    /// <summary>
    /// Store a specified value of type float by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    void Store(string key, float value);

    /// <summary>
    /// Store a specified value of type bool by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    void Store(string key, bool value);

    /// <summary>
    /// Delete the specified key and value (or at the very least strand the value)
    /// </summary>
    /// <param name="key">the key we are interested in deleting</param>
    void Delete(string key);
  }
}
