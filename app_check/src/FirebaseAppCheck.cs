/*
 * Copyright 2023 Google LLC
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

namespace Firebase.AppCheck {

/// @brief Firebase App Check object.
public sealed class FirebaseAppCheck {
  // The C++ object that this wraps.
  private AppCheckInternal appCheckInternal;

  private static Dictionary<FirebaseApp, FirebaseAppCheck> appCheckMap =
    new Dictionary<FirebaseApp, FirebaseAppCheck>();
  // The user provided Factory.
  private static IAppCheckProviderFactory appCheckFactory;
  private static Dictionary<FirebaseApp, IAppCheckProvider> providerMap =
    new Dictionary<FirebaseApp, IAppCheckProvider>();

  // Make the constructor private, since users aren't meant to make it.
  private FirebaseAppCheck(AppCheckInternal internalObject) {
    appCheckInternal = internalObject;
  }

  private void ThrowIfNull() {
    if (appCheckInternal == null ||
        AppCheckInternal.getCPtr(appCheckInternal).Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /// Gets the instance of FirebaseAppCheck associated with the default
  /// {@link FirebaseApp} instance.
  public static FirebaseAppCheck DefaultInstance {
    get {
      return GetInstance(FirebaseApp.DefaultInstance);
    }
  }

  /// Gets the instance of FirebaseAppCheck associated with the given
  /// {@link FirebaseApp} instance.
  public static FirebaseAppCheck GetInstance(FirebaseApp app) {
    FirebaseAppCheck result;
    if (!appCheckMap.TryGetValue(app, out result)) {
      AppCheckInternal internalObject = AppCheckInternal.GetInstance(app);
      result = new FirebaseAppCheck(internalObject);
      appCheckMap[app] = result;
      // TODO(amaurice): Logic to remove from map when App is destroyed?
    }
    return result;
  }

  /// Installs the given {@link AppCheckProviderFactory}, overwriting any that
  /// were previously associated with this {@code FirebaseAppCheck} instance.
  /// Any {@link AppCheckTokenListener}s attached to this
  /// {@code FirebaseAppCheck} instance will be transferred from existing
  /// factories to the newly installed one.
  ///
  /// <p>Automatic token refreshing will only occur if the global {@code
  /// isDataCollectionDefaultEnabled} flag is set to true. To allow automatic
  /// token refreshing for Firebase App Check without changing the {@code
  /// isDataCollectionDefaultEnabled} flag for other Firebase SDKs, call
  /// {@link #setTokenAutoRefreshEnabled(bool)} after installing the {@code
  /// factory}.
  ///
  /// This method should be called before initializing the Firebase App.
  public static void SetAppCheckProviderFactory(IAppCheckProviderFactory factory) {
    appCheckFactory = factory;
    // TODO(amaurice): Clear the provider map when the factory changes?
  }

  /// Sets the {@code isTokenAutoRefreshEnabled} flag.
  public void SetTokenAutoRefreshEnabled(bool isTokenAutoRefreshEnabled) {
    ThrowIfNull();
    appCheckInternal.SetTokenAutoRefreshEnabled(isTokenAutoRefreshEnabled);
  }

  /// Requests a Firebase App Check token. This method should be used ONLY if you
  /// need to authorize requests to a non-Firebase backend. Requests to Firebase
  /// backends are authorized automatically if configured.
  public System.Threading.Tasks.Task<AppCheckToken>
      GetAppCheckTokenAsync(bool forceRefresh) {
    ThrowIfNull();
    throw new NotImplementedException();
  }

  /// Called on the client when an AppCheckToken is created or changed.
  public System.EventHandler<TokenChangedEventArgs> TokenChanged;
}

}
