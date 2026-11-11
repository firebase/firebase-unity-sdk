// Copyright 2026 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Firebase {

internal enum GooglePlayServicesAvailability {
  AvailabilityAvailable = 0,
  AvailabilityUnavailableDisabled = 1,
  AvailabilityUnavailableInvalid = 2,
  AvailabilityUnavailableMissing = 3,
  AvailabilityUnavailablePermissions = 4,
  AvailabilityUnavailableUpdateRequired = 5,
  AvailabilityUnavailableUpdating = 6,
  AvailabilityUnavailableOther = 7,
}

internal static class AppUtil {
  internal static void PollCallbacks() { }
}

/// <summary>
/// Firebase application object.
///
/// FirebaseApp acts as a conduit for communication between all Firebase
/// services used by an application. A default instance is created
/// automatically, based on settings in your Firebase configuration file,
/// and all of the Firebase APIs connect with it automatically.
/// </summary>
#if UNITY_6000_6_OR_NEWER
[global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
public sealed class FirebaseApp : global::System.IDisposable {
  private const string DefaultAppNameConst = "__FIRAPP_DEFAULT";
  // Used to indicate that no thread is currently running CheckDependencies.
  private const int CheckDependenciesNoThread = -1;
  // Used to indicate that a call to CheckDependencies is occurring, but the
  // thread has not yet been created to perform it.
  private const int CheckDependenciesPendingThread = -2;

  // Global lock to serialize Dispose() methods.
  internal static readonly object disposeLock = new object();
  private static readonly object _registryLock = new object();
  private static readonly Dictionary<string, FirebaseApp> _apps =
      new Dictionary<string, FirebaseApp>(StringComparer.Ordinal);
  // Lock for _checkDependenciesThread, as CheckDependencies could be incorrectly
  // called multiple times.
  private static readonly object _checkDepsLock = new object();

  private static volatile int _logLevel = (int)LogLevel.Info;
  // Used to determine which thread is running CheckDependencies.
  private static int _checkDependenciesThread = CheckDependenciesNoThread;
  // Whether Crashlytics initialization has been attempted.
  private static bool _crashlyticsInitAttempted;
  private static volatile int _unityMainThreadId;

  private readonly string _name;
  private readonly AppOptions _options;
  private Platform.FirebaseAppPlatform _appPlatform;
  private EventHandler _appDisposed;
  private volatile bool _disposed;
  private volatile bool _dataCollectionDefaultEnabled = true;

  /// <summary>Configuration parameters used to register an optional Firebase module.</summary>
  public class EnableModuleParams {
    /// <summary>Gets or sets the C++ module name.</summary>
    public string CppModuleName { get; set; }
    /// <summary>Gets or sets the name of the C# class that is required to interact with the module.</summary>
    public string CSharpClassName { get; set; }
    /// <summary>Gets or sets whether to always enable the module regardless of the C# class being present.</summary>
    public bool AlwaysEnable { get; set; }

    /// <summary>Initializes a new instance of <see cref="EnableModuleParams" />.</summary>
    /// <param name="csharp">Name of the C# class that is required to interact with the module.</param>
    /// <param name="cpp">Name of the C++ module.</param>
    /// <param name="always">Whether to always enable the module regardless of the C# class being present.</param>
    public EnableModuleParams(string csharp, string cpp, bool always = false) {
      CSharpClassName = csharp;
      CppModuleName = cpp;
      AlwaysEnable = always;
    }
  }

  // Static constructor to initialize C# logging.
  static FirebaseApp() => LogUtil.InitializeLogging();

  private FirebaseApp(string name, AppOptions options, bool dataCollectionEnabled) {
    _name = name;
    _options = options;
    _dataCollectionDefaultEnabled = dataCollectionEnabled;
  }

  [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
  private static void OnSubsystemRegistration() {
    CaptureMainThreadContext();
    DisposeAllApps();
  }

  /// Gets the default name for FirebaseApp objects.
  public static string DefaultName => DefaultAppNameConst;

  /// @brief Get the default FirebaseApp instance.
  ///
  /// @return Reference to the default app, if it hasn't been created this
  /// method will create it.
  public static FirebaseApp DefaultInstance => GetInstance(DefaultName) ?? Create();

  /// @brief Gets or sets the minimum log verbosity level for Firebase features.
  ///
  /// - LogLevel.Verbose allows all log messages to be displayed.
  /// - LogLevel.Assert prevents displaying all but fatal errors.
  ///
  /// @note Some Firebase plugins may require you to set their LogLevel separately.
  ///
  /// @return The current LogLevel.
  public static LogLevel LogLevel { get => (LogLevel)_logLevel; set => _logLevel = (int)value; }

  /// Get the name of this App instance.
  ///
  /// @returns The name of this App instance.  If a name wasn't provided via
  /// Create(), this returns @ref DefaultName.
  public string Name => _name;

  /// @brief Get the AppOptions the FirebaseApp was created with.
  /// @returns AppOptions used to create the FirebaseApp.
  public AppOptions Options => _disposed ? throw new NullReferenceException("App has been disposed") : _options.Clone();

  /// @brief Event raised when FirebaseApp is disposed.
  internal event EventHandler AppDisposed {
    add { lock (_registryLock) { if (!_disposed) _appDisposed += value; } }
    remove { lock (_registryLock) { _appDisposed -= value; } }
  }

  /// Gets or sets whether automatic data collection is enabled for all
  /// products.
  ///
  /// By default, automatic data collection is enabled. To disable automatic
  /// data collection in your mobile app, add to your Android application's
  /// manifest:
  ///
  /// @if NOT_DOXYGEN
  ///   <meta-data android:name="firebase_data_collection_default_enabled"
  ///   android:value="false" />
  /// @else
  /// @code
  ///   &lt;meta-data android:name="firebase_data_collection_default_enabled"
  ///   android:value="false" /&gt;
  /// @endcode
  /// @endif
  ///
  /// or on iOS or tvOS to your Info.plist:
  ///
  /// @if NOT_DOXYGEN
  ///   <key>FirebaseDataCollectionDefaultEnabled</key>
  ///   <false/>
  /// @else
  /// @code
  ///   &lt;key&gt;FirebaseDataCollectionDefaultEnabled&lt;/key&gt;
  ///   &lt;false/&gt;
  /// @endcode
  /// @endif
  ///
  /// Once your mobile app is set to disable automatic data collection, you can
  /// ask users to consent to data collection, and then enable it after their
  /// approval by setting this property.
  ///
  /// This value is persisted across runs of the app so that it can be set once
  /// when users have consented to collection.
  internal bool IsDataCollectionDefaultEnabled {
    get => _dataCollectionDefaultEnabled;
    set {
      _dataCollectionDefaultEnabled = value;
      MobileAppBridge.TrySetDataCollectionDefaultEnabled(_name, value);
    }
  }

  internal Platform.FirebaseAppPlatform AppPlatform {
    get { lock (typeof(Platform.FirebaseAppPlatform)) return _appPlatform ??= new Platform.FirebaseAppPlatform(this); }
  }

  // Get the synchronization context for the current thread.
  // We do not use SynchronizationContext.Current as it's possible
  // The thread is not managed by Firebase in which case we could be
  // scheduling operations in a way that doesn't conform to our requirements.
  // For example, Unity 2017 installs a SynchronizationContext that stops
  // excecuting tasks when Time.timeScale is 0.
  internal static SynchronizationContext ThreadSynchronizationContext =>
      Platform.FirebaseHandler.DefaultInstance?.IsMainThread() == true
          ? Platform.PlatformInformation.SynchronizationContext
          : null;

  // On desktop, when FirebaseApp.Create() is invoked without parameters, it
  // searches for config files such as 'google-services-desktop.json'.
  // This function sets the directory location to search in.
  internal static void SetDefaultConfigPath(string path) => AppConfigLoader.SetDefaultConfigPath(path);

  /// @brief Get an instance of an app by name.
  ///
  /// @param name Name of the app to retrieve.
  ///
  /// @return Reference to the app if it was previously created, null otherwise.
  public static FirebaseApp GetInstance(string name) {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    if (name == null) throw new ArgumentNullException(nameof(name));
    lock (_registryLock) return _apps.TryGetValue(name, out FirebaseApp app) && !app._disposed ? app : null;
  }

  /// @brief Initializes the default FirebaseApp with default options.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create() {
    ThrowIfCheckDependenciesRunning();
    EnsurePlatformHandlerSafe(fullSetup: true);
    lock (_registryLock) {
      if (_apps.TryGetValue(DefaultName, out FirebaseApp existing) && !existing._disposed) return existing;
    }
    AppOptions loaded = AppConfigLoader.TryLoadDefaultOptions()
        ?? throw new InitializationException(
            InitResult.FailedMissingDependency,
            "Firebase app creation failed: unable to load default options from " +
            "google-services-desktop.json, google-services.json, or GoogleService-Info.plist.");
    return CreateCore(loaded, DefaultName, mergeDefaults: false);
  }

  /// @brief Initializes the default FirebaseApp with the given options.
  ///
  /// @param[in] options Options that control the creation of the FirebaseApp.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create(AppOptions options) {
    if (options == null) throw new ArgumentNullException(nameof(options));
    return CreateCore(options, DefaultName, mergeDefaults: true);
  }

  /// @brief Initializes a FirebaseApp with the given options that operate
  /// on the named app.
  ///
  /// @param[in] options Options that control the creation of the FirebaseApp.
  /// @param[in] name Name of this FirebaseApp instance.  This is only
  /// required when one application uses multiple FirebaseApp instances.
  ///
  /// @returns New FirebaseApp instance.
  public static FirebaseApp Create(AppOptions options, string name) {
    if (options == null) throw new ArgumentNullException(nameof(options));
    return CreateCore(options, string.IsNullOrEmpty(name) ? DefaultName : name, mergeDefaults: true);
  }

  private static FirebaseApp CreateCore(AppOptions inputOptions, string resolvedName, bool mergeDefaults) {
    // Validate CheckDependencies is not running before obtaining the lock.
    ThrowIfCheckDependenciesRunning();
    EnsurePlatformHandlerSafe(fullSetup: true);
    lock (_registryLock) {
      if (_apps.TryGetValue(resolvedName, out FirebaseApp hit) && !hit._disposed) return hit;
    }

    AppOptions effective = inputOptions.Clone();
    if (mergeDefaults &&
        (string.IsNullOrEmpty(effective.AppId) ||
         string.IsNullOrEmpty(effective.ApiKey) ||
         string.IsNullOrEmpty(effective.ProjectId))) {
      AppOptions defaults = AppConfigLoader.TryLoadDefaultOptions();
      if (defaults != null) {
        if (string.IsNullOrEmpty(effective.AppId)) effective.AppId = defaults.AppId;
        if (string.IsNullOrEmpty(effective.ApiKey)) effective.ApiKey = defaults.ApiKey;
        if (string.IsNullOrEmpty(effective.ProjectId)) effective.ProjectId = defaults.ProjectId;
      }
    }

    bool dataCollectionEnabled = true;
    MobileAppBridge.TryInitializeNativeApp(resolvedName, effective, ref dataCollectionEnabled);

    FirebaseApp created;
    bool initCrashlytics = false;
    lock (_registryLock) {
      if (_apps.TryGetValue(resolvedName, out FirebaseApp existing) && !existing._disposed) {
        return existing;
      }
      created = new FirebaseApp(resolvedName, effective, dataCollectionEnabled);
      _apps[resolvedName] = created;
      if (!_crashlyticsInitAttempted && !IsCheckDependenciesRunning()) {
        _crashlyticsInitAttempted = true;
        initCrashlytics = true;
      }
    }
    // We initialize Crashlytics outside the lock because Crashlytics gets a
    // reference to FirebaseApp DefaultInstance.
    if (initCrashlytics) TryInitializeCrashlyticsNonBlocking();
    return created;
  }

  /// <summary>Releases this <see cref="FirebaseApp" /> instance and notifies dependent services.</summary>
  public void Dispose() {
    Dispose(true);
    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Releases this <see cref="FirebaseApp" /> instance and optionally notifies dependent services.
  /// </summary>
  /// <param name="disposing">
  /// <c>true</c> to notify dependent services via <c>AppDisposed</c>; <c>false</c> to skip event notifications.
  /// </param>
  public void Dispose(bool disposing) {
    ThrowIfCheckDependenciesRunning();
    EventHandler handlers;
    lock (_registryLock) {
      if (_disposed) return;
      _disposed = true;
      if (_apps.TryGetValue(_name, out FirebaseApp cur) && ReferenceEquals(cur, this)) {
        _apps.Remove(_name);
      }
      if (_apps.Count == 0) _crashlyticsInitAttempted = false;
      // Raise AppDisposed event once when FirebaseApp is disposed.
      handlers = _appDisposed;
      _appDisposed = null;
    }
    // Only raise events when disposing is true.
    if (disposing && handlers != null) {
      handlers(this, EventArgs.Empty);
    }
  }

  // Dispose all created apps.
  internal static void DisposeAllApps() {
    List<FirebaseApp> snapshot;
    lock (_registryLock) {
      if (_apps.Count == 0) return;
      snapshot = new List<FirebaseApp>(_apps.Values);
    }
    foreach (FirebaseApp app in snapshot) app.Dispose();
  }

  /// @brief Asynchronously checks if all of the necessary dependencies for
  /// Firebase are present on the system, and in the necessary state.
  ///
  /// @note In some cases, this operation can take a long time. It's recommended
  /// to perform other application specific tasks in parallel while checking
  /// dependencies for Firebase.
  ///
  /// @return A Task that on completion will contain the DependencyStatus enum
  /// value, indicating the state of the required dependencies.
  public static Task<DependencyStatus> CheckDependenciesAsync() {
    EnsurePlatformHandlerSafe(fullSetup: false);
    if (!MobileAppBridge.UsesAsyncAndroidDependencyCheck()) {
      return Task.FromResult(DependencyStatus.Available);
    }
    SetCheckDependenciesThread(CheckDependenciesPendingThread);
    return Task.Run(() => {
      // Set the thread id, so that this thread can access and create a
      // FirebaseApp.
      SetCheckDependenciesThread(Thread.CurrentThread.ManagedThreadId);
      try {
        return MobileAppBridge.CheckDependencies();
      } finally {
        // Finished running CheckDependencies, so clear the thread for others.
        SetCheckDependenciesThread(CheckDependenciesNoThread);
      }
    });
  }

  /// @brief Asynchronously checks if all of the necessary dependencies for
  /// Firebase are present on the system, and in the necessary state and
  /// attempts to fix them if they are not.
  ///
  /// @note In some cases, this operation can take a long time and in some cases
  /// may prompt the user to update other services on the device. It's recommended
  /// to perform other application specific tasks in parallel while checking and
  /// potentially fixing dependencies for Firebase.
  ///
  /// If it's appropriate for your app to handle checking and fixing dependencies
  /// separately, you can. Here's effectively what CheckAndFixDependenciesAsync
  /// does:
  ///
  /// @code{.cs}
  ///   using System.Threading.Tasks;  // Needed for the Unwrap extension method.
  ///
  ///   // ...
  ///
  ///   Firebase.FirebaseApp.CheckDependenciesAsync().ContinueWith(checkTask => {
  ///     // Peek at the status and see if we need to try to fix dependencies.
  ///     Firebase.DependencyStatus status = checkTask.Result;
  ///     if (status != Firebase.DependencyStatus.Available) {
  ///       return Firebase.FirebaseApp.FixDependenciesAsync().ContinueWith(t => {
  ///         return Firebase.FirebaseApp.CheckDependenciesAsync();
  ///       }).Unwrap();
  ///     } else {
  ///       return checkTask;
  ///     }
  ///   }).Unwrap().ContinueWith(task => {
  ///     dependencyStatus = task.Result;
  ///     if (dependencyStatus == Firebase.DependencyStatus.Available) {
  ///       // TODO: Continue with Firebase initialization.
  ///     } else {
  ///       Debug.LogError(
  ///         "Could not resolve all Firebase dependencies: " + dependencyStatus);
  ///     }
  ///   });
  /// @endcode
  ///
  /// @return A Task that on completion will contain the DependencyStatus enum
  /// value, indicating the state of the required dependencies.
  public static async Task<DependencyStatus> CheckAndFixDependenciesAsync() {
    DependencyStatus status = await CheckDependenciesAsync().ConfigureAwait(false);
    if (status == DependencyStatus.Available) return status;
    await FixDependenciesAsync().ConfigureAwait(false);
    return await CheckDependenciesAsync().ConfigureAwait(false);
  }

  /// @brief Attempts to fix any missing dependencies that would prevent Firebase
  /// from working on the system.
  ///
  /// Since this function is asynchronous, the returned Task must be monitored
  /// in order to tell when it has completed.  Also note, that depending on the
  /// fixes necessary, the user may be prompted for additional input.
  ///
  /// @return System.Threading.Tasks.Task A task that tracks the progress of
  /// the fix.
  public static Task FixDependenciesAsync() {
    EnsurePlatformHandlerSafe(fullSetup: false);
    return MobileAppBridge.FixDependenciesAsync();
  }

  // Execute a closure rethrowing DllNotFoundException (including via
  // TypeInitializationException) as a Firebase.InitializationException.
  internal static void TranslateDllNotFoundException(Action closureToExecute) {
    try {
      closureToExecute();
    } catch (Exception ex) when (ex.GetBaseException() is DllNotFoundException) {
      throw new InitializationException(InitResult.FailedMissingDependency, ErrorMessages.DllNotFoundExceptionErrorMessage, ex);
    }
  }

  // Convert a URL string to System.Uri returning null if the string is empty / an invalid Uri.
  internal static Uri UrlStringToUri(string urlString) =>
      !string.IsNullOrEmpty(urlString) && Uri.TryCreate(urlString, UriKind.Absolute, out Uri parsed) ? parsed : null;

  // Convert a System.Uri to a URL string.  If the uri is null this returns an empty string.
  internal static string UriToUrlString(Uri uri) => uri != null ? uri.OriginalString : string.Empty;

  // Get the target of a WeakReference returning null if the handle is invalid.
  // This will also return null if the specified reference is null.
  internal static object WeakReferenceGetTarget(WeakReference weakReference) {
    if (weakReference == null) return null;
    try { return weakReference.Target; }
    catch (InvalidOperationException) {
      // WeakReference already finalized.
      return null;
    }
  }

  // Set the thread id to indicate if something is running CheckDependencies.
  // Throws an exception if trying to set the value from an invalid context.
  private static void SetCheckDependenciesThread(int threadId) {
    lock (_checkDepsLock) {
      if (_checkDependenciesThread != CheckDependenciesNoThread &&
          _checkDependenciesThread != CheckDependenciesPendingThread &&
          _checkDependenciesThread != Thread.CurrentThread.ManagedThreadId) {
        throw new InvalidOperationException("Don't call other Firebase functions while CheckDependencies is running.");
      }
      _checkDependenciesThread = threadId;
    }
  }

  // While CheckDependencies is running, no other Firebase calls should
  // be occurring.
  private static void ThrowIfCheckDependenciesRunning() {
    lock (_checkDepsLock) {
      if (_checkDependenciesThread != CheckDependenciesNoThread &&
          _checkDependenciesThread != Thread.CurrentThread.ManagedThreadId) {
        throw new InvalidOperationException("Don't call Firebase functions before CheckDependencies has finished");
      }
    }
  }

  // Checks if CheckDependencies is running on any thread.
  private static bool IsCheckDependenciesRunning() {
    lock (_checkDepsLock) return _checkDependenciesThread != CheckDependenciesNoThread;
  }

  private static void CaptureMainThreadContext() {
    _unityMainThreadId = Thread.CurrentThread.ManagedThreadId;
    MobileAppBridge.CaptureMainThread();
    AppConfigLoader.TryWarmUnityPathsOnMainThread();
  }

  private static void EnsurePlatformHandlerSafe(bool fullSetup) {
    var existing = Platform.FirebaseHandler.DefaultInstance;
    if (existing == null && _unityMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != _unityMainThreadId) return;
    if (existing?.IsMainThread() == true) CaptureMainThreadContext();
    if (existing != null && !fullSetup) return;
    try {
      if (fullSetup) Platform.FirebaseHandler.Create(Platform.FirebaseAppUtils.Instance);
      else Platform.FirebaseHandler.CreatePartialOnMainThread(Platform.FirebaseAppUtils.Instance);
      if (Platform.FirebaseHandler.DefaultInstance?.IsMainThread() == true) CaptureMainThreadContext();
    } catch (InitializationException) {
      throw;
    } catch (Exception) {
      // Background thread before main-thread init, or headless .NET runtime.
    }
  }

  // Initialize Crashlytics via reflection without blocking app creation.
  private static void TryInitializeCrashlyticsNonBlocking() {
    MethodInfo initMethod;
    try {
      initMethod = Type.GetType("Firebase.Crashlytics.Crashlytics, Firebase.Crashlytics", throwOnError: false)
          ?.GetMethod("Initialize", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
    } catch (Exception) {
      return;
    }
    if (initMethod == null) return;
    if (Platform.FirebaseHandler.DefaultInstance?.IsMainThread() != false) {
      InvokeCrashlyticsInit(initMethod);
    } else {
      // Crashlytics initialization must be run on the Unity main thread.
      _ = Platform.FirebaseHandler.RunOnMainThreadAsync(() => { InvokeCrashlyticsInit(initMethod); return true; });
    }
  }

  private static void InvokeCrashlyticsInit(MethodInfo initMethod) {
    try {
      initMethod.Invoke(null, null);
    } catch (Exception ex) {
      LogUtil.LogMessage(LogLevel.Warning, $"Crashlytics initialization failed: {ex.Message}");
    }
  }
}

}  // namespace Firebase
