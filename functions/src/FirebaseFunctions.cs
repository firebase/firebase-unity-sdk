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
using System.Net.Http;
using System.Threading;
using Firebase.Internal;

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
#if UNITY_6000_6_OR_NEWER
    [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    private static readonly FirebaseInstanceCache<FirebaseFunctions> _instances =
        new FirebaseInstanceCache<FirebaseFunctions>(fn => fn.Dispose());

    private readonly FirebaseApp _firebaseApp;
    private readonly string _region;
    private readonly HttpClient _httpClient;
    private string _emulator_origin;
    private int _disposed;

    /// <summary>
    /// Construct an instance associated with the specified app and region.
    /// </summary>
    private FirebaseFunctions(FirebaseApp app, string region) {
      _firebaseApp = app;
      _region = region;
      // Default timeout is 70 seconds matching native SDKs.
      _httpClient = HttpHelpers.CreateHttpClient(TimeSpan.FromSeconds(70));
    }

    private void Dispose() {
      if (Interlocked.Exchange(ref _disposed, 1) == 0) {
        _httpClient.Dispose();
      }
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
      return _instances.GetOrCreate(app, () => new FirebaseFunctions(app, region), region);
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
    public HttpsCallableReference GetHttpsCallable(string name, HttpsCallableOptions options = null) {
      return new HttpsCallableReference(this, GetUrl(name), options);
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(string url, HttpsCallableOptions options = null) {
      return new HttpsCallableReference(this, url, options);
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(Uri url, HttpsCallableOptions options = null) {
      return GetHttpsCallableFromURL(url.ToString(), options);
    }

    /// <summary>
    ///   Sets an origin of a Cloud Functions Emulator instance to use.
    /// </summary>
    public void UseFunctionsEmulator(string origin) {
      _emulator_origin = origin;
    }
  }
}
