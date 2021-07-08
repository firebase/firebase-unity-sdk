/*
 * Copyright 2018 Google LLC
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

namespace Firebase.Crashlytics.Editor {
  using System;
  using System.Collections.Generic;

  /// <summary>
  /// Mock implementation that stores values locally
  /// </summary>
  public class MockFirebaseConfigurationStore : IFirebaseConfigurationStorage {
    private Dictionary<string, string> _stringDict;
    private Dictionary<string, int> _intDict;
    private Dictionary<string, float> _floatDict;
    private Dictionary<string, bool> _boolDict;

    /// <summary>
    /// Initialize all the dictionaries
    /// </summary>
    public MockFirebaseConfigurationStore() {
      _stringDict = new Dictionary<string, string>();
      _intDict = new Dictionary<string, int>();
      _floatDict = new Dictionary<string, float>();
      _boolDict = new Dictionary<string, bool>();
    }

    /// <summary>
    /// Fetch the string value for the specified key.
    /// </summary>
    /// <returns>A string value if the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    public string FetchString(string key) {
      string value = "";
      if (_stringDict.TryGetValue(key, out value)) {
        return value;
      }
      throw new KeyNotFoundException(String.Format("Oh, man, couldn't find {0}", key));
    }

    /// <summary>
    /// Fetch the int value for the specified key.
    /// </summary>
    /// <returns>An int value if the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    public int FetchInt(string key) {
      int value = 0;
      if (_intDict.TryGetValue(key, out value)) {
        return value;
      }
      throw new KeyNotFoundException(String.Format("Oh, man, couldn't find {0}", key));
    }

    /// <summary>
    /// Fetch the float value for the specified key.
    /// </summary>
    /// <returns>A float value the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    public float FetchFloat(string key) {
      float value = 0;
      if (_floatDict.TryGetValue(key, out value)) {
        return value;
      }
      throw new KeyNotFoundException(String.Format("Oh, man, couldn't find {0}", key));
    }

    /// <summary>
    /// Fetch the bool value for the specified key.
    /// </summary>
    /// <returns>A bool value the key exists</returns>
    /// <exception>KeyNotFoundException is the intended result of not finding
    /// the key in storage. This is not enforced and may differ implementation
    /// to implementation.</exception>
    /// <param name="key">The key we are interested in</param>
    public bool FetchBool(string key) {
      bool value = false;
      if (_boolDict.TryGetValue(key, out value)) {
        return value;
      }
      throw new KeyNotFoundException(String.Format("Oh, man, couldn't find {0}", key));
    }

    /// <summary>
    /// Checks if a particular key exists
    /// </summary>
    /// <returns><c>true</c>, if key exists, <c>false</c> otherwise.</returns>
    /// <param name="key">Key.</param>
    public bool HasKey(string key){
      return _stringDict.ContainsKey(key) || _intDict.ContainsKey(key) ||
        _floatDict.ContainsKey(key) || _boolDict.ContainsKey(key);
    }

    /// <summary>
    /// Store a specified value of type string by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, string value) {
      Delete(key);
      _stringDict.Add(key, value);
    }

    /// <summary>
    /// Store a specified value of type int by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, int value) {
      Delete(key);
      _intDict.Add(key, value);
    }

    /// <summary>
    /// Store a specified value of type float by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, float value) {
      Delete(key);
      _floatDict.Add(key, value);
    }

    /// <summary>
    /// Store a specified value of type bool by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, bool value) {
      Delete(key);
      _boolDict.Add(key, value);
    }

    /// <summary>
    /// Delete the specified key and value (or at the very least strand the value)
    /// </summary>
    /// <param name="key">the key we are interested in deleting</param>
    public void Delete(string key) {
      if (_stringDict.ContainsKey(key)) {
        _stringDict.Remove(key);
      }

      if (_intDict.ContainsKey(key)) {
        _intDict.Remove(key);
      }

      if (_floatDict.ContainsKey(key)) {
        _floatDict.Remove(key);
      }

      if (_boolDict.ContainsKey(key)) {
        _boolDict.Remove(key);
      }
    }
  }
}
