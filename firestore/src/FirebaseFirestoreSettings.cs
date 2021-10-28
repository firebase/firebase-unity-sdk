// Copyright 2017 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// Settings used to configure a <see cref="FirebaseFirestore"/> instance.
  /// </summary>
  public sealed class FirebaseFirestoreSettings {
    /// Constant to use when setting <see cref="FirebaseFirestoreSettings.CacheSizeBytes"/> to
    /// disable garbage collection.
    public static readonly long CacheSizeUnlimited = SettingsProxy.kCacheSizeUnlimited;

    // The lock that must be held during read and write operations to all instance variables
    // declared below, such as `_firestoreProxy` and `_host`.
    private readonly ReaderWriterLock _lock = new ReaderWriterLock();

    // The `FirestoreProxy` to which to apply the settings from this object.
    // This field will be set to `null` after the settings are applied.
    private FirestoreProxy _firestoreProxy;

    private string _host;
    private bool _sslEnabled;
    private bool _persistenceEnabled;
    private long _cacheSizeBytes;

    internal FirebaseFirestoreSettings(FirestoreProxy firestoreProxy) {
      Preconditions.CheckNotNull(firestoreProxy, nameof(firestoreProxy));
      _firestoreProxy = firestoreProxy;

      SettingsProxy settingsProxy = firestoreProxy.settings();
      Preconditions.CheckNotNull(settingsProxy, nameof(settingsProxy));
      _host = settingsProxy.host();
      _sslEnabled = settingsProxy.is_ssl_enabled();
      _persistenceEnabled = settingsProxy.is_persistence_enabled();
      _cacheSizeBytes = settingsProxy.cache_size_bytes();
    }

    internal void Dispose() {
      _lock.AcquireWriterLock(Int32.MaxValue);
      try {
        _firestoreProxy = null;
      } finally {
        _lock.ReleaseWriterLock();
      }
    }

    /// <summary>
    /// The host of the Cloud Firestore backend.
    /// </summary>
    ///
    /// <remarks>
    /// This property must not be modified after calling non-static methods in the owning
    /// <see cref="FirebaseFirestore"/> object. Attempting to do so will result in an exception.
    /// </remarks>
    public string Host {
      get { return WithReadLock(() => _host); }
      set {
        Preconditions.CheckNotNullOrEmpty(value, nameof(value));
        WithWriteLock(() => { _host = value; });
      }
    }

    /// <summary>
    /// Whether or not to use SSL for communication.
    /// </summary>
    ///
    /// <remarks>
    /// This property must not be modified after calling non-static methods in the owning
    /// <see cref="FirebaseFirestore"/> object. Attempting to do so will result in an exception.
    /// </remarks>
    public bool SslEnabled {
      get { return WithReadLock(() => _sslEnabled); }
      set {
        WithWriteLock(() => { _sslEnabled = value; });
      }
    }

    /// <summary>
    /// Whether or not to use local persistence storage.
    /// </summary>
    ///
    /// <remarks>
    /// This property must not be modified after calling non-static methods in the owning
    /// <see cref="FirebaseFirestore"/> object. Attempting to do so will result in an exception.
    /// </remarks>
    public bool PersistenceEnabled {
      get { return WithReadLock(() => _persistenceEnabled); }
      set {
        WithWriteLock(() => { _persistenceEnabled = value; });
      }
    }

    /// <summary>
    /// Sets an approximate cache size threshold for the on-disk data. If the
    /// cache grows beyond this size, Cloud Firestore will start removing data
    /// that hasn't been recently used. The size is not a guarantee that the
    /// cache will stay below that size, only that if the cache exceeds the
    /// given size, cleanup will be attempted.
    ///
    /// By default, collection is enabled with a cache size of 100 MB. The
    /// minimum value is 1 MB.
    /// </summary>
    ///
    /// <remarks>
    /// This property must not be modified after calling non-static methods in the owning
    /// <see cref="FirebaseFirestore"/> object. Attempting to do so will result in an exception.
    /// </remarks>
    public long CacheSizeBytes {
      get { return WithReadLock(() => _cacheSizeBytes); }
      set {
        WithWriteLock(() => { _cacheSizeBytes = value; });
      }
    }

    private T WithReadLock<T>(Func<T> func) {
      _lock.AcquireReaderLock(Int32.MaxValue);
      try {
        return func();
      } finally {
        _lock.ReleaseReaderLock();
      }
    }

    private void WithWriteLock(Action action) {
      _lock.AcquireWriterLock(Int32.MaxValue);
      try {
        if (_firestoreProxy == null) {
          throw new InvalidOperationException(
              "The settings cannot be modified after calling non-static methods in the " +
              "FirebaseFirestore instance or after the FirebaseFirestore instance is disposed.");
        }
        action();
      } finally {
        _lock.ReleaseWriterLock();
      }
    }

    /// <summary>
    /// The first time that this method is invoked it will apply the settings from this object to
    /// the <a cref="FirestoreProxy" /> object that was given to the constructor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All subsequent invocations after the first invocation will simply do nothing and return as
    /// if successful.
    /// </para>
    /// <para>
    /// After this method is invoked then any attempts to change the values of the properties in
    /// this object will result in an exception.
    /// </para>
    /// </remarks>
    internal void EnsureAppliedToFirestoreProxy() {
      if (_firestoreProxy == null) {
        return;
      }

      _lock.AcquireWriterLock(Int32.MaxValue);
      try {
        if (_firestoreProxy == null) {
          return;
        }

        SettingsProxy settingsProxy = new SettingsProxy();
        settingsProxy.set_host(_host);
        settingsProxy.set_ssl_enabled(_sslEnabled);
        settingsProxy.set_persistence_enabled(_persistenceEnabled);
        settingsProxy.set_cache_size_bytes(_cacheSizeBytes);

        _firestoreProxy.set_settings(settingsProxy);

        _firestoreProxy = null;
      } finally {
        _lock.ReleaseWriterLock();
      }
    }

    /// <inheritdoc />
    public override string ToString() {
      return nameof(FirebaseFirestoreSettings) + "{" + nameof(Host) + "=" + Host + ", " +
             nameof(SslEnabled) + "=" + SslEnabled + ", " + nameof(PersistenceEnabled) + "=" +
             PersistenceEnabled + ", " + nameof(CacheSizeBytes) + "=" + CacheSizeBytes + "}";
    }
  }
}
