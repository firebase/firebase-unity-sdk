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
using System.Threading;
using Firebase.AppCheck.Internal;
using Firebase.Internal;

namespace Firebase.AppCheck {

  /// Implementation of an IAppCheckProviderFactory that builds
  /// DebugAppCheckProviders.
  ///
  /// DebugAppCheckProvider can exchange a debug token registered in the Firebase
  /// console for a Firebase App Check token. The debug provider is designed to
  /// enable testing applications on a simulator or test environment.
  ///
  /// NOTE: Do not use the debug provider in applications used by real users.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class DebugAppCheckProviderFactory : IAppCheckProviderFactory {
    // The static Factory singleton
    private static DebugAppCheckProviderFactory s_instance;

    // Map from App names to Providers already created by the factory
    private readonly FirebaseInstanceCache<DebugAppCheckProvider> _providers = new(p => p.Dispose());

    private volatile string _debugToken;

    // Private constructor as users are meant to use the GetInstance function.
    private DebugAppCheckProviderFactory() { }

    internal string DebugToken => _debugToken;

    /**
    * Gets an instance of this class for installation into a
    * FirebaseAppCheck instance.
    */
    public static DebugAppCheckProviderFactory Instance => LazyInitializer.EnsureInitialized(ref s_instance, () => new DebugAppCheckProviderFactory());

    /**
    * Gets the IAppCheckProvider associated with the given
    * FirebaseApp instance, or creates one if none already exists.
    */
    public IAppCheckProvider CreateProvider(FirebaseApp app) =>
        app == null ? throw new ArgumentNullException(nameof(app)) : _providers.GetOrCreate(app, () => new DebugAppCheckProvider(app, this));

    /**
    * Sets the Debug Token to use when communicating with the backend.
    * This should match a debug token set in the Firebase console for your
    * App.
    */
    public void SetDebugToken(string token) => _debugToken = token ?? string.Empty;
  }

  /// Implementation of an IAppCheckProviderFactory that builds
  /// providering using the Play Integrity service. To be used
  /// when targeting the Android platform.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class PlayIntegrityProviderFactory : IAppCheckProviderFactory {
    // The static Factory singleton
    private static PlayIntegrityProviderFactory s_instance;

    // Map from App names to Providers already created by the factory
    private readonly FirebaseInstanceCache<IAppCheckProvider> _providers = new(p => (p as IDisposable)?.Dispose());

    // Private constructor as users are meant to use the GetInstance function.
    private PlayIntegrityProviderFactory() { }

    /**
    * Gets an instance of this class for installation into a
    * FirebaseAppCheck instance.
    */
    public static PlayIntegrityProviderFactory Instance => MobileAppCheckBridge.GetAndroidFactory(ref s_instance, () => new PlayIntegrityProviderFactory());

    /**
    * Gets the IAppCheckProvider associated with the given
    * FirebaseApp instance, or creates one if none already exists.
    */
    public IAppCheckProvider CreateProvider(FirebaseApp app) => MobileAppCheckBridge.GetAndroidProvider(_providers, app);
  }

  /// Implementation of an IAppCheckProviderFactory that builds
  /// providering using the App Attest service. To be used
  /// when targeting the iOS platform.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class AppAttestProviderFactory : IAppCheckProviderFactory {
    // The static Factory singleton
    private static AppAttestProviderFactory s_instance;

    // Map from App names to Providers already created by the factory
    private readonly FirebaseInstanceCache<IAppCheckProvider> _providers = new(p => (p as IDisposable)?.Dispose());

    // Private constructor as users are meant to use the GetInstance function.
    private AppAttestProviderFactory() { }

    /**
    * Gets an instance of this class for installation into a
    * FirebaseAppCheck instance.
    */
    public static AppAttestProviderFactory Instance => MobileAppCheckBridge.GetIosFactory(ref s_instance, () => new AppAttestProviderFactory(), "App Attest");

    /**
    * Gets the IAppCheckProvider associated with the given
    * FirebaseApp instance, or creates one if none already exists.
    */
    public IAppCheckProvider CreateProvider(FirebaseApp app) => MobileAppCheckBridge.GetIosProvider(_providers, app, "App Attest", "FIRAppAttestProvider");
  }

  /// Implementation of an IAppCheckProviderFactory that builds
  /// providering using the Device Check service. To be used
  /// when targeting the iOS platform.
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class DeviceCheckProviderFactory : IAppCheckProviderFactory {
    // The static Factory singleton
    private static DeviceCheckProviderFactory s_instance;

    // Map from App names to Providers already created by the factory
    private readonly FirebaseInstanceCache<IAppCheckProvider> _providers = new(p => (p as IDisposable)?.Dispose());

    // Private constructor as users are meant to use the GetInstance function.
    private DeviceCheckProviderFactory() { }

    /**
    * Gets an instance of this class for installation into a
    * FirebaseAppCheck instance.
    */
    public static DeviceCheckProviderFactory Instance => MobileAppCheckBridge.GetIosFactory(ref s_instance, () => new DeviceCheckProviderFactory(), "Device Check");

    /**
    * Gets the IAppCheckProvider associated with the given
    * FirebaseApp instance, or creates one if none already exists.
    */
    public IAppCheckProvider CreateProvider(FirebaseApp app) => MobileAppCheckBridge.GetIosProvider(_providers, app, "Device Check", "FIRDeviceCheckProvider");
  }
}
