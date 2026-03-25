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
using System.Collections.Concurrent;
using System.Reflection;
using System.Net.Http;

namespace Firebase.Functions {
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
  public sealed class FirebaseFunctions {
    private static readonly ConcurrentDictionary<string, FirebaseFunctions> _instances = new();

    private readonly FirebaseApp _firebaseApp;
    private string _emulator_origin;
    private string _region;
    private EventInfo _appDisposedEvent;
    private MethodInfo _appDisposedMethod;
    private Delegate _onAppDisposedHandler;

    private readonly HttpClient _httpClient;

    private static void LogError(string message) {
#if FUNCTIONS_DEBUG_LOGGING
      UnityEngine.Debug.LogError(message);
#endif
    }

    // Key of this instance within _instances
    private string _instanceKey;

    /// <summary>
    /// Construct an instance associated with the specified app and region.
    /// </summary>
    private FirebaseFunctions(FirebaseApp app, string region) {
      _firebaseApp = app;
      _region = region;
      _instanceKey = InstanceKey(app, region);

      // Default timeout is 70 seconds matching native SDKs.
      _httpClient = new HttpClient();
      _httpClient.Timeout = TimeSpan.FromSeconds(70);

      try {
        var appType = _firebaseApp.GetType();
        _appDisposedEvent = appType.GetEvent("AppDisposed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        if (_appDisposedEvent != null) {
          _appDisposedMethod = this.GetType().GetMethod("OnAppDisposed", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

          _onAppDisposedHandler = Delegate.CreateDelegate(_appDisposedEvent.EventHandlerType, this, _appDisposedMethod);

          var addMethod = _appDisposedEvent.GetAddMethod(true);

          if (addMethod != null) {
            addMethod.Invoke(_firebaseApp, new object[] { _onAppDisposedHandler });
          }
          else {
            LogError("Found the event, but couldn't find its hidden 'add' method.");
          }
        }
        else {
          LogError("AppDisposed event not found via reflection.");
        }
      }
      catch (System.Exception ex) {
        LogError($"Failed to attach to AppDisposed via reflection: {ex.Message}");
      }
    }

    /// <summary>
    /// Remove the reference to this object from the _instances dictionary.
    /// </summary>
    ~FirebaseFunctions() {
      Dispose();
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      Dispose();
    }

    // Remove the reference to this instance from _instances and clean up events.
    private void Dispose() {
      System.GC.SuppressFinalize(this);

      _instances.TryRemove(_instanceKey, out _);
      if (_appDisposedEvent != null && _onAppDisposedHandler != null) {
        var removeMethod = _appDisposedEvent.GetRemoveMethod(true);
        removeMethod?.Invoke(_firebaseApp, new object[] { _onAppDisposedHandler });
      }
      _httpClient.Dispose();
    }

    internal HttpClient HttpClient { get { return _httpClient; } }

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
    public static FirebaseFunctions DefaultInstance {
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

    private static string InstanceKey(FirebaseApp app, string region) {
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
    public static FirebaseFunctions GetInstance(FirebaseApp app) {
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
    public static FirebaseFunctions GetInstance(string region) {
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
    public static FirebaseFunctions GetInstance(FirebaseApp app, string region) {
      if (app == null) {
        app = FirebaseApp.DefaultInstance;
      }

      string key = InstanceKey(app, region);
      return _instances.GetOrAdd(key, _ => new FirebaseFunctions(app, region));
    }

    private string GetUrl(in string name) {
      string proj = _firebaseApp.Options.ProjectId;
      string url = string.IsNullOrEmpty(_emulator_origin)
        ? $"https://{_region}-{proj}.cloudfunctions.net/{name}"
        : $"{_emulator_origin}/{proj}/{_region}/{name}";
      return url;
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a name.
    /// </summary>
    public HttpsCallableReference GetHttpsCallable(string name) {
      return new HttpsCallableReference(this, GetUrl(name));
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(string url) {
      return new HttpsCallableReference(this, url);
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(Uri url) {
      return GetHttpsCallableFromURL(url.ToString());
    }

    /// <summary>
    ///   Sets an origin of a Cloud Functions Emulator instance to use.
    /// </summary>
    public void UseFunctionsEmulator(string origin) {
      _emulator_origin = origin;
    }
  }
}
