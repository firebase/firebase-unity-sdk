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
///
/// App Check helps protect your API resources from abuse by preventing
/// unauthorized clients from accessing your backend resources.
///
/// With App Check, devices running your app will use an AppCheckProvider that
/// attests to one or both of the following:
/// * Requests originate from your authentic app
/// * Requests originate from an authentic, untampered device
public sealed class FirebaseAppCheck {
  // The C++ object that this wraps.
  private AppCheckInternal appCheckInternal;

  // Use the FirebaseApp's name instead of the App itself, to not
  // keep it alive unnecessarily.
  private static Dictionary<string, FirebaseAppCheck> appCheckMap =
    new Dictionary<string, FirebaseAppCheck>();
  // The user provided Factory.
  private static IAppCheckProviderFactory appCheckFactory;
  private static Dictionary<string, IAppCheckProvider> providerMap =
    new Dictionary<string, IAppCheckProvider>();

  // Function for C++ to call when it needs to fetch a Token.
  private static AppCheckUtil.GetTokenFromCSharpDelegate getTokenDelegate =
    new AppCheckUtil.GetTokenFromCSharpDelegate(GetTokenFromCSharpMethod);
  // Function for C++ to call when it needs to fetch a Limited Use Token.
  private static AppCheckUtil.GetTokenFromCSharpDelegate getLimitedUseTokenDelegate =
    new AppCheckUtil.GetTokenFromCSharpDelegate(GetLimitedUseTokenFromCSharpMethod);
  // Function for C++ to call when the Token changes.
  private static AppCheckUtil.TokenChangedDelegate tokenChangedDelegate =
    new AppCheckUtil.TokenChangedDelegate(TokenChangedMethod);

  // Make the constructor private, since users aren't meant to make it.
  private FirebaseAppCheck(AppCheckInternal internalObject, FirebaseApp app) {
    appCheckInternal = internalObject;
    appName = app.Name;
    // The C++ AppCheck object is deleted along with its App, so drop this
    // wrapper from the cache when the App is disposed. Otherwise, re-creating an App with
    // the same name in the same domain (e.g. Enter Play Mode with domain reload disabled,
    // or calling FirebaseApp.Dispose()) would return a wrapper around a deleted object.
    app.AppDisposed += OnAppDisposed;
  }

  // The name of the FirebaseApp this instance is associated with.
  private readonly string appName;

  private void OnAppDisposed(object sender, System.EventArgs eventArgs) {
    lock (appCheckMap) {
      FirebaseAppCheck cached;
      if (appCheckMap.TryGetValue(appName, out cached) && cached == this) {
        appCheckMap.Remove(appName);
      }
    }
    lock (providerMap) {
      // Providers are created per App, so the old one must not be reused either.
      providerMap.Remove(appName);
    }
    TokenChangedImpl = null;
    // Do not dispose: the C++ object is owned by (and deleted with) the App.
    appCheckInternal = null;
  }

