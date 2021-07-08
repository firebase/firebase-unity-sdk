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

  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>
  /// An in memory Dictionary-based implementation of an
  /// IFirebaseConfigurationStorage layer. This object is
  /// a serializable implementation of the storage layer.
  ///
  /// This class uses lists to hold all of these properties
  /// to facilitate serialization out to the .asset format
  /// supported by Unity. As a result, this is not a suitable
  /// implementation for very large sets of properties.
  ///
  /// If we hit performance issues, we should consider a
  /// HashSet or some other hashed data structure that is
  /// also Serializable. For now, the intended use case is
  /// too small to warrant the investment.
  /// </summary>
  [Serializable]
  public class AssociationConfigurationStorage : IFirebaseConfigurationStorage {
    private static string KEY_NOT_FOUND_DEFAULT_ERROR = "Could not find setting {0}. Please ensure it has been set.";

    /// <summary>
    /// These properties are reserved for manipulation by Unity. The
    /// intent is that the class interacts with the properties, which
    /// will ensure lazy initialization giving Unity a chance to set
    /// these rather than overwriting the value during construction.
    /// </summary>
    [SerializeField]
    private List<Associations.StringAssociation> stringProperties;
    [SerializeField]
    private List<Associations.IntAssociation> intProperties;
    [SerializeField]
    private List<Associations.FloatAssociation> floatProperties;
    [SerializeField]
    private List<Associations.BoolAssociation> booleanProperties;

    private object stringLock = new object();
    private object intLock = new object();
    private object floatLock = new object();
    private object booleanLock = new object();

    /// <summary>
    /// stringProperties null check safe property wrapper
    /// </summary>
    List<Associations.StringAssociation> StringProperties {
      get {
        if (stringProperties == null) {
          stringProperties = new List<Associations.StringAssociation>();
        }

        return stringProperties;
      }
      set { stringProperties = value; }
    }

    /// <summary>
    /// intProperties null check safe property wrapper
    /// </summary>
    List<Associations.IntAssociation> IntProperties {

      get {
        if (intProperties == null) {
          intProperties = new List<Associations.IntAssociation>();
        }

        return intProperties;
      }
      set { intProperties = value; }

    }

    /// <summary>
    /// floatProperties null check safe property wrapper
    /// </summary>
    List<Associations.FloatAssociation> FloatProperties {
      get {
        if (floatProperties == null) {
          floatProperties = new List<Associations.FloatAssociation>();
        }

        return floatProperties;
      }
      set { floatProperties = value; }

    }

    /// <summary>
    /// boolProperties null check safe property wrapper
    /// </summary>
    List<Associations.BoolAssociation> BooleanProperties {
      get {
        if (booleanProperties == null) {
          booleanProperties = new List<Associations.BoolAssociation>();
        }

        return booleanProperties;
      }
      set { booleanProperties = value; }
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
      lock (stringLock) {
        int index = StringProperties.FindIndex(association => association.Key.Equals(key));
        if (index < 0) {
          throw new KeyNotFoundException(String.Format(KEY_NOT_FOUND_DEFAULT_ERROR, key));
        }

        return StringProperties[index].Value;
      }
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
      lock (intLock) {
        int index = IntProperties.FindIndex(association => association.Key.Equals(key));
        if (index < 0) {
          throw new KeyNotFoundException(String.Format(KEY_NOT_FOUND_DEFAULT_ERROR, key));
        }

        return IntProperties[index].Value;
      }
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
      lock (floatLock) {
        int index = FloatProperties.FindIndex(association => association.Key.Equals(key));
        if (index < 0) {
          throw new KeyNotFoundException(String.Format(KEY_NOT_FOUND_DEFAULT_ERROR, key));
        }

        return FloatProperties[index].Value;
      }
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
      lock (booleanLock) {
        int index = BooleanProperties.FindIndex(association => association.Key.Equals(key));
        if (index < 0) {
          throw new KeyNotFoundException(String.Format(KEY_NOT_FOUND_DEFAULT_ERROR, key));
        }

        return BooleanProperties[index].Value;
      }
    }

    /// <summary>
    /// Checks if a particular key exists
    /// </summary>
    /// <returns><c>true</c>, if key exists, <c>false</c> otherwise.</returns>
    /// <param name="key">Key.</param>
    public bool HasKey(string key){
      return HasString(key) || HasInt(key) ||
        HasFloat(key) || HasBool(key);
    }

    /// <summary>
    /// Store a specified value of type string by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, string value) {
      //locks independently
      DeleteString(key);

      lock (stringLock) {
        Associations.StringAssociation association = new Associations.StringAssociation{Key=key, Value = value};
        StringProperties.Add(association);
      }
    }

    /// <summary>
    /// Store a specified value of type int by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, int value) {
      //locks independently
      DeleteInt(key);
      lock (intLock) {
        Associations.IntAssociation association = new Associations.IntAssociation{Key=key, Value = value};
        IntProperties.Add(association);
      }
    }

    /// <summary>
    /// Store a specified value of type float by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, float value) {
      //locks independently
      DeleteFloat(key);
      lock (floatLock) {
        Associations.FloatAssociation association = new Associations.FloatAssociation{Key=key, Value = value};
        FloatProperties.Add(association);
      }
    }

    /// <summary>
    /// Store a specified value of type bool by a key
    /// </summary>
    /// <param name="key">The key to indicate the value</param>
    /// <param name="value">The value to store</param>
    public void Store(string key, bool value) {
      //locks independently
      DeleteBool(key);
      lock (booleanLock) {
        Associations.BoolAssociation association = new Associations.BoolAssociation{Key=key, Value = value};
        BooleanProperties.Add(association);
      }
    }

    /// <summary>
    /// Delete the specified key and value (or at the very least strand the value)
    /// </summary>
    /// <param name="key">the key we are interested in deleting</param>
    public void Delete(string key) {
      bool removed = DeleteString(key);

      if (!removed) {
        removed = DeleteInt(key);
      }

      if (!removed) {
        removed = DeleteFloat(key);
      }

      if (!removed) {
        DeleteBool(key);
      }
    }

    private bool HasString(string key) {
      int index = StringProperties.FindIndex(association => association.Key.Equals(key));
      if (index < 0) {
        return false;
      }

      return true;
    }

    private bool HasInt(string key) {
      int index = IntProperties.FindIndex(association => association.Key.Equals(key));
      if (index < 0) {
        return false;
      }

      return true;
    }

    private bool HasFloat(string key) {
      int index = FloatProperties.FindIndex(association => association.Key.Equals(key));
      if (index < 0) {
        return false;
      }

      return true;
    }

    private bool HasBool(string key) {
      int index = BooleanProperties.FindIndex(association => association.Key.Equals(key));
      if (index < 0) {
        return false;
      }

      return true;
    }

    private bool DeleteString(string key) {
      lock (stringLock) {
        int index = StringProperties.FindIndex(association => association.Key.Equals(key));
        if (index >= 0) {
          StringProperties.RemoveAt(index);
          return true;
        }
      }

      return false;
    }

    private bool DeleteInt(string key) {
      lock (intLock) {
        int index = IntProperties.FindIndex(association => association.Key.Equals(key));
        if (index >= 0) {
           IntProperties.RemoveAt(index);
          return true;
        }
        return false;
      }
    }

    private bool DeleteFloat(string key) {
      lock (floatLock) {
        int index = FloatProperties.FindIndex(association => association.Key.Equals(key));
        if (index >= 0) {
          FloatProperties.RemoveAt(index);
          return true;
        }
        return false;
      }
    }

    private bool DeleteBool(string key) {
      lock (booleanLock) {
        int index = BooleanProperties.FindIndex(association => association.Key.Equals(key));
        if (index >= 0) {
          BooleanProperties.RemoveAt(index);
          return true;
        }
        return false;
      }
    }
  }
}
