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
    // Dictionary of FirebaseFunctions instances indexed by a key FirebaseFunctionsInternal.InstanceKey.
    private static readonly Dictionary<string, FirebaseFunctions> functionsByInstanceKey =
        new Dictionary<string, FirebaseFunctions>();

    // Proxy for the C++ firebase::functions::Functions object.
    private FirebaseFunctionsInternal functionsInternal;
    // Proxy for the C++ firebase::app:App object.
    private readonly FirebaseApp firebaseApp;
    // Key of this instance within functionsByInstanceKey.
    private string instanceKey;

    /// <summary>
    /// Construct a this instance associated with the specified app and region.
    /// </summary>
    /// <param name="functions">C# proxy for firebase::functions::Functions.</param>
    /// <param name="app">App the C# proxy functionsInternal was created from.</param>
    private FirebaseFunctions(FirebaseFunctionsInternal functions, FirebaseApp app,
        string region) {
      firebaseApp = app;
      firebaseApp.AppDisposed += OnAppDisposed;
      functionsInternal = functions;
      // As we know there is only one reference to the C++ firebase::functions::Functions object here
      // we'll let the proxy object take ownership so the C++ object can be deleted when the
      // proxy's Dispose() method is executed.
      functionsInternal.SetSwigCMemOwn(true);
      instanceKey = InstanceKey(app, region);
    }

    /// <summary>
    /// Remove the reference to this object from the functionsByInstanceKey dictionary.
    /// </summary>
    ~FirebaseFunctions() {
      Dispose();
    }

    void OnAppDisposed(object sender, System.EventArgs eventArgs) {
      Dispose();
    }

    // Remove the reference to this instance from functionsByInstanceKey and dispose the proxy.
    private void Dispose() {
      System.GC.SuppressFinalize(this);
      lock (functionsByInstanceKey) {
        if (functionsInternal == null) return;
        functionsByInstanceKey.Remove(instanceKey);

        functionsInternal.Dispose();
        functionsInternal = null;

        firebaseApp.AppDisposed -= OnAppDisposed;
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
    public FirebaseApp App { get { return firebaseApp; } }

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
      lock (functionsByInstanceKey) {
        var instanceKey = InstanceKey(app, region);
        FirebaseFunctions functions = null;
        if (functionsByInstanceKey.TryGetValue(instanceKey, out functions)) {
          if (functions != null) return functions;
        }

        app = app ?? FirebaseApp.DefaultInstance;
        InitResult initResult;
        FirebaseFunctionsInternal functionsInternal =
          FirebaseFunctionsInternal.GetInstanceInternal(app, region, out initResult);
        if (initResult != InitResult.Success) {
          throw new Firebase.InitializationException(
              initResult,
              Firebase.ErrorMessages.DllNotFoundExceptionErrorMessage);
        } else if (functionsInternal == null) {
          LogUtil.LogMessage(LogLevel.Warning,
              "Unable to create FirebaseFunctions.");
          return null;
        }

        functions = new FirebaseFunctions(functionsInternal, app, region);
        functionsByInstanceKey[instanceKey] = functions;
        return functions;
      }
    }

    // Throw a NullReferenceException if this proxy references a deleted object.
    private void ThrowIfNull() {
      if (functionsInternal == null ||
          FirebaseFunctionsInternal.getCPtr(functionsInternal).Handle == System.IntPtr.Zero) {
        throw new System.NullReferenceException();
      }
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a name.
    /// </summary>
    public HttpsCallableReference GetHttpsCallable(string name) {
      ThrowIfNull();
      return new HttpsCallableReference(this, functionsInternal.GetHttpsCallable(name));
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(string url) {
      ThrowIfNull();
      return new HttpsCallableReference(this, functionsInternal.GetHttpsCallableFromURL(url));
    }

    /// <summary>
    ///   Creates a <see cref="HttpsCallableReference" /> given a URL.
    /// </summary>
    public HttpsCallableReference GetHttpsCallableFromURL(Uri url) {
      ThrowIfNull();
      return GetHttpsCallableFromURL(url.ToString());
    }

    /// <summary>
    ///   Sets an origin of a Cloud Functions Emulator instance to use.
    /// </summary>
    public void UseFunctionsEmulator(string origin) {
      ThrowIfNull();
      functionsInternal.UseFunctionsEmulator(origin);
    }
  }
}
