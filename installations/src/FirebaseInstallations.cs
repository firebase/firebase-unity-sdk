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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Installations.Internal;
using Firebase.Internal;

[assembly: InternalsVisibleTo("Firebase.TestHarness"), InternalsVisibleTo("Firebase.Tests")]
[assembly: InternalsVisibleTo("Firebase.Benchmarks"), InternalsVisibleTo("Assembly-CSharp"), InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace Firebase.Installations {
  /// <summary>Installations error codes.</summary>
  public enum Error {
    /// <summary>The operation succeeded.</summary>
    None = 0,
    /// <summary>An unknown error occurred.</summary>
    Unknown = 1,
    /// <summary>Installations service cannot be accessed.</summary>
    NoAccess = 2,
    /// <summary>Some of the parameters of the request were invalid.</summary>
    InvalidConfiguration = 3,
  }

  /// <summary>
  /// Installations provides a unique identifier for each app instance and
  /// a mechanism to authenticate and authorize actions (for example, sending an
  /// FCM message).
  /// </summary>
  /// <remarks>
  /// Provides a unique identifier for a Firebase installation.
  /// Provides an auth token for a Firebase installation.
  /// Provides an API to perform data deletion for a Firebase
  /// installation.
  /// </remarks>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public class FirebaseInstallations : IDisposable {
    private sealed class Slot { internal FirebaseInstallations Current; internal bool Disposed; }
    private static readonly FirebaseInstanceCache<Slot> _slots = new(s => {
      FirebaseInstallations cur;
      lock (s) { s.Disposed = true; cur = s.Current; s.Current = null; }
      cur?.DisposeCore(fromSlot: true);
    });

    private FirebaseApp _app;
    private readonly Slot _slot;
    private readonly InstallationsClient _client;
    private int _disposed;

    static FirebaseInstallations() { RegisterInteropsProvider(); FirebaseInstanceCache.RegisterResetHook(RegisterInteropsProvider); }
#if UNITY_2019_3_OR_NEWER
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnSubsystemRegistration() { MobileInstallationsBridge.CaptureMainThread(); RegisterInteropsProvider(); }
#endif
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void OnEditorLoad() { MobileInstallationsBridge.CaptureMainThread(); RegisterInteropsProvider(); }
#endif
    internal static void RegisterInteropsProvider() => FirebaseInterops.RegisterInstallationsProvider(
        app => app != null ? GetInstance(app)?.GetIdAsync() : Task.FromResult<string>(null),
        (app, forceRefresh) => app != null ? GetInstance(app)?.GetTokenAsync(forceRefresh) : Task.FromResult<string>(null));
    private FirebaseInstallations(FirebaseApp app, Slot slot) => (_app, _slot, _client) = (app, slot, new InstallationsClient(app));
    internal InstallationsClient Client => _client;

    /// @brief App object associated with this Installations.
    public FirebaseApp App => _app;

    /// @brief Installations associated with the default Firebase App.
    ///
    /// @returns An Installations object associated with the default Firebase App.
    public static FirebaseInstallations DefaultInstance => FirebaseApp.DefaultInstance is FirebaseApp app ? GetInstance(app) : null;

    /// @brief Returns the `Installations` object for an `App` creating the
    /// `Installations` if required.
    ///
    /// @param[in] app The `App` to create an `Installations` object from.
    ///
    /// @returns The `Installations` object associated with `app`.
    public static FirebaseInstallations GetInstance(FirebaseApp app) {
      if (app == null) throw new ArgumentNullException(nameof(app));
      RegisterInteropsProvider();
      Slot slot = _slots.GetOrCreate(app, () => new Slot());
      lock (slot) { return slot.Disposed ? throw new ObjectDisposedException(nameof(FirebaseApp)) : (slot.Current ??= new FirebaseInstallations(app, slot)); }
    }

    /// @brief Returns a stable identifier that uniquely identifies the app
    /// instance.
    ///
    /// @returns Unique identifier for the app instance.
    public Task<string> GetIdAsync() { ThrowIfDisposed(); return _client.GetIdAsync(); }

    /// @brief Returns a token that authorizes an Entity to perform an action on
    /// behalf of the application identified by Installations.
    ///
    /// This is similar to an OAuth2 token except it applies to the
    /// application instance instead of a user.
    ///
    /// @param forceRefresh If set true, will always return a new token.
    ///
    /// @returns A token that can identify and authorize the instance of the
    /// application on the device.
    public Task<string> GetTokenAsync(bool forceRefresh) { ThrowIfDisposed(); return _client.GetTokenAsync(forceRefresh); }

    /// @brief Delete the ID associated with the app, revoke all tokens, and
    /// clear local state.
    ///
    /// @returns A task that completes when the installation has been deleted.
    public Task DeleteAsync() { ThrowIfDisposed(); return _client.DeleteAsync(); }
    /// <summary>Releases all resources used by this <see cref="FirebaseInstallations" /> instance.</summary>
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    /// <summary>Releases resources used by this <see cref="FirebaseInstallations" /> instance.</summary>
    /// <param name="disposing"><c>true</c> when called deterministically from <see cref="Dispose()" />.</param>
    public virtual void Dispose(bool disposing) { if (disposing) DisposeCore(fromSlot: false); }

    private void DisposeCore(bool fromSlot) {
      if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
      if (!fromSlot) { lock (_slot) { if (ReferenceEquals(_slot.Current, this)) _slot.Current = null; } }
      _app = null;
      _client.Dispose();
    }
    private void ThrowIfDisposed() { if (Volatile.Read(ref _disposed) != 0) throw new NullReferenceException("FirebaseInstallations instance has been disposed."); }
  }
}
