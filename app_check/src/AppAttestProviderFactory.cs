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

/// Implementation of an IAppCheckProviderFactory that builds
/// providering using the App Attest service. To be used
/// when targeting the iOS platform.
public sealed class AppAttestProviderFactory : IAppCheckProviderFactory {
  // The static Factory singleton
  private static AppAttestProviderFactory s_factoryInstance;

  // The C++ factory that this class wraps
  private AppAttestProviderFactoryInternal factoryInternal;

  // Map from App names to Providers already created by the factory
  private Dictionary<string, BuiltInProviderWrapper> providerMap = new Dictionary<string, BuiltInProviderWrapper>();

  // Private constructor as users are meant to use the GetInstance function.
  private AppAttestProviderFactory(AppAttestProviderFactoryInternal factoryInternal) {
    this.factoryInternal = factoryInternal;
  }

  private void ThrowIfNull() {
    if (factoryInternal == null ||
        AppAttestProviderFactoryInternal.getCPtr(factoryInternal).Handle == System.IntPtr.Zero) {
      throw new System.NullReferenceException();
    }
  }

  /**
  * Gets an instance of this class for installation into a
  * FirebaseAppCheck instance.
  */
  public static AppAttestProviderFactory Instance {
    get {
      if (s_factoryInstance != null) {
        return s_factoryInstance;
      }

      // Get the C++ Factory, and wrap it
      AppAttestProviderFactoryInternal factoryInternal = AppAttestProviderFactoryInternal.GetInstance();
      // The returned factory can be null, if the platform isn't supported.
      if (factoryInternal == null) {
        throw new FirebaseException((int)AppCheckError.UnsupportedProvider,
          "App Attest is only supported on iOS+ platforms.");
      }
      s_factoryInstance = new AppAttestProviderFactory(factoryInternal);
      return s_factoryInstance;
    }
  }

  /**
  * Gets the IAppCheckProvider associated with the given
  * FirebaseApp instance, or creates one if none already exists.
  */
  public IAppCheckProvider CreateProvider(FirebaseApp app) {
    BuiltInProviderWrapper provider;
    if (providerMap.TryGetValue(app.Name, out provider)) {
      return provider;
    }

    ThrowIfNull();
    AppCheckProviderInternal providerInternal = factoryInternal.CreateProvider(app);
    provider = new BuiltInProviderWrapper(providerInternal);
    providerMap[app.Name] = provider;
    return provider;
  }
}

}
