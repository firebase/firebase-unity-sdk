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
using Firebase.Firestore.Internal;

namespace Firebase.Firestore {
  /// <summary>
  /// Settings used to configure a <see cref="FirebaseFirestore"/> instance.
  /// </summary>
#if UNITY_6000_6_OR_NEWER
  [global::Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
  public sealed class FirebaseFirestoreSettings {
    private const string DefaultHost = "firestore.googleapis.com";
    private const long DefaultCacheSizeBytes = 100L * 1024L * 1024L;
    private const long MinimumCacheSizeBytes = 1024L * 1024L;

    /// Constant to use when setting <see cref="FirebaseFirestoreSettings.CacheSizeBytes"/> to
    /// disable garbage collection.
    public static readonly long CacheSizeUnlimited = -1L;

    private readonly object _lock = new object();
    private bool _frozen;
    private string _host = DefaultHost;
    private bool _sslEnabled = true;
    private bool _persistenceEnabled = true;
    private long _cacheSizeBytes = DefaultCacheSizeBytes;

    internal FirebaseFirestoreSettings() {
    }

    internal void MarkDisposed() {
      lock (_lock) {
        _frozen = true;
      }
    }

    internal void ValidateAndFreeze() {
      lock (_lock) {
        if (_frozen) return;
        if (string.Equals(_host, DefaultHost, StringComparison.OrdinalIgnoreCase) && !_sslEnabled) {
          throw new InvalidOperationException(
              "You can't set the 'SslEnabled' setting to false unless you also set a non-default 'Host'.");
        }
        if (_cacheSizeBytes != CacheSizeUnlimited && _cacheSizeBytes < MinimumCacheSizeBytes) {
          throw new InvalidOperationException(
              $"Cache size must be set to at least {MinimumCacheSizeBytes} bytes or CacheSizeUnlimited.");
        }
        _frozen = true;
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
      get { lock (_lock) return _host; }
      set {
        Preconditions.CheckNotNullOrEmpty(value, nameof(value));
        lock (_lock) {
          EnsureWritableLocked();
          _host = value;
        }
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
      get { lock (_lock) return _sslEnabled; }
      set {
        lock (_lock) {
          EnsureWritableLocked();
          _sslEnabled = value;
        }
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
      get { lock (_lock) return _persistenceEnabled; }
      set {
        lock (_lock) {
          EnsureWritableLocked();
          _persistenceEnabled = value;
        }
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
      get { lock (_lock) return _cacheSizeBytes; }
      set {
        lock (_lock) {
          EnsureWritableLocked();
          if (value != CacheSizeUnlimited && value < MinimumCacheSizeBytes) {
            throw new ArgumentException(
                $"Cache size must be at least {MinimumCacheSizeBytes} bytes.", nameof(value));
          }
          _cacheSizeBytes = value;
        }
      }
    }

    private void EnsureWritableLocked() {
      if (_frozen) {
        throw new InvalidOperationException(
            "The settings cannot be modified after calling non-static methods in the " +
            "FirebaseFirestore instance or after the FirebaseFirestore instance is disposed.");
      }
    }

    /// <inheritdoc />
    public override string ToString() =>
        nameof(FirebaseFirestoreSettings) + "{" + nameof(Host) + "=" + Host + ", " +
        nameof(SslEnabled) + "=" + SslEnabled + ", " + nameof(PersistenceEnabled) + "=" +
        PersistenceEnabled + ", " + nameof(CacheSizeBytes) + "=" + CacheSizeBytes + "}";
  }
}
