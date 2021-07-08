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

namespace Firebase.Crashlytics.Editor {
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using NUnit.Framework;
  using UnityEditor;
  using UnityEngine;
  /// <summary>
  /// Unit Tests for the DictionaryConfigurationStorage Class.
  /// </summary>
  public class AssociationConfigurationStorageUnitTest : CrashlyticsEditorTestBase {

    IFirebaseConfigurationStorage _defaultPopulated;
    /// <summary>
    /// Setup the test with default dictionary storage
    /// </summary>
    public AssociationConfigurationStorageUnitTest() : base() {
        _defaultPopulated = new AssociationConfigurationStorage();
        _defaultPopulated.Store("string", "string");
        _defaultPopulated.Store("int", 1);
        _defaultPopulated.Store("float", 2.3F);
        _defaultPopulated.Store("true", true);
        _defaultPopulated.Store("false", true);
    }

    /// <summary>
    /// Test that we properly store and fetch a string.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", "value")]
    public void TestStoreAndFetchString(string key, string value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        Assert.Throws<KeyNotFoundException>(() => storage.FetchString(key));
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchString(key));
    }

    /// <summary>
    /// Test that we properly store and fetch an int
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", 1)]
    public void TestStoreAndFetchInt(string key, int value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        Assert.Throws<KeyNotFoundException>(() => storage.FetchInt(key));
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchInt(key));
    }

    /// <summary>
    /// Test that we properly store and fetch a float
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", 2.3F)]
    public void TestStoreAndFetchFloat(string key, float value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        Assert.Throws<KeyNotFoundException>(() => storage.FetchFloat(key));
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchFloat(key));
    }

    /// <summary>
    /// Test that we properly store and fetch an bool
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("true", true)]
    [TestCase("false", false)]
    public void TestStoreAndFetchBoolean(string key, bool value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        Assert.Throws<KeyNotFoundException>(() => storage.FetchBool(key));
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchBool(key));
    }

    /// <summary>
    /// Test that we can properly determine whether or not
    /// a key is present of each type leveraging the
    /// default populated storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="expValue"></param>
    [TestCase("string", true)]
    [TestCase("int", true)]
    [TestCase("float", true)]
    [TestCase("true", true)]
    [TestCase("false", true)]
    [TestCase("random", false)]
    public void TestHasKey(string key, bool expValue) {
        Assert.AreEqual(expValue, _defaultPopulated.HasKey(key));
    }

    /// <summary>
    /// Test that we can properly delete a key
    /// from storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", "value")]
    public void TestDeleteKeyString(string key, string value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchString(key));
        storage.Delete(key);
        Assert.Throws<KeyNotFoundException>(() => storage.FetchString(key));
    }

    /// <summary>
    /// Test that we can properly delete a key
    /// from storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", 1)]
    public void TestDeleteKeyInt(string key, int value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchInt(key));
        storage.Delete(key);
        Assert.Throws<KeyNotFoundException>(() => storage.FetchInt(key));
    }

    /// <summary>
    /// Test that we can properly delete a key
    /// from storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", 2.3F)]
    public void TestDeleteKeyFloat(string key, float value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchFloat(key));
        storage.Delete(key);
        Assert.Throws<KeyNotFoundException>(() => storage.FetchFloat(key));
    }

    /// <summary>
    /// Test that we can properly delete a key
    /// from storage.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    [TestCase("key", false)]
    public void TestDeleteKeyBool(string key, bool value) {
        AssociationConfigurationStorage storage = new AssociationConfigurationStorage();
        storage.Store(key, value);
        Assert.AreEqual(value, storage.FetchBool(key));
        storage.Delete(key);
        Assert.Throws<KeyNotFoundException>(() => storage.FetchBool(key));
    }
  }
}
