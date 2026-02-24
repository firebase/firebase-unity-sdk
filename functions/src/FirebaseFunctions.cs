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

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Firebase.Functions
{
  /// <summary>
  ///   FirebaseFunctions is a service that supports calling Google Cloud Functions.
  /// </summary>
  /// <remarks>
  ///   FirebaseFunctions is a service that supports calling Google Cloud Functions.
  ///   Pass a custom instance of
  ///   <see cref="Firebase.FirebaseApp" />
  ///   to
  ///   <see cref="GetInstance" />
  ///   which will use Auth and InstanceID from the app.
  ///   .
  ///   <p />
  ///   Otherwise, if you call
  ///   <see cref="DefaultInstance" />
  ///   without a FirebaseApp, the
  ///   FirebaseFunctions instance will initialize with the default
  ///   <see cref="Firebase.FirebaseApp" />
  ///   obtainable from
  ///   <see cref="FirebaseApp.DefaultInstance" />.
  /// </remarks>
  public sealed class FirebaseFunctions
  {
    private static readonly ConcurrentDictionary<string, FirebaseFunctions> _instances = new();

    private readonly FirebaseApp _firebaseApp;
    private string _emulator_origin;
    private string _region;



    // Key of this instance within _instances
    private string _instanceKey;

    /// <summary>
    /// Construct this instance associated with the specified app and region.
    /// </summary>
private FirebaseFunctions(FirebaseApp app, string region)
{
    _firebaseApp = app;
    _region = region;
    _instanceKey = InstanceKey(app, region);

    try
{
    var appType = _firebaseApp.GetType();
    var appDisposedEvent = appType.GetEvent("AppDisposed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    
    if (appDisposedEvent != null)
    {
        // 1. Get YOUR handler method
        var methodInfo = this.GetType().GetMethod("OnAppDisposed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        
        // 2. Create the delegate
        Delegate handlerDelegate = Delegate.CreateDelegate(appDisposedEvent.EventHandlerType, this, methodInfo);
        
        // 3. THE FIX: Grab the non-public 'add' method directly (passing 'true' means include non-public)
        var addMethod = appDisposedEvent.GetAddMethod(true); 
        
        if (addMethod != null)
        {
            // 4. Invoke the hidden 'add_AppDisposed' method!
            addMethod.Invoke(_firebaseApp, new object[] { handlerDelegate });
            UnityEngine.Debug.Log("Success! Attached to internal AppDisposed event.");
        }
        else
        {
            UnityEngine.Debug.LogError("Found the event, but couldn't find its hidden 'add' method.");
        }
    }
    else
    {
        UnityEngine.Debug.LogWarning("AppDisposed event not found via reflection.");
    }
}
catch (System.Exception ex)
{
    UnityEngine.Debug.LogError($"Failed to attach to AppDisposed via reflection: {ex.Message}");
}
}

    /// <summary>
    /// Remove the reference to this object from the _instances dictionary.
    /// </summary>
    ~FirebaseFunctions()
    {
      Dispose();
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs)
    {
      Dispose();
    }

    // Remove the reference to this instance from _instances and clean up events.
    private void Dispose()
    {
      System.GC.SuppressFinalize(this);
      
      _instances.TryRemove(_instanceKey, out _);

      var appType = _firebaseApp.GetType();
      var appDisposedEvent = appType.GetEvent("AppDisposed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (appDisposedEvent != null)
      {
        appDisposedEvent.RemoveEventHandler(_firebaseApp, new EventHandler(OnAppDisposed));
      }
    }




    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   , initialized with the default
    ///   <see cref="Firebase.FirebaseApp" />
    ///   .
    /// </summary>
    /// <value>
    ///   a
    ///   <see cref="FirebaseFunctions" />
    ///   instance.
    /// </value>
    public static FirebaseFunctions DefaultInstance
    {
      get { return GetInstance(FirebaseApp.DefaultInstance); }
    }

    /// <summary>
    ///   The
    ///   <see cref="Firebase.FirebaseApp" />
    ///   associated with this
    ///   <see cref="FirebaseFunctions" />
    ///   instance.
    /// </summary>
    public FirebaseApp App { get { return _firebaseApp; } }

    private static string InstanceKey(FirebaseApp app, string region)
    {
      return app.Name + "/" + region;
    }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   , initialized with a custom
    ///   <see cref="Firebase.FirebaseApp" />
    /// </summary>
    /// <param name="app">
    ///   The custom
    ///   <see cref="Firebase.FirebaseApp" />
    ///   used for initialization.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="FirebaseFunctions" />
    ///   instance.
    /// </returns>
    public static FirebaseFunctions GetInstance(FirebaseApp app)
    {
      return GetInstance(app, "us-central1");
    }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   , initialized with the default <see cref="Firebase.FirebaseApp" />.
    /// </summary>
    /// <param name="region">
    ///   The region to call Cloud Functions in.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="FirebaseFunctions" />
    ///   instance.
    /// </returns>
    public static FirebaseFunctions GetInstance(string region)
    {
      return GetInstance(FirebaseApp.DefaultInstance, region);
    }

    /// <summary>
    ///   Returns the
    ///   <see cref="FirebaseFunctions" />
    ///   , initialized with a custom
    ///   <see cref="Firebase.FirebaseApp" />
    ///   and region.
    /// </summary>
    /// <param name="app">
    ///   The custom
    ///   <see cref="Firebase.FirebaseApp" />
    ///   used for initialization.
    /// </param>
    /// <returns>
    ///   a
    ///   <see cref="FirebaseFunctions" />
    ///   instance.
    /// </returns>
    public static FirebaseFunctions GetInstance(FirebaseApp app, string region)
    {
      if (app == null)
      {
        app = FirebaseApp.DefaultInstance;
      }

      string key = InstanceKey(app, region);
      if (_instances.ContainsKey(key))
      {
        return _instances[key];
      }

      return _instances.GetOrAdd(key, _ => new FirebaseFunctions(app, region));
    }

  private string GetUrl(in string name)
  {
    string proj = _firebaseApp.Options.ProjectId;
    string url = string.IsNullOrEmpty(_emulator_origin)
      ? $"https://{_region}-{proj}.cloudfunctions.net/{name}"
      : $"{_emulator_origin}/{proj}/{_region}/{name}";
    return url;
  }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a name.
    /// </summary>
    public HttpsCallableReference GetHttpsCallable(string name)
    {
      return new HttpsCallableReference(this, GetUrl(name));
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(string url)
    {
      return new HttpsCallableReference(this, url);
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(Uri url)
    {
      return GetHttpsCallableFromURL(url.ToString());
    }

    /// <summary>
    ///   Sets an origin of a Cloud Functions Emulator instance to use.
    /// </summary>
    public void UseFunctionsEmulator(string origin)
    {
      _emulator_origin = origin;
    }
  }
}