  private void ThrowIfNull() {
    if (appCheckInternal == null ||
        AppCheckInternal.getCPtr(appCheckInternal).Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /// Gets the instance of FirebaseAppCheck associated with the default
  /// FirebaseApp instance.
  public static FirebaseAppCheck DefaultInstance {
    get {
      return GetInstance(FirebaseApp.DefaultInstance);
    }
  }

  /// Gets the instance of FirebaseAppCheck associated with the given
  /// FirebaseApp instance.
  public static FirebaseAppCheck GetInstance(FirebaseApp app) {
    FirebaseAppCheck result;
    lock (appCheckMap) {
      if (!appCheckMap.TryGetValue(app.Name, out result)) {
        AppCheckInternal internalObject = AppCheckInternal.GetInstance(app);
        result = new FirebaseAppCheck(internalObject, app);
        appCheckMap[app.Name] = result;
      }
    }
    return result;
  }

  /// Installs the given IAppCheckProviderFactory, overwriting any that
  /// were previously associated with this FirebaseAppCheck instance.
  ///
  /// Automatic token refreshing will only occur if the global
  /// isDataCollectionDefaultEnabled flag is set to true. To allow automatic
  /// token refreshing for Firebase App Check without changing the
  /// isDataCollectionDefaultEnabled flag for other Firebase SDKs, call
  /// SetTokenAutoRefreshEnabled(bool) after installing the factory.
  ///
  /// This method should be called before initializing the Firebase App.
  public static void SetAppCheckProviderFactory(IAppCheckProviderFactory factory) {
    if (appCheckFactory == factory) return;

    appCheckFactory = factory;
    // Clear out the Providers that were previously made. When future calls to
    // GetToken fails to find a provider in the map, it will use the new factory
    // to create a new provider.
    lock (providerMap) {
      providerMap.Clear();
    }

    // Register the callback for C++ SDK to use that will reach this factory.
    if (factory == null) {
      AppCheckUtil.SetGetTokenCallback(null, null);
    } else {
      AppCheckUtil.SetGetTokenCallback(getTokenDelegate, getLimitedUseTokenDelegate);
    }
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
    return appCheckInternal.GetAppCheckTokenAsync(forceRefresh).ContinueWith(task => {
      if (task.IsFaulted) {
        throw task.Exception;
      }
      AppCheckTokenInternal tokenInternal = task.Result;
      return AppCheckToken.FromAppCheckTokenInternal(tokenInternal);
    });
  }

  /// Requests a Firebase App Check token. This method should be used ONLY if you need to authorize
  /// requests to a non-Firebase backend.
  ///
  /// Returns limited-use tokens that are intended for use with your non-Firebase backend endpoints
  /// that are protected with Replay Protection. This method does
  /// not affect the token generation behavior of the GetAppCheckTokenAsync() method.
  public System.Threading.Tasks.Task<AppCheckToken>
      GetLimitedUseAppCheckTokenAsync() {
    ThrowIfNull();
    return appCheckInternal.GetLimitedUseAppCheckTokenAsync().ContinueWith(task => {
      if (task.IsFaulted) {
        throw task.Exception;
      }
      AppCheckTokenInternal tokenInternal = task.Result;
      return AppCheckToken.FromAppCheckTokenInternal(tokenInternal);
    });
  }

  /// Called on the client when an AppCheckToken is created or changed.
  private event EventHandler<TokenChangedEventArgs> TokenChangedImpl;
  public event EventHandler<TokenChangedEventArgs> TokenChanged {
    add {
      ThrowIfNull();
      // If this is the first listener, hook into C++.
      if (TokenChangedImpl == null ||
          TokenChangedImpl.GetInvocationList().Length == 0) {
        AppCheckUtil.SetTokenChangedCallback(appCheckInternal, tokenChangedDelegate);
      }

      TokenChangedImpl += value;
    }
    remove {
      ThrowIfNull();
      TokenChangedImpl -= value;

      // If that was the last listener, remove the C++ hooks.
      if (TokenChangedImpl == null ||
          TokenChangedImpl.GetInvocationList().Length == 0) {
        AppCheckUtil.SetTokenChangedCallback(appCheckInternal, null);
      }
    }
  }

  internal void OnTokenChanged(AppCheckToken token) {
    EventHandler<TokenChangedEventArgs> handler = TokenChangedImpl;
    if (handler != null) {
      handler(this, new TokenChangedEventArgs() { Token = token });
    }
  }

  [MonoPInvokeCallback(typeof(AppCheckUtil.GetTokenFromCSharpDelegate))]
  private static void GetTokenFromCSharpMethod(string appName, int key) {
    if (appCheckFactory == null) {
      AppCheckUtil.FinishGetTokenCallback(key, "", 0,
        (int)AppCheckError.InvalidConfiguration,
        "Missing IAppCheckProviderFactory.");
      return;
    }
    FirebaseApp app = FirebaseApp.GetInstance(appName);
    if (app == null) {
      AppCheckUtil.FinishGetTokenCallback(key, "", 0,
        (int)AppCheckError.Unknown,
        "Unable to find App with name: " + appName);
      return;
    }
    IAppCheckProvider provider;
    lock (providerMap) {
      if (!providerMap.TryGetValue(app.Name, out provider)) {
        provider = appCheckFactory.CreateProvider(app);
        if (provider == null) {
          AppCheckUtil.FinishGetTokenCallback(key, "", 0,
            (int)AppCheckError.InvalidConfiguration,
            "Failed to create IAppCheckProvider for App: " + appName);
          return;
        }
        providerMap[app.Name] = provider;
      }
    }
    provider.GetTokenAsync().ContinueWith(task => {
      if (task.IsFaulted) {
        AppCheckUtil.FinishGetTokenCallback(key, "", 0,
          (int)AppCheckError.Unknown,
          "Provider returned an Exception: " + task.Exception);
      } else {
        AppCheckToken token = task.Result;
        AppCheckUtil.FinishGetTokenCallback(key, token.Token,
          token.ExpireTimeMs, 0, "");
      }
    });
  }

  [MonoPInvokeCallback(typeof(AppCheckUtil.GetTokenFromCSharpDelegate))]
  private static void GetLimitedUseTokenFromCSharpMethod(string appName, int key) {
    if (appCheckFactory == null) {
      AppCheckUtil.FinishGetTokenCallback(key, "", 0,
        (int)AppCheckError.InvalidConfiguration,
        "Missing IAppCheckProviderFactory.");
      return;
    }
    FirebaseApp app = FirebaseApp.GetInstance(appName);
    if (app == null) {
      AppCheckUtil.FinishGetTokenCallback(key, "", 0,
        (int)AppCheckError.Unknown,
        "Unable to find App with name: " + appName);
      return;
    }
    IAppCheckProvider provider;
    lock (providerMap) {
      if (!providerMap.TryGetValue(app.Name, out provider)) {
        provider = appCheckFactory.CreateProvider(app);
        if (provider == null) {
          AppCheckUtil.FinishGetTokenCallback(key, "", 0,
            (int)AppCheckError.InvalidConfiguration,
            "Failed to create IAppCheckProvider for App: " + appName);
          return;
        }
        providerMap[app.Name] = provider;
      }
    }

    provider.GetLimitedUseTokenAsync().ContinueWith(task => {
      if (task.IsFaulted) {
        AppCheckUtil.FinishGetTokenCallback(key, "", 0,
          (int)AppCheckError.Unknown,
          "Provider returned an Exception: " + task.Exception);
      } else {
        AppCheckToken token = task.Result;
        AppCheckUtil.FinishGetTokenCallback(key, token.Token,
          token.ExpireTimeMs, 0, "");
      }
    });
  }

  [MonoPInvokeCallback(typeof(AppCheckUtil.TokenChangedDelegate))]
  private static void TokenChangedMethod(string appName, System.IntPtr tokenCPtr) {
    AppCheckTokenInternal tokenInternal = new AppCheckTokenInternal(tokenCPtr, false);
    AppCheckToken token = AppCheckToken.FromAppCheckTokenInternal(tokenInternal);

    FirebaseAppCheck appCheck;
    lock (appCheckMap) {
      appCheckMap.TryGetValue(appName, out appCheck);
    }
    if (appCheck != null) {
      appCheck.OnTokenChanged(token);
    }
  }
}

}
